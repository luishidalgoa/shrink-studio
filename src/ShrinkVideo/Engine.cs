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
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("tags")] public FfTags? Tags { get; set; }
    public string Lang => Tags?.Language ?? "";
}
internal sealed class FfTags { [JsonPropertyName("language")] public string? Language { get; set; } }
internal sealed class FfFormat
{
    [JsonPropertyName("bit_rate")] public string? BitRate { get; set; }
    [JsonPropertyName("duration")] public string? Duration { get; set; }
}

/// <summary>Info resumida de pistas para la lista de la UI.</summary>
public sealed class ProbeInfo
{
    public string Codec { get; set; } = "";
    public int DurationSec { get; set; }
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
/// Motor de compresión. Espejo fiel de Shrink-Video.ps1: recodifica a HEVC con
/// aceleración por hardware, conserva los idiomas de audio elegidos con el preferido
/// por defecto, y nunca toca los originales.
/// </summary>
public sealed class Engine
{
    private static readonly string[] LossyAudio = { "aac", "opus", "mp3", "vorbis" };
    private static readonly string[] CoverCodecs = { "png", "mjpeg", "bmp", "gif" };
    // Nota: .ts (MPEG-TS) se omite a propósito: colisiona con TypeScript y llenaría
    // la lista de archivos de código en carpetas de desarrollo.
    public static readonly string[] VideoExtensions =
        { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".webm", ".mpg", ".mpeg", ".flv" };

    private string? _cachedEncoder;

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
    public async Task<string> SelectEncoderAsync()
    {
        if (_cachedEncoder != null) return _cachedEncoder;
        var (_, encList, _) = await RunAsync(Ffmpeg, new[] { "-hide_banner", "-encoders" });
        foreach (var cand in new[] { "hevc_qsv", "hevc_nvenc", "hevc_amf" })
        {
            if (!encList.Contains(cand)) continue;
            var (code, _, _) = await RunAsync(Ffmpeg, new[]
            {
                "-hide_banner", "-loglevel", "error", "-f", "lavfi",
                "-i", "testsrc=size=640x480:duration=0.1", "-c:v", cand, "-f", "null", "-"
            });
            if (code == 0) return _cachedEncoder = cand;
        }
        return _cachedEncoder = "libx265";
    }

    public static bool IsHardware(string encoder) => encoder != "libx265";

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

    private static List<string> EncoderArgs(string encoder, int quality) => encoder switch
    {
        "hevc_qsv" => new() { "-c:v", "hevc_qsv", "-global_quality", $"{quality}", "-preset", "slow" },
        "hevc_nvenc" => new() { "-c:v", "hevc_nvenc", "-rc", "vbr", "-cq", $"{quality}", "-preset", "p6", "-tune", "hq" },
        "hevc_amf" => new() { "-c:v", "hevc_amf", "-rc", "cqp", "-qp_i", $"{quality}", "-qp_p", $"{quality}", "-quality", "quality" },
        _ => new() { "-c:v", "libx265", "-crf", $"{quality}", "-preset", "medium" },
    };

    // ---------- análisis de pistas (para el scan de la UI) ----------
    public async Task<ProbeInfo> ProbeAsync(string path)
    {
        var pr = await ProbeFullAsync(path);
        var info = new ProbeInfo();
        if (pr == null) return info;
        var vid = pr.Streams.FirstOrDefault(s => s.CodecType == "video" && !CoverCodecs.Contains(s.CodecName));
        info.Codec = vid?.CodecName ?? "";
        if (double.TryParse(pr.Format?.Duration, System.Globalization.CultureInfo.InvariantCulture, out var d))
            info.DurationSec = (int)d;
        info.AudioLangs = pr.Streams.Where(s => s.CodecType == "audio")
            .Select(s => string.IsNullOrEmpty(s.Lang) ? "?" : s.Lang).Distinct().ToList();
        info.SubLangs = pr.Streams.Where(s => s.CodecType == "subtitle")
            .Select(s => string.IsNullOrEmpty(s.Lang) ? "?" : s.Lang).Distinct().ToList();
        return info;
    }

    private static async Task<FfProbe?> ProbeFullAsync(string path)
    {
        var (code, stdout, _) = await RunAsync(Ffprobe, new[]
        {
            "-v", "error",
            "-show_entries", "stream=index,codec_type,codec_name,height:stream_tags=language:format=bit_rate,duration",
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
        var encoder = await SelectEncoderAsync();
        int quality = opt.Quality > 0 ? opt.Quality : (encoder == "libx265" ? 23 : 27);
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
            string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(name) + ".mkv");

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
                for (int i = 0; i < audio.Count; i++)
                {
                    if (opt.AudioBitrate == 0 || LossyAudio.Contains(audio[i].CodecName))
                        a.AddRange(new[] { $"-c:a:{i}", "copy" });
                    else
                        a.AddRange(new[] { $"-c:a:{i}", "aac", $"-b:a:{i}", $"{opt.AudioBitrate}k" });
                }
                if (withSubs) a.AddRange(new[] { "-c:s", "copy" });
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
                if (ct.IsCancellationRequested) { rep.Log("    cancelado."); throw new OperationCanceledException(); }
                rep.Log($"    ERROR al codificar (código {code})");
                var r = new FileResult { Name = name, InBytes = fi.Length, OutBytes = null, Status = "ERROR" };
                results.Add(r); rep.FileDone(r);
            }
        }
        return results;
    }

    // ---------- ejecutar ffmpeg con progreso + cancelación ----------
    private static readonly Regex TimeRx =
        new(@"time=(\d+):(\d+):(\d+)\.(\d+)", RegexOptions.Compiled);

    private static async Task<int> RunFfmpegAsync(
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
            try { proc.Kill(entireProcessTree: true); } catch { }
            try { await stderrTask; } catch { }
            throw;
        }
        try { await stderrTask; } catch { }
        return proc.ExitCode;
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
