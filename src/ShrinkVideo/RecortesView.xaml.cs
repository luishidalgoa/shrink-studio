using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        SizeChanged += (_, _) => PintarRegla();
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

        video.Source = new Uri(ruta);
        video.Play();
        video.Pause();          // primer fotograma a la vista, sin arrancar la reproducción
        _pausado = true;

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
        btnCortar.IsEnabled = true;
        RefrescarEstimacion();
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

    private async Task ExportarAsync()
    {
        if (_fuente == null || _tramos.Count == 0) return;

        var destino = Path.Combine(Path.GetDirectoryName(_fuente.Path)!, "recortes");
        Directory.CreateDirectory(destino);

        _cancelar = new CancellationTokenSource();
        btnExportar.IsEnabled = false;
        var rep = new Reportero(this);
        int n = 0;

        try
        {
            foreach (var t in _tramos.ToList())
            {
                n++;
                lblProgreso.Text = $"Tramo {n} de {_tramos.Count}…";
                var opt = Opciones();
                opt.Output = destino;
                opt.Desde = t.Inicio;
                opt.Duracion = t.Duracion;
                opt.NombreSalida = t.Nombre;
                // Force: el original puede estar ya en H.265 y aquí no se comprime por
                // comprimir — se está cortando, así que hay que procesarlo igual.
                opt.Force = true;

                await _engine.CompressAsync(new[] { _fuente.Path }, opt, rep, _cancelar.Token);
            }
            lblProgreso.Text = $"Listo · {_tramos.Count} ficheros en «recortes»";
            Log?.Invoke($"Recortes: {_tramos.Count} ficheros creados en {destino}");
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
        public void FileProgress(double fraccion, string cruda) =>
            _v.Dispatcher.Invoke(() =>
            {
                var t = _v.lblProgreso.Text;
                var corte = t.IndexOf(" · ", StringComparison.Ordinal);
                if (corte > 0) t = t[..corte];
                _v.lblProgreso.Text = $"{t} · {fraccion * 100:0}%";
            });
        public void FileDone(FileResult r) { }
    }
}
