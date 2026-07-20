using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShrinkVideo;

/// <summary>Una fila de la lista de vídeos, con notificación de cambios para la UI.</summary>
public sealed class VideoRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void N([CallerMemberName] string? p = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    private bool _sel = true;
    private string _estado = "…", _codec = "", _audio = "", _subs = "", _dur = "";

    public bool Sel { get => _sel; set { _sel = value; N(); } }
    public string Estado { get => _estado; set { _estado = value; N(); } }
    public string Codec { get => _codec; set { _codec = value; N(); } }
    public string Audio { get => _audio; set { _audio = value; N(); } }
    public string Subs { get => _subs; set { _subs = value; N(); } }
    public string Dur { get => _dur; set { _dur = value; N(); } }

    public string Name { get; init; } = "";
    public string Dir { get; init; } = "";
    public string SizeMB { get; init; } = "";
    public string Path { get; init; } = "";
    public long Bytes { get; init; }
}
