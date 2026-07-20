namespace ShrinkVideo;

/// <summary>Opciones de compresión. Espejo de los parámetros del motor original.</summary>
public sealed class EncodeOptions
{
    public string? Output { get; set; }
    public string Container { get; set; } = "mkv";   // mkv | mp4 | webm
    public string VideoCodec { get; set; } = "hevc"; // hevc | h264 | av1
    public bool AudioOnly { get; set; }              // extraer solo el audio (sin vídeo)
    public string AudioFormat { get; set; } = "mp3"; // mp3 | m4a | flac | opus (cuando AudioOnly)
    public string Lang { get; set; } = "spa";
    public List<string> KeepLangs { get; set; } = new();   // vacío = preferido + eng
    public List<string>? SubLangs { get; set; }            // null = todos
    public bool NoSubs { get; set; }
    public int Quality { get; set; }        // 0 = automático
    public int MaxHeight { get; set; }      // 0 = sin reescalar
    public int AudioBitrate { get; set; }   // 0 = copiar audio original
    public bool Force { get; set; }
    public bool DryRun { get; set; }
}
