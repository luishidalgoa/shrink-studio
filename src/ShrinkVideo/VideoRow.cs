using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShrinkVideo;

/// <summary>Una fila de la lista de vídeos, con notificación de cambios para la UI.</summary>
public sealed class VideoRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void N([CallerMemberName] string? p = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    private string _estado = "…", _codec = "", _audio = "", _subs = "", _dur = "";

    public string Estado { get => _estado; set { _estado = value; N(); N(nameof(EstadoBrush)); } }

    /// <summary>Ya está en un códec eficiente con bitrate bajo: no merece la pena recomprimirlo.</summary>
    public bool YaComprimido { get; set; }

    /// <summary>
    /// Color del estado. Verde = terminado con ahorro · rojo = error · morado = en curso ·
    /// apagado = saltado o pendiente. El texto ya distingue por sí solo, así que el color
    /// solo refuerza (no se depende de él).
    /// </summary>
    public string EstadoBrush => _estado switch
    {
        var s when s.StartsWith('−') || s.StartsWith('-') => "Ok",
        var s when s.StartsWith("Error") || s.StartsWith("error") => "Err",
        var s when s.StartsWith("Comprimiendo") || s.StartsWith("En pausa") => "Live",
        _ => "Muted",
    };
    public string Codec { get => _codec; set { _codec = value; N(); } }
    public string Audio { get => _audio; set { _audio = value; N(); } }
    public string Subs { get => _subs; set { _subs = value; N(); } }
    public string Dur { get => _dur; set { _dur = value; N(); } }

    public string Name { get; init; } = "";
    public string Dir { get; init; } = "";
    public string SizeMB { get; init; } = "";
    public string Path { get; init; } = "";
    public long Bytes { get; init; }

    // Datos del análisis, para la estimación de ahorro
    public bool Probed { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public int DurationSec { get; set; }
    public int VideoBitrateKbps { get; set; }
    public int AudioBitrateKbps { get; set; }
    public int Channels { get; set; }
    public string AudioCodec { get; set; } = "";
}
