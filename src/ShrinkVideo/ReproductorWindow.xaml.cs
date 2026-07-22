using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ShrinkVideo;

/// <summary>
/// Reproductor integrado, en modo focus: una ventana oscura para contestar «¿qué capítulo
/// es?» sin salir de la app.
///
/// Los controles flotan sobre el vídeo y se apartan solos cuando no se usan — la imagen es
/// lo que importa. Usa MediaElement (los códecs del sistema): si el fichero no se puede
/// decodificar, se dice claro y se ofrece el reproductor del sistema.
/// </summary>
public partial class ReproductorWindow : Window
{
    private readonly string _ruta;
    private readonly DispatcherTimer _reloj;
    private readonly DispatcherTimer _apagon;   // esconde los controles tras un rato quieto
    private bool _pausado;
    private bool _arrastrando;
    private bool _reanudarTrasArrastre;
    private bool _abierto;
    private bool _desdeReloj;                   // el reloj mueve la barra: eso no es buscar
    private bool _visible = true;
    private bool _mudo;
    private readonly bool _eraMarcador;   // ¿estaba solo en la nube antes de abrirlo?
    private readonly CirculosCargando _cargando = new();

    // ── la previa de la barra ── mismo patrón que Recortes: hueco de 5 s, caché y rebote
    private const int HuecoPrevia = 5;
    private readonly Dictionary<int, BitmapImage> _previas = new();
    private readonly DispatcherTimer _esperaPrevia;
    private int _previaPedida = -1;
    private bool _sacandoPrevia;
    private string? _tempPrevias;
    private double _volumenPrevio = 0.8;
    private Point _ultimoRaton;
    private int _tics;

    private static readonly Geometry GlifoPausa = Geometry.Parse("M6,3 L6,17 M13,3 L13,17");
    private static readonly Geometry GlifoPlay = Geometry.Parse("M6.5,3.5 L17,10 L6.5,16.5 Z");
    private static readonly Geometry GlifoAltavoz =
        Geometry.Parse("M4,7.5 L7,7.5 L11,4 L11,16 L7,12.5 L4,12.5 Z M13.5,7.5 A 3.5,3.5 0 0 1 13.5,12.5");
    private static readonly Geometry GlifoMudo =
        Geometry.Parse("M4,7.5 L7,7.5 L11,4 L11,16 L7,12.5 L4,12.5 Z M13,8 L17,12 M17,8 L13,12");
    private static readonly Geometry GlifoExpandir =
        Geometry.Parse("M3,7.5 L3,3 L7.5,3 M12.5,3 L17,3 L17,7.5 M17,12.5 L17,17 L12.5,17 M7.5,17 L3,17 L3,12.5");
    private static readonly Geometry GlifoContraer =
        Geometry.Parse("M7.5,3 L7.5,7.5 L3,7.5 M17,7.5 L12.5,7.5 L12.5,3 M12.5,17 L12.5,12.5 L17,12.5 M3,12.5 L7.5,12.5 L7.5,17");

    public ReproductorWindow(string ruta)
    {
        InitializeComponent();
        _ruta = ruta;
        lblTitulo.Text = Path.GetFileName(ruta);

        // Se anota ANTES de tocar el fichero: reproducirlo lo descarga, y después ya no
        // habría forma de saber si estaba en el disco porque el usuario quiso.
        _eraMarcador = Reindex.Nube.EsMarcador(ruta);

        // El vídeo se escala a la ventana; para una vista de identificación no hace falta
        // el remuestreo caro. Es la diferencia entre un escalado por hardware y uno fino.
        RenderOptions.SetBitmapScalingMode(video, BitmapScalingMode.LowQuality);
        glifoVol.Fill = null;

        huecoCargando.Children.Add(_cargando.Raiz);
        _cargando.Arrancar(_eraMarcador
            ? "Descargando el vídeo de la nube…"   // puede tardar: baja el fichero entero
            : "Abriendo el vídeo…");

        btnCerrar.Click += (_, _) => Close();
        btnPlay.Click += (_, _) => Alternar();
        btnAtras.Click += (_, _) => Saltar(-10);
        btnAdelante.Click += (_, _) => Saltar(10);
        btnPantalla.Click += (_, _) => AlternarPantalla();
        btnMudo.Click += (_, _) => AlternarMudo();
        btnSistema.Click += (_, _) => { AbrirEnSistema(); Close(); };

        lienzo.MouseLeftButtonUp += (_, e) => { if (e.ClickCount == 1) Alternar(); };
        lienzo.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) AlternarPantalla(); };

        PreviewKeyDown += AlPulsarTecla;
        raiz.MouseMove += AlMoverRaton;

        volumen.ValueChanged += (_, e) =>
        {
            video.Volume = e.NewValue;
            _mudo = e.NewValue <= 0.001;
            glifoVol.Data = _mudo ? GlifoMudo : GlifoAltavoz;
        };

        // ── Buscar ──
        // Mientras se arrastra: pausa + ScrubbingEnabled, que es justo para lo que existe
        // (refresca el fotograma con el vídeo parado). Fuera del arrastre va apagado: con
        // él encendido, cada cambio de posición obliga a decodificar de más.
        barra.ValueChanged += (_, e) =>
        {
            if (_desdeReloj || !_abierto) return;
            video.Position = TimeSpan.FromSeconds(e.NewValue);
            lblPos.Text = Fmt(TimeSpan.FromSeconds(e.NewValue));
        };
        barra.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) =>
        {
            _arrastrando = true;
            _reanudarTrasArrastre = !_pausado;
            video.ScrubbingEnabled = true;
            video.Pause();
        }));
        barra.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            _arrastrando = false;
            video.ScrubbingEnabled = false;
            if (_reanudarTrasArrastre) video.Play();
        }));

        video.MediaOpened += (_, _) =>
        {
            _abierto = true;
            // Con un fichero que se está descargando, el Play() del arranque llega cuando
            // todavía no hay nada que reproducir y se queda en nada. Se repite aquí, que es
            // cuando el vídeo ya está de verdad: si no, acababa cargado pero parado.
            if (!_pausado) { video.Play(); glifoPlay.Data = GlifoPausa; }
            if (video.NaturalDuration.HasTimeSpan)
            {
                barra.Maximum = video.NaturalDuration.TimeSpan.TotalSeconds;
                lblDur.Text = Fmt(video.NaturalDuration.TimeSpan);
            }
        };
        barra.MouseMove += AlPasarPorLaBarra;
        barra.MouseLeave += (_, _) => globoPrevia.Visibility = Visibility.Collapsed;

        _esperaPrevia = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _esperaPrevia.Tick += async (_, _) => { _esperaPrevia.Stop(); await SacarPreviaAsync(); };

        video.MediaEnded += (_, _) => { _pausado = true; glifoPlay.Data = GlifoPlay; Mostrar(); };
        video.MediaFailed += (_, e) =>
        {
            lblFallo.Text = "Este vídeo no se puede reproducir aquí (códec no soportado por " +
                            $"el decodificador del sistema): {e.ErrorException?.Message}";
            panelFallo.Visibility = Visibility.Visible;
            _cargando.Parar();
        };
        // El péndulo se queda hasta que el vídeo AVANZA de verdad, no cuando dice estar
        // abierto: con un fichero descargándose, MediaOpened llega mucho antes que el primer
        // fotograma, y quitarlo ahí dejaba el rectángulo negro sin explicación.
        video.BufferingStarted += (_, _) => { Chip("Cargando…"); _cargando.Arrancar(); };
        video.BufferingEnded += (_, _) => RevisarNube();

        _reloj = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _reloj.Tick += (_, _) =>
        {
            if (video.Position > TimeSpan.Zero) _cargando.Parar();
            if (_arrastrando || !video.NaturalDuration.HasTimeSpan) return;
            _desdeReloj = true;
            barra.Value = video.Position.TotalSeconds;
            _desdeReloj = false;
            lblPos.Text = Fmt(video.Position);
            if (++_tics % 25 == 0) RevisarNube();   // ¿ya terminó de bajar de la nube?
        };

        _apagon = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _apagon.Tick += (_, _) => { if (!_pausado && !_arrastrando && !RatonEnControles()) Ocultar(); };

        Loaded += (_, _) =>
        {
            RevisarNube();
            try
            {
                video.Source = new Uri(ruta);
                video.Volume = volumen.Value;
                video.Play();
                _reloj.Start();
                _apagon.Start();
            }
            catch (Exception ex)
            {
                lblFallo.Text = $"No se pudo abrir el vídeo: {ex.Message}";
                panelFallo.Visibility = Visibility.Visible;
                _cargando.Parar();
            }
        };
        Closed += (_, _) =>
        {
            _reloj.Stop();
            _apagon.Stop();
            _cargando.Parar();
            _esperaPrevia.Stop();
            video.Close();
            BorrarPrevias();

            // Si estaba solo en la nube, se devuelve a la nube: verlo para identificarlo no
            // es motivo para quedarse 250 MB en el disco. Va después de Close() porque
            // mientras el reproductor tenga el fichero abierto no se puede liberar.
            if (_eraMarcador) NubeLocal.Liberar(_ruta);
        };
    }

    // ─────────────────────────── reproducción ───────────────────────────

    private void Alternar()
    {
        if (_pausado) { video.Play(); glifoPlay.Data = GlifoPausa; }
        else { video.Pause(); glifoPlay.Data = GlifoPlay; }
        _pausado = !_pausado;
        Mostrar();
    }

    private void Saltar(double segundos)
    {
        if (!_abierto) return;
        var destino = video.Position.TotalSeconds + segundos;
        var tope = video.NaturalDuration.HasTimeSpan ? video.NaturalDuration.TimeSpan.TotalSeconds : destino;
        video.Position = TimeSpan.FromSeconds(Math.Clamp(destino, 0, tope));
        _desdeReloj = true;
        barra.Value = video.Position.TotalSeconds;
        _desdeReloj = false;
        lblPos.Text = Fmt(video.Position);
        Mostrar();
    }

    private void AlternarMudo()
    {
        if (_mudo) { volumen.Value = _volumenPrevio > 0.02 ? _volumenPrevio : 0.8; }
        else { _volumenPrevio = volumen.Value; volumen.Value = 0; }
    }

    private void AlternarPantalla()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        glifoPantalla.Data = WindowState == WindowState.Maximized ? GlifoContraer : GlifoExpandir;
        Mostrar();
    }

    private void AlPulsarTecla(object remitente, KeyEventArgs e)
    {
        var salto = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 30 : 5;
        switch (e.Key)
        {
            case Key.Space: Alternar(); break;
            case Key.Left: Saltar(-salto); break;
            case Key.Right: Saltar(salto); break;
            case Key.Up: volumen.Value = Math.Min(1, volumen.Value + 0.05); Mostrar(); break;
            case Key.Down: volumen.Value = Math.Max(0, volumen.Value - 0.05); Mostrar(); break;
            case Key.F: AlternarPantalla(); break;
            case Key.M: AlternarMudo(); Mostrar(); break;
            case Key.Escape:
                if (WindowState == WindowState.Maximized) AlternarPantalla(); else Close();
                break;
            default: return;
        }
        e.Handled = true;
    }

    // ─────────────────────────── controles que se apartan ───────────────────────────

    private void AlMoverRaton(object remitente, MouseEventArgs e)
    {
        // WPF dispara MouseMove también cuando el ratón no se ha movido (al repintar bajo
        // el cursor). Sin este filtro los controles nunca llegarían a esconderse.
        var p = e.GetPosition(raiz);
        if ((p - _ultimoRaton).Length < 2) return;
        _ultimoRaton = p;
        Mostrar();
    }

    private bool RatonEnControles() =>
        capaInferior.IsMouseOver || capaSuperior.IsMouseOver;

    private void Mostrar()
    {
        _apagon.Stop();
        _apagon.Start();
        if (_visible) return;
        _visible = true;
        raiz.Cursor = Cursors.Arrow;
        Fundir(capaSuperior, 1);
        Fundir(capaInferior, 1);
    }

    private void Ocultar()
    {
        if (!_visible || panelFallo.Visibility == Visibility.Visible) return;
        _visible = false;
        raiz.Cursor = Cursors.None;
        Fundir(capaSuperior, 0);
        Fundir(capaInferior, 0);
    }

    private static void Fundir(UIElement capa, double destino)
    {
        capa.IsHitTestVisible = destino > 0;
        capa.BeginAnimation(OpacityProperty,
            new DoubleAnimation(destino, TimeSpan.FromMilliseconds(180)));
    }

    // ─────────────────────────── ficheros en la nube ───────────────────────────

    /// <summary>
    /// Con la sincronización «bajo demanda» el vídeo puede no estar en el disco: se
    /// descarga mientras se reproduce. Decirlo evita que parezca que el reproductor está
    /// roto. Da igual el proveedor — se miran los atributos, no quién los puso.
    /// </summary>
    private void RevisarNube()
    {
        {
            Chip(Reindex.Nube.EsMarcador(_ruta) ? "En la nube · descargando para verlo" : null);
        }
    }

    private void Chip(string? texto)
    {
        lblNube.Text = texto ?? "";
        chipNube.Visibility = texto == null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ─────────────────────────── varios ───────────────────────────

    // ─────────────────────────── la previa de la barra ───────────────────────────

    /// <summary>
    /// El fotograma de por dónde va el ratón, en un globo sobre la barra. La posición sale
    /// del ratón y no del valor del control: así funciona igual pasando por encima que
    /// arrastrando el punto.
    /// </summary>
    private void AlPasarPorLaBarra(object remitente, MouseEventArgs e)
    {
        if (!_abierto || barra.Maximum <= 0) { globoPrevia.Visibility = Visibility.Collapsed; return; }

        double x = Math.Clamp(e.GetPosition(barra).X, 0, barra.ActualWidth);
        double seg = x / Math.Max(1, barra.ActualWidth) * barra.Maximum;

        globoPrevia.Visibility = Visibility.Visible;
        // Centrado con el ancho REAL y sujeto a los bordes: un globo medio salido de la
        // ventana no se lee.
        double ancho = globoPrevia.ActualWidth > 0 ? globoPrevia.ActualWidth : 200;
        double alto = globoPrevia.ActualHeight > 0 ? globoPrevia.ActualHeight : 122;
        Canvas.SetLeft(globoPrevia, Math.Clamp(x - ancho / 2, 0, Math.Max(0, barra.ActualWidth - ancho)));
        Canvas.SetTop(globoPrevia, -alto - 10);
        lblPrevia.Text = Fmt(TimeSpan.FromSeconds(seg));

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

    /// <summary>
    /// Saca UN fotograma, el último que se pidió. Los de en medio se descartan a propósito:
    /// recorriendo la barra se piden decenas por segundo, y sacarlos todos dejaría la previa
    /// siempre por detrás del ratón.
    /// </summary>
    private async Task SacarPreviaAsync()
    {
        if (_sacandoPrevia || _previaPedida < 0) return;

        // Un fichero que aún está solo en la nube no se abre para sacarle un fotograma: eso
        // lo descargaría entero. Cuando termine de bajar, las previas salen solas.
        if (Reindex.Nube.EsMarcador(_ruta)) { lblPreviaCargando.Text = "en la nube"; return; }

        int hueco = _previaPedida;
        _sacandoPrevia = true;
        try
        {
            var jpg = Path.Combine(CarpetaDePrevias(), $"{hueco}.jpg");
            if (await Engine.MakeThumbnailAsync(_ruta, jpg, hueco))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // se lee entera; el fichero queda libre
                bmp.DecodePixelWidth = 240;
                bmp.UriSource = new Uri(jpg);
                bmp.EndInit();
                bmp.Freeze();
                _previas[hueco] = bmp;
                try { File.Delete(jpg); } catch { }

                if (globoPrevia.Visibility == Visibility.Visible && _previaPedida == hueco)
                {
                    imgPrevia.Source = bmp;
                    lblPreviaCargando.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch { /* sin ffmpeg o fichero ilegible: la previa simplemente no sale */ }
        finally
        {
            _sacandoPrevia = false;
            // Mientras se sacaba esa, el ratón puede haberse ido a otro sitio.
            if (_previaPedida != hueco) { _esperaPrevia.Stop(); _esperaPrevia.Start(); }
        }
    }

    private string CarpetaDePrevias()
    {
        if (_tempPrevias != null) return _tempPrevias;
        _tempPrevias = Path.Combine(Path.GetTempPath(),
            $"shrinkstudio-repro-{Environment.ProcessId}-{Environment.TickCount}");
        Directory.CreateDirectory(_tempPrevias);
        return _tempPrevias;
    }

    /// <summary>Al cerrar: fuera los fotogramas de memoria y su carpeta del temporal.</summary>
    private void BorrarPrevias()
    {
        _previas.Clear();
        imgPrevia.Source = null;
        if (_tempPrevias == null) return;
        try { Directory.Delete(_tempPrevias, recursive: true); } catch { }
        _tempPrevias = null;
    }

    private void AbrirEnSistema()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_ruta) { UseShellExecute = true });
        }
        catch { /* sin reproductor asociado: no hay más que hacer */ }
    }

    private static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}
