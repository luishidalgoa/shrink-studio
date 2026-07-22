using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private bool _desdeReloj;
    private CancellationTokenSource? _cancelar;
    private string _tramoActual = "";
    private string? _destino;      // null = junto al vídeo original
    private bool _exportando;
    private string? _tempMiniaturas;
    // Fotogramas ya sacados, por segundo redondeado al hueco. Es la unica cache que hay y
    // se vacia entera al cambiar de video o al salir de la pagina.
    private readonly Dictionary<int, BitmapImage> _previas = new();
    private readonly DispatcherTimer _esperaPrevia;
    private int _previaPedida = -1;
    private bool _sacandoPrevia;

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
        btnDestino.Click += (_, _) => ElegirDestino();

        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } f) Cargar(f[0]);
        };

        PreviewKeyDown += (_, e) =>
        {
            if (_fuente == null) return;
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

        barra.ValueChanged += (_, e) =>
        {
            if (_desdeReloj || _fuente == null) return;
            video.Position = TimeSpan.FromSeconds(e.NewValue);
            lblPos.Text = TramoFila.Reloj(e.NewValue);
        };

        video.MediaOpened += (_, _) =>
        {
            if (!video.NaturalDuration.HasTimeSpan) return;
            _duracion = video.NaturalDuration.TimeSpan.TotalSeconds;
            barra.Maximum = _duracion;
            lblDur.Text = TramoFila.Reloj(_duracion);
            Rehacer(Tramos.Entero(_duracion));
        };
        video.MediaFailed += (_, _) =>
            lblSinVideo.Text = "Este vídeo no se puede previsualizar aquí, pero sí cortarlo.";

        _reloj = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _reloj.Tick += (_, _) =>
        {
            if (_fuente == null) return;
            _desdeReloj = true;
            barra.Value = video.Position.TotalSeconds;
            _desdeReloj = false;
            lblPos.Text = TramoFila.Reloj(video.Position.TotalSeconds);
        };
        _reloj.Start();

        // Rebote: arrastrando se disparan decenas de posiciones por segundo y sacar un
        // fotograma cuesta ~200 ms. Se pide el ultimo, no todos.
        _esperaPrevia = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _esperaPrevia.Tick += async (_, _) => { _esperaPrevia.Stop(); await SacarPreviaAsync(); };

        barra.MouseMove += AlPasarPorLaBarra;
        barra.MouseLeave += (_, _) => globoPrevia.Visibility = Visibility.Collapsed;

        SizeChanged += (_, _) => PintarRegla();
        // Al salir de la página no se deja nada en memoria ni en disco temporal.
        Unloaded += (_, _) => LiberarMiniaturas();
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
    public async void Cargar(string ruta)
    {
        if (!File.Exists(ruta)) return;

        var fi = new FileInfo(ruta);
        _fuente = new VideoRow { Path = ruta, Bytes = fi.Length };
        lblVideo.Text = fi.Name;
        lblVideoDet.Text = "Analizando…";
        lblSinVideo.Visibility = Visibility.Collapsed;
        _tramos.Clear();
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

        if (_duracion <= 0 && info.DurationSec > 0)
        {
            _duracion = info.DurationSec;
            barra.Maximum = _duracion;
            lblDur.Text = TramoFila.Reloj(_duracion);
            Rehacer(Tramos.Entero(_duracion));
        }

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
        barra.IsEnabled = !si;
        foreach (var b in new[] { btnPlay, btnAtras, btnAdelante }) b.IsEnabled = !si;
    }

    /// <summary>Cada cuántos segundos se guarda un fotograma. Más fino sería más lento.</summary>
    private const int HuecoPrevia = 5;

    /// <summary>
    /// El globo sigue al cursor por encima de la barra y enseña el fotograma de ese punto.
    /// La posición se saca del ratón y no del valor del control: así funciona igual pasando
    /// por encima que arrastrando, y no obliga a mover el vídeo para mirar.
    /// </summary>
    private void AlPasarPorLaBarra(object remitente, MouseEventArgs e)
    {
        if (_fuente == null || _duracion <= 0) { globoPrevia.Visibility = Visibility.Collapsed; return; }

        double x = Math.Clamp(e.GetPosition(barra).X, 0, barra.ActualWidth);
        double seg = x / Math.Max(1, barra.ActualWidth) * _duracion;

        globoPrevia.Visibility = Visibility.Visible;

        // Centrado con el ancho REAL, y sujeto a los bordes de la barra: un globo medio
        // salido de la ventana no se lee.
        double ancho = globoPrevia.ActualWidth > 0 ? globoPrevia.ActualWidth : 200;
        double alto = globoPrevia.ActualHeight > 0 ? globoPrevia.ActualHeight : 132;
        Canvas.SetLeft(globoPrevia, Math.Clamp(x - ancho / 2, 0, Math.Max(0, barra.ActualWidth - ancho)));
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

    /// <summary>
    /// Saca el fotograma pedido. De uno en uno: encadenar ffmpeg por cada píxel del arrastre
    /// solo consigue una cola que llega tarde. El jpg se carga ENTERO en memoria y se borra
    /// al momento, así que en disco no queda nada.
    /// </summary>
    private async Task SacarPreviaAsync()
    {
        if (_sacandoPrevia || _fuente == null) return;
        int hueco = _previaPedida;
        if (hueco < 0 || _previas.ContainsKey(hueco)) return;

        _sacandoPrevia = true;
        try
        {
            _tempMiniaturas ??= Path.Combine(Path.GetTempPath(),
                "shrinkstudio-previa-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempMiniaturas);
            var jpg = Path.Combine(_tempMiniaturas, $"{hueco}.jpg");

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
                }
                catch { /* un fotograma que no sale no rompe nada */ }
                finally { try { File.Delete(jpg); } catch { } }
            }
        }
        finally { _sacandoPrevia = false; }

        // Mientras se sacaba ese, el cursor ya estará en otro sitio: se atiende el último.
        if (_previaPedida != hueco && !_previas.ContainsKey(_previaPedida)) _esperaPrevia.Start();
    }

    /// <summary>Suelta los fotogramas y borra lo que quedara en disco. Nada se acumula.</summary>
    private void LiberarMiniaturas()
    {
        globoPrevia.Visibility = Visibility.Collapsed;
        imgPrevia.Source = null;
        _previas.Clear();
        _previaPedida = -1;
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
    private void Rehacer(IReadOnlyList<Tramo> nuevos)
    {
        var escritos = _tramos.ToDictionary(t => (t.Inicio, t.Fin), t => t.Nombre);
        var baseNombre = _fuente != null
            ? Path.GetFileNameWithoutExtension(_fuente.Path) : "recorte";
        var sugeridos = Tramos.Nombrar(baseNombre, nuevos.Count);

        _tramos.Clear();
        for (int i = 0; i < nuevos.Count; i++)
        {
            var t = nuevos[i];
            escritos.TryGetValue((t.Inicio, t.Fin), out var previo);
            _tramos.Add(new TramoFila
            {
                Inicio = t.Inicio,
                Fin = t.Fin,
                Numero = i + 1,
                Nombre = string.IsNullOrWhiteSpace(previo) ? sugeridos[i] : previo,
            });
        }
        btnExportar.Content = _tramos.Count switch
        {
            0 => "Exportar",
            1 => "Exportar 1 tramo",
            _ => $"Exportar {_tramos.Count} tramos",
        };
        lblAyudaTramos.Text = _tramos.Count == 1
            ? "Un solo tramo: se exportará el vídeo entero. Corta para partirlo."
            : $"{_tramos.Count} tramos = {_tramos.Count} ficheros.";
        PintarRegla();
        RefrescarEstimacion();
    }

    /// <summary>Las juntas entre tramos, dibujadas justo encima de la barra de posición.</summary>
    private void PintarRegla()
    {
        regla.Children.Clear();
        if (_duracion <= 0 || regla.ActualWidth <= 0) return;

        foreach (var t in _tramos)
        {
            double x = t.Inicio / _duracion * regla.ActualWidth;
            double w = Math.Max(1, t.Duracion / _duracion * regla.ActualWidth - 2);
            var barrita = new Rectangle
            {
                Width = w, Height = 4, RadiusX = 2, RadiusY = 2,
                Fill = (Brush)FindResource("Accent600"),
            };
            Canvas.SetLeft(barrita, x + 1);
            Canvas.SetTop(barrita, 3);
            regla.Children.Add(barrita);
        }
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
        var destino = Math.Clamp(video.Position.TotalSeconds + s, 0, _duracion);
        video.Position = TimeSpan.FromSeconds(destino);
        _desdeReloj = true;
        barra.Value = destino;
        _desdeReloj = false;
        lblPos.Text = TramoFila.Reloj(destino);
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
        var fuente = new Uri(_fuente.Path);
        video.Stop();
        video.Close();
        video.Source = null;

        _cancelar = new CancellationTokenSource();
        btnExportar.IsEnabled = false;
        btnCortar.IsEnabled = false;
        var rep = new Reportero(this);
        var hechos = new List<string>();
        var fallidos = new List<string>();
        int n = 0;

        try
        {
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

                await _engine.CompressAsync(new[] { _fuente.Path }, opt, rep, _cancelar.Token);

                // Se comprueba el fichero EN DISCO, no que la llamada volviera: el motor
                // puede saltarse un fichero y devolver lista vacía. Dar eso por bueno fue
                // justo el fallo — decía «2 ficheros creados» sin haber creado ninguno.
                var esperado = Path.Combine(destino,
                    LibraryTemplate.LimpiarNombre(t.Nombre) + Engine.OutputExtension(opt));
                if (File.Exists(esperado)) hechos.Add(Path.GetFileName(esperado));
                else fallidos.Add(t.Nombre);
            }

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
            _tramoActual = "";
            foreach (var f in _tramos) f.EnCurso = false;
            video.Source = fuente;      // vuelve la previsualización
            video.Play();
            video.Pause();
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
        public void Log(string linea) => _v.Dispatcher.Invoke(() => _v.Log?.Invoke(linea));
        public void FileStart(int i, int total, string nombre, double dur) { }
        // El porcentaje se cuelga del rótulo guardado del tramo, no de recomponer el texto
        // de la etiqueta: con un nombre que llevara « · » se comía media frase.
        public void FileProgress(double fraccion, string cruda) =>
            _v.Dispatcher.Invoke(() =>
                _v.lblProgreso.Text = $"{_v._tramoActual} · {fraccion * 100:0} %");
        public void FileDone(FileResult r) { }
        public void FileSkipped(string ruta, string porque) =>
            _v.Dispatcher.Invoke(() =>
            {
                _v.lblProgreso.Text = $"{_v._tramoActual} · SALTADO: {porque}";
                _v.Log?.Invoke($"Recortes: el motor se saltó este tramo — {porque}");
            });
    }
}
