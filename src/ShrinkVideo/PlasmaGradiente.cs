using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ShrinkVideo;

/// <summary>
/// El gradiente de plasma del aviso de exportación — el shader de lightswind (distorsión de
/// dominio con viñeta y glow), pero calculado de forma que NO pueda repetir el problema de
/// rendimiento que costó tanto cerrar. La regla es del usuario y es la línea roja: la
/// fluidez manda.
///
/// Cómo se consigue idéntico a la vista Y barato:
///   · Se calcula DIMINUTO (≈160×90). El plasma es de frecuencia baja —todo son manchas
///     borrosas—, así que a esa resolución, escalado a pantalla con interpolación, es
///     indistinguible del grande. Pero cuesta ~200× menos.
///   · Se calcula en un HILO DE FONDO, por CPU. Durante un encode por hardware la CPU está
///     ociosa (medido: la app al 3 %), así que este cálculo es gratis y —crucial— NO toca
///     ni el hilo de la UI (el que el arrastre de la ventana necesita) ni la GPU (la que el
///     codificador necesita). Las dos contenciones que causaban los tirones, esquivadas.
///   · Solo el volcado del buffer al mapa de bits va al hilo de la UI, y es un memcpy de
///     ~90 KB a 14 fps: imperceptible.
///
/// La intensidad respira: cada pocos segundos elige un objetivo nuevo al azar y va hacia él,
/// así el brillo sube y baja sin patrón, como pidió el usuario.
/// </summary>
public sealed class PlasmaGradiente : IDisposable
{
    private const int Ancho = 160, Alto = 90;   // 16:9 diminuto; se escala borroso a pantalla
    private const int Iteraciones = 32;          // el shader usa 40; 32 es idéntico a la vista y más barato
    private const double Fps = 14;               // el plasma se mueve lento: 14 fps no se distingue de 60

    /// <summary>El mapa de bits que pinta la interfaz. Se escala a la capa entera.</summary>
    public WriteableBitmap Bitmap { get; }

    private readonly Dispatcher _ui;
    private readonly byte[] _buffer = new byte[Ancho * Alto * 4];   // donde pinta el hilo de fondo
    private readonly byte[] _frame = new byte[Ancho * Alto * 4];    // copia estable que lee la UI
    private readonly System.Windows.Int32Rect _rect = new(0, 0, Ancho, Alto);
    private readonly Random _rnd = new();

    private Thread? _hilo;
    private volatile bool _vivo;
    private volatile bool _pausado;
    private volatile bool _volcarPendiente;   // fusiona: no encola otro volcado si ya hay uno

    // Intensidad «amplia»: del casi apagado al vibrante, cambiando de objetivo cada 2–6 s.
    private const double IntMin = 0.30, IntMax = 1.0;
    private double _int = 0.7, _intObjetivo = 0.9, _cambioEn;

    public PlasmaGradiente()
    {
        _ui = Dispatcher.CurrentDispatcher;
        Bitmap = new WriteableBitmap(Ancho, Alto, 96, 96, PixelFormats.Pbgra32, null);
    }

    public void Arrancar()
    {
        if (_vivo) { _pausado = false; return; }
        _vivo = true; _pausado = false;
        _hilo = new Thread(Bucle)
        {
            IsBackground = true,
            // Por debajo de lo normal: si alguna vez la máquina va justa, este adorno cede
            // antes que el trabajo de verdad. Nunca al revés.
            Priority = ThreadPriority.BelowNormal,
            Name = "PlasmaGradiente",
        };
        _hilo.Start();
    }

    /// <summary>Congela el plasma (p. ej. mientras se arrastra la ventana) sin matar el hilo.</summary>
    public void Pausar() => _pausado = true;
    public void Reanudar() => _pausado = false;

    private void Bucle()
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        double t = 0, ultimo = 0;
        var espera = TimeSpan.FromSeconds(1.0 / Fps);

        while (_vivo)
        {
            if (_pausado) { Thread.Sleep(60); ultimo = reloj.Elapsed.TotalSeconds; continue; }

            double ahora = reloj.Elapsed.TotalSeconds;
            double dt = Math.Min(0.1, ahora - ultimo);
            ultimo = ahora;
            t += dt;

            // La intensidad busca un objetivo nuevo cada 2–6 s y se acerca suavemente
            if (ahora >= _cambioEn)
            {
                _intObjetivo = IntMin + _rnd.NextDouble() * (IntMax - IntMin);
                _cambioEn = ahora + 2.0 + _rnd.NextDouble() * 4.0;
            }
            _int += (_intObjetivo - _int) * Math.Min(1.0, dt * 0.9);

            Render(t, _int);

            // El volcado al mapa de bits va al hilo de la UI, pero es solo el memcpy. Con
            // BeginInvoke (no Invoke) el hilo de fondo NUNCA se bloquea esperando a la UI —
            // importa durante el arrastre, cuando la UI está en su bucle modal—. Y si un
            // volcado sigue pendiente, no se encola otro: mejor saltar un fotograma que
            // apilarlos.
            if (!_volcarPendiente)
            {
                // Copia estable: el hilo de fondo seguirá pintando en _buffer para el
                // siguiente fotograma; la UI lee _frame, que no se toca mientras hay un
                // volcado pendiente. Sin esto, la UI leería píxeles a medio pintar (desgarro).
                Buffer.BlockCopy(_buffer, 0, _frame, 0, _frame.Length);
                _volcarPendiente = true;
                try
                {
                    _ui.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        if (_vivo) Bitmap.WritePixels(_rect, _frame, Ancho * 4, 0);
                        _volcarPendiente = false;
                    });
                }
                catch { break; }   // la app se está cerrando
            }

            var resto = espera - TimeSpan.FromSeconds(reloj.Elapsed.TotalSeconds - ahora);
            if (resto > TimeSpan.Zero) Thread.Sleep(resto);
        }
    }

    /// <summary>El shader de lightswind, píxel a píxel. Es una traducción directa del GLSL.</summary>
    private void Render(double iTime, double intensidad)
    {
        double time = iTime * 1.25;
        double ct = CosRange(time * 5.0, 3.0, 1.1);
        double xBoost = CosRange(time * 0.2, 5.0, 5.0);
        double yBoost = CosRange(time * 0.1, 10.0, 5.0);
        double fScale = CosRange(time * 15.5, 1.25, 0.5);
        double cosCt = Math.Cos(ct);
        double maxDim = Math.Max(Ancho, Alto);

        System.Threading.Tasks.Parallel.For(0, Alto, y =>
        {
            double uy = (double)y / Alto;
            int fila = y * Ancho * 4;
            for (int x = 0; x < Ancho; x++)
            {
                double px = (2.0 * x - Ancho) / maxDim;
                double py = (2.0 * y - Alto) / maxDim;

                for (int i = 1; i < Iteraciones; i++)
                {
                    double nx = px + 0.25 / i * Math.Sin(i * py + time * cosCt * 0.5 / 20.0 + 0.005 * i) * fScale + xBoost;
                    double ny = py + 0.25 / i * Math.Sin(i * px + time * ct * 0.3 / 40.0 + 0.03 * (i + 15)) * fScale + yBoost;
                    px = nx; py = ny;
                }

                double r = (0.5 * Math.Sin(3.0 * px) + 0.5) * 0.975;
                double g = (0.5 * Math.Sin(3.0 * py) + 0.5) * 0.975;
                double b = Math.Sin(px + py) * 0.975;
                r = Clamp01(r); g = Clamp01(g); b = Clamp01(b);

                double ux = (double)x / Ancho;
                double vignette = (1.0 - 5.0 * (uy - 0.5) * (uy - 0.5)) * (1.0 - 5.0 * (ux - 0.5) * (ux - 0.5));
                double alpha = Clamp01((r + g + b) / 4.0 * 1.5 * vignette) * intensidad;

                // PBGRA premultiplicado: color × alpha, sobre el fondo oscuro de la capa
                int p = fila + x * 4;
                _buffer[p + 0] = (byte)(b * alpha * 255);
                _buffer[p + 1] = (byte)(g * alpha * 255);
                _buffer[p + 2] = (byte)(r * alpha * 255);
                _buffer[p + 3] = (byte)(alpha * 255);
            }
        });
    }

    private static double CosRange(double amt, double range, double min) =>
        ((1.0 + Math.Cos(amt * Math.PI / 180.0)) * 0.5) * range + min;

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    public void Dispose()
    {
        _vivo = false;
        try { _hilo?.Join(200); } catch { }
    }
}
