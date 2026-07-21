using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShrinkVideo;

/// <summary>Qué hacer con cada archivo original una vez comprimido correctamente.</summary>
public enum AfterCompress
{
    Ask,               // preguntar al empezar (con opción "no volver a mostrar")
    RecycleOriginal,   // enviar el original a la Papelera automáticamente
    Keep,              // conservar los originales
}

/// <summary>Preferencias de la app, persistidas en %AppData%\ShrinkStudio\settings.json.</summary>
public sealed class Settings
{
    // --- General ---
    public string DefaultPreset { get; set; } = "";      // preset a aplicar al abrir ("" = ninguno)
    public string DefaultLang { get; set; } = "spa";     // idioma de audio por defecto
    public bool Recurse { get; set; } = true;            // analizar subcarpetas
    public bool CheckUpdatesOnStart { get; set; } = true;

    // --- Al comprimir ---
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AfterCompress AfterCompress { get; set; } = AfterCompress.Ask;

    // --- Renombrado de la salida (estilo PowerRename) ---
    public RenameRule Rename { get; set; } = new();
    public List<string> RenameSearchHistory { get; set; } = new();    // valores recientes de «Buscar»
    public List<string> RenameReplaceHistory { get; set; } = new();   // valores recientes de «Reemplazar por»

    // --- Rendimiento y disco ---
    public int MinFreeMb { get; set; } = 200;            // margen mínimo de disco antes de pausar
    public bool UseHardware { get; set; } = true;        // usar aceleración por hardware si existe

    public Settings Clone() => (Settings)MemberwiseClone();
}

/// <summary>Carga/guarda las preferencias (junto a los presets).</summary>
public static class SettingsStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShrinkStudio");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public static void Save(Settings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, JsonOpts));
        }
        catch { }
    }
}
