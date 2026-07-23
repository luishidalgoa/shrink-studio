using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ShrinkVideo;

// ---------- modelos de ffprobe ----------
internal sealed class FfProbe
{
    [JsonPropertyName("streams")] public List<FfStream> Streams { get; set; } = new();
    [JsonPropertyName("format")] public FfFormat? Format { get; set; }
}
internal sealed class FfStream
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("codec_type")] public string CodecType { get; set; } = "";
    [JsonPropertyName("codec_name")] public string CodecName { get; set; } = "";
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("bit_rate")] public string? BitRate { get; set; }
    [JsonPropertyName("r_frame_rate")] public string? RFrameRate { get; set; }
    [JsonPropertyName("channels")] public int? Channels { get; set; }
    [JsonPropertyName("tags")] public FfTags? Tags { get; set; }
    public string Lang => Tags?.Language ?? "";
}
internal sealed class FfTags { [JsonPropertyName("language")] public string? Language { get; set; } }

/// <summary>
/// Contexto de serialización generado en compilación. Permite publicar el binario
/// recortado (PublishTrimmed) sin que el recortador se lleve por delante los tipos de
/// ffprobe, que sin esto solo se descubren por reflexión.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FfProbe))]
internal partial class FfProbeJsonContext : JsonSerializerContext { }
internal sealed class FfFormat
{
    [JsonPropertyName("bit_rate")] public string? BitRate { get; set; }
    [JsonPropertyName("duration")] public string? Duration { get; set; }
    [JsonPropertyName("size")] public string? Size { get; set; }
}

/// <summary>Info resumida de pistas para la lista de la UI y la estimación.</summary>
public sealed class ProbeInfo
{
    public string Codec { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public int DurationSec { get; set; }
    public int VideoBitrateKbps { get; set; }
    public int AudioBitrateKbps { get; set; }
    public int Channels { get; set; }
    public string AudioCodec { get; set; } = "";
    public List<string> AudioLangs { get; set; } = new();
    public List<string> SubLangs { get; set; } = new();
}

/// <summary>Resultado de comprimir un archivo.</summary>
public sealed class FileResult
{
    public string Name { get; set; } = "";
    public long InBytes { get; set; }
    public long? OutBytes { get; set; }
    public string Status { get; set; } = "";
    public string SourcePath { get; set; } = "";   // ruta del original
    public string OutputPath { get; set; } = "";   // ruta del comprimido resultante
    /// <summary>Si se perdió algún subtítulo por el contenedor elegido, el motivo (para avisar en la UI).</summary>
    public string? SubtitleWarning { get; set; }
    public bool Ok => OutBytes is > 0;
}

/// <summary>Reporta el avance de la compresión a la UI.</summary>
public interface IEngineReporter
{
    void Log(string line);
    void FileStart(int index, int total, string name, double durationSec);
    void FileProgress(double fraction, string rawLine);   // 0..1 del archivo actual
    void FileDone(FileResult result);
    void DiskFull(bool paused) { }                        // disco lleno: en pausa esperando espacio

    /// <summary>
    /// Un archivo se salta y por qué. Antes esto solo iba al registro, así que la tabla
    /// no podía contar el motivo («ya está en HEVC», «ya hecho»…).
    /// </summary>
    void FileSkipped(string sourcePath, string reason) { }

}

/// <summary>
/// Motor de compresión: recodifica a HEVC/H.264/AV1 con aceleración por hardware,
/// conserva los idiomas de audio elegidos con el preferido por defecto, permite
/// pausar/reanudar y detener limpiamente, y nunca toca los originales.
/// </summary>
public sealed class Engine
{
    private static readonly string[] LossyAudio = { "aac", "opus", "mp3", "vorbis" };
    private static readonly string[] CoverCodecs = { "png", "mjpeg", "bmp", "gif" };
    private static readonly string[] Mp4Audio = { "aac", "ac3", "eac3", "mp3", "alac" };  // codecs que MP4 admite por copia
    // Subtítulos "de imagen": mapas de bits, no texto. MP4 no tiene dónde meterlos
    // (mov_text es texto), así que al pasar a MP4 hay que descartarlos a propósito.
    private static readonly string[] ImageSubs =
        { "hdmv_pgs_subtitle", "pgssub", "dvd_subtitle", "dvdsub", "dvb_subtitle", "dvbsub", "xsub" };
    // Nota: .ts (MPEG-TS) se omite a propósito: colisiona con TypeScript y llenaría
    // la lista de archivos de código en carpetas de desarrollo.
    public static readonly string[] VideoExtensions =
        { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".webm", ".mpg", ".mpeg", ".flv" };

    private readonly Dictionary<string, string> _cachedEncoder = new();

    // ---------- localización de ffmpeg/ffprobe ----------
    private static string ResolveTool(string exe)
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var rel in new[] { $"{exe}.exe", $"ffmpeg\\{exe}.exe", $"ffmpeg\\bin\\{exe}.exe" })
        {
            var p = Path.Combine(baseDir, rel);
            if (File.Exists(p)) return p;
        }
        return exe; // en el PATH
    }
    private static string Ffmpeg => ResolveTool("ffmpeg");
    private static string Ffprobe => ResolveTool("ffprobe");

    /// <summary>
    /// El título grabado DENTRO del contenedor (la etiqueta «title» del MKV/MP4), o null.
    /// Existe para los ficheros sin título en el nombre: el metadato suele conservarlo.
    /// Barato a propósito: solo cabecera de formato, sin analizar pistas.
    /// </summary>
    public static async Task<string?> LeerTituloAsync(string path)
    {
        try
        {
            var (code, stdout, _) = await RunAsync(Ffprobe, new[]
            {
                "-v", "quiet", "-show_entries", "format_tags=title", "-of", "json", path,
            });
            if (code != 0) return null;

            using var doc = System.Text.Json.JsonDocument.Parse(stdout);
            if (doc.RootElement.TryGetProperty("format", out var f) &&
                f.TryGetProperty("tags", out var tags))
                foreach (var prop in tags.EnumerateObject())
                    if (prop.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                    {
                        var t = prop.Value.GetString();
                        return string.IsNullOrWhiteSpace(t) ? null : t.Trim();
                    }
            return null;
        }
        catch { return null; }   // sin ffprobe o fichero ilegible: simplemente no hay metadato
    }

    public static async Task<bool> ToolsAvailableAsync()
    {
        try
        {
            var (code, _, _) = await RunAsync(Ffmpeg, new[] { "-version" });
            return code == 0;
        }
        catch { return false; }
    }

    // ---------- elección de codificador ----------
    // Candidatos por códec: primero los de hardware, con fallback a software.
    // Candidatos por códec: hardware (se prueban en vivo) y software (se usa el primero que exista).
    private static (string[] hw, string[] sw) Candidates(string codec) => codec switch
    {
        "h264" => (new[] { "h264_qsv", "h264_nvenc", "h264_amf" }, new[] { "libx264" }),
        "av1" => (new[] { "av1_qsv", "av1_nvenc", "av1_amf" }, new[] { "libsvtav1", "libaom-av1" }),
        "vp9" => (Array.Empty<string>(), new[] { "libvpx-vp9" }),   // VP9 por software: fiable entre equipos
        _ => (new[] { "hevc_qsv", "hevc_nvenc", "hevc_amf" }, new[] { "libx265" }),
    };

    public async Task<string> SelectEncoderAsync(string codec = "hevc")
    {
        if (_cachedEncoder.TryGetValue(codec, out var cached)) return cached;
        var (hw, sw) = Candidates(codec);
        var (_, encList, _) = await RunAsync(Ffmpeg, new[] { "-hide_banner", "-encoders" });
        foreach (var cand in AllowHardware ? hw : Array.Empty<string>())
        {
            if (!encList.Contains(cand)) continue;
            var (code, _, _) = await RunAsync(Ffmpeg, new[]
            {
                "-hide_banner", "-loglevel", "error", "-f", "lavfi",
                "-i", "testsrc=size=640x480:duration=0.1", "-c:v", cand, "-f", "null", "-"
            });
            if (code == 0) return _cachedEncoder[codec] = cand;
        }
        // primer codificador software que realmente exista en esta build de FFmpeg
        foreach (var s in sw) if (encList.Contains(s)) return _cachedEncoder[codec] = s;
        return _cachedEncoder[codec] = sw[0];
    }

    public static bool IsHardware(string encoder) => !encoder.StartsWith("lib");

    /// <summary>
    /// ¿Este vídeo ya está bien comprimido y no merece la pena tocarlo? Misma regla que
    /// aplica CompressAsync al saltárselo; se expone para que la tabla pueda avisar al
    /// analizar, en vez de descubrirlo el usuario cuando ya ha lanzado la tanda.
    /// </summary>
    public static bool AlreadyCompressed(string codec, int totalKbps) =>
        (codec is "hevc" or "av1") && totalKbps > 0 && totalKbps < 2500;

    /// <summary>Extrae un fotograma como miniatura JPG. Devuelve true si lo consiguió.</summary>
    public static async Task<bool> MakeThumbnailAsync(string video, string destJpg, int atSec)
    {
        var (code, _, _) = await RunAsync(Ffmpeg, new[]
        {
            "-v", "error", "-ss", $"{atSec}", "-i", video, "-frames:v", "1",
            "-vf", "scale=480:-2", "-q:v", "4", "-y", destJpg
        });
        return code == 0 && File.Exists(destJpg);
    }

    // ---------- previsualización de 10 s con los ajustes actuales ----------

    /// <summary>Args de codificación para la preview: mismo códec, pero preset lo MÁS rápido posible
    /// (es solo una vista de la calidad; no vale la pena esperar minutos con un encoder de software).</summary>
    private static List<string> PreviewEncoderArgs(string encoder, int quality) => encoder switch
    {
        "libx264" or "libx265" => new() { "-c:v", encoder, "-crf", $"{quality}", "-preset", "ultrafast" },
        "libsvtav1" => new() { "-c:v", "libsvtav1", "-crf", $"{quality}", "-preset", "12" },
        "libaom-av1" => new() { "-c:v", "libaom-av1", "-crf", $"{quality}", "-b:v", "0", "-cpu-used", "8", "-usage", "realtime" },
        "libvpx-vp9" => new() { "-c:v", "libvpx-vp9", "-crf", $"{quality}", "-b:v", "0", "-deadline", "realtime", "-cpu-used", "8", "-row-mt", "1" },
        _ => EncoderArgs(encoder, quality),   // hardware ya es rápido
    };

    /// <summary>
    /// Renderiza 10 s desde `startSec` con el códec/calidad/resolución elegidos (preset rápido),
    /// a un archivo temporal, para comprobar el resultado antes de comprimir. Devuelve la ruta o null.
    /// </summary>
    public async Task<string?> PreviewAsync(string input, EncodeOptions opt, int startSec, string dest, IEngineReporter rep, CancellationToken ct)
    {
        string vcodec = opt.Container == "webm" ? "vp9" : opt.VideoCodec;
        var encoder = await SelectEncoderAsync(vcodec);
        int quality = opt.Quality > 0 ? opt.Quality : (IsHardware(encoder) ? 27 : 23);
        var pr = await ProbeFullAsync(input);
        var video = pr?.Streams.FirstOrDefault(s => s.CodecType == "video" && !CoverCodecs.Contains(s.CodecName));
        var allAudio = pr?.Streams.Where(s => s.CodecType == "audio").ToList() ?? new();
        var pickAudio = allAudio.FirstOrDefault(s => s.Lang == opt.Lang) ?? allAudio.FirstOrDefault();  // idioma preferido

        var a = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-stats", "-y",
            "-ss", startSec.ToString(), "-t", "10", "-i", input,
            "-map", "0:v:0",
        };
        a.AddRange(pickAudio != null ? new[] { "-map", $"0:{pickAudio.Index}" } : new[] { "-map", "0:a:0?" });
        if (opt.MaxHeight > 0 && (video?.Height ?? 0) > opt.MaxHeight)
            a.AddRange(new[] { "-vf", $"scale=-2:{opt.MaxHeight}" });
        a.AddRange(PreviewEncoderArgs(encoder, quality));
        int abr = opt.AudioBitrate > 0 ? opt.AudioBitrate : 192;
        a.AddRange(new[] { "-c:a", "aac", "-b:a", $"{abr}k", dest });

        var (code, _) = await RunFfmpegAsync(a, 10, rep, ct);
        return code == 0 && File.Exists(dest) ? dest : null;
    }

    private static List<string> EncoderArgs(string encoder, int quality) => encoder switch
    {
        "hevc_qsv" or "h264_qsv" or "av1_qsv" =>
            new() { "-c:v", encoder, "-global_quality", $"{quality}", "-preset", "slow" },
        "hevc_nvenc" or "h264_nvenc" or "av1_nvenc" =>
            new() { "-c:v", encoder, "-rc", "vbr", "-cq", $"{quality}", "-preset", "p6", "-tune", "hq" },
        "hevc_amf" or "h264_amf" or "av1_amf" =>
            new() { "-c:v", encoder, "-rc", "cqp", "-qp_i", $"{quality}", "-qp_p", $"{quality}", "-quality", "quality" },
        "vp9_qsv" => new() { "-c:v", "vp9_qsv", "-global_quality", $"{quality}", "-preset", "slow" },
        "libsvtav1" => new() { "-c:v", "libsvtav1", "-crf", $"{quality}", "-preset", "6" },
        "libvpx-vp9" => new() { "-c:v", "libvpx-vp9", "-crf", $"{quality}", "-b:v", "0", "-row-mt", "1" },
        "libaom-av1" => new() { "-c:v", "libaom-av1", "-crf", $"{quality}", "-b:v", "0", "-cpu-used", "6", "-row-mt", "1" },
        _ => new() { "-c:v", encoder, "-crf", $"{quality}", "-preset", "medium" },   // libx264 / libx265
    };

    private static string AudioExt(string fmt) => fmt switch
    {
        "m4a" => ".m4a", "flac" => ".flac", "opus" => ".opus", _ => ".mp3",
    };

    /// <summary>Extensión del archivo de salida según el formato elegido (la usa también la vista previa de renombrado).</summary>
    public static string OutputExtension(EncodeOptions opt) => opt.AudioOnly ? AudioExt(opt.AudioFormat)
        : opt.Container == "mp4" ? ".mp4"
        : opt.Container == "webm" ? ".webm" : ".mkv";
    private static List<string> AudioOnlyArgs(EncodeOptions opt)
    {
        int br = opt.AudioBitrate > 0 ? opt.AudioBitrate : 192;
        return opt.AudioFormat switch
        {
            "flac" => new() { "-c:a", "flac" },
            "opus" => new() { "-c:a", "libopus", "-b:a", $"{br}k" },
            "m4a" => new() { "-c:a", "aac", "-b:a", $"{br}k" },
            _ => new() { "-c:a", "libmp3lame", "-b:a", $"{br}k" },   // mp3
        };
    }

    // ---------- análisis de pistas (para el scan de la UI) ----------
    public async Task<ProbeInfo> ProbeAsync(string path)
    {
        var pr = await ProbeFullAsync(path);
        var info = new ProbeInfo();
        if (pr == null) return info;

        var vid = pr.Streams.FirstOrDefault(s => s.CodecType == "video" && !CoverCodecs.Contains(s.CodecName));
        info.Codec = vid?.CodecName ?? "";
        info.Width = vid?.Width ?? 0;
        info.Height = vid?.Height ?? 0;
        info.Fps = ParseFps(vid?.RFrameRate);

        double durSec = double.TryParse(pr.Format?.Duration, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        info.DurationSec = (int)durSec;

        var firstAudio = pr.Streams.FirstOrDefault(s => s.CodecType == "audio");
        info.Channels = firstAudio?.Channels ?? 2;
        info.AudioCodec = firstAudio?.CodecName ?? "";
        int audioKbps = ParseKbps(firstAudio?.BitRate);
        int vidKbps = ParseKbps(vid?.BitRate);

        // MKV rara vez expone bitrate por pista: derivarlo del total del contenedor.
        int overallKbps = ParseKbps(pr.Format?.BitRate);
        if (overallKbps == 0 && long.TryParse(pr.Format?.Size, out var sz) && durSec > 0)
            overallKbps = (int)(sz * 8 / durSec / 1000);
        if (audioKbps == 0) audioKbps = info.Channels >= 6 ? 448 : 192;
        if (vidKbps == 0 && overallKbps > 0)
            vidKbps = Math.Max(overallKbps - audioKbps, (int)(overallKbps * 0.85));

        info.VideoBitrateKbps = vidKbps;
        info.AudioBitrateKbps = audioKbps;

        info.AudioLangs = pr.Streams.Where(s => s.CodecType == "audio")
            .Select(s => string.IsNullOrEmpty(s.Lang) ? "?" : s.Lang).Distinct().ToList();
        info.SubLangs = pr.Streams.Where(s => s.CodecType == "subtitle")
            .Select(s => string.IsNullOrEmpty(s.Lang) ? "?" : s.Lang).Distinct().ToList();
        return info;
    }

    private static double ParseFps(string? r)
    {
        if (string.IsNullOrEmpty(r)) return 0;
        var parts = r.Split('/');
        if (parts.Length == 2 && double.TryParse(parts[0], out var n)
            && double.TryParse(parts[1], out var den) && den > 0) return n / den;
        return double.TryParse(r, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0;
    }
    private static int ParseKbps(string? bitrate) => int.TryParse(bitrate, out var b) && b > 0 ? b / 1000 : 0;

    private static async Task<FfProbe?> ProbeFullAsync(string path)
    {
        var (code, stdout, _) = await RunAsync(Ffprobe, new[]
        {
            "-v", "error",
            "-show_entries", "stream=index,codec_type,codec_name,width,height,bit_rate,r_frame_rate,channels:stream_tags=language:format=bit_rate,duration,size",
            "-of", "json", "--", path
        });
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return null;
        try
        {
            // Se usa el contexto generado en compilación (ver FfProbeJsonContext): con la
            // versión por reflexión, al recortar el binario se perdían los tipos y ffprobe
            // devolvía datos vacíos (duración 0, resolución 0x0).
            return JsonSerializer.Deserialize(stdout, FfProbeJsonContext.Default.FfProbe);
        }
        catch { return null; }
    }

    // ---------- ¿aún descargando? ----------
    private static bool StillDownloading(string path)
    {
        foreach (var ext in new[] { ".part", ".crdownload", ".!ut", ".downloading", ".tmp", "!qB" })
            if (File.Exists(path + ext)) return true;
        // FileShare.Read, NO FileShare.None: lo que delata una descarga a medias es que
        // alguien lo tenga abierto para ESCRIBIR. La apertura exclusiva también fallaba con
        // cualquier LECTOR — OneDrive hidratando, el indexador, o el propio reproductor de
        // Recortes, que suelta el fichero con retraso — y en Recortes eso montaba un bucle:
        // salto por «descargando», el finally reabría el vídeo, y el siguiente intento
        // volvía a encontrarlo cogido. Solo se salía reiniciando la app.
        try { using var s = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch { return true; }   // retenido por un ESCRITOR: eso sí es una descarga en curso
        return false;
    }

    // ---------- compresión ----------
    public async Task<List<FileResult>> CompressAsync(
        IReadOnlyList<string> files, EncodeOptions opt, IEngineReporter rep, CancellationToken ct)
    {
        var results = new List<FileResult>();
        string vcodec = opt.Container == "webm" ? "vp9" : opt.VideoCodec;   // WebM: VP9 (más compatible entre builds de FFmpeg)
        var encoder = await SelectEncoderAsync(vcodec);
        int quality = opt.Quality > 0 ? opt.Quality : (IsHardware(encoder) ? 27 : 23);
        var encArgs = EncoderArgs(encoder, quality);
        if (opt.AudioOnly) rep.Log($"Modo solo audio → {opt.AudioFormat.ToUpperInvariant()}");
        else
        {
            rep.Log($"Codificador: {encoder} [{(IsHardware(encoder) ? "hardware" : "software (CPU, lento)")}] · calidad {quality}");
            if (encoder is "libaom-av1")
                rep.Log("  AVISO: AV1 por software es MUY lento (puede tardar horas). Para ir rápido usa H.265, que aprovecha tu GPU.");
        }

        var keepLangs = opt.KeepLangs.Count > 0 ? opt.KeepLangs : new List<string> { opt.Lang, "eng" };
        bool keepAll = keepLangs.Contains("all");
        bool subsAll = opt.SubLangs == null || opt.SubLangs.Count == 0 || opt.SubLangs.Contains("all");

        int total = files.Count, n = 0;
        int renamedCount = 0;                                              // contador de la regla de renombrado
        var usedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            n++;
            var fi = new FileInfo(f);
            string name = fi.Name;
            string outDir = opt.Output ?? Path.Combine(fi.DirectoryName!, "comprimido");
            string ext = OutputExtension(opt);

            // nombre de salida, con la regla de renombrado (estilo PowerRename) si la hay
            string outName = (opt.NombreSalida is { Length: > 0 } propio
                ? Reindex.LibraryTemplate.LimpiarNombre(propio)
                : Path.GetFileNameWithoutExtension(name)) + ext;
            string? renamedTo = null;
            if (opt.NameRule is { } rule && rule.HasEffect)
            {
                DateTime created;
                try { created = fi.CreationTime; } catch { created = DateTime.Now; }
                if (rule.Apply(outName, renamedCount, created) is { } nuevo)
                {
                    outName = renamedTo = nuevo;
                    renamedCount++;
                }
            }
            string outPath = UniqueOutput(Path.Combine(outDir, outName), usedOutputs);

            if (File.Exists(outPath) && !opt.Force) { rep.Log($"[{n}/{total}] {name} → ya hecho, salto"); rep.FileSkipped(f, "Ya hecho"); continue; }
            if (StillDownloading(f)) { rep.Log($"[{n}/{total}] {name} → descargando aún, salto"); rep.FileSkipped(f, "Descargando"); continue; }

            var pr = await ProbeFullAsync(f);
            if (pr == null) { rep.Log($"[{n}/{total}] {name} → no se puede leer, salto"); rep.FileSkipped(f, "No se puede leer"); continue; }

            var video = pr.Streams.FirstOrDefault(s => s.CodecType == "video" && !CoverCodecs.Contains(s.CodecName));
            if (video == null) { rep.Log($"[{n}/{total}] {name} → sin pista de vídeo, salto"); rep.FileSkipped(f, "Sin vídeo"); continue; }

            int kbps = int.TryParse(pr.Format?.BitRate, out var br) ? br / 1000 : 0;
            if (!opt.AudioOnly && !opt.Force && (video.CodecName is "hevc" or "av1") && kbps > 0 && kbps < 2500)
            { rep.Log($"[{n}/{total}] {name} → ya comprimido ({video.CodecName}, {kbps} kbps), salto"); rep.FileSkipped(f, $"Ya en {video.CodecName!.ToUpperInvariant()}"); continue; }

            var allAudio = pr.Streams.Where(s => s.CodecType == "audio").ToList();
            if (allAudio.Count == 0) { rep.Log($"[{n}/{total}] {name} → sin audio, salto"); rep.FileSkipped(f, "Sin audio"); continue; }
            var pref = allAudio.Where(s => s.Lang == opt.Lang).ToList();
            var other = allAudio.Where(s => s.Lang != opt.Lang && (keepAll || keepLangs.Contains(s.Lang))).ToList();
            var audio = pref.Concat(other).ToList();
            if (audio.Count == 0) audio = allAudio;   // ningún idioma coincide: conservar todo

            var subs = opt.NoSubs ? new List<FfStream>()
                : pr.Streams.Where(s => s.CodecType == "subtitle" && (subsAll || (opt.SubLangs?.Contains(s.Lang) ?? true))).ToList();

            // Qué subtítulos caben de verdad en el contenedor elegido:
            //   · MKV admite texto e imagen, y se copian tal cual.
            //   · MP4 solo admite texto (convertido a mov_text); los de imagen
            //     (PGS, VobSub, DVB…) no tienen equivalente y se descartan aquí,
            //     a propósito, en vez de hacer fallar toda la codificación.
            //   · WebM no lleva subtítulos en esta versión.
            bool webmOut = opt.Container == "webm";
            bool mp4Out = opt.Container == "mp4";
            var keptSubs = webmOut ? new List<FfStream>()
                         : mp4Out ? subs.Where(s => IsTextSubtitle(s.CodecName)).ToList()
                         : subs;
            var lostSubs = subs.Where(s => !keptSubs.Contains(s)).ToList();

            double durSec = double.TryParse(pr.Format?.Duration, System.Globalization.CultureInfo.InvariantCulture, out var dd) ? dd : 0;
            // Con un tramo, lo que se codifica es SU duración: si no, la barra de progreso
            // mediría contra el vídeo entero y se quedaría clavada al 10 %.
            if (opt.Duracion is > 0) durSec = Math.Min(opt.Duracion.Value, durSec > 0 ? durSec : opt.Duracion.Value);

            // ---- modo solo audio: extraer sin vídeo ----
            if (opt.AudioOnly)
            {
                if (opt.DryRun) { rep.Log($"[{n}/{total}] {name} → solo audio → {opt.AudioFormat}"); continue; }
                rep.Log($"[{n}/{total}] {name}");
                rep.Log($"    extrayendo audio ({audio.Count} pista/s) → {opt.AudioFormat}");
                if (renamedTo != null) rep.Log($"    renombrado → {Path.GetFileName(outPath)}");
                rep.FileStart(n, total, name, durSec);
                Directory.CreateDirectory(outDir);
                string atmp = outPath + ".tmp" + ext;
                try { if (File.Exists(atmp)) File.Delete(atmp); } catch { }
                var aargs = new List<string> { "-hide_banner", "-loglevel", "warning", "-stats", "-y", "-i", f, "-vn" };
                foreach (var au in audio) aargs.AddRange(new[] { "-map", $"0:{au.Index}" });
                aargs.AddRange(AudioOnlyArgs(opt));
                aargs.Add(atmp);
                try
                {
                    var (acode, _) = await RunFfmpegAsync(aargs, durSec, rep, ct);
                    if (acode == 0 && File.Exists(atmp))
                    {
                        try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                        File.Move(atmp, outPath);
                        long ob = new FileInfo(outPath).Length;
                        rep.Log($"    OK  audio → {ob / 1048576.0:n1} MB");
                        var ar = new FileResult { Name = name, InBytes = fi.Length, OutBytes = ob, Status = "audio", SourcePath = f, OutputPath = outPath };
                        results.Add(ar); rep.FileDone(ar);
                    }
                    else { try { if (File.Exists(atmp)) File.Delete(atmp); } catch { } rep.Log($"    ERROR al extraer audio (código {acode})"); }
                }
                catch (OperationCanceledException) { try { if (File.Exists(atmp)) File.Delete(atmp); } catch { } rep.Log("    detenido."); throw; }
                continue;
            }

            // ---- construir argumentos ----
            List<string> BuildArgs(bool withSubs)
            {
                var (ssAntes, tDespues) = Reindex.Tramos.ArgsFfmpeg(opt.Desde, opt.Duracion);
                var a = new List<string> { "-hide_banner", "-loglevel", "warning", "-stats", "-y" };
                a.AddRange(ssAntes);        // el salto, ANTES de la entrada: busca por índice
                a.AddRange(new[] { "-i", f });
                a.AddRange(tDespues);
                bool webm = opt.Container == "webm";
                a.AddRange(new[] { "-map", $"0:{video.Index}" });
                foreach (var au in audio) a.AddRange(new[] { "-map", $"0:{au.Index}" });
                if (withSubs) foreach (var s in keptSubs) a.AddRange(new[] { "-map", $"0:{s.Index}" });
                if (opt.MaxHeight > 0 && (video.Height ?? 0) > opt.MaxHeight)
                    a.AddRange(new[] { "-vf", $"scale=-2:{opt.MaxHeight}" });
                a.AddRange(encArgs);
                bool mp4 = opt.Container == "mp4";
                for (int i = 0; i < audio.Count; i++)
                {
                    if (webm)   // WebM: audio siempre Opus
                    {
                        int wbr = opt.AudioBitrate > 0 ? opt.AudioBitrate : 160;
                        a.AddRange(new[] { $"-c:a:{i}", "libopus", $"-b:a:{i}", $"{wbr}k" });
                        continue;
                    }
                    var ac = audio[i].CodecName;
                    bool copy = opt.AudioBitrate == 0 || LossyAudio.Contains(ac);
                    if (mp4 && copy && !Mp4Audio.Contains(ac)) copy = false;   // MP4 no admite copiar este códec
                    if (copy)
                        a.AddRange(new[] { $"-c:a:{i}", "copy" });
                    else
                    {
                        int br = opt.AudioBitrate > 0 ? opt.AudioBitrate : 192;
                        a.AddRange(new[] { $"-c:a:{i}", "aac", $"-b:a:{i}", $"{br}k" });
                    }
                }
                if (withSubs && keptSubs.Count > 0) a.AddRange(new[] { "-c:s", mp4 ? "mov_text" : "copy" });
                a.AddRange(new[] { "-disposition:a:0", "default" });
                for (int i = 1; i < audio.Count; i++) a.AddRange(new[] { $"-disposition:a:{i}", "0" });
                a.AddRange(new[] { "-map_metadata", "0" });
                return a;
            }

            string langs = string.Join("+", audio.Select(x => string.IsNullOrEmpty(x.Lang) ? "?" : x.Lang));
            int dropped = allAudio.Count - audio.Count;
            string infoLine = $"audio: {langs}"
                + (dropped > 0 ? $" (descarto {dropped})" : "")
                + (keptSubs.Count > 0 ? $", {keptSubs.Count} sub" : "")
                + (opt.MaxHeight > 0 && (video.Height ?? 0) > opt.MaxHeight ? $", reescalo a {opt.MaxHeight}p" : "");

            // Aviso de subtítulos que este contenedor no puede llevar. No basta con el
            // registro: el usuario los marcó en la UI y da por hecho que van dentro.
            var lostSoFar = new List<FfStream>(lostSubs);
            if (lostSubs.Count > 0)
                rep.Log($"    AVISO: {SubtitleLossMessage(lostSubs, opt.Container)}");

            if (opt.DryRun) { rep.Log($"[{n}/{total}] {name} → {infoLine}"); continue; }

            rep.Log($"[{n}/{total}] {name}");
            rep.Log($"    {infoLine}");
            if (renamedTo != null) rep.Log($"    renombrado → {Path.GetFileName(outPath)}");
            rep.FileStart(n, total, name, durSec);

            Directory.CreateDirectory(outDir);
            await WaitForSpaceAsync(outDir, MinFreeBytes, rep, ct);   // no empezar si el disco ya está lleno
            // El temporal DEBE llevar la extensión del contenedor elegido: ffmpeg escoge
            // el muxer por la extensión, así que un ".tmp.mkv" producía un Matroska aunque
            // luego se renombrara a .mp4 (y con él, mov_text fallaba siempre).
            string tmp = outPath + ".tmp" + ext;
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }

            try
            {
                int code; string err;
                while (true)
                {
                    (code, err) = await RunFfmpegAsync(BuildArgs(true).Append(tmp).ToList(), durSec, rep, ct);

                    // red de seguridad: si aun así el subtítulo no entra (formato raro que
                    // no supimos clasificar), sacar el vídeo sin subtítulos antes que nada.
                    if (code != 0 && !IsDiskFull(err) && keptSubs.Count > 0 && !ct.IsCancellationRequested)
                    {
                        rep.Log("    reintentando sin subtítulos…");
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                        (code, err) = await RunFfmpegAsync(BuildArgs(false).Append(tmp).ToList(), durSec, rep, ct);
                        if (code == 0) lostSoFar.AddRange(keptSubs);
                    }

                    if (code != 0 && IsDiskFull(err))
                    {
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }   // liberar el temporal a medias
                        await WaitForSpaceAsync(outDir, MinFreeBytes, rep, ct);      // pausa hasta que haya espacio
                        continue;                                                   // reintentar el MISMO archivo (la cola se mantiene)
                    }
                    break;
                }

                if (code == 0 && File.Exists(tmp))
                {
                    try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                    File.Move(tmp, outPath);
                    long inB = fi.Length, outB = new FileInfo(outPath).Length;
                    int pct = (int)Math.Round(100 - (outB / (double)Math.Max(inB, 1) * 100));
                    rep.Log($"    OK  {inB / 1048576} MB → {outB / 1048576} MB  (-{pct}%)");
                    var r = new FileResult
                    {
                        Name = name, InBytes = inB, OutBytes = outB, Status = $"-{pct}%",
                        SourcePath = f, OutputPath = outPath,
                        SubtitleWarning = lostSoFar.Count > 0 ? SubtitleLossMessage(lostSoFar, opt.Container) : null,
                    };
                    results.Add(r); rep.FileDone(r);
                }
                else
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    rep.Log($"    ERROR al codificar (código {code})");
                    var r = new FileResult { Name = name, InBytes = fi.Length, OutBytes = null, Status = "ERROR", SourcePath = f, OutputPath = outPath };
                    results.Add(r); rep.FileDone(r);
                }
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }   // al detener no dejamos el temporal a medias
                rep.Log("    detenido; temporal eliminado.");
                throw;
            }
        }
        return results;
    }

    /// <summary>
    /// Mide el bitrate de vídeo REAL codificando varias muestras cortas repartidas por el
    /// vídeo con los ajustes elegidos. Es la única forma fiable de anticipar el tamaño:
    /// CRF fija la calidad, no el tamaño, así que el peso depende del contenido y ninguna
    /// fórmula lo adivina. Devuelve kbps (0 si no se pudo medir).
    /// </summary>
    public async Task<int> MeasureVideoBitrateAsync(string input, EncodeOptions opt, IEngineReporter rep,
                                                    CancellationToken ct, int samples = 3, int secondsEach = 8)
    {
        var pr = await ProbeFullAsync(input);
        var video = pr?.Streams.FirstOrDefault(s => s.CodecType == "video" && !CoverCodecs.Contains(s.CodecName));
        if (pr == null || video == null) return 0;
        double dur = double.TryParse(pr.Format?.Duration, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        if (dur < 4) return 0;

        string vcodec = opt.Container == "webm" ? "vp9" : opt.VideoCodec;
        var encoder = await SelectEncoderAsync(vcodec);
        int quality = opt.Quality > 0 ? opt.Quality : (IsHardware(encoder) ? 27 : 23);
        var encArgs = EncoderArgs(encoder, quality);

        // repartimos las muestras por el 90% central (evita cabecera y créditos)
        samples = Math.Max(1, samples);
        double start0 = dur * 0.05, usable = dur * 0.90;
        if (usable < (double)samples * secondsEach) secondsEach = Math.Max(2, (int)(usable / samples));

        string dir = Path.Combine(Path.GetTempPath(), "shrinkvideo_measure");
        Directory.CreateDirectory(dir);
        long totalBytes = 0; double totalSecs = 0;
        try
        {
            for (int i = 0; i < samples; i++)
            {
                ct.ThrowIfCancellationRequested();
                double at = start0 + usable * (i + 0.5) / samples - secondsEach / 2.0;
                at = Math.Clamp(at, 0, Math.Max(0, dur - secondsEach));
                string tmp = Path.Combine(dir, $"m{i}_{Guid.NewGuid():N}.mkv");
                var a = new List<string>
                {
                    "-hide_banner", "-loglevel", "error", "-y",
                    "-ss", at.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    "-i", input,
                    "-t", secondsEach.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-map", $"0:{video.Index}", "-an", "-sn",
                };
                if (opt.MaxHeight > 0 && (video.Height ?? 0) > opt.MaxHeight)
                    a.AddRange(new[] { "-vf", $"scale=-2:{opt.MaxHeight}" });
                a.AddRange(encArgs);
                a.Add(tmp);

                var (code, _) = await RunFfmpegAsync(a, 0, rep, ct);
                if (code == 0 && File.Exists(tmp))
                {
                    totalBytes += new FileInfo(tmp).Length;
                    totalSecs += secondsEach;
                }
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                rep.FileProgress((i + 1.0) / samples, "");
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        if (totalSecs <= 0 || totalBytes <= 0) return 0;
        // Cada muestra empieza con un fotograma clave, así que salen algo "caras"
        // respecto a una codificación continua: descontamos ese sesgo.
        const double SampleKeyframeBias = 0.94;
        return (int)Math.Round(totalBytes * 8.0 / totalSecs / 1000.0 * SampleKeyframeBias);
    }

    /// <summary>¿Ese códec de subtítulo es de texto (y por tanto convertible a mov_text para MP4)?</summary>
    public static bool IsTextSubtitle(string codec) =>
        !ImageSubs.Contains(codec, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explica en cristiano qué subtítulos se han quedado fuera y por qué, para poder
    /// enseñarlo tanto en el registro como en el aviso final de la ventana.
    /// </summary>
    private static string SubtitleLossMessage(IEnumerable<FfStream> lost, string container) =>
        SubtitleLossMessage(
            lost.Select(s => (s.CodecName, string.IsNullOrEmpty(s.Lang) ? "?" : s.Lang)).ToList(),
            container);

    internal static string SubtitleLossMessage(IReadOnlyList<(string codec, string lang)> lost, string container)
    {
        if (lost.Count == 0) return "";
        string cont = container.ToUpperInvariant();
        string langs = string.Join(", ", lost.Select(x => x.lang).Distinct());
        bool allImage = lost.All(x => !IsTextSubtitle(x.codec));
        bool una = lost.Count == 1;
        string qué = una ? "1 pista de subtítulos" : $"{lost.Count} pistas de subtítulos";
        string quedó = una ? "se ha quedado fuera" : "se han quedado fuera";
        string porqué = allImage
            ? $"{(una ? "es" : "son")} de imagen (tipo {string.Join("/", lost.Select(x => FriendlyCodec(x.codec)).Distinct())})"
              + $" y {cont} no admite ese formato"
            : $"{cont} no ha podido incluirla{(una ? "" : "s")}";
        return $"{qué} ({langs}) {quedó}: {porqué}. Comprime a MKV si {(una ? "la necesitas" : "las necesitas")}.";
    }

    private static string FriendlyCodec(string codec) => codec.ToLowerInvariant() switch
    {
        "hdmv_pgs_subtitle" or "pgssub" => "PGS",
        "dvd_subtitle" or "dvdsub" => "VobSub",
        "dvb_subtitle" or "dvbsub" => "DVB",
        "xsub" => "XSUB",
        _ => codec,
    };

    /// <summary>
    /// Evita que dos vídeos distintos acaben escribiendo en el mismo archivo dentro de
    /// la misma tanda (posible al renombrar): al segundo se le añade " (2)", " (3)"…
    /// </summary>
    private static string UniqueOutput(string path, HashSet<string> used)
    {
        if (used.Add(path)) return path;
        string dir = Path.GetDirectoryName(path) ?? "";
        string bn = Path.GetFileNameWithoutExtension(path);
        string ex = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var cand = Path.Combine(dir, $"{bn} ({i}){ex}");
            if (used.Add(cand)) return cand;
        }
    }

    // ---------- pausa / reanudación del FFmpeg en curso ----------
    private Process? _active;
    public void Pause()  { var p = _active; if (p is { HasExited: false }) ProcessControl.Suspend(p); }
    public void Resume() { var p = _active; if (p is { HasExited: false }) ProcessControl.Resume(p); }

    // ---------- ejecutar ffmpeg con progreso + cancelación ----------
    private static readonly Regex TimeRx =
        new(@"time=(\d+):(\d+):(\d+)\.(\d+)", RegexOptions.Compiled);

    // ---------- detección y espera por disco lleno ----------
    // Margen mínimo de disco antes de pausar (configurable desde Preferencias). Por defecto 200 MB.
    internal static long MinFreeBytes = 200L * 1024 * 1024;

    // ¿Se permite usar codificadores por hardware? (configurable desde Preferencias)
    public static bool AllowHardware = true;

    /// <summary>
    /// ¿Debe enviarse el original a la Papelera tras comprimir? Solo si está activado, la
    /// compresión fue correcta, y el comprimido NO es el propio original (evita autoborrado
    /// cuando la salida coincide con la entrada).
    /// </summary>
    public static bool ShouldRecycleSource(bool enabled, FileResult r) =>
        enabled && r.Ok
        && !string.IsNullOrEmpty(r.SourcePath)
        && !string.Equals(
            Path.GetFullPath(r.SourcePath),
            string.IsNullOrEmpty(r.OutputPath) ? "\0" : Path.GetFullPath(r.OutputPath),
            StringComparison.OrdinalIgnoreCase);

    internal static bool IsDiskFull(string err) =>
        err.Contains("No space left", StringComparison.OrdinalIgnoreCase) ||
        err.Contains("ENOSPC", StringComparison.OrdinalIgnoreCase);

    // Inyectable para pruebas (simular disco lleno). En producción consulta el disco real.
    internal static Func<string, long> FreeSpaceProvider = DefaultFreeSpace;
    private static long DefaultFreeSpace(string dir)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace; }
        catch { return long.MaxValue; }
    }
    private static long FreeSpace(string dir) => FreeSpaceProvider(dir);

    /// <summary>Espera (sin bloquear ni cancelar) hasta que haya al menos `need` bytes libres.</summary>
    private static async Task WaitForSpaceAsync(string dir, long need, IEngineReporter rep, CancellationToken ct)
    {
        bool notified = false;
        while (FreeSpace(dir) < need)
        {
            ct.ThrowIfCancellationRequested();
            if (!notified) { rep.DiskFull(true); rep.Log("    Disco lleno — pausado. Libera espacio y continuará automáticamente."); notified = true; }
            await Task.Delay(2500, ct);
        }
        if (notified) { rep.DiskFull(false); rep.Log("    Espacio disponible — continuando…"); }
    }

    /// <summary>
    /// ffmpeg codifica con TODOS los núcleos y ahoga a la propia app. Bajar la prioridad a
    /// «por debajo de lo normal» no bastaba: medido en esta máquina (8 hilos, x265), con la
    /// CPU al 100 % la ventana seguía recibiendo solo un ~4,9 % de CPU y la interfaz respondía
    /// tarde. La razón es que «por debajo de lo normal» aún COMPITE por los ocho núcleos; si
    /// los ocho están llenos, el hilo de la interfaz espera su turno.
    ///
    /// La cura de verdad es RESERVAR núcleos: se le prohíbe a ffmpeg usar uno (o unos pocos)
    /// núcleos con la afinidad de proceso, así la interfaz SIEMPRE tiene un núcleo libre,
    /// pase lo que pase con la codificación. Cuesta ese tanto por ciento de velocidad de
    /// encode (un núcleo de ocho ≈ 12 %), que en una tarea de fondo no se nota y en la
    /// interfaz —que es lo que el usuario está mirando— se nota muchísimo.
    ///
    /// Se mantiene además «por debajo de lo normal»: sobre los núcleos que sí comparte, la
    /// interfaz (prioridad normal) le gana el turno igualmente.
    /// </summary>
    private static void ApartarDelPasoDeLaInterfaz(Process proc)
    {
        // Puede haber terminado ya (un ffmpeg que falla al instante) o negarse por permisos:
        // no poder apartarlo no es motivo para no codificar.
        try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        try
        {
            int cores = Environment.ProcessorCount;
            // Por debajo de 3 núcleos no se reserva: quitarle uno a una máquina de 2 dejaría
            // la codificación coja sin ganar una interfaz fluida de verdad.
            if (cores < 3) return;

            // Se reserva ~1 de cada 4 (mínimo 1) para la app. En una máquina de 8 son 2, que
            // en equipos con HyperThreading libera un núcleo físico entero para la interfaz.
            int reservados = Math.Max(1, cores / 4);
            int usables = cores - reservados;
            // Máscara con los `usables` núcleos bajos encendidos; los altos quedan libres para
            // la interfaz, el hilo de render y todo lo demás de la app.
            nint mascara = (nint)((1L << usables) - 1);
            proc.ProcessorAffinity = mascara;
        }
        catch { /* la afinidad puede negarse (permisos, >64 núcleos): la prioridad ya ayuda */ }
    }

    private async Task<(int code, string err)> RunFfmpegAsync(
        List<string> args, double durSec, IEngineReporter rep, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Ffmpeg)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        ApartarDelPasoDeLaInterfaz(proc);
        _active = proc;
        var err = new StringBuilder();
        try
        {
            var stderrTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await proc.StandardError.ReadLineAsync()) != null)
                {
                    var m = TimeRx.Match(line);
                    if (m.Success && durSec > 0)
                    {
                        double sec = int.Parse(m.Groups[1].Value) * 3600
                                   + int.Parse(m.Groups[2].Value) * 60
                                   + int.Parse(m.Groups[3].Value)
                                   + double.Parse("0." + m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
                        rep.FileProgress(Math.Clamp(sec / durSec, 0, 1), line);
                    }
                    else if (line.Length > 0)
                    {
                        lock (err) { if (err.Length < 4000) err.AppendLine(line); }   // guardar líneas de error
                    }
                }
            });
            _ = proc.StandardOutput.ReadToEndAsync();

            try
            {
                await proc.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                ProcessControl.Resume(proc);                     // si estaba en pausa, reanudar para poder matarlo
                try { proc.Kill(entireProcessTree: true); } catch { }
                try { await stderrTask; } catch { }
                throw;
            }
            try { await stderrTask; } catch { }
            string errStr; lock (err) errStr = err.ToString();
            return (proc.ExitCode, errStr);
        }
        finally { _active = null; }
    }

    // ---------- ejecutar un proceso y capturar salida ----------
    private static async Task<(int code, string stdout, string stderr)> RunAsync(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = new Process { StartInfo = psi };
        proc.Start();
        // El sondeo y las miniaturas del import también van por aquí: un ffmpeg que busca y
        // decodifica un fotograma de un vídeo grande da un tirón a la interfaz si corre a
        // prioridad normal. Se aparta igual que la codificación.
        ApartarDelPasoDeLaInterfaz(proc);
        var so = proc.StandardOutput.ReadToEndAsync();
        var se = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, await so, await se);
    }
}
