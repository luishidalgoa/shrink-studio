namespace ShrinkVideo;

/// <summary>
/// El único sitio donde se traduce «lo que eligió el usuario en los desplegables» a
/// <see cref="EncodeOptions"/>.
///
/// Existe porque Comprimir y Recortes ofrecen los MISMOS ajustes de salida: con dos copias
/// del switch, el día que se añada un formato o cambie un CRF una de las dos se queda atrás
/// y nadie se entera hasta que el fichero sale distinto. Aquí también viven los textos de
/// los desplegables, para que las dos páginas ni siquiera puedan ofrecer opciones distintas.
/// </summary>
public static class OpcionesSalida
{
    public static readonly string[] Formatos =
    {
        "MKV", "MP4", "WebM", "MP3 · solo audio", "M4A · solo audio", "FLAC · solo audio", "Opus · solo audio",
    };
    public static readonly string[] Codecs = { "H.265", "H.264", "AV1" };
    public static readonly string[] Calidades =
        { "Automática", "22 · muy alta", "24 · alta", "27 · equilibrada", "30 · muy comprimida" };
    public static readonly string[] Resoluciones = { "Sin cambio", "1080p", "720p", "480p" };
    public static readonly string[] Audios =
    {
        "Máxima (copiar original)", "AAC 192 kbps", "AAC 160 kbps", "AAC 128 kbps", "AAC 96 kbps",
    };

    public static string CodecDe(int i) => i switch { 1 => "h264", 2 => "av1", _ => "hevc" };
    public static int CalidadDe(int i) => i switch { 1 => 22, 2 => 24, 3 => 27, 4 => 30, _ => 0 };
    public static int AlturaDe(int i) => i switch { 1 => 1080, 2 => 720, 3 => 480, _ => 0 };
    public static int AudioDe(int i) => i switch { 1 => 192, 2 => 160, 3 => 128, 4 => 96, _ => 0 };

    /// <summary>Aplica el formato elegido sobre unas opciones ya empezadas.</summary>
    public static void PonerFormato(EncodeOptions opt, int i)
    {
        switch (i)
        {
            case 1: opt.Container = "mp4"; break;
            case 2: opt.Container = "webm"; break;
            case 3: opt.AudioOnly = true; opt.AudioFormat = "mp3"; break;
            case 4: opt.AudioOnly = true; opt.AudioFormat = "m4a"; break;
            case 5: opt.AudioOnly = true; opt.AudioFormat = "flac"; break;
            case 6: opt.AudioOnly = true; opt.AudioFormat = "opus"; break;
            default: opt.Container = "mkv"; break;
        }
    }

    /// <summary>Las cinco elecciones, ya convertidas en opciones de codificación.</summary>
    public static EncodeOptions Construir(int formato, int codec, int calidad, int resolucion, int audio)
    {
        var opt = new EncodeOptions
        {
            VideoCodec = CodecDe(codec),
            Quality = CalidadDe(calidad),
            MaxHeight = AlturaDe(resolucion),
            AudioBitrate = AudioDe(audio),
        };
        PonerFormato(opt, formato);
        return opt;
    }
}
