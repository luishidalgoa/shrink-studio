using System.IO;
using Microsoft.Win32;

namespace ShrinkVideo;

/// <summary>
/// Integra «Abrir con ShrinkStudio» en el menú contextual del Explorador, solo para
/// archivos de vídeo y solo para el usuario actual (HKCU: sin permisos de administrador).
///
/// NOTA sobre dónde aparece: en Windows 11 esta entrada sale en «Mostrar más opciones»
/// (o con Mayús+clic derecho). Para aparecer en el menú moderno de primer nivel —donde
/// están Clipchamp o PowerRename— hace falta un paquete MSIX firmado con un manejador
/// IExplorerCommand; se comprobó que todas esas entradas vienen de paquetes .ShellExtension.
/// Sin certificado de firma no es posible, así que se usa la vía clásica, que funciona
/// hoy y sin privilegios.
/// </summary>
internal static class ShellIntegration
{
    private const string VerbName = "ShrinkStudio.Comprimir";
    private const string VerbLabel = "Comprimir con ShrinkStudio";

    /// <summary>Ruta del ejecutable a registrar. Inyectable para poder probarlo.</summary>
    internal static string? ExePathOverride;
    private static string ExePath => ExePathOverride ?? Environment.ProcessPath ?? "";

    private static string KeyPath(string ext) =>
        $@"Software\Classes\SystemFileAssociations\{ext}\shell\{VerbName}";

    /// <summary>¿Está la entrada puesta y apuntando a este mismo ejecutable?</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath(Engine.VideoExtensions[0]) + @"\command");
            if (k?.GetValue(null) is not string cmd) return false;
            return cmd.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Añade la entrada para cada extensión de vídeo conocida.</summary>
    public static bool Register()
    {
        if (string.IsNullOrEmpty(ExePath) || !File.Exists(ExePath)) return false;
        try
        {
            foreach (var ext in Engine.VideoExtensions)
            {
                using var verb = Registry.CurrentUser.CreateSubKey(KeyPath(ext));
                verb.SetValue(null, VerbLabel);
                verb.SetValue("Icon", $"\"{ExePath}\",0");
                // «Player» hace que el Explorador abra UNA sola vez con todos los
                // seleccionados, en vez de lanzar un proceso por archivo.
                verb.SetValue("MultiSelectModel", "Player");

                using var cmd = verb.CreateSubKey("command");
                cmd.SetValue(null, $"\"{ExePath}\" \"%1\"");
            }
            ShellNotify.AssociationsChanged();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Quita la entrada de todas las extensiones.</summary>
    public static bool Unregister()
    {
        try
        {
            foreach (var ext in Engine.VideoExtensions)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(KeyPath(ext), throwOnMissingSubKey: false); }
                catch { }
            }
            ShellNotify.AssociationsChanged();
            return true;
        }
        catch { return false; }
    }
}
