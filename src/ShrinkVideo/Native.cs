using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShrinkVideo;

/// <summary>
/// Suspender/reanudar un proceso (para pausar FFmpeg sin perder el progreso).
/// En Windows via ntdll; en Linux/macOS con las señales SIGSTOP/SIGCONT.
/// </summary>
internal static class ProcessControl
{
    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr handle);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr handle);
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)] private static extern int UnixKill(int pid, int sig);

    // Los números de señal NO coinciden entre sistemas:
    //   Linux → SIGSTOP 19, SIGCONT 18   ·   macOS/BSD → SIGSTOP 17, SIGCONT 19
    private static int SigStop => OperatingSystem.IsMacOS() ? 17 : 19;
    private static int SigCont => OperatingSystem.IsMacOS() ? 19 : 18;

    public static void Suspend(Process p)
    {
        try
        {
            if (OperatingSystem.IsWindows()) NtSuspendProcess(p.Handle);
            else UnixKill(p.Id, SigStop);
        }
        catch { }
    }

    public static void Resume(Process p)
    {
        try
        {
            if (OperatingSystem.IsWindows()) NtResumeProcess(p.Handle);
            else UnixKill(p.Id, SigCont);
        }
        catch { }
    }
}

/// <summary>Traer al frente la ventana de la instancia que ya está abierta (instancia única).</summary>
internal static class WindowActivation
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>Restaura la ventana si estaba minimizada (conservando maximizado) y la pone delante.</summary>
    public static void BringToFront(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        try
        {
            if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);   // SW_RESTORE respeta si estaba maximizada
            SetForegroundWindow(hWnd);
        }
        catch { }
    }

    /// <summary>Cede a otro proceso el derecho a ponerse en primer plano (lo llama la instancia nueva).</summary>
    public static void AllowForeground(int pid)
    {
        try { AllowSetForegroundWindow(pid); } catch { }
    }
}

/// <summary>
/// Esquinas redondeadas al estilo de Windows 11.
///
/// La app dibuja su propia barra de título (<c>WindowStyle="None"</c>), y eso hace que
/// Windows deje de aplicar el redondeo que sí le da a las ventanas normales: la ventana
/// queda como un rectángulo recto que desentona con el resto del escritorio.
///
/// No se dibuja a mano: se le PIDE al gestor de ventanas con
/// <c>DWMWA_WINDOW_CORNER_PREFERENCE</c>. Así el radio, el antialiasing y la sombra son
/// exactamente los del sistema, cambian solos si Microsoft los cambia, y el redondeo
/// desaparece al maximizar igual que en cualquier otra ventana.
///
/// Hacerlo a mano exigiría <c>AllowsTransparency</c>, que convierte la ventana en una
/// capa: adiós a la aceleración por hardware y a buena parte del comportamiento nativo
/// de arrastre y redimensión.
/// </summary>
internal static class WindowCorners
{
    // Disponible desde Windows 11 (build 22000). En Windows 10 la llamada falla y no pasa
    // nada: las esquinas rectas son justo el aspecto correcto en Windows 10.
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;   // radio grande, el de las ventanas principales

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Round(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
        try
        {
            int preferencia = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preferencia, sizeof(int));
        }
        catch (DllNotFoundException) { }   // dwmapi no existe: sistema sin composición
        catch (EntryPointNotFoundException) { }
    }
}

/// <summary>Avisar al Explorador de que han cambiado las asociaciones de archivo.</summary>
internal static class ShellNotify
{
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>Refresca el menú contextual sin tener que reiniciar el Explorador.</summary>
    public static void AssociationsChanged()
    {
        try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); } catch { }
    }
}

/// <summary>Enviar archivos/carpetas a la Papelera de reciclaje (sin dependencia de VisualBasic).</summary>
internal static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>Envía una ruta (archivo o carpeta) a la papelera. Devuelve true si tuvo éxito.</summary>
    public static bool Send(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",   // doble NUL: fin de lista
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        return SHFileOperation(ref op) == 0 && op.fAnyOperationsAborted == 0;
    }
}
