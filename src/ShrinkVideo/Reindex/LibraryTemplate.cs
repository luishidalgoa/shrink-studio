using System.IO;
using System.Text;

namespace ShrinkVideo.Reindex;

/// <summary>
/// La convención canónica de nombres de la biblioteca: «&lt;serie&gt; - S&lt;temp&gt;E&lt;num&gt; - &lt;título&gt;».
///
/// OJO: no es lo mismo que el «Renombrado libre» (la herramienta tipo PowerRename). Aquel
/// transforma el nombre que ya hay con búsqueda/reemplazo; este CONSTRUYE el nombre desde
/// el catálogo. Son dos sistemas distintos y el diseño decide no unificarlos.
/// </summary>
public sealed class LibraryTemplate
{
    public const string PatronPorDefecto = "<serie> - S<temp>E<num> - <título>";

    public string Patron { get; }

    public LibraryTemplate(string? patron = null) =>
        Patron = string.IsNullOrWhiteSpace(patron) ? PatronPorDefecto : patron.Trim();

    /// <summary>
    /// Nombre final (con extensión) para un episodio identificado. Devuelve null si el
    /// patrón no deja nada utilizable, para no crear jamás un fichero sin nombre.
    /// </summary>
    public string? Render(ReindexCatalog catalogo, CatalogEpisode episodio, FileSignals archivo)
    {
        var sb = new StringBuilder(Patron);

        // La temporada del episodio manda; si el catálogo no la trae, se usa la que
        // insinúa la carpeta del fichero, y si tampoco, se deja el hueco vacío.
        var temporada = episodio.Temporada?.ToString() ?? archivo.Temporada?.ToString() ?? "";

        sb.Replace("<serie>", catalogo.Serie);
        sb.Replace("<temp>", temporada);
        sb.Replace("<num>", episodio.Num.ToString());
        sb.Replace("<título>", episodio.TituloCompleto);
        sb.Replace("<titulo>", episodio.TituloCompleto);   // sin tilde, por comodidad
        // Los sub-segmentos («[438a]») necesitan distinguirse o se pisarían al renombrar
        sb.Replace("<seg>", archivo.SubSegmento ?? "");

        var nombre = Limpiar(sb.ToString());
        if (nombre.Length == 0) return null;

        return nombre + archivo.Extension;
    }

    /// <summary>
    /// Caracteres prohibidos en un nombre de fichero de Windows. Se usa esta lista fija en
    /// TODAS las plataformas en vez de <c>Path.GetInvalidFileNameChars()</c>, que en Linux
    /// solo devuelve «/» y el nulo: una biblioteca de vídeo termina casi siempre en un disco
    /// compartido o en un NAS que la sirve a Windows, así que un «:» colado desde Linux
    /// rompería el fichero justo donde se va a ver. Mejor un nombre válido en todas partes.
    /// </summary>
    private static readonly char[] Prohibidos = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    /// <summary>
    /// Quita lo que no vale en un nombre de fichero y recorta el resultado para no pasarse
    /// del límite de ruta. Un título con «:» o «?» es de lo más normal en estas series, así
    /// que esto salta constantemente.
    /// </summary>
    private static string Limpiar(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(Array.IndexOf(Prohibidos, ch) >= 0 || char.IsControl(ch) ? ' ' : ch);

        var limpio = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
        // Windows no admite terminar en punto o espacio: el propio explorador los borra
        limpio = limpio.TrimEnd('.', ' ');

        // 150 deja aire para la carpeta contenedora sin acercarse al límite de MAX_PATH
        if (limpio.Length > 150) limpio = limpio[..150].TrimEnd('.', ' ');
        return limpio;
    }
}
