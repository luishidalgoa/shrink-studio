using System.Text.RegularExpressions;

namespace ShrinkVideo.Reindex;

/// <summary>
/// Lo que se puede deducir de UN fichero antes de mirar el catálogo. Todo es opcional:
/// un fichero puede no traer ni fecha, ni número, ni título reconocible.
/// </summary>
public sealed class FileSignals
{
    /// <summary>Ruta completa; también hace de clave del fichero en el lote.</summary>
    public string Path { get; init; } = "";
    /// <summary>Nombre con extensión, tal cual se ve.</summary>
    public string NombreArchivo { get; init; } = "";
    public string Extension { get; init; } = "";
    /// <summary>Carpeta contenedora (solo el nombre), de donde sale la temporada.</summary>
    public string Carpeta { get; init; } = "";

    public DateOnly? Fecha { get; init; }
    /// <summary>Número que YA trae el fichero (puede estar mal: eso es lo que venimos a arreglar).</summary>
    public int? Indice { get; init; }
    /// <summary>Sufijo de sub-segmento de «[438a]»: distingue mitades del mismo episodio.</summary>
    public string? SubSegmento { get; init; }
    public bool Especial { get; init; }
    public int? IndiceEspecial { get; init; }
    public int? Temporada { get; init; }

    /// <summary>Lo que queda del nombre tras quitar fecha, índice y marcas.</summary>
    public string TituloNombre { get; init; } = "";
    /// <summary>Título del metadato del contenedor (MKV/MP4). Novedad frente a los scripts.</summary>
    public string? TituloMeta { get; init; }

    /// <summary>
    /// Trozos del título si el nombre venía multi-segmento (separador ┃ o |). Si solo hay
    /// uno, la lista está vacía: no hay nada que partir.
    /// </summary>
    public IReadOnlyList<string> Segmentos { get; init; } = Array.Empty<string>();

    /// <summary>Identidad estable del fichero para recordar decisiones (§4 de la epic).</summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>Si el fichero no se pudo leer o el nombre no da nada, el motivo.</summary>
    public string? Error { get; init; }

    /// <summary>¿Hay algo con lo que identificar? Si no, es ERROR de entrada.</summary>
    public bool TieneSeñales => Indice.HasValue || Fecha.HasValue
                                || !string.IsNullOrWhiteSpace(TituloNombre)
                                || !string.IsNullOrWhiteSpace(TituloMeta);
}

/// <summary>Lee las señales del nombre del fichero. Función pura: mismo nombre ⇒ mismas señales.</summary>
public static partial class SignalExtractor
{
    // «2005-04-22 …» al inicio
    [GeneratedRegex(@"^\s*(\d{4})-(\d{2})-(\d{2})")]
    private static partial Regex RxFecha();

    // «[S]» o «[S12]» — desvía a la rama de especiales
    [GeneratedRegex(@"\[\s*S\s*(\d*)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex RxEspecial();

    // «[438]» o «[438a]» (sub-segmento)
    [GeneratedRegex(@"\[\s*(\d{1,4})\s*([a-z])?\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex RxIndiceCorchetes();

    // «S03E12» / «s03e12»
    [GeneratedRegex(@"\bS(\d{1,4})E(\d{1,4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex RxSxxExx();

    // «E72» suelto
    [GeneratedRegex(@"\bE(\d{1,4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex RxEpisodioE();

    // «72 Título» — número al principio seguido de separador
    [GeneratedRegex(@"^\s*(\d{1,4})\s*[-–_.\s]+")]
    private static partial Regex RxNumeroInicial();

    // Carpeta: «Season 2007», «Temporada 3», «2007», «S03»
    [GeneratedRegex(@"^(?:season|temporada|t|s)?\s*_?-?\s*(\d{1,4})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RxCarpetaTemporada();

    /// <summary>Separadores de multi-segmento del nombre.</summary>
    private static readonly char[] SeparadoresSegmento = { '┃', '|' };

    /// <summary>
    /// Extrae las señales de un nombre de fichero. No toca el disco: <paramref name="tituloMeta"/>
    /// lo aporta quien haya leído el contenedor, para que esta función siga siendo testeable.
    /// </summary>
    public static FileSignals Extract(string rutaCompleta, string? carpeta = null, string? tituloMeta = null,
                                      string? fingerprint = null)
    {
        var nombreArchivo = System.IO.Path.GetFileName(rutaCompleta);
        var ext = System.IO.Path.GetExtension(rutaCompleta);
        var resto = System.IO.Path.GetFileNameWithoutExtension(rutaCompleta) ?? "";

        // Un nombre suelto sin carpeta deja GetDirectoryName en cadena vacía, y DirectoryInfo
        // lanza con eso. Antes reventaba en vez de limitarse a no saber la temporada.
        if (carpeta == null)
        {
            var dir = System.IO.Path.GetDirectoryName(rutaCompleta);
            carpeta = string.IsNullOrEmpty(dir) ? "" : new System.IO.DirectoryInfo(dir).Name;
        }

        DateOnly? fecha = null;
        int? indice = null, indiceEspecial = null;
        string? subSegmento = null;
        bool especial = false;

        // 1. fecha al inicio — la primera, para que su «22» no se confunda con un índice
        var mFecha = RxFecha().Match(resto);
        if (mFecha.Success)
        {
            if (DateOnly.TryParse($"{mFecha.Groups[1].Value}-{mFecha.Groups[2].Value}-{mFecha.Groups[3].Value}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d))
                fecha = d;
            resto = resto[mFecha.Length..];
        }

        // 2. especial antes que índice: «[S12]» no debe leerse como número de episodio regular
        var mEsp = RxEspecial().Match(resto);
        if (mEsp.Success)
        {
            especial = true;
            if (int.TryParse(mEsp.Groups[1].Value, out var ne)) indiceEspecial = ne;
            resto = resto.Remove(mEsp.Index, mEsp.Length);
        }

        // 3. índice: corchetes → SxxExx → E72 → número inicial (en orden de fiabilidad)
        var mCor = RxIndiceCorchetes().Match(resto);
        if (mCor.Success)
        {
            indice = int.Parse(mCor.Groups[1].Value);
            if (mCor.Groups[2].Success) subSegmento = mCor.Groups[2].Value.ToLowerInvariant();
            resto = resto.Remove(mCor.Index, mCor.Length);
        }
        else
        {
            var mSE = RxSxxExx().Match(resto);
            if (mSE.Success)
            {
                indice = int.Parse(mSE.Groups[2].Value);
                resto = resto.Remove(mSE.Index, mSE.Length);
            }
            else
            {
                var mE = RxEpisodioE().Match(resto);
                if (mE.Success)
                {
                    indice = int.Parse(mE.Groups[1].Value);
                    resto = resto.Remove(mE.Index, mE.Length);
                }
                else
                {
                    var mNum = RxNumeroInicial().Match(resto);
                    if (mNum.Success)
                    {
                        indice = int.Parse(mNum.Groups[1].Value);
                        resto = resto[mNum.Length..];
                    }
                }
            }
        }

        // 4. temporada de la carpeta contenedora
        int? temporada = null;
        var mCarpeta = RxCarpetaTemporada().Match(carpeta.Trim());
        if (mCarpeta.Success && int.TryParse(mCarpeta.Groups[1].Value, out var t)) temporada = t;
        else
        {
            var mCarpSE = RxSxxExx().Match(carpeta);
            if (mCarpSE.Success) temporada = int.Parse(mCarpSE.Groups[1].Value);
        }

        // 5. lo que queda es el título; los trozos si venía multi-segmento
        var titulo = LimpiarTitulo(resto);
        var segmentos = titulo.Split(SeparadoresSegmento, StringSplitOptions.RemoveEmptyEntries)
                              .Select(LimpiarTitulo)
                              .Where(s => s.Length > 0)
                              .ToList();
        if (segmentos.Count < 2) segmentos.Clear();   // no había nada que partir

        return new FileSignals
        {
            Path = rutaCompleta,
            NombreArchivo = nombreArchivo,
            Extension = ext,
            Carpeta = carpeta,
            Fecha = fecha,
            Indice = indice,
            SubSegmento = subSegmento,
            Especial = especial,
            IndiceEspecial = indiceEspecial,
            Temporada = temporada,
            TituloNombre = titulo,
            TituloMeta = string.IsNullOrWhiteSpace(tituloMeta) ? null : tituloMeta.Trim(),
            Segmentos = segmentos,
            Fingerprint = fingerprint ?? rutaCompleta,
        };
    }

    /// <summary>Quita separadores sobrantes de los bordes y espacios repetidos.</summary>
    private static string LimpiarTitulo(string s)
    {
        s = s.Trim().Trim('-', '–', '_', '.', ' ', '\t');
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }
}
