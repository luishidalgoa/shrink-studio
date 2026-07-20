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
}

/// <summary>Reporta el avance de la compresión a la UI.</summary>
public interface IEngineReporter
{
    void Log(string line);
    void FileStart(int index, int total, string name, double durationSec);
    void FileProgress(double fraction, string rawLine);   // 0..1 del archivo actual
    void FileDone(FileResult result);
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
    private static (string[] hw, string sw) Candidates(string codec) => codec switch
    {
        "h264" => (new[] { "h264_qsv", "h264_nvenc", "h264_amf" }, "libx264"),
        "av1" => (new[] { "av1_qsv", "av1_nvenc", "av1_amf" }, "libsvtav1"),
        _ => (new[] { "hevc_qsv", "hevc_nvenc", "hevc_amf" }, "libx265"),
    };

    public async Task<string> SelectEncoderAsync(string codec = "hevc")
    {
        if (_cachedEncoder.TryGetValue(codec, out var cached)) return cached;
        var (hw, sw) = Candidates(codec);
        var (_, encList, _) = await RunAsync(Ffmpeg, new[] { "-hide_banner", "-encoders" });
        foreach (var cand in hw)
        {
            if (!encList.Contains(cand)) continue;
            var (code, _, _) = await RunAsync(Ffmpeg, new[]
            {
                "-hide_banner", "-loglevel", "error", "-f", "lavfi",
                "-i", "testsrc=size=640x480:duration=0.1", "-c:v", cand, "-f", "null", "-"
            });
            if (code == 0) return _cachedEncoder[codec] = cand;
        }
        return _cachedEncoder[codec] = sw;
    }

    public static bool IsHardware(string encoder) => !encoder.StartsWith("lib");

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
    private sealed class NullReporter : IEngineReporter
    {
        public static readonly NullReporter Instance = new();
        public void Log(string l) { }
        public void FileStart(int i, int t, string n, double d) { }
        public void FileProgress(double f, string r) { }
        public void FileDone(FileResult r) { }
    }

    /// <summary>
    /// Renderiza 10 s desde `startSec` con el códec/calidad/resolución elegidos, a un archivo
    /// temporal, para que el usuario compruebe el resultado antes de comprimir. El audio se pasa
    /// a AAC para que se reproduzca en cualquier reproductor. Devuelve la ruta o null si falla.
    /// </summary>
    public async Task<string?> PreviewAsync(string input, EncodeOptions opt, int startSec, string dest, CancellationToken ct)
    {
        var encoder = await SelectEncoderAsync(opt.VideoCodec);
        int quality = opt.Quality > 0 ? opt.Quality : (IsHardware(encoder) ? 27 : 23);
        var pr = await ProbeFullAsync(input);
        var video = pr?.Streams.FirstOrDefault(s => s.CodecType == "video" && !CoverCodecs.Contains(s.CodecName));

        var a = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-ss", startSec.ToString(), "-t", "10", "-i", input,
            "-map", "0:v:0", "-map", "0:a:0?",
        };
        if (opt.MaxHeight > 0 && (video?.Height ?? 0) > opt.MaxHeight)
            a.AddRange(new[] { "-vf", $"scale=-2:{opt.MaxHeight}" });
        a.AddRange(EncoderArgs(encoder, quality));
        int abr = opt.AudioBitrate > 0 ? opt.AudioBitrate : 192;
        a.AddRange(new[] { "-c:a", "aac", "-b:a", $"{abr}k", dest });

        int code = await RunFfmpegAsync(a, 10, NullReporter.Instance, ct);
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
        "libsvtav1" => new() { "-c:v", "libsvtav1", "-crf", $"{quality}", "-preset", "6" },
        _ => new() { "-c:v", encoder, "-crf", $"{quality}", "-preset", "medium" },   // libx264 / libx265
    };

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
            return JsonSerializer.Deserialize<FfProbe>(stdout,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    // ---------- ¿aún descargando? ----------
    private static bool StillDownloading(string path)
    {
        foreach (var ext in new[] { ".part", ".crdownload", ".!ut", ".downloading", ".tmp", "!qB" })
            if (File.Exists(path + ext)) return true;
        try { using var s = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None); }
        catch { return true; }   // bloqueado por el gestor de descargas
        return false;
    }

    // ---------- compresión ----------
    public async Task<List<FileResult>> CompressAsync(
        IReadOnlyList<string> files, EncodeOptions opt, IEngineReporter rep, CancellationToken ct)
    {
        var results = new List<FileResult>();
        var encoder = await SelectEncoderAsync(opt.VideoCodec);
        int quality = opt.Quality > 0 ? opt.Quality : (IsHardware(encoder) ? 27 : 23);
        var encArgs = EncoderArgs(encoder, quality);
        rep.Log($"Codificador: {encoder} [{(IsHardware(encoder) ? "hardware" : "software (CPU, lento)")}] · calidad {quality}");

        var keepLangs = opt.KeepLangs.Count > 0 ? opt.KeepLangs : new List<string> { opt.Lang, "eng" };
        bool keepAll = keepLangs.Contains("all");
        bool subsAll = opt.SubLangs == null || opt.SubLangs.Count == 0 || opt.SubLangs.Contains("all");

        int total = files.Count, n = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            n++;
            var fi = new FileInfo(f);
            string name = fi.Name;
            string outDir = opt.Output ?? Path.Combine(fi.DirectoryName!, "comprimido");
            string ext = opt.Container == "mp4" ? ".mp4" : ".mkv";
            string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(name) + ext);

            if (File.Exists(outPath) && !opt.Force) { rep.Log($"[{n}/{total}] {name} → ya hecho, salto"); continue; }
            if (StillDownloading(f)) { rep.Log($"[{n}/{total}] {name} → descargando aún, salto"); continue; }

            var pr = await ProbeFullAsync(f);
            if (pr == null) { rep.Log($"[{n}/{total}] {name} → no se puede leer, salto"); continue; }

            var video = pr.Streams.FirstOrDefault(s => s.CodecType == "video" && !CoverCodecs.Contains(s.CodecName));
            if (video == null) { rep.Log($"[{n}/{total}] {name} → sin pista de vídeo, salto"); continue; }

            int kbps = int.TryParse(pr.Format?.BitRate, out var br) ? br / 1000 : 0;
            if (!opt.Force && (video.CodecName is "hevc" or "av1") && kbps > 0 && kbps < 2500)
            { rep.Log($"[{n}/{total}] {name} → ya comprimido ({video.CodecName}, {kbps} kbps), salto"); continue; }

            var allAudio = pr.Streams.Where(s => s.CodecType == "audio").ToList();
            if (allAudio.Count == 0) { rep.Log($"[{n}/{total}] {name} → sin audio, salto"); continue; }
            var pref = allAudio.Where(s => s.Lang == opt.Lang).ToList();
            var other = allAudio.Where(s => s.Lang != opt.Lang && (keepAll || keepLangs.Contains(s.Lang))).ToList();
            var audio = pref.Concat(other).ToList();
            if (audio.Count == 0) audio = allAudio;   // ningún idioma coincide: conservar todo

            var subs = opt.NoSubs ? new List<FfStream>()
                : pr.Streams.Where(s => s.CodecType == "subtitle" && (subsAll || (opt.SubLangs?.Contains(s.Lang) ?? true))).ToList();

            double durSec = double.TryParse(pr.Format?.Duration, System.Globalization.CultureInfo.InvariantCulture, out var dd) ? dd : 0;

            // ---- construir argumentos ----
            List<string> BuildArgs(bool withSubs)
            {
                var a = new List<string> { "-hide_banner", "-loglevel", "warning", "-stats", "-y", "-i", f };
                a.AddRange(new[] { "-map", $"0:{video.Index}" });
                foreach (var au in audio) a.AddRange(new[] { "-map", $"0:{au.Index}" });
                if (withSubs) foreach (var s in subs) a.AddRange(new[] { "-map", $"0:{s.Index}" });
                if (opt.MaxHeight > 0 && (video.Height ?? 0) > opt.MaxHeight)
                    a.AddRange(new[] { "-vf", $"scale=-2:{opt.MaxHeight}" });
                a.AddRange(encArgs);
                bool mp4 = opt.Container == "mp4";
                for (int i = 0; i < audio.Count; i++)
                {
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
                if (withSubs) a.AddRange(new[] { "-c:s", mp4 ? "mov_text" : "copy" });
                a.AddRange(new[] { "-disposition:a:0", "default" });
                for (int i = 1; i < audio.Count; i++) a.AddRange(new[] { $"-disposition:a:{i}", "0" });
                a.AddRange(new[] { "-map_metadata", "0" });
                return a;
            }

            string langs = string.Join("+", audio.Select(x => string.IsNullOrEmpty(x.Lang) ? "?" : x.Lang));
            int dropped = allAudio.Count - audio.Count;
            string infoLine = $"audio: {langs}"
                + (dropped > 0 ? $" (descarto {dropped})" : "")
                + (subs.Count > 0 ? $", {subs.Count} sub" : "")
                + (opt.MaxHeight > 0 && (video.Height ?? 0) > opt.MaxHeight ? $", reescalo a {opt.MaxHeight}p" : "");

            if (opt.DryRun) { rep.Log($"[{n}/{total}] {name} → {infoLine}"); continue; }

            rep.Log($"[{n}/{total}] {name}");
            rep.Log($"    {infoLine}");
            rep.FileStart(n, total, name, durSec);

            Directory.CreateDirectory(outDir);
            string tmp = outPath + ".tmp.mkv";
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }

            try
            {
                int code = await RunFfmpegAsync(BuildArgs(true).Append(tmp).ToList(), durSec, rep, ct);

                // reintento sin subtítulos (algunos formatos no se copian a MKV)
                if (code != 0 && subs.Count > 0 && !ct.IsCancellationRequested)
                {
                    rep.Log("    reintentando sin subtítulos…");
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    code = await RunFfmpegAsync(BuildArgs(false).Append(tmp).ToList(), durSec, rep, ct);
                }

                if (code == 0 && File.Exists(tmp))
                {
                    try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                    File.Move(tmp, outPath);
                    long inB = fi.Length, outB = new FileInfo(outPath).Length;
                    int pct = (int)Math.Round(100 - (outB / (double)Math.Max(inB, 1) * 100));
                    rep.Log($"    OK  {inB / 1048576} MB → {outB / 1048576} MB  (-{pct}%)");
                    var r = new FileResult { Name = name, InBytes = inB, OutBytes = outB, Status = $"-{pct}%" };
                    results.Add(r); rep.FileDone(r);
                }
                else
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    rep.Log($"    ERROR al codificar (código {code})");
                    var r = new FileResult { Name = name, InBytes = fi.Length, OutBytes = null, Status = "ERROR" };
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

    // ---------- pausa / reanudación del FFmpeg en curso ----------
    private Process? _active;
    public void Pause()  { var p = _active; if (p is { HasExited: false }) ProcessControl.Suspend(p); }
    public void Resume() { var p = _active; if (p is { HasExited: false }) ProcessControl.Resume(p); }

    // ---------- ejecutar ffmpeg con progreso + cancelación ----------
    private static readonly Regex TimeRx =
        new(@"time=(\d+):(\d+):(\d+)\.(\d+)", RegexOptions.Compiled);

    private async Task<int> RunFfmpegAsync(
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
        _active = proc;
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
            return proc.ExitCode;
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
        var so = proc.StandardOutput.ReadToEndAsync();
        var se = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, await so, await se);
    }
}
