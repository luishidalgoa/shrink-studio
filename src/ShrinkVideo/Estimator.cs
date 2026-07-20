namespace ShrinkVideo;

/// <summary>Pronóstico de tamaño/ahorro y valoraciones calidad↔ahorro para un vídeo + opciones.</summary>
public sealed class Estimate
{
    public bool Valid { get; set; }
    public long OrigBytes { get; set; }
    public long EstBytes { get; set; }
    public long SavedBytes => Math.Max(0, OrigBytes - EstBytes);
    public int SavedPct => OrigBytes > 0 ? (int)Math.Round(100.0 * SavedBytes / OrigBytes) : 0;
    public int EstVideoKbps { get; set; }
    public int EstAudioKbps { get; set; }
    public int VideoQuality { get; set; }   // 0..5
    public int VideoSaving { get; set; }    // 0..5
    public int AudioQuality { get; set; }   // 0..5
    public int AudioSaving { get; set; }    // 0..5
}

/// <summary>
/// Modelo heurístico de estimación: aproxima el bitrate resultante a partir de la
/// resolución, fps, códec y calidad elegidos. Es una orientación, no una medida exacta.
/// </summary>
public static class Estimator
{
    private static readonly string[] Mp4Audio = { "aac", "ac3", "eac3", "mp3", "alac" };

    /// <summary>Cuántas pistas de audio se conservan según los idiomas elegidos.</summary>
    private static int KeptAudioTracks(VideoRow r, EncodeOptions o)
    {
        var langs = string.IsNullOrWhiteSpace(r.Audio)
            ? new List<string>()
            : r.Audio.Split('+').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (langs.Count == 0) return 1;
        if (o.KeepLangs.Count == 0 || o.KeepLangs.Contains("all")) return langs.Count;
        int n = langs.Count(l => o.KeepLangs.Contains(l) || l == o.Lang);
        return n < 1 ? langs.Count : n;   // si ningún idioma coincide, el motor conserva todas
    }

    public static Estimate Compute(VideoRow r, EncodeOptions o)
    {
        var e = new Estimate { OrigBytes = r.Bytes };
        if (!r.Probed || r.DurationSec <= 0) return e;
        e.Valid = true;

        // --- modo solo audio: sin vídeo ---
        if (o.AudioOnly)
        {
            int nA = KeptAudioTracks(r, o);
            int aKbps = (o.AudioFormat == "flac"
                ? (r.AudioBitrateKbps > 0 ? Math.Max(r.AudioBitrateKbps, 700) : 800)
                : (o.AudioBitrate > 0 ? o.AudioBitrate : 192)) * nA;
            e.EstAudioKbps = aKbps;
            e.EstBytes = Math.Min((long)(aKbps * 1000.0 / 8.0 * r.DurationSec * 1.02), r.Bytes);
            e.VideoQuality = 0; e.VideoSaving = 5;               // sin vídeo = máximo ahorro de espacio
            e.AudioQuality = o.AudioFormat == "flac" ? 5 : Clamp(aKbps / 48.0);
            e.AudioSaving = Clamp((1 - e.EstBytes / (double)Math.Max(r.Bytes, 1)) * 5);
            return e;
        }
        if (r.Width <= 0 || r.Height <= 0) { e.Valid = false; return e; }

        // Resolución de salida (posible reescalado)
        int outH = r.Height, outW = r.Width;
        if (o.MaxHeight > 0 && r.Height > o.MaxHeight)
        {
            double scale = (double)o.MaxHeight / r.Height;
            outH = o.MaxHeight;
            outW = (int)Math.Round(r.Width * scale / 2) * 2;
        }
        double fps = r.Fps > 1 ? r.Fps : 25;

        // Calidad efectiva (CRF). 0 = automático → referencia 27.
        int crf = o.Quality > 0 ? o.Quality : 27;
        // Eficiencia relativa a HEVC
        double codecFactor = o.VideoCodec switch { "h264" => 1.8, "av1" => 0.65, _ => 1.0 };
        // bits por píxel·frame (HEVC a CRF 28); cada -6 de CRF duplica el bitrate
        double bpp = 0.05 * Math.Pow(2, (28.0 - crf) / 6.0) * codecFactor;

        int estVideoKbps = (int)(bpp * outW * outH * fps / 1000.0);
        if (r.VideoBitrateKbps > 0) estVideoKbps = Math.Min(estVideoKbps, r.VideoBitrateKbps); // no "recomprimir hacia arriba"
        estVideoKbps = Math.Max(estVideoKbps, 120);

        // ¿el audio se copia tal cual o se recodifica? (WebM siempre Opus; MP4 no copia FLAC/PCM…)
        bool webm = o.Container == "webm";
        bool willCopy = o.AudioBitrate == 0 && !webm
                        && !(o.Container == "mp4" && !Mp4Audio.Contains(r.AudioCodec));
        int estAudioKbps = willCopy
            ? (r.AudioBitrateKbps > 0 ? r.AudioBitrateKbps : 192)
            : (o.AudioBitrate > 0 ? o.AudioBitrate : (webm ? 160 : 192));
        if (willCopy && r.AudioBitrateKbps > 0) estAudioKbps = Math.Min(estAudioKbps, r.AudioBitrateKbps);

        int numAudio = KeptAudioTracks(r, o);
        e.EstVideoKbps = estVideoKbps;
        e.EstAudioKbps = estAudioKbps * numAudio;   // suma de todas las pistas conservadas
        long bytes = (long)((estVideoKbps + e.EstAudioKbps) * 1000.0 / 8.0 * r.DurationSec * 1.02);
        e.EstBytes = Math.Min(bytes, r.Bytes);

        // Valoraciones 0..5
        double q = 5 - (crf - 20) / 2.4;                 // CRF 20≈5 · 27≈2.9 · 30≈2
        if (o.VideoCodec == "av1") q += 0.6; else if (o.VideoCodec == "h264") q -= 0.4;
        if (outH < r.Height) q -= 0.6;
        e.VideoQuality = Clamp(q);
        double savV = r.VideoBitrateKbps > 0 ? 1 - (double)estVideoKbps / r.VideoBitrateKbps : 0.5;
        e.VideoSaving = Clamp(savV * 5.0 + 0.5);

        if (willCopy) { e.AudioQuality = 5; e.AudioSaving = 1; }
        else
        {
            e.AudioQuality = Clamp(estAudioKbps / 48.0);   // 96≈2 · 192≈4 · 256≈5
            double savA = r.AudioBitrateKbps > 0 ? 1 - (double)estAudioKbps / r.AudioBitrateKbps : 0.4;
            e.AudioSaving = Clamp(savA * 5.0 + 0.5);
        }
        return e;
    }

    private static int Clamp(double v) => Math.Max(0, Math.Min(5, (int)Math.Round(v)));
}
