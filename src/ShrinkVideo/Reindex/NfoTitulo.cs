using System.Xml.Linq;

namespace ShrinkVideo.Reindex;

/// <summary>
/// Saca el «title» de un .nfo de Kodi/Jellyfin. Es la fuente ideal para ficheros sin título
/// en el nombre: leer un XML pequeño es instantáneo, mientras que sondear el vídeo con
/// ffprobe en una carpeta de OneDrive obliga a hidratar cabeceras desde la nube.
///
/// Función pura sobre el CONTENIDO: el disco lo lee quien llama.
/// </summary>
public static class NfoTitulo
{
    public static string? Extraer(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try
        {
            var doc = XDocument.Parse(xml.Trim());
            var titulo = doc.Root?.Element("title")?.Value?.Trim();
            return string.IsNullOrWhiteSpace(titulo) ? null : titulo;
        }
        catch
        {
            // Un .nfo roto o que no es XML no puede tumbar la identificación: simplemente
            // no aporta título.
            return null;
        }
    }
}
