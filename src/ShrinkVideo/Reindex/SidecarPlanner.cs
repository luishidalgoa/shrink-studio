using System.IO;

namespace ShrinkVideo.Reindex;

/// <summary>
/// Los ficheros compañeros de un vídeo (.nfo, .srt, carátulas con el mismo nombre) tienen
/// que viajar con él al renombrar: si el vídeo cambia de nombre y su .nfo se queda con el
/// viejo, el reproductor de biblioteca deja de asociarlos y el metadato se vuelve huérfano.
///
/// Función pura: entra (origen, destino, lo que hay en la carpeta), salen los pares a mover.
/// El disco lo toca quien aplica, no esto.
/// </summary>
public static class SidecarPlanner
{
    /// <summary>
    /// Compañero = mismo nombre base que el vídeo + «.» + lo que sea («.nfo», «.es.srt»).
    /// El sufijo se conserva entero: un subtítulo «.es.srt» sigue siendo «.es.srt».
    /// </summary>
    public static List<(string De, string A)> Planear(
        string origenVideo, string destinoVideo, IEnumerable<string> enCarpeta)
    {
        var plan = new List<(string, string)>();
        if (string.Equals(origenVideo, destinoVideo, StringComparison.OrdinalIgnoreCase))
            return plan;   // el vídeo no se mueve: sus compañeros tampoco

        var baseOrigen = SinExtensionDeVideo(origenVideo);
        var baseDestino = SinExtensionDeVideo(destinoVideo);
        var carpetaDestino = Path.GetDirectoryName(destinoVideo) ?? "";

        foreach (var f in enCarpeta)
        {
            if (string.Equals(f, origenVideo, StringComparison.OrdinalIgnoreCase)) continue;

            var nombre = Path.GetFileName(f);
            if (!nombre.StartsWith(baseOrigen + ".", StringComparison.OrdinalIgnoreCase)) continue;

            var sufijo = nombre[baseOrigen.Length..];   // «.nfo», «.es.srt»…
            plan.Add((f, Path.Combine(carpetaDestino, baseDestino + sufijo)));
        }
        return plan;
    }

    private static string SinExtensionDeVideo(string ruta) =>
        Path.GetFileNameWithoutExtension(ruta);
}
