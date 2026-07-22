using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ShrinkVideo;

/// <summary>
/// Reproductor integrado, en modo focus: una ventana modal oscura para contestar «¿qué
/// capítulo es?» sin salir de la app.
///
/// Usa MediaElement (los códecs del sistema): si el fichero no se puede decodificar, se dice
/// claro y se ofrece el reproductor del sistema — mejor un plan B honesto que una pantalla
/// negra sin explicación.
/// </summary>
public partial class ReproductorWindow : Window
{
    private readonly string _ruta;
    private readonly DispatcherTimer _reloj;
    private bool _pausado;
    private bool _arrastrando;

    public ReproductorWindow(string ruta)
    {
        InitializeComponent();
        _ruta = ruta;
        lblTitulo.Text = Path.GetFileName(ruta);

        btnCerrar.Click += (_, _) => Close();
        btnPlay.Click += (_, _) => Alternar();
        btnSistema.Click += (_, _) => { AbrirEnSistema(); Close(); };

        // Espacio = pausa, Esc = cerrar: los dos gestos universales de un reproductor
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space) { Alternar(); e.Handled = true; }
            else if (e.Key == Key.Escape) Close();
        };

        volumen.ValueChanged += (_, _) => video.Volume = volumen.Value;

        video.MediaOpened += (_, _) =>
        {
            if (video.NaturalDuration.HasTimeSpan)
                barra.Maximum = video.NaturalDuration.TimeSpan.TotalSeconds;
        };
        video.MediaEnded += (_, _) => { _pausado = true; glifoPlay.Text = "▶"; };
        video.MediaFailed += (_, e) =>
        {
            lblFallo.Text = "Este vídeo no se puede reproducir aquí (códec no soportado por " +
                            $"el decodificador del sistema): {e.ErrorException?.Message}";
            panelFallo.Visibility = Visibility.Visible;
        };

        // Arrastrar la barra busca en el vídeo; mientras se arrastra, el reloj no la pisa
        barra.PreviewMouseLeftButtonDown += (_, _) => _arrastrando = true;
        barra.PreviewMouseLeftButtonUp += (_, _) =>
        {
            video.Position = TimeSpan.FromSeconds(barra.Value);
            _arrastrando = false;
        };

        _reloj = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _reloj.Tick += (_, _) =>
        {
            if (_arrastrando || !video.NaturalDuration.HasTimeSpan) return;
            barra.Value = video.Position.TotalSeconds;
            lblTiempo.Text = $"{Fmt(video.Position)} / {Fmt(video.NaturalDuration.TimeSpan)}";
        };

        Loaded += (_, _) =>
        {
            try
            {
                video.Source = new Uri(ruta);
                video.Volume = volumen.Value;
                video.Play();
                _reloj.Start();
            }
            catch (Exception ex)
            {
                lblFallo.Text = $"No se pudo abrir el vídeo: {ex.Message}";
                panelFallo.Visibility = Visibility.Visible;
            }
        };
        Closed += (_, _) => { _reloj.Stop(); video.Close(); };
    }

    private void Alternar()
    {
        if (_pausado) { video.Play(); glifoPlay.Text = "⏸"; }
        else { video.Pause(); glifoPlay.Text = "▶"; }
        _pausado = !_pausado;
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
