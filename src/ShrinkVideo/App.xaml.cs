using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace ShrinkVideo;

public partial class App : Application
{
    // El instalador usa este mutex (AppMutex) para cerrar la app al actualizar.
    // Además nos sirve para detectar que ya hay una instancia abierta.
    private const string MutexName = "ShrinkVideoSingleInstanceMutex";
    private const string ShowEventName = "ShrinkVideoShowWindowEvent";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            // Ya hay una instancia abierta: la traemos al frente y esta se cierra
            // sin abrir una segunda ventana.
            ActivateRunningInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ListenForActivation();

        var win = new MainWindow();
        MainWindow = win;
        win.Show();
    }

    /// <summary>Instancia nueva: pone en primer plano la ventana de la que ya está corriendo.</summary>
    private static void ActivateRunningInstance()
    {
        // 1) empujar su ventana desde aquí (esta instancia acaba de nacer de un clic del
        //    usuario, así que tiene derecho a primer plano y puede cedérselo a la otra)
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

        // 2) y avisarla para que se active ella misma (cubre el caso de que aún no
        //    tuviera MainWindowHandle o estuviera minimizada)
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
                using (ev) ev.Set();
        }
        catch { }
    }

    /// <summary>Instancia viva: escucha si otra intenta abrirse y se pone delante.</summary>
    private void ListenForActivation()
    {
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var ev = _showEvent;
            var t = new Thread(() =>
            {
                try
                {
                    while (ev.WaitOne()) Dispatcher.BeginInvoke(BringSelfToFront);
                }
                catch { /* la app se está cerrando */ }
            })
            { IsBackground = true, Name = "activation-listener" };
            t.Start();
        }
        catch { }
    }

    private void BringSelfToFront()
    {
        if (MainWindow is not { } w) return;
        w.Show();
        WindowActivation.BringToFront(new WindowInteropHelper(w).Handle);
        w.Activate();
    }
}
