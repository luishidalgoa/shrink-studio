using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Threading;
using Microsoft.Win32;
using ShrinkVideo.Reindex;

namespace ShrinkVideo;

/// <summary>Un tramo, tal como se ve en la lista de la derecha.</summary>
public sealed class TramoFila : INotifyPropertyChanged
{
    private string _nombre = "";
    public double Inicio { get; set; }
    public double Fin { get; set; }
    public int Numero { get; set; }
    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; PropertyChanged?.Invoke(this, new(nameof(Nombre))); }
    }
    private bool _enCurso;
    /// <summary>Se está exportando ahora mismo: la tarjeta lo enseña.</summary>
    public bool EnCurso
    {
        get => _enCurso;
        set { _enCurso = value; PropertyChanged?.Invoke(this, new(nameof(EnCurso))); }
    }
    public double Duracion => Fin - Inicio;
    public string Rango => $"{Reloj(Inicio)} – {Reloj(Fin)}  ({Reloj(Duracion)})";

    /// <summary>
    /// Los extremos han cambiado (se ha arrastrado una junta). Se avisa a mano porque
    /// arrastrando se tocan sesenta veces por segundo y rehacer la lista entera a ese ritmo
    /// le quita el foco al campo del nombre en mitad de una palabra.
    /// </summary>
    public void Refrescar()
    {
        PropertyChanged?.Invoke(this, new(nameof(Rango)));
        PropertyChanged?.Invoke(this, new(nameof(Duracion)));
    }

    public static string Reloj(double s) =>
        s >= 3600 ? $"{(int)(s / 3600)}:{(int)(s % 3600 / 60):00}:{(int)(s % 60):00}"
                  : $"{(int)(s / 60)}:{(int)(s % 60):00}";

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Página «Recortes»: parte un vídeo en los trozos que hagan falta.
///
/// El modelo es UNO: la lista de tramos (ver <see cref="Tramos"/>). Se arranca con el vídeo
/// entero, «Cortar aquí» parte en dos el tramo donde va la reproducción, y quitar un tramo
/// lo descarta. Así «esto son dos capítulos» y «quítale este cacho» son la misma herramienta.
///
/// La salida NO reinventa nada: los mismos desplegables (<see cref="OpcionesSalida"/>), la
/// misma estimación (<see cref="Estimator"/>) y el mismo codificador que Comprimir — un tramo
/// se le pide al motor como unas opciones con «Desde» y «Duración».
/// </summary>
public partial class RecortesView : UserControl
{
    private readonly ObservableCollection<TramoFila> _tramos = new();
    private readonly Engine _engine = new();
    private readonly DispatcherTimer _reloj;
    private VideoRow? _fuente;
    private double _duracion;
    private bool _pausado = true;
    private CancellationTokenSource? _cancelar;
    private string _tramoActual = "";
    private string? _destino;      // null = junto al vídeo original
    private bool _exportando;
    private string? _tempMiniaturas;
    private bool _partirAlSaberDuracion;
    /// <summary>Cancela la descarga de un vídeo que estaba solo en la nube (Esc).</summary>
    private CancellationTokenSource? _cancelaDescarga;

    // ── ampliación de la línea de tiempo ──
    private const double ZoomMin = 1, ZoomMax = 40;
    private double _zoom = 1;
    // Fotogramas ya sacados, por segundo redondeado al hueco. Es la unica cache que hay y
    // se vacia entera al cambiar de video o al salir de la pagina.
    private readonly Dictionary<int, BitmapImage> _previas = new();
    private readonly DispatcherTimer _esperaPrevia;
    private int _previaPedida = -1;
    private bool _sacandoPrevia;
    // El fondo de la pista pide sus fotogramas por esta cola; el que mira el cursor se cuela
    // por delante, porque es el unico que el usuario esta esperando de verdad.
    private readonly Queue<int> _colaFondo = new();
    private readonly List<(Image Celda, int Hueco)> _celdas = new();
    // Puntos de los que ffmpeg no ha sabido sacar fotograma. Se anotan para no pedirlos en bucle.
    private readonly HashSet<int> _sinFotograma = new();
    private readonly DispatcherTimer _esperaPista;

    /// <summary>Qué se está arrastrando con el ratón sobre la pista.</summary>
    private enum Agarre { Nada, Cabezal, Junta }

    private Agarre _agarre = Agarre.Nada;
    private int _juntaTramo;
    private Extremo _juntaExtremo;

    /// <summary>Se avisa al anfitrión para que lo escriba en el registro compartido.</summary>
    public event Action<string>? Log;

    public RecortesView()
    {
        InitializeComponent();
        listaTramos.ItemsSource = _tramos;

        cboFmt.ItemsSource = OpcionesSalida.Formatos;
        cboCodec.ItemsSource = OpcionesSalida.Codecs;
        cboQ.ItemsSource = OpcionesSalida.Calidades;
        cboRes.ItemsSource = OpcionesSalida.Resoluciones;
        cboAud.ItemsSource = OpcionesSalida.Audios;
        foreach (var c in new[] { cboFmt, cboCodec, cboQ, cboRes, cboAud })
        {
            c.SelectedIndex = 0;
            c.SelectionChanged += (_, _) => RefrescarEstimacion();
        }

        btnElegir.Click += (_, _) => ElegirVideo();
        btnPlay.Click += (_, _) => Alternar();
        btnAtras.Click += (_, _) => Saltar(-10);
        btnAdelante.Click += (_, _) => Saltar(10);
        btnCortar.Click += (_, _) => CortarAqui();
        btnExportar.Click += async (_, _) => await ExportarAsync();
        btnPausarExp.Click += (_, _) => AlternarPausaExp();
        btnDetenerExp.Click += (_, _) => DetenerExportacion();
        btnDestino.Click += (_, _) => ElegirDestino();

        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } f) Cargar(f[0]);
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _cancelaDescarga != null)
            { _cancelaDescarga.Cancel(); e.Handled = true; return; }
            if (_fuente == null) return;
            // Escribiendo el nombre de un tramo las teclas son letras, no atajos. Sin esto,
            // en ese campo no se podía ni poner un espacio ni escribir una «c»: se los comía
            // el atajo de cortar antes de que el cuadro de texto los viera.
            if (Keyboard.FocusedElement is TextBox) return;
            switch (e.Key)
            {
                case Key.Space: Alternar(); break;
                case Key.Left: Saltar(-5); break;
                case Key.Right: Saltar(5); break;
                case Key.C: CortarAqui(); break;
                default: return;
            }
            e.Handled = true;
        };

        video.MediaOpened += (_, _) =>
        {
            if (!video.NaturalDuration.HasTimeSpan) return;
            Duracion(video.NaturalDuration.TimeSpan.TotalSeconds);
        };
        video.MediaFailed += (_, _) =>
            lblSinVideo.Text = "Este vídeo no se puede previsualizar aquí, pero sí cortarlo.";

        _reloj = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _reloj.Tick += (_, _) =>
        {
            // Arrastrando manda el ratón: si el reloj sigue recolocando el cabezal, el
            // tirador y el cabezal se pelean por la misma línea y da tirones.
            if (_fuente == null || _exportando || _agarre != Agarre.Nada) return;
            Cabezal(video.Position.TotalSeconds);
        };
        _reloj.Start();

        // Rebote: arrastrando se disparan decenas de posiciones por segundo y sacar un
        // fotograma cuesta ~200 ms. Se pide el ultimo, no todos.
        _esperaPrevia = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _esperaPrevia.Tick += async (_, _) => { _esperaPrevia.Stop(); await SacarPreviaAsync(); };

        // Al redimensionar la ventana la pista se repinta al momento (es barato), pero los
        // fotogramas del fondo esperan a que el usuario suelte: cada uno es una llamada a
        // ffmpeg y arrastrando el borde saldrían cientos.
        _esperaPista = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _esperaPista.Tick += (_, _) => { _esperaPista.Stop(); TenderFotogramas(); };

        pista.MouseLeftButtonDown += (_, e) =>
        {
            if (_fuente == null || _duracion <= 0) return;
            _agarre = Agarre.Cabezal;
            pista.CaptureMouse();
            Buscar(SegundoEn(e.GetPosition(pista).X));
        };
        pista.MouseMove += AlPasarPorLaPista;
        pista.MouseLeftButtonUp += (_, _) => SoltarLaPista();
        pista.LostMouseCapture += (_, _) => _agarre = Agarre.Nada;
        pista.MouseLeave += (_, _) =>
        {
            if (_agarre == Agarre.Nada) globoPrevia.Visibility = Visibility.Collapsed;
        };

        SizeChanged += (_, _) => { AjustarAnchoPista(); PintarPista(); _esperaPista.Stop(); _esperaPista.Start(); };
        visorPista.SizeChanged += (_, _) => AjustarAnchoPista();
        visorPista.ScrollChanged += (_, e) => { if (Math.Abs(e.HorizontalChange) > 0.5) AlDesplazarPista(); };

        // Ctrl + rueda amplía; la rueda sola sigue siendo desplazarse, como en cualquier
        // editor. PreviewMouseWheel para adelantarse al desplazamiento del propio visor.
        visorPista.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            Ampliar(e.Delta > 0 ? 1.25 : 1 / 1.25, e.GetPosition(visorPista).X);
            e.Handled = true;
        };
        // Ctrl+Z / Ctrl+Y para el historial; Ctrl + y Ctrl − para el aumento (Ctrl+0, entero).
        PreviewKeyDown += (_, e) =>
        {
            // HasFlag y no «==»: con «==» exacto, Ctrl+Mayús+Z (el rehacer de toda la vida)
            // no entraba nunca aquí.
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            switch (e.Key)
            {
                case Key.Z when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): RehacerAccion(); e.Handled = true; break;
                case Key.Z: Deshacer(); e.Handled = true; break;
                case Key.Y: RehacerAccion(); e.Handled = true; break;
                case Key.OemPlus or Key.Add: Ampliar(1.25); e.Handled = true; break;
                case Key.OemMinus or Key.Subtract: Ampliar(1 / 1.25); e.Handled = true; break;
                case Key.D0 or Key.NumPad0: Ampliar(ZoomMin / Math.Max(_zoom, 0.0001)); e.Handled = true; break;
            }
        };
        // Al salir de la página no se deja nada en memoria ni en disco temporal.
        Unloaded += (_, _) => LiberarMiniaturas();
    }

    /// <summary>
    /// Ya se sabe cuánto dura: la pista puede dibujarse y pedir sus fotogramas. La duración
    /// llega por dos caminos —el reproductor al abrir y el sondeo— y además el reproductor
    /// se recarga al acabar una exportación, así que esto se ejecuta varias veces sobre el
    /// mismo vídeo. Los tramos solo se rehacen si de verdad es otro material; si no, volver
    /// de exportar borraría los cortes que el usuario acaba de hacer.
    /// </summary>
    /// <summary>
    /// La pista mide el ancho del visor por el aumento. A 1× cabe entera; a 8× hay que
    /// desplazarse, y cada segundo ocupa ocho veces más — que es de lo que se trata.
    /// </summary>
    private bool _ajustandoAncho;

    // ── deshacer / rehacer ──
    // Rehacer() es el ÚNICO sitio por el que cambian los tramos, así que basta con guardar
    // ahí la foto anterior. Las dos pilas se manejan como en cualquier editor: una acción
    // nueva invalida la rama de rehacer.
    private readonly Stack<List<Tramo>> _atras = new();
    private readonly Stack<List<Tramo>> _adelante = new();
    private bool _pausadaExp;

    private void AjustarAnchoPista()
    {
        // Pestillo: cambiar el ancho fuerza un relayout, ese relayout puede mover el
        // ViewportWidth (aparece o desaparece la barra de desplazamiento) y eso vuelve a
        // llamar aquí. Sin el pestillo se realimenta hasta reventar la pila — la primera
        // versión mataba el proceso sin dejar ni un mensaje.
        if (_ajustandoAncho) return;
        double visible = visorPista.ViewportWidth;
        if (visible <= 0) return;
        double nuevo = Math.Round(visible * _zoom);
        if (Math.Abs(pista.Width - nuevo) < 0.5) return;

        _ajustandoAncho = true;
        try { pista.Width = nuevo; pista.UpdateLayout(); }
        finally { _ajustandoAncho = false; }
    }

    /// <summary>
    /// Amplía o reduce dejando quieto el segundo que hay bajo <paramref name="anclaX"/> (una
    /// x del VISOR). Es lo que hace que ampliar con la rueda se sienta natural: el punto que
    /// estás mirando no se te escapa de debajo del cursor.
    /// </summary>
    internal void Ampliar(double factor, double? anclaX = null)
    {
        double antes = _zoom;
        double nuevo = Math.Clamp(_zoom * factor, ZoomMin, ZoomMax);
        if (Math.Abs(nuevo - antes) < 0.001) return;

        double x = anclaX ?? visorPista.ViewportWidth / 2;
        // El segundo del ancla, ANTES de cambiar nada.
        double segAncla = SegundoEn(visorPista.HorizontalOffset + x);

        _zoom = nuevo;
        AjustarAnchoPista();

        // Y se recoloca el desplazamiento para que ese segundo vuelva a caer en la misma x.
        // El UpdateLayout no es un adorno: ScrollToHorizontalOffset no surte efecto hasta el
        // siguiente pase de diseño, y girando la rueda deprisa llegan varios pasos dentro
        // del mismo fotograma — el segundo llamaría leyendo un desplazamiento viejo y el
        // punto anclado se iría de debajo del cursor (medido: 389 s de deriva en 5 pasos).
        visorPista.ScrollToHorizontalOffset(Math.Max(0, XDe(segAncla) - x));
        visorPista.UpdateLayout();

        PintarPista();
        _esperaPista.Stop();
        _esperaPista.Start();
        lblZoom.Text = _zoom <= 1.01 ? "" : $"×{_zoom:0.#}";
    }

    private void Duracion(double segundos)
    {
        if (segundos <= 0) return;
        bool otroVideo = Math.Abs(_duracion - segundos) > 0.01;
        _duracion = segundos;
        lblDur.Text = TramoFila.Reloj(_duracion);
        if (otroVideo || _tramos.Count == 0)
        {
            var inicial = Tramos.Entero(_duracion);
            // La junta solo se puede poner cuando ya se sabe cuánto dura el vídeo.
            if (_partirAlSaberDuracion)
            {
                _partirAlSaberDuracion = false;
                inicial = Tramos.Partir(inicial, _duracion / 2);
            }
            Rehacer(inicial);
        }
        else PintarPista();
        Cabezal(video.Position.TotalSeconds);
        TenderFotogramas();
    }

    // ─────────────────────────── cargar ───────────────────────────

    private void ElegirVideo()
    {
        var d = new OpenFileDialog
        {
            Title = "Elegir el vídeo que quieres recortar",
            Filter = "Vídeos|*.mkv;*.mp4;*.avi;*.mov;*.m4v;*.webm;*.ts|Todos|*.*",
        };
        if (d.ShowDialog() == true) Cargar(d.FileName);
    }

    /// <summary>Carga un vídeo. Público porque Organizar abre aquí el fichero de una fila.</summary>
    /// <summary>
    /// Carga un vídeo. Con <paramref name="partirPorLaMitad"/> llega ya con una junta puesta
    /// en el centro: es el caso de «este fichero trae dos episodios», donde lo único que
    /// falta es arrastrarla al sitio exacto.
    /// </summary>
    public async void Cargar(string ruta, bool partirPorLaMitad = false)
    {
        if (!File.Exists(ruta)) return;

        // Cargar otro vídeo mientras se exporta dejaría la exportación hablando de un
        // fichero que ya no está en pantalla. Y si hay cortes hechos, se van a perder: eso
        // se pregunta, no se hace.
        var dueno = Window.GetWindow(this);
        if (_exportando)
        {
            DialogWindow.Aviso(dueno, "Recortes",
                "Se está exportando ahora mismo. Detén la exportación antes de cargar otro vídeo.");
            return;
        }
        if (_fuente != null && !string.Equals(_fuente.Path, ruta, StringComparison.OrdinalIgnoreCase)
            && _tramos.Count > 1
            && !DialogWindow.Confirmar(dueno, "Recortes",
                    $"Tienes {_tramos.Count} tramos preparados sobre " +
                    $"«{Path.GetFileName(_fuente.Path)}».{Environment.NewLine}{Environment.NewLine}" +
                    "Cargar otro vídeo los descarta. ¿Seguir?"))
            return;
        _partirAlSaberDuracion = partirPorLaMitad;

        var fi = new FileInfo(ruta);

        // Un vídeo que está solo en la nube se baja ENTERO antes de abrir nada. Trabajar
        // sobre el marcador a medias era la causa de dos males a la vez: las miniaturas y
        // la codificación iban a velocidad de red (la app parecía ahogada), y la
        // comprobación de «¿está libre?» del motor tropezaba con la propia descarga — el
        // export decía «SALTADO: Descargando» una y otra vez. Va ANTES de tocar ningún
        // estado: cancelar con Esc deja el proyecto anterior como estaba.
        if (NubeLocal.EsMarcador(ruta))
        {
            Ocupado(true, "Descargando de la nube…");
            barCarga.IsIndeterminate = false;
            lblCargaDet.Text = $"{Humano(fi.Length)} · Esc para cancelar";
            _cancelaDescarga = new CancellationTokenSource();
            try
            {
                var avance = new Progress<double>(pr =>
                {
                    barCarga.Value = pr;
                    lblCarga.Text = $"Descargando de la nube… {pr * 100:0} %";
                });
                await NubeLocal.DescargarAsync(ruta, avance, _cancelaDescarga.Token);
            }
            catch (OperationCanceledException)
            {
                Ocupado(false);
                Log?.Invoke("Recortes: descarga cancelada; el vídeo sigue en la nube.");
                return;
            }
            catch (Exception ex)
            {
                Ocupado(false);
                Log?.Invoke($"Recortes: no se pudo descargar «{fi.Name}»: {ex.Message}");
                return;
            }
            finally { _cancelaDescarga = null; }
            fi.Refresh();
            Log?.Invoke($"Recortes: «{fi.Name}» descargado ({Humano(fi.Length)}).");
        }
        _fuente = new VideoRow { Path = ruta, Bytes = fi.Length };
        lblVideo.Text = fi.Name;
        lblVideoDet.Text = "Analizando…";
        lblSinVideo.Visibility = Visibility.Collapsed;
        _tramos.Clear();
        _duracion = 0;            // la del vídeo anterior no vale, y sin esto no se recalcula
        LiberarMiniaturas();      // los fotogramas del vídeo anterior no valen para este

        video.Source = new Uri(ruta);
        video.Play();
        video.Pause();          // primer fotograma a la vista, sin arrancar la reproducción
        _pausado = true;

        Ocupado(true, "Analizando el vídeo…");
        var info = await _engine.ProbeAsync(ruta);
        _fuente.Width = info.Width; _fuente.Height = info.Height; _fuente.Fps = info.Fps;
        _fuente.DurationSec = info.DurationSec;
        _fuente.VideoBitrateKbps = info.VideoBitrateKbps;
        _fuente.AudioBitrateKbps = info.AudioBitrateKbps;
        _fuente.Channels = info.Channels; _fuente.AudioCodec = info.AudioCodec;
        _fuente.Codec = info.Codec; _fuente.Probed = true;

        // Si el reproductor no ha sabido abrirlo, la duración la da el sondeo: cortar tiene
        // que seguir siendo posible aunque no se pueda previsualizar.
        if (_duracion <= 0) Duracion(info.DurationSec);

        lblVideoDet.Text = $"{info.Codec.ToUpperInvariant()} · {info.Width}×{info.Height} · " +
                           $"{Humano(fi.Length)}";
        lblDuracionTotal.Text = TramoFila.Reloj(_duracion);
        MostrarDestino();

        Ocupado(false);
        btnCortar.IsEnabled = true;
        RefrescarEstimacion();
    }

    /// <summary>
    /// Deshabilita la página mientras se prepara el material. Trabajar con el vídeo a medio
    /// cargar no lleva a ningún sitio bueno, y sin aviso parece que la app se ha colgado.
    /// </summary>
    private void Ocupado(bool si, string que = "")
    {
        capaCarga.Visibility = si ? Visibility.Visible : Visibility.Collapsed;
        lblCarga.Text = que;
        lblCargaDet.Text = "";
        barCarga.Value = 0;
        barCarga.IsIndeterminate = si;
        btnElegir.IsEnabled = !si;
        btnCortar.IsEnabled = !si && _fuente != null;
        btnExportar.IsEnabled = false;      // lo recalcula RefrescarEstimacion al terminar
        listaTramos.IsEnabled = !si;
        pista.IsEnabled = !si;
        foreach (var b in new[] { btnPlay, btnAtras, btnAdelante }) b.IsEnabled = !si;
    }

    /// <summary>Cada cuántos segundos se guarda un fotograma. Más fino sería más lento.</summary>
    private const int HuecoPrevia = 5;

    /// <summary>El segundo del vídeo que hay bajo una x de la pista, y la vuelta.</summary>
    private double SegundoEn(double x) =>
        _duracion <= 0 ? 0 : Math.Clamp(x / Math.Max(1, pista.ActualWidth) * _duracion, 0, _duracion);

    private double XDe(double segundo) =>
        _duracion <= 0 ? 0 : segundo / _duracion * pista.ActualWidth;

    /// <summary>
    /// De una x de la PISTA a una x de lo que se ve. Con la pista ampliada las dos dejan de
    /// coincidir, y el globo y el botón de cortar viven fuera del visor: sin restar el
    /// desplazamiento saldrían corridos justo cuando más precisión hace falta.
    /// </summary>
    /// <summary>Cuántos tramos hay preparados. Solo para las pruebas.</summary>
    internal int NumTramos => _tramos.Count;

    /// <summary>El segundo que hay en una x de la pista. Solo para las pruebas.</summary>
    internal double SegundoBajo(double xPista) => SegundoEn(xPista);

    /// <summary>Enciende la capa de «exportando» sin exportar. Solo para las pruebas.</summary>
    internal void MostrarCapaExportando(bool si) => PintarExportando(si);

    private double XVisible(double xPista) => xPista - visorPista.HorizontalOffset;

    /// <summary>
    /// Todo lo que pasa moviendo el ratón por la pista. La posición se saca del ratón y no
    /// del estado de ningún control: así funciona igual pasando por encima que arrastrando.
    ///
    /// Los tiradores y los bloques están DENTRO de la pista, así que este manejador recibe
    /// también sus movimientos al burbujear — que es lo que se quiere: arrastrando una junta
    /// el globo sigue enseñando el fotograma exacto por donde va a partir.
    /// </summary>
    private void AlPasarPorLaPista(object remitente, MouseEventArgs e)
    {
        if (_fuente == null || _duracion <= 0) { globoPrevia.Visibility = Visibility.Collapsed; return; }

        double x = Math.Clamp(e.GetPosition(pista).X, 0, pista.ActualWidth);
        double seg = SegundoEn(x);

        switch (_agarre)
        {
            case Agarre.Junta: ArrastrarJunta(seg); break;
            case Agarre.Cabezal: Buscar(seg); break;
        }

        globoPrevia.Visibility = Visibility.Visible;

        // Centrado con el ancho REAL, y sujeto a los bordes de la pista: un globo medio
        // salido de la ventana no se lee.
        double ancho = globoPrevia.ActualWidth > 0 ? globoPrevia.ActualWidth : 200;
        double alto = globoPrevia.ActualHeight > 0 ? globoPrevia.ActualHeight : 132;
        Canvas.SetLeft(globoPrevia, Math.Clamp(XVisible(x) - ancho / 2, 0,
            Math.Max(0, visorPista.ViewportWidth - ancho)));
        Canvas.SetTop(globoPrevia, -alto - 8);
        lblPrevia.Text = TramoFila.Reloj(seg);

        int hueco = (int)(seg / HuecoPrevia) * HuecoPrevia;
        if (_previas.TryGetValue(hueco, out var ya))
        {
            imgPrevia.Source = ya;
            lblPreviaCargando.Visibility = Visibility.Collapsed;
            return;
        }
        _previaPedida = hueco;
        lblPreviaCargando.Visibility = imgPrevia.Source == null ? Visibility.Visible : Visibility.Collapsed;
        _esperaPrevia.Stop();
        _esperaPrevia.Start();
    }

    /// <summary>Se suelta el ratón: se acaba el arrastre.</summary>
    private void SoltarLaPista()
    {
        _agarre = Agarre.Nada;
        if (pista.IsMouseCaptured) pista.ReleaseMouseCapture();
        if (!pista.IsMouseOver) globoPrevia.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Dónde se dejan los jpg mientras se cargan. UNA por ejecución: cada uno se borra nada
    /// más leerlo, pero con una carpeta por vídeo cargado la carpeta vacía se quedaba en el
    /// temporal, y ahora la pista pide fotogramas siempre (antes solo si pasabas el ratón),
    /// así que se acumularían de verdad.
    /// </summary>
    private string CarpetaDeFotogramas()
    {
        if (_tempMiniaturas != null) return _tempMiniaturas;
        _tempMiniaturas = Path.Combine(Path.GetTempPath(), $"shrinkstudio-previa-{Environment.ProcessId}");
        Directory.CreateDirectory(_tempMiniaturas);
        BarrerCarpetasHuerfanas();
        return _tempMiniaturas;
    }

    /// <summary>
    /// Las carpetas que dejaran ejecuciones anteriores que se fueron sin recoger (un cierre
    /// forzado, un cuelgue). Si el proceso dueño sigue vivo no se toca: puede haber dos
    /// ShrinkStudio abiertos y no se le va a quitar el suelo al otro.
    /// </summary>
    private static void BarrerCarpetasHuerfanas()
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Path.GetTempPath(), "shrinkstudio-previa-*"))
            {
                // Las viejas llevaban un Guid en vez de un pid: esas no tienen dueño posible.
                if (int.TryParse(Path.GetFileName(d).Split('-')[^1], out var pid))
                {
                    if (pid == Environment.ProcessId) continue;
                    try { using (Process.GetProcessById(pid)) continue; }   // vive: no es huérfana
                    catch (ArgumentException) { }
                }
                try { Directory.Delete(d, true); } catch { }
            }
        }
        catch { /* barrer es cortesía: si no se puede, no rompe nada */ }
    }

    /// <summary>Ya se intentó: o está en la caché, o se sabe que de ahí no sale fotograma.</summary>
    private bool Intentado(int hueco) => _previas.ContainsKey(hueco) || _sinFotograma.Contains(hueco);

    /// <summary>
    /// El siguiente fotograma que hace falta. Manda el que está mirando el cursor: el fondo
    /// de la pista puede esperar, pero el globo lo tiene el usuario delante de los ojos.
    /// Devuelve -1 si no queda nada por sacar.
    ///
    /// MIRA la cola sin vaciarla, porque esto se llama también para preguntar «¿queda algo?»
    /// y una consulta no puede tirar trabajo pendiente.
    /// </summary>
    private int SiguienteHueco()
    {
        if (_previaPedida >= 0 && !Intentado(_previaPedida)) return _previaPedida;
        while (_colaFondo.Count > 0 && Intentado(_colaFondo.Peek())) _colaFondo.Dequeue();
        return _colaFondo.Count > 0 ? _colaFondo.Peek() : -1;
    }

    /// <summary>
    /// Saca el fotograma pedido. De uno en uno: encadenar ffmpeg por cada píxel del arrastre
    /// solo consigue una cola que llega tarde. El jpg se carga ENTERO en memoria y se borra
    /// al momento, así que en disco no queda nada.
    /// </summary>
    private async Task SacarPreviaAsync()
    {
        if (_exportando) return;   // ídem: nada de ffmpegs de miniaturas durante el export
        if (_sacandoPrevia || _fuente == null) return;
        int hueco = SiguienteHueco();
        if (hueco < 0) return;

        _sacandoPrevia = true;
        try
        {
            var jpg = Path.Combine(CarpetaDeFotogramas(), $"{hueco}.jpg");

            if (await Engine.MakeThumbnailAsync(_fuente.Path, jpg, hueco))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;   // se lee entera; el fichero queda libre
                    bmp.DecodePixelWidth = 240;
                    bmp.UriSource = new Uri(jpg);
                    bmp.EndInit();
                    bmp.Freeze();
                    _previas[hueco] = bmp;
                    if (_previaPedida == hueco)
                    {
                        imgPrevia.Source = bmp;
                        lblPreviaCargando.Visibility = Visibility.Collapsed;
                    }
                    // El mismo fotograma puede alimentar varias celdas del fondo si el vídeo
                    // es corto y dos celdas caen en el mismo hueco.
                    foreach (var (celda, suyo) in _celdas)
                        if (suyo == hueco) celda.Source = bmp;
                }
                catch { _sinFotograma.Add(hueco); }
                finally { try { File.Delete(jpg); } catch { } }
            }
            // Si de ese punto no sale fotograma se anota y no se vuelve a pedir: sin esto el
            // globo lo reintenta cada 90 ms y no para nunca.
            else _sinFotograma.Add(hueco);
        }
        finally { _sacandoPrevia = false; }

        // Mientras se sacaba ese, el cursor ya estará en otro sitio y el fondo seguirá a
        // medias: se sigue con lo que quede.
        if (SiguienteHueco() >= 0) _esperaPrevia.Start();
    }

    /// <summary>Suelta los fotogramas y borra lo que quedara en disco. Nada se acumula.</summary>
    private void LiberarMiniaturas()
    {
        globoPrevia.Visibility = Visibility.Collapsed;
        imgPrevia.Source = null;
        _previas.Clear();
        _previaPedida = -1;
        _colaFondo.Clear();
        _celdas.Clear();
        _sinFotograma.Clear();
        capaFotogramas.Children.Clear();
        if (_tempMiniaturas != null)
        {
            try { Directory.Delete(_tempMiniaturas, true); } catch { }
            _tempMiniaturas = null;
        }
    }

    // ─────────────────────────── tramos ───────────────────────────

    private List<Tramo> Actuales() =>
        _tramos.Select(t => new Tramo(t.Inicio, t.Fin, t.Nombre)).ToList();

    private void CortarAqui() => Rehacer(Tramos.Partir(Actuales(), video.Position.TotalSeconds));

    private void OnQuitarTramo(object remitente, RoutedEventArgs e)
    {
        if (remitente is not Button { Tag: TramoFila f }) return;
        var i = _tramos.IndexOf(f);
        if (i >= 0) Rehacer(Tramos.Quitar(Actuales(), i));
    }

    /// <summary>
    /// Vuelca la lista de tramos a la vista. Los nombres que el usuario haya escrito se
    /// respetan; los que sigan siendo los sugeridos se recalculan, porque al cambiar el
    /// número de tramos el reparto de historias del nombre ya no es el mismo.
    /// </summary>
    private void Rehacer(IReadOnlyList<Tramo> nuevos, bool registrar = true)
    {
        // La foto de ANTES, para poder volver. Cargar un vídeo o deshacer no se registran:
        // el primero no tiene «antes» y el segundo ya gestiona las pilas él mismo.
        if (registrar && _tramos.Count > 0)
        {
            _atras.Push(_tramos.Select(f => new Tramo(f.Inicio, f.Fin, f.Nombre)).ToList());
            _adelante.Clear();
        }
        var escritos = _tramos.ToDictionary(t => (t.Inicio, t.Fin), t => t.Nombre);
        var baseNombre = _fuente != null
            ? Path.GetFileNameWithoutExtension(_fuente.Path) : "recorte";
        var sugeridos = Tramos.Nombrar(baseNombre, nuevos.Count);

        _tramos.Clear();
        for (int i = 0; i < nuevos.Count; i++)
        {
            var t = nuevos[i];
            escritos.TryGetValue((t.Inicio, t.Fin), out var previo);
            var fila = new TramoFila
            {
                Inicio = t.Inicio,
                Fin = t.Fin,
                Numero = i + 1,
                Nombre = string.IsNullOrWhiteSpace(previo) ? sugeridos[i] : previo,
            };
            // El bloque de la pista lleva escrito el nombre del fichero que va a salir: si se
            // reescribe en la lista de la derecha, la pista tiene que decir lo mismo.
            fila.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(TramoFila.Nombre)) PintarPista(); };
            _tramos.Add(fila);
        }
        btnExportar.Content = _tramos.Count switch
        {
            0 => "Exportar",
            1 => "Exportar 1 tramo",
            _ => $"Exportar {_tramos.Count} tramos",
        };
        lblAyudaTramos.Text = _tramos.Count switch
        {
            0 => "No queda ningún tramo: así no se exportaría nada.",
            1 => "Un solo tramo: se exportará el vídeo entero. Corta con la ✂ de la pista para partirlo.",
            _ => $"{_tramos.Count} tramos = {_tramos.Count} ficheros. Arrastra las juntas de la pista para afinar.",
        };
        PintarPista();
        RefrescarEstimacion();
    }

    // ─────────────────────────── la pista ───────────────────────────

    /// <summary>
    /// Dibuja la pista: lo que se descarta oscurecido, un bloque por tramo y un tirador por
    /// junta. Se rehace entera en cada cambio — son una docena de elementos y así no hay que
    /// llevar la cuenta de qué había antes, que es de donde salen los dibujos a medias.
    /// </summary>
    private void PintarPista()
    {
        capaBloques.Children.Clear();
        capaTiradores.Children.Clear();
        if (_duracion <= 0 || pista.ActualWidth <= 0) return;
        double alto = pista.ActualHeight;

        // Lo que NO se exporta, oscurecido: un hueco es un trozo que el usuario ha quitado y
        // tiene que verse de un vistazo que no va a salir en ningún fichero.
        double llevado = 0;
        foreach (var t in _tramos)
        {
            if (t.Inicio > llevado) Sombra(llevado, t.Inicio, alto);
            llevado = Math.Max(llevado, t.Fin);
        }
        if (llevado < _duracion) Sombra(llevado, _duracion, alto);

        for (int i = 0; i < _tramos.Count; i++) Bloque(i, alto);

        // Un tirador por junta. Dos tramos que se tocan comparten UNA, no dos: dibujar dos
        // tiradores pegados invita a separarlos y a abrir un agujero sin querer.
        for (int i = 0; i < _tramos.Count; i++)
        {
            bool pegadoAlAnterior = i > 0 && Math.Abs(_tramos[i - 1].Fin - _tramos[i].Inicio) < 1e-6;
            if (!pegadoAlAnterior) Tirador(i, Extremo.Inicio, _tramos[i].Inicio, alto);
            Tirador(i, Extremo.Fin, _tramos[i].Fin, alto);
        }
    }

    private void Sombra(double desde, double hasta, double alto)
    {
        var s = new Rectangle
        {
            Width = Math.Max(0, XDe(hasta) - XDe(desde)), Height = alto,
            Fill = new SolidColorBrush(Color.FromArgb(0xC4, 0x06, 0x07, 0x0D)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(s, XDe(desde));
        capaBloques.Children.Add(s);
    }

    /// <summary>
    /// Un tramo. Lleva su número y su nombre encima porque la pista y la lista de la derecha
    /// hablan de lo mismo, y saber cuál es cuál sin contar bloques es media faena. La ✕ solo
    /// asoma al pasar por encima: fija en cada bloque, la pista se llena de aspas.
    /// </summary>
    private void Bloque(int indice, double alto)
    {
        var f = _tramos[indice];
        double x = XDe(f.Inicio), ancho = Math.Max(3, XDe(f.Fin) - XDe(f.Inicio));
        var dentro = new Grid();

        // Número y nombre van juntos arriba, como el rótulo de un clip: así el resto del
        // bloque queda despejado y se siguen viendo los fotogramas, que es para lo que están.
        var rotulo = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (ancho >= 30)
            rotulo.Children.Add(new Border
            {
                Background = (Brush)FindResource("Accent800"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock
                {
                    Text = f.Numero.ToString(), FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("Accent200"),
                },
            });

        // El hueco descontado deja sitio al número, a los márgenes y a la ✕ de quitar.
        if (ancho >= 130)
            rotulo.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(4, 0, 0, 0),
                MaxWidth = ancho - 68,
                Child = new TextBlock
                {
                    Text = f.Nombre, FontSize = 10, Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            });
        dentro.Children.Add(rotulo);

        Button? quitar = null;
        if (ancho >= 46)
        {
            quitar = new Button
            {
                Style = (Style)FindResource("QuitarBloque"),
                Tag = f,
                // Separada del borde para no pisar el tirador de la junta, que va justo ahí.
                Margin = new Thickness(0, 5, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Hidden,
                ToolTip = "Quitar este tramo: no se exportará",
            };
            quitar.Click += OnQuitarTramo;
            dentro.Children.Add(quitar);
        }

        var bloque = new Border
        {
            Width = ancho, Height = alto,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(2),
            BorderBrush = (Brush)FindResource("Accent"),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0x96, 0x8A, 0xE0)),
            Child = dentro,
        };
        if (quitar != null)
        {
            bloque.MouseEnter += (_, _) => quitar.Visibility = Visibility.Visible;
            bloque.MouseLeave += (_, _) => quitar.Visibility = Visibility.Hidden;
        }
        Canvas.SetLeft(bloque, x);
        capaBloques.Children.Add(bloque);
    }

    /// <summary>
    /// El tirador de una junta. La captura del ratón se la queda la PISTA, no el tirador:
    /// arrastrando se repinta la pista entera y el tirador de debajo del cursor deja de
    /// existir a media pasada — con la captura en él, el arrastre se cortaba en seco.
    /// </summary>
    private void Tirador(int indice, Extremo extremo, double segundo, double alto)
    {
        var rayas = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (int k = 0; k < 2; k++)
            rayas.Children.Add(new Rectangle
            {
                Width = 1.4, Height = Math.Min(14, alto - 20), RadiusX = 1, RadiusY = 1,
                Fill = Brushes.White, Opacity = 0.9, Margin = new Thickness(1.1, 0, 1.1, 0),
            });

        var t = new Border
        {
            Width = 10, Height = alto,
            CornerRadius = new CornerRadius(3),
            Background = (Brush)FindResource("Accent"),
            Cursor = Cursors.SizeWE,
            Child = rayas,
            ToolTip = "Arrastra para mover el corte. No puede pasarse de los tramos de al lado.",
        };
        // Sujeto a la pista: los de los dos extremos del vídeo caen justo en el borde y, sin
        // esto, se quedan medio recortados y con la mitad de sitio donde agarrarlos.
        Canvas.SetLeft(t, Math.Clamp(XDe(segundo) - 5, 0, Math.Max(0, pista.ActualWidth - 10)));
        t.MouseLeftButtonDown += (_, e) =>
        {
            _agarre = Agarre.Junta;
            _juntaTramo = indice;
            _juntaExtremo = extremo;
            pista.CaptureMouse();
            e.Handled = true;      // agarrar la junta no es además saltar la reproducción ahí
        };
        capaTiradores.Children.Add(t);
    }

    /// <summary>
    /// Arrastre en curso. Se tocan los números del tramo EN EL SITIO y se repinta la pista,
    /// pero no se rehace la lista de la derecha: reconstruirla sesenta veces por segundo le
    /// quita el foco al campo del nombre y no se podría ni escribir mientras.
    /// </summary>
    private void ArrastrarJunta(double segundo)
    {
        var nuevos = Tramos.MoverJunta(Actuales(), _juntaTramo, _juntaExtremo, segundo, _duracion);
        if (nuevos.Count != _tramos.Count) return;
        for (int i = 0; i < nuevos.Count; i++)
        {
            _tramos[i].Inicio = nuevos[i].Inicio;
            _tramos[i].Fin = nuevos[i].Fin;
            _tramos[i].Refrescar();
        }
        PintarPista();
        RefrescarEstimacion();
    }

    /// <summary>
    /// El cabezal, y con él el botón de cortar: la tijera va donde va a partir, no en una
    /// esquina de la pantalla.
    /// </summary>
    private void Cabezal(double segundo)
    {
        lblPos.Text = TramoFila.Reloj(segundo);
        if (_duracion <= 0 || pista.ActualWidth <= 0) return;

        double x = XDe(Math.Clamp(segundo, 0, _duracion));
        cabezal.Visibility = Visibility.Visible;
        Canvas.SetLeft(cabezal, x - 2);

        btnCortar.Visibility = Visibility.Visible;
        double ancho = btnCortar.ActualWidth > 0 ? btnCortar.ActualWidth : 24;
        Canvas.SetLeft(btnCortar, Math.Clamp(XVisible(x) - ancho / 2, 0,
            Math.Max(0, visorPista.ViewportWidth - ancho)));
        Canvas.SetTop(btnCortar, 1);
    }

    /// <summary>Mueve la reproducción, y con ella el cabezal.</summary>
    private void Buscar(double segundo)
    {
        if (_fuente == null) return;
        var donde = Math.Clamp(segundo, 0, _duracion);
        video.Position = TimeSpan.FromSeconds(donde);
        Cabezal(donde);
    }

    /// <summary>
    /// Tiende los fotogramas del fondo. Van por la MISMA caché y la misma disciplina que el
    /// globo: cada jpg se carga entero en memoria, se borra del disco al momento y todo se
    /// suelta al cambiar de vídeo. Redondear al mismo hueco de 5 s hace que el fondo y el
    /// globo se aprovechen los fotogramas el uno al otro en vez de sacarlos dos veces.
    /// </summary>
    private void TenderFotogramas()
    {
        if (_exportando) return;   // no robarle lecturas al fichero que se está codificando
        capaFotogramas.Children.Clear();
        _celdas.Clear();
        _colaFondo.Clear();
        if (_fuente == null || _duracion <= 0 || pista.ActualWidth <= 0) return;

        double alto = pista.ActualHeight;
        double ancho = Math.Round(alto * 16 / 9.0);          // una celda ≈ un fotograma 16:9
        int total = Math.Max(1, (int)Math.Ceiling(pista.ActualWidth / ancho));

        // Solo se tienden las celdas que se VEN, más un margen a cada lado para que al
        // desplazarse no aparezcan vacías. A 40× la pista mide decenas de miles de píxeles:
        // tenderla entera serían cientos de extracciones de fotograma para nada.
        const int margen = 3;
        int desde = Math.Max(0, (int)(visorPista.HorizontalOffset / ancho) - margen);
        int hasta = Math.Min(total, (int)Math.Ceiling(
            (visorPista.HorizontalOffset + Math.Max(visorPista.ViewportWidth, 1)) / ancho) + margen);

        for (int k = desde; k < hasta; k++)
        {
            double centro = (k + 0.5) * ancho / pista.ActualWidth * _duracion;
            int hueco = (int)(Math.Clamp(centro, 0, _duracion) / HuecoPrevia) * HuecoPrevia;

            var celda = new Image
            {
                Width = ancho, Height = alto, Stretch = Stretch.UniformToFill,
                Opacity = 0.85,     // el fondo sitúa; los bloques y el cabezal son lo que se lee
            };
            _previas.TryGetValue(hueco, out var ya);
            celda.Source = ya;
            Canvas.SetLeft(celda, k * ancho);
            capaFotogramas.Children.Add(celda);
            _celdas.Add((celda, hueco));
            if (ya == null) _colaFondo.Enqueue(hueco);
        }

        if (SiguienteHueco() >= 0) _esperaPrevia.Start();
    }

    /// <summary>Al desplazarse hay celdas nuevas a la vista: se tienden con un respiro.</summary>
    /// <summary>Ctrl+Z: vuelve al estado anterior de los tramos.</summary>
    /// <summary>Suspende o reanuda ffmpeg donde va. Reanudar sigue, no reempieza.</summary>
    private void AlternarPausaExp()
    {
        if (!_exportando) return;
        _pausadaExp = !_pausadaExp;
        if (_pausadaExp) { _engine.Pause(); btnPausarExp.Content = "Reanudar"; lblProgreso.Text = "En pausa"; }
        else { _engine.Resume(); btnPausarExp.Content = "Pausar"; lblProgreso.Text = _tramoActual; }
    }

    /// <summary>
    /// Corta la exportación. Si estaba en pausa se reanuda ANTES de cancelar: un proceso
    /// suspendido no puede atender su propia muerte, y quedaría vivo de fondo — que es
    /// exactamente lo que no se quiere.
    /// </summary>
    private void DetenerExportacion()
    {
        if (!_exportando) return;
        if (_pausadaExp) { _engine.Resume(); _pausadaExp = false; btnPausarExp.Content = "Pausar"; }
        lblProgreso.Text = "Deteniendo…";
        btnDetenerExp.IsEnabled = false;
        _cancelar?.Cancel();
    }

    /// <summary>La cara de «se está exportando»: la capa sobre el vídeo y los botones.</summary>
    private void PintarExportando(bool si)
    {
        capaExportando.Visibility = si ? Visibility.Visible : Visibility.Collapsed;
        btnPausarExp.Visibility = btnDetenerExp.Visibility = si ? Visibility.Visible : Visibility.Collapsed;
        btnPausarExp.IsEnabled = btnDetenerExp.IsEnabled = si;
        btnPausarExp.Content = "Pausar";
        if (si) LatirPulsos(); else PararPulsos();
    }

    /// <summary>
    /// Los pulsos de luz de la capa. A 5 fps a propósito: van sobre un vídeo y toda la
    /// máquina está codificando — es justo la animación que no puede costar nada.
    /// </summary>
    /// <summary>
    /// El latido de las dos bandas laterales: la luz sube desde el canto y se retira.
    ///
    /// Van a 20 fps, no a 5 como antes. Con la caché puesta en cada banda —y no en el
    /// grupo— animar la opacidad es mezclar un mapa de bits ya pintado, así que subir el
    /// ritmo sale casi gratis; medido sobre la capa vacía, las dos bandas cuestan dos
    /// décimas de punto de CPU. A 5 fps el latido se veía a saltos.
    /// </summary>
    private void LatirPulsos()
    {
        void Latido(UIElement e, TranslateTransform asoma, double desdeX,
                    double tope, double segundos, double retraso)
        {
            var suave = new SineEase { EasingMode = EasingMode.EaseInOut };

            var luz = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(retraso),
            };
            luz.KeyFrames.Add(new EasingDoubleKeyFrame(
                tope, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(segundos / 2)), suave));
            luz.KeyFrames.Add(new EasingDoubleKeyFrame(
                0.12, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(segundos)), suave));
            Timeline.SetDesiredFrameRate(luz, 20);
            e.BeginAnimation(UIElement.OpacityProperty, luz);

            // El desplazamiento es lo que hace que la luz ASOME desde el borde en vez de
            // encenderse en el sitio. Es un transform sobre un elemento cacheado: mover el
            // mapa de bits, no volver a pintarlo.
            var entra = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(retraso),
            };
            entra.KeyFrames.Add(new EasingDoubleKeyFrame(
                0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(segundos / 2)), suave));
            entra.KeyFrames.Add(new EasingDoubleKeyFrame(
                desdeX, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(segundos)), suave));
            Timeline.SetDesiredFrameRate(entra, 20);
            asoma.BeginAnimation(TranslateTransform.XProperty, entra);
        }
        Latido(pulso1, asomaIzq, -26, 1.0, 3.4, 0);
        Latido(pulso2, asomaDer, 26, 0.85, 4.6, 1.1);
    }

    private void PararPulsos()
    {
        pulso1.BeginAnimation(UIElement.OpacityProperty, null);
        pulso2.BeginAnimation(UIElement.OpacityProperty, null);
        asomaIzq.BeginAnimation(TranslateTransform.XProperty, null);
        asomaDer.BeginAnimation(TranslateTransform.XProperty, null);
        pulso1.Opacity = pulso2.Opacity = 0;
    }

    private void Deshacer()
    {
        if (_exportando || _atras.Count == 0) return;
        _adelante.Push(_tramos.Select(f => new Tramo(f.Inicio, f.Fin, f.Nombre)).ToList());
        Rehacer(_atras.Pop(), registrar: false);
        Log?.Invoke("Recortes: deshecho.");
    }

    /// <summary>Ctrl+Y (o Ctrl+Mayús+Z): vuelve a aplicar lo deshecho.</summary>
    private void RehacerAccion()
    {
        if (_exportando || _adelante.Count == 0) return;
        _atras.Push(_tramos.Select(f => new Tramo(f.Inicio, f.Fin, f.Nombre)).ToList());
        Rehacer(_adelante.Pop(), registrar: false);
        Log?.Invoke("Recortes: rehecho.");
    }

    private void AlDesplazarPista()
    {
        Cabezal(video.Position.TotalSeconds);
        _esperaPista.Stop();
        _esperaPista.Start();
    }

    // ─────────────────────────── reproducción ───────────────────────────

    private void Alternar()
    {
        if (_fuente == null) return;
        if (_pausado) { video.Play(); glifoPlay.Data = Geometry.Parse("M6,3 L6,17 M13,3 L13,17"); glifoPlay.Fill = null; }
        else { video.Pause(); glifoPlay.Data = Geometry.Parse("M6.5,3.5 L17,10 L6.5,16.5 Z"); glifoPlay.Fill = Brushes.White; }
        _pausado = !_pausado;
    }

    private void Saltar(double s)
    {
        if (_fuente == null) return;
        Buscar(video.Position.TotalSeconds + s);
    }

    // ─────────────────────────── salida ───────────────────────────

    private EncodeOptions Opciones() => OpcionesSalida.Construir(
        cboFmt.SelectedIndex, cboCodec.SelectedIndex, cboQ.SelectedIndex,
        cboRes.SelectedIndex, cboAud.SelectedIndex);

    /// <summary>
    /// La estimación es la MISMA que la de Comprimir, escalada a lo que se va a exportar: si
    /// de 20 minutos se guardan 10, sale la mitad. Nada de una segunda fórmula.
    /// </summary>
    private void RefrescarEstimacion()
    {
        btnExportar.IsEnabled = _fuente is { Probed: true } && _tramos.Count > 0 && _cancelar == null;
        if (_fuente is not { Probed: true } || _duracion <= 0 || _tramos.Count == 0)
        {
            lblEst.Text = "—";
            lblEstDet.Text = "Elige un vídeo para estimar el tamaño";
            return;
        }

        var est = Estimator.Compute(_fuente, Opciones());
        if (!est.Valid) { lblEst.Text = "—"; lblEstDet.Text = "No se puede estimar este vídeo"; return; }

        double parte = _tramos.Sum(t => t.Duracion) / _duracion;
        long bytes = (long)(est.EstBytes * parte);
        lblEst.Text = "≈ " + Humano(bytes);
        lblEstDet.Text = $"{_tramos.Count} fichero{(_tramos.Count == 1 ? "" : "s")} · " +
                         $"{TramoFila.Reloj(_tramos.Sum(t => t.Duracion))} de vídeo · " +
                         "estimación aproximada, el resultado real depende del contenido.";
    }

    private void ElegirDestino()
    {
        var d = new OpenFolderDialog
        {
            Title = "Dónde dejar los recortes",
            InitialDirectory = CarpetaDestino(),
        };
        if (d.ShowDialog() == true) { _destino = d.FolderName; MostrarDestino(); }
    }

    /// <summary>Por defecto, junto al original: es donde el usuario está mirando.</summary>
    private string CarpetaDestino() =>
        _destino ?? (_fuente != null ? Path.GetDirectoryName(_fuente.Path)! : "");

    private void MostrarDestino()
    {
        var c = CarpetaDestino();
        lblDestino.Text = c.Length == 0 ? ""
            : $"Se guardará en: {c}{(_destino == null ? "  (junto al original)" : "")}";
    }

    /// <summary>
    /// Con los tramos ya en disco, ofrece deshacerse del original — que es lo que casi
    /// siempre quieres tras partir un vídeo, y buscarlo a mano después es una lata.
    ///
    /// Va a la PAPELERA, no a borrado directo: la comprobación de que los tramos existen
    /// dice que los ficheros están, no que estén bien. Si al verlos algo falla, el original
    /// sigue ahí. Devuelve true solo si el fichero se ha ido de verdad.
    /// </summary>
    private bool OfrecerBorrarOriginal(string ruta, int cuantos, string destino)
    {
        if (!File.Exists(ruta)) return false;

        var fi = new FileInfo(ruta);
        var dueno = Window.GetWindow(this);
        if (!DialogWindow.Confirmar(dueno, "¿Borrar el original?",
                $"Los {cuantos} tramos están en:{Environment.NewLine}{destino}" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"«{fi.Name}» ({Humano(fi.Length)}) ya no hace falta." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                "Va a la papelera de reciclaje, así que se puede recuperar.",
                "Sí, a la papelera", "No, conservarlo"))
            return false;

        if (!RecycleBin.Send(ruta))
        {
            Log?.Invoke($"Recortes: no se pudo enviar «{fi.Name}» a la papelera; sigue donde estaba.");
            DialogWindow.Aviso(dueno, "Recortes",
                $"No se pudo borrar «{fi.Name}». Sigue en su sitio.");
            return false;
        }

        Log?.Invoke($"Recortes: «{fi.Name}» a la papelera tras exportar {cuantos} tramos.");
        lblProgreso.Text = $"Listo · {cuantos} tramos · original a la papelera";

        // Sin fichero no hay proyecto: dejar la línea de tiempo con los cortes de un vídeo
        // que ya no existe solo lleva a exportar de nuevo y no entender por qué falla.
        _fuente = null;
        _tramos.Clear();
        _duracion = 0;
        LiberarMiniaturas();
        lblVideo.Text = "";
        lblVideoDet.Text = "";
        lblSinVideo.Visibility = Visibility.Visible;
        _atras.Clear();
        _adelante.Clear();
        PintarPista();
        return true;
    }

    private async Task ExportarAsync()
    {
        // Reentrada: exportar tarda, y sin cerrojo cada clic lanza otra tanda entera sobre
        // los mismos ficheros. Pasó: cinco tandas solapadas y ni un fichero.
        if (_exportando || _fuente == null || _tramos.Count == 0) return;
        _exportando = true;

        var destino = CarpetaDestino();
        Directory.CreateDirectory(destino);

        // El reproductor de esta página tiene el fichero abierto, y el motor comprueba que
        // nadie lo tenga cogido para no pillar una descarga a medias: con el vídeo cargado
        // se saltaba el fichero entero y no salía nada. Se suelta antes de codificar.
        var rutaOriginal = _fuente.Path;   // en texto: Uri.LocalPath tropieza con '#' en el nombre
        var fuente = new Uri(_fuente.Path);
        video.Stop();
        video.Close();
        video.Source = null;

        _cancelar = new CancellationTokenSource();
        _pausadaExp = false;
        btnExportar.IsEnabled = false;
        btnCortar.IsEnabled = false;
        PintarExportando(true);
        // Las miniaturas se paran: leerían el mismo fichero que se está codificando, y con
        // uno recién bajado de la nube ese goteo de ffmpegs era parte de la lentitud.
        _esperaPrevia.Stop();
        _esperaPista.Stop();
        _colaFondo.Clear();
        var rep = new Reportero(this);
        var hechos = new List<string>();
        var fallidos = new List<string>();
        int n = 0;
        bool salioTodo = false;

        try
        {
            // Cinturón y tirantes: si el sistema volvió a dejar el vídeo solo en la nube
            // («liberar espacio») desde que se cargó, se baja ahora, con progreso visible.
            // Codificar leyendo de la red parece la app colgada.
            if (NubeLocal.EsMarcador(_fuente.Path))
            {
                var avance = new Progress<double>(pr =>
                    lblProgreso.Text = $"Descargando de la nube… {pr * 100:0} %");
                await NubeLocal.DescargarAsync(_fuente.Path, avance, _cancelar.Token);
            }

            foreach (var t in _tramos.ToList())
            {
                n++;
                _tramoActual = $"Tramo {n} de {_tramos.Count} · {t.Nombre}";
                lblProgreso.Text = _tramoActual;
                foreach (var f in _tramos) f.EnCurso = ReferenceEquals(f, t);
                var opt = Opciones();
                opt.Output = destino;
                opt.Desde = t.Inicio;
                opt.Duracion = t.Duracion;
                opt.NombreSalida = t.Nombre;
                // Force: el original puede estar ya en H.265 y aquí no se comprime por
                // comprimir — se está cortando, así que hay que procesarlo igual.
                opt.Force = true;

                // Task.Run: el motor es async, pero sus trozos SÍNCRONOS (abrir el fichero
                // para el candado, sondearlo, mirar el espacio…) corrían en el hilo de
                // interfaz — y sobre OneDrive cada uno es un viaje de red. Era la razón de
                // que la ventana fuera a tirones al exportar con la CPU libre.
                var tk = _cancelar.Token;
                var salidos = await Task.Run(
                    () => _engine.CompressAsync(new[] { _fuente.Path }, opt, rep, tk), tk);

                // El nombre real lo dice EL MOTOR, no se recalcula: si la carpeta ya tenía
                // un fichero igual (de un intento anterior), el motor saca la salida con
                // sufijo y el nombre recalculado no existía — contaba «1 de 2 sin salir»
                // con los dos tramos en el disco. Y se sigue comprobando en disco, porque
                // el motor puede saltarse un fichero y devolver lista vacía.
                var salida = salidos.FirstOrDefault();
                if (salida is { Ok: true } && File.Exists(salida.OutputPath))
                    hechos.Add(Path.GetFileName(salida.OutputPath));
                else fallidos.Add(t.Nombre);
            }

            // Solo cuenta como completo si TODOS los tramos están en disco. Es la condición
            // que habilita ofrecer el borrado del original: con un tramo sin salir, borrarlo
            // perdería ese trozo para siempre.
            salioTodo = fallidos.Count == 0 && hechos.Count == _tramos.Count && hechos.Count > 0;

            lblProgreso.Text = fallidos.Count == 0
                ? $"Listo · {hechos.Count} fichero{(hechos.Count == 1 ? "" : "s")}"
                : $"{hechos.Count} de {_tramos.Count} · {fallidos.Count} sin salir";
            Log?.Invoke(fallidos.Count == 0
                ? $"Recortes: {hechos.Count} ficheros creados en {destino}"
                : $"Recortes: NO salieron {fallidos.Count} de {_tramos.Count} tramos " +
                  $"({string.Join(", ", fallidos)}) — el motor dice el porqué en las líneas de arriba.");
        }
        catch (OperationCanceledException) { lblProgreso.Text = "Cancelado"; }
        catch (Exception ex)
        {
            lblProgreso.Text = "Error";
            Log?.Invoke($"Recortes: {ex.Message}");
        }
        finally
        {
            _cancelar = null;
            _exportando = false;
            _pausadaExp = false;
            _tramoActual = "";
            PintarExportando(false);
            foreach (var f in _tramos) f.EnCurso = false;

            // La pregunta va ANTES de reabrir el vídeo: el reproductor mantiene el fichero
            // cogido y con él abierto el borrado falla en silencio.
            bool borrado = salioTodo && OfrecerBorrarOriginal(rutaOriginal, hechos.Count, destino);

            if (!borrado)
            {
                video.Source = fuente;      // vuelve la previsualización
                video.Play();
                video.Pause();
            }
            btnCortar.IsEnabled = true;
            RefrescarEstimacion();
        }
    }

    private static string Humano(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):0.##} GB"
        : b >= 1L << 20 ? $"{b / (double)(1L << 20):0.#} MB"
        : $"{b / 1024.0:0} KB";

    /// <summary>Puente entre el motor y la barra de estado de esta página.</summary>
    private sealed class Reportero : IEngineReporter
    {
        private readonly RecortesView _v;
        public Reportero(RecortesView v) => _v = v;
        // BeginInvoke y no Invoke, como en Comprimir: Invoke BLOQUEA al hilo que lee la
        // salida de ffmpeg y encola por encima del dibujado y de la entrada de teclado.
        public void Log(string linea) => _v.Dispatcher.BeginInvoke(() => _v.Log?.Invoke(linea));
        public void FileStart(int i, int total, string nombre, double dur) { }
        // El porcentaje se cuelga del rótulo guardado del tramo, no de recomponer el texto
        // de la etiqueta: con un nombre que llevara « · » se comía media frase.
        public void FileProgress(double fraccion, string cruda) =>
            _v.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                () => _v.lblProgreso.Text = $"{_v._tramoActual} · {fraccion * 100:0} %");
        public void FileDone(FileResult r) { }
        public void FileSkipped(string ruta, string porque) =>
            _v.Dispatcher.BeginInvoke(() =>
            {
                _v.lblProgreso.Text = $"{_v._tramoActual} · SALTADO: {porque}";
                _v.Log?.Invoke($"Recortes: el motor se saltó este tramo — {porque}");
            });
    }
}
