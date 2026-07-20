using System.IO;
using System.Text.Json;

namespace ShrinkVideo;

/// <summary>Una configuración guardada (índices de los controles de la UI).</summary>
public sealed class Preset
{
    public string Name { get; set; } = "";
    public bool Factory { get; set; }
    public string Lang { get; set; } = "spa";
    public int Fmt { get; set; }       // índice de cboFmt
    public int Codec { get; set; }     // índice de cboCodec
    public int Quality { get; set; }   // índice de cboQ
    public int Res { get; set; }       // índice de cboRes
    public int Audio { get; set; }     // índice de cboAud
    public bool Recurse { get; set; } = true;

    public override string ToString() => Factory ? Name : Name + "  ·  guardado";
}

/// <summary>Presets de fábrica + los guardados por el usuario en %AppData%\ShrinkStudio\presets.json.</summary>
public static class PresetStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShrinkStudio");
    private static string FilePath => Path.Combine(Dir, "presets.json");

    public static List<Preset> Factory() => new()
    {
        new() { Name = "Equilibrado (HEVC)",              Factory = true, Fmt = 0, Codec = 0, Quality = 0, Res = 0, Audio = 0 },
        new() { Name = "Máxima compatibilidad (MP4/H.264)", Factory = true, Fmt = 1, Codec = 1, Quality = 2, Res = 0, Audio = 1 },
        new() { Name = "Archivar (AV1, alta calidad)",    Factory = true, Fmt = 0, Codec = 2, Quality = 2, Res = 0, Audio = 0 },
        new() { Name = "Ligero para móvil (720p)",        Factory = true, Fmt = 1, Codec = 0, Quality = 0, Res = 2, Audio = 3 },
        new() { Name = "Solo audio (MP3)",                Factory = true, Fmt = 3, Codec = 0, Quality = 0, Res = 0, Audio = 3 },
    };

    public static List<Preset> LoadUser()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<Preset>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public static void SaveUser(List<Preset> user)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(user, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
