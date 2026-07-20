using System.Threading;
using System.Windows;

namespace ShrinkVideo;

public partial class App : Application
{
    // El instalador usa este mutex (AppMutex) para cerrar la app al actualizar.
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "ShrinkVideoSingleInstanceMutex");
        base.OnStartup(e);
    }
}
