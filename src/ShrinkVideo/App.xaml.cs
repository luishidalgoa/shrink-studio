using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ShrinkVideo;

public partial class App : Application
{
    // El instalador usa este mutex (AppMutex) para cerrar la app al actualizar.
    // Además nos sirve para detectar que ya hay una instancia abierta.
    private const string MutexName = "ShrinkVideoSingleInstanceMutex";

    // Canal por el que las instancias nuevas le pasan archivos a la que ya corre.
    private static string PipeName => "ShrinkStudio.Abrir." + Environment.UserName;

    private static Mutex? _mutex;

    /// <summary>
    /// ¿Este texto se está viendo cortado? Se mide con FormattedText, que es solo lectura:
    /// la alternativa habitual —volver a llamar a Measure()— invalida el arreglo del control
    /// y en una tabla virtualizada eso significa recalcular filas cada vez que pasas el ratón.
    /// </summary>
    private static bool EstaRecortado(TextBlock tb)
    {
        if (tb.TextTrimming == TextTrimming.None) return true;   // no puede recortarse: se respeta el tooltip
        var texto = tb.Text;
        if (string.IsNullOrEmpty(texto) || tb.ActualWidth <= 0) return false;

        try
        {
            var medida = new FormattedText(
                texto, System.Globalization.CultureInfo.CurrentCulture, tb.FlowDirection,
                new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                tb.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(tb).PixelsPerDip);

            double disponible = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
            return medida.Width > disponible + 0.5;   // medio píxel de holgura para el redondeo
        }
        catch { return true; }   // ante la duda, mejor enseñar el tooltip que ocultarlo
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var files = FilterVideos(e.Args);

        _mutex = new Mutex(true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            // Ya hay una instancia: le mandamos los archivos (si vienen del menú
            // contextual), la traemos al frente y esta se cierra sin abrir otra ventana.
            SendToRunningInstance(files);
            ActivateRunningInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Esquinas redondeadas de Windows 11 para TODA ventana de la app. Se engancha una
        // sola vez por clase en vez de repetirlo en cada ventana: así una ventana que se
        // añada mañana lo hereda sola, en lugar de salir con esquinas rectas hasta que
        // alguien se acuerde. SourceInitialized es el momento exacto en que ya existe el
        // handle nativo y todavía no se ha pintado nada.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) =>
            {
                if (s is Window w) WindowCorners.Round(new WindowInteropHelper(w).Handle);
            }));

        // El tema pone un tooltip en todo lo que puede acabar en puntos suspensivos. Aquí se
        // cancela cuando el texto cabía entero: si no, saldría un tooltip repitiendo lo que
        // ya se está leyendo, que molesta más de lo que ayuda.
        EventManager.RegisterClassHandler(typeof(TextBlock), ToolTipService.ToolTipOpeningEvent,
            new ToolTipEventHandler((s, e) =>
            {
                if (s is TextBlock tb && !EstaRecortado(tb)) e.Handled = true;
            }));

        var win = new MainWindow();
        MainWindow = win;
        win.Show();

        StartPipeServer();                          // escucha a las instancias siguientes
        if (files.Count > 0) win.AddFilesFromShell(files);
    }

    /// <summary>
    /// De los argumentos se queda con los vídeos: admite archivos y carpetas (misma
    /// lógica que al soltar con el ratón, para que las tres vías se comporten igual).
    /// </summary>
    private static List<string> FilterVideos(IEnumerable<string> args) =>
        ShellIntegration.ExpandVideos(
            args.Where(a => !a.StartsWith('-') && !a.StartsWith('/')), recurse: true);

    // ---------- instancia nueva → instancia viva ----------

    /// <summary>Manda la lista de archivos a la instancia que ya está corriendo.</summary>
    private static void SendToRunningInstance(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(3000);
            var data = Encoding.UTF8.GetBytes(string.Join("\n", files));
            pipe.Write(data, 0, data.Length);
            pipe.Flush();
        }
        catch { /* si no contesta, al menos la traeremos al frente */ }
    }

    /// <summary>Pone en primer plano la ventana de la instancia que ya está corriendo.</summary>
    private static void ActivateRunningInstance()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(me.ProcessName))
            {
                using (p)
                {
                    if (p.Id == me.Id) continue;
                    WindowActivation.AllowForeground(p.Id);
                    WindowActivation.BringToFront(p.MainWindowHandle);
                }
            }
        }
        catch { }
    }

    // ---------- instancia viva: escucha ----------

    /// <summary>
    /// Atiende a las instancias que se lanzan después (por ejemplo desde el menú
    /// contextual del Explorador): recibe sus archivos, los añade a la tabla y se
    /// pone delante. Así el atajo funciona igual con la app abierta o cerrada.
    /// </summary>
    private void StartPipeServer()
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances);
                    server.WaitForConnection();

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var files = reader.ReadToEnd()
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0)
                        .ToList();
                    if (files.Count == 0) continue;

                    Dispatcher.BeginInvoke(() =>
                    {
                        BringSelfToFront();
                        (MainWindow as MainWindow)?.AddFilesFromShell(files);
                    });
                }
                catch { Thread.Sleep(200); }   // cliente cortado o app cerrándose
            }
        })
        { IsBackground = true, Name = "shell-open-listener" };
        t.Start();
    }

    private void BringSelfToFront()
    {
        if (MainWindow is not { } w) return;
        w.Show();
        WindowActivation.BringToFront(new WindowInteropHelper(w).Handle);
        w.Activate();
    }
}
