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
/// <summary>
/// Una marca de la plantilla, con lo necesario para ofrecerla en la interfaz. Vive junto al
/// código que la sustituye: separarlas garantizaría que un día la lista diga una cosa y el
/// renderizado haga otra.
/// </summary>
public sealed record MarcaPlantilla(string Marca, string Nombre, string Descripcion, string Ejemplo);

public sealed class LibraryTemplate
{
    public const string PatronPorDefecto = "<serie> - S<temp>E<num> - <título>";

    /// <summary>Las marcas disponibles, en el orden en que se ofrecen.</summary>
    public static readonly MarcaPlantilla[] Marcas =
    {
        new("<serie>",  "Serie",
            "El nombre de la serie, tal cual lo escribiste en el catálogo.", "Doraemon (2005)"),
        new("<temp>",   "Temporada",
            "El año o número de temporada del episodio. Si el catálogo no lo trae, se usa el de la carpeta.", "2005"),
        new("<num>",    "Número",
            "El número del episodio según el catálogo: el que se corrige si el fichero traía otro.", "2"),
        new("<título>", "Título",
            "El título del episodio. Si tiene varias historias se unen con «+».",
            "Con calma y con prisa + La mujer de Nobita"),
        new("<seg>",    "Sub-segmento",
            "La letra de «[438a]», para distinguir mitades de un mismo episodio. Vacío si no la hay.", "a"),
        new("<num:000>", "Número con ceros",
            "El número relleno hasta esas cifras: «<num:000>» da 001, 012, 278. Si el número ya es más largo, no se recorta.",
            "012"),
        new("<título: ┃ >", "Título con otro separador",
            "Como «<título>» pero uniendo las historias con lo que pongas tras los dos puntos, espacios incluidos.",
            "El cometa ┃ Nieve en agosto"),
    };

    /// <summary>
    /// Una marca, con parámetro opcional tras los dos puntos: «&lt;num:000&gt;», «&lt;título: ┃ &gt;».
    ///
    /// El parámetro existe porque sin él la plantilla no puede describir una biblioteca que
    /// ya está ordenada con otra convención — y entonces todo sale como pendiente de
    /// renombrar aunque el trabajo esté hecho. El caso que lo destapó: ficheros
    /// «S2005E001 - A ┃ B», que la app sabe LEER (┃ es separador de segmentos en el
    /// extractor) pero no sabía ESCRIBIR.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex RxMarca =
        new(@"<(serie|temp|num|título|titulo|seg)(?::([^>]*))?>",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    public string Patron { get; }

    public LibraryTemplate(string? patron = null) =>
        Patron = string.IsNullOrWhiteSpace(patron) ? PatronPorDefecto : patron.Trim();

    /// <summary>
    /// Nombre final (con extensión) para un episodio identificado. Devuelve null si el
    /// patrón no deja nada utilizable, para no crear jamás un fichero sin nombre.
    /// </summary>
    public string? Render(ReindexCatalog catalogo, CatalogEpisode episodio, FileSignals archivo)
    {
        // La temporada del episodio manda; si el catálogo no la trae, se usa la que
        // insinúa la carpeta del fichero, y si tampoco, se deja el hueco vacío.
        var temporada = episodio.Temporada?.ToString() ?? archivo.Temporada?.ToString() ?? "";

        var texto = RxMarca.Replace(Patron, m =>
        {
            var marca = m.Groups[1].Value;
            var param = m.Groups[2].Success ? m.Groups[2].Value : null;

            return marca switch
            {
                "serie" => catalogo.Serie,
                "temp" => temporada,
                // «<num:000>» rellena hasta esas cifras. Nunca recorta: un número más largo
                // que el relleno es el número de verdad, y perder un dígito renombraría mal.
                // Con sub-segmento, la letra va PEGADA al número (E413b): sin ella, la
                // historia suelta se pisaría con el episodio completo o con su otra mitad.
                "num" => (param is { Length: > 0 }
                    ? episodio.Num.ToString().PadLeft(param.Length, '0')
                    : episodio.Num.ToString()) + archivo.SubSegmento,
                "título" or "titulo" => TituloPara(episodio, archivo, param),
                // Los sub-segmentos («[438a]») necesitan distinguirse o se pisarían al renombrar
                "seg" => archivo.SubSegmento ?? "",
                _ => m.Value,
            };
        });

        var nombre = LimpiarNombre(texto);
        if (nombre.Length == 0) return null;

        return nombre + archivo.Extension;
    }

    /// <summary>
    /// El título que corresponde: si el fichero es SOLO una historia (segmento «a», «b»…),
    /// el de ESA historia — «E413b - ¡En busca de una sonrisa!», no el episodio entero.
    /// Con una letra que no casa con ninguna historia no se inventa nada: título completo.
    /// </summary>
    private static string TituloPara(CatalogEpisode episodio, FileSignals archivo, string? separador)
    {
        var seg = archivo.SubSegmento;
        if (!string.IsNullOrEmpty(seg))
        {
            // Cada letra del sub-segmento es una historia: «b» = la 2.ª, «ac» = la 1.ª y la 3.ª.
            // Se juntan SOLO esas, en su orden, con el mismo separador que el título completo.
            var elegidos = seg
                .Select(c => char.ToLowerInvariant(c) - 'a')
                .Where(i => i >= 0 && i < episodio.TitulosSalida.Count)
                .Select(i => episodio.TitulosSalida[i])
                .ToList();
            if (elegidos.Count > 0) return string.Join(separador ?? " + ", elegidos);
        }
        return separador != null
            ? string.Join(separador, episodio.TitulosSalida)
            : episodio.TituloCompleto;
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
    public static string LimpiarNombre(string s)
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
