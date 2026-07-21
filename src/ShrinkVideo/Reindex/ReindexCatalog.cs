using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShrinkVideo.Reindex;

/// <summary>Un episodio del catálogo de referencia.</summary>
public sealed class CatalogEpisode
{
    /// <summary>Número DESTINO: el que irá en el nombre final.</summary>
    [JsonPropertyName("num")] public int Num { get; set; }
    [JsonPropertyName("temporada")] public int? Temporada { get; set; }
    /// <summary>Fecha de emisión ISO, o null (Shin-chan no trae fechas).</summary>
    [JsonPropertyName("fecha")] public string? Fecha { get; set; }
    [JsonPropertyName("especial")] public bool Especial { get; set; }
    [JsonPropertyName("emitido_es")] public bool? EmitidoEs { get; set; }
    /// <summary>
    /// Títulos por idioma. SIEMPRE arrays: un episodio puede tener 2-3 mini-historias
    /// («segmentos»), y cualquiera de ellas identifica al episodio.
    /// </summary>
    [JsonPropertyName("titulos")] public Dictionary<string, List<string>> Titulos { get; set; } = new();
    [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = new();

    // ---- calculado al cargar, no viene del JSON ----
    [JsonIgnore] public DateOnly? FechaParsed { get; private set; }

    /// <summary>
    /// Títulos del idioma de SALIDA: los que se escriben en el nombre del fichero.
    /// Se separan de los comparables a propósito — puedes querer el nombre en español
    /// aunque el fichero venga titulado en inglés.
    /// </summary>
    [JsonIgnore] public IReadOnlyList<string> TitulosSalida { get; private set; } = Array.Empty<string>();

    /// <summary>Todos los títulos con los que se intenta emparejar, sin normalizar.</summary>
    [JsonIgnore] public IReadOnlyList<string> TitulosComparables { get; private set; } = Array.Empty<string>();

    /// <summary>Los comparables ya normalizados, que es contra lo que mide el motor.</summary>
    [JsonIgnore] public IReadOnlyList<string> TitulosNorm { get; private set; } = Array.Empty<string>();

    internal void Precompute(IdiomasCatalogo idiomas)
    {
        // Mismo formato exacto que exige la validación: si aceptara más de lo que se valida,
        // las dos reglas podrían separarse con el tiempo y el catálogo diría una cosa y el
        // motor entendería otra.
        FechaParsed = DateOnly.TryParseExact(Fecha, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;

        static IEnumerable<string> Limpios(IEnumerable<string>? xs) =>
            (xs ?? Enumerable.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t));

        // ── el que se escribe ──
        var salida = new List<string>();
        if (Titulos.TryGetValue(idiomas.Salida, out var deSalida)) salida.AddRange(Limpios(deSalida));
        // Si ese idioma falta en este episodio, se tira del primero que haya: mejor un
        // nombre en otro idioma que un «Episodio 437» sin más.
        if (salida.Count == 0)
            foreach (var lista in Titulos.Values) { salida.AddRange(Limpios(lista)); if (salida.Count > 0) break; }
        TitulosSalida = salida;

        // ── los que se comparan ──
        // Por defecto TODOS los idiomas del catálogo. No es temerario: norm() reduce lo que
        // no sea [a-z0-9] a espacios, así que un título en japonés queda en cadena vacía y
        // se descarta solo. El inglés, en cambio, sobrevive — y es justo lo que hace falta
        // cuando el fichero viene titulado en un idioma y lo quieres nombrado en otro.
        var comparables = new List<string>();
        foreach (var (idioma, lista) in Titulos)
            if (idiomas.SeCompara(idioma)) comparables.AddRange(Limpios(lista));
        comparables.AddRange(Limpios(Aliases));

        TitulosComparables = comparables;
        TitulosNorm = comparables.Select(TitleMatch.Norm).Where(s => s.Length > 0).Distinct().ToList();
    }

    /// <summary>Título preferente para el nombre final.</summary>
    public string TituloPrincipal => TitulosSalida.Count > 0 ? TitulosSalida[0] : $"Episodio {Num}";

    /// <summary>Todos los segmentos unidos, para mostrar en la propuesta.</summary>
    public string TituloCompleto => TitulosSalida.Count > 1
        ? string.Join(" + ", TitulosSalida.Take(3))
        : TituloPrincipal;
}

/// <summary>
/// Qué idioma se escribe y con cuáles se compara. Son cosas distintas: puedes querer los
/// ficheros nombrados en español aunque te lleguen titulados en inglés, y entonces el
/// inglés hace falta para reconocerlos aunque no se escriba nunca.
/// </summary>
public sealed class IdiomasCatalogo
{
    /// <summary>Idioma del título que acaba en el nombre del fichero.</summary>
    [JsonPropertyName("salida")] public string Salida { get; set; } = "es";

    /// <summary>
    /// Idiomas con los que se intenta emparejar. Vacío o ausente = todos los del catálogo,
    /// que es lo razonable: comparar de más no hace daño (los que no comparten alfabeto se
    /// descartan solos al normalizar) y comparar de menos deja ficheros sin identificar.
    /// </summary>
    [JsonPropertyName("comparar")] public List<string>? Comparar { get; set; }

    public bool SeCompara(string idioma) =>
        Comparar == null || Comparar.Count == 0 ||
        Comparar.Contains(idioma, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Catálogo de referencia de una serie (esquema reindex/1.0).</summary>
public sealed class ReindexCatalog
{
    [JsonPropertyName("esquema")] public string Esquema { get; set; } = "";
    [JsonPropertyName("serie")] public string Serie { get; set; } = "";
    /// <summary>Qué significa «num» en esta serie (oficial, segmento, continuo…).</summary>
    [JsonPropertyName("clave")] public string Clave { get; set; } = "";
    [JsonPropertyName("notas")] public string Notas { get; set; } = "";
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("episodios")] public List<CatalogEpisode> Episodios { get; set; } = new();

    /// <summary>Qué idioma se escribe y con cuáles se compara. Ausente = español de salida
    /// y comparación contra todos los idiomas que traiga el catálogo.</summary>
    [JsonPropertyName("idiomas")] public IdiomasCatalogo? Idiomas { get; set; }

    /// <summary>La configuración efectiva, con los valores por defecto ya aplicados.</summary>
    [JsonIgnore] public IdiomasCatalogo IdiomasEfectivos => Idiomas ?? new IdiomasCatalogo();

    // ---- índices calculados ----
    [JsonIgnore] private Dictionary<int, CatalogEpisode> _porNum = new();
    [JsonIgnore] public IReadOnlyList<CatalogEpisode> Regulares { get; private set; } = Array.Empty<CatalogEpisode>();
    [JsonIgnore] public IReadOnlyList<CatalogEpisode> Especiales { get; private set; } = Array.Empty<CatalogEpisode>();

    /// <summary>Versión mayor del esquema que esta app sabe leer.</summary>
    public const int EsquemaMayorSoportado = 1;

    /// <summary>Avisos de esta serie que la UI DEBE enseñar antes de identificar nada.</summary>
    [JsonIgnore] public IReadOnlyList<string> Advertencias { get; private set; } = Array.Empty<string>();

    public CatalogEpisode? PorNum(int num) => _porNum.TryGetValue(num, out var e) ? e : null;
    public bool ExisteNum(int num) => _porNum.ContainsKey(num);

    public static ReindexCatalog Load(string path) => Parse(File.ReadAllText(path, System.Text.Encoding.UTF8));

    /// <summary>
    /// Catálogo de ejemplo que la app ofrece como punto de partida. Vive aquí, junto a las
    /// reglas que debe cumplir, y no en la vista: así un test puede comprobar que sigue
    /// siendo válido. Entregar un ejemplo que no importa sería el peor recibimiento posible.
    ///
    /// Cubre las tres formas que más confunden al escribir el primero: un episodio con
    /// varios segmentos, uno simple y un especial.
    /// </summary>
    public const string Ejemplo = """
    {
      "esquema": "reindex/1.0",
      "serie": "Mi serie (2005)",
      "clave": "oficial",
      "notas": "Cambia «serie» por el nombre que quieres que aparezca en los ficheros.",
      "episodios": [
        {
          "num": 1,
          "temporada": 2005,
          "fecha": "2005-04-22",
          "especial": false,
          "titulos": {
            "es": ["Primer episodio", "Segunda historia del mismo episodio"]
          },
          "aliases": []
        },
        {
          "num": 2,
          "temporada": 2005,
          "fecha": "2005-04-29",
          "titulos": { "es": ["Segundo episodio"] }
        },
        {
          "num": 901,
          "temporada": 2005,
          "especial": true,
          "titulos": { "es": ["Especial de Navidad"] }
        }
      ]
    }
    """;

    /// <summary>
    /// Lee un catálogo. Lanza <see cref="ReindexCatalogException"/> si el esquema es de
    /// una versión mayor que no entendemos; los campos desconocidos se ignoran, para que
    /// un catálogo más nuevo con extras siga funcionando.
    /// </summary>
    public static ReindexCatalog Parse(string json)
    {
        ReindexCatalog? cat;
        try
        {
            cat = JsonSerializer.Deserialize(json, ReindexJsonContext.Default.ReindexCatalog);
        }
        catch (JsonException ex)
        {
            throw new ReindexCatalogException("El archivo no es un JSON válido: " + ex.Message);
        }
        if (cat == null) throw new ReindexCatalogException("El archivo está vacío.");

        // «reindex/1.0» → mayor = 1. Una mayor superior traería reglas que no conocemos.
        var esquema = cat.Esquema ?? "";
        if (!esquema.StartsWith("reindex/", StringComparison.OrdinalIgnoreCase))
            throw new ReindexCatalogException($"No parece un catálogo de reindexado (esquema «{esquema}»).");
        var version = esquema["reindex/".Length..];
        int mayor = int.TryParse(version.Split('.')[0], out var m) ? m : -1;
        if (mayor < 0) throw new ReindexCatalogException($"Versión de esquema irreconocible: «{esquema}».");
        if (mayor > EsquemaMayorSoportado)
            throw new ReindexCatalogException(
                $"El catálogo usa el esquema {esquema} y esta versión de ShrinkStudio solo entiende " +
                $"hasta reindex/{EsquemaMayorSoportado}.x. Actualiza la app.");

        if (string.IsNullOrWhiteSpace(cat.Serie))
            throw new ReindexCatalogException(
                "Falta el campo «serie». Es el nombre que se escribirá en los ficheros, así que no puede ir vacío.");

        if (cat.Episodios.Count == 0) throw new ReindexCatalogException("El catálogo no tiene episodios.");

        cat.Validar();
        cat.Index();
        return cat;
    }

    /// <summary>
    /// Comprueba los episodios uno a uno y junta TODOS los fallos antes de rendirse. Un
    /// catálogo escrito a mano suele traer varios errores del mismo tipo; enseñarlos de uno
    /// en uno obliga a importar, corregir y reimportar sin final.
    /// </summary>
    private void Validar()
    {
        var fallos = new List<string>();
        var numerosVistos = new Dictionary<int, int>();   // num → posición donde salió primero

        for (int i = 0; i < Episodios.Count; i++)
        {
            var e = Episodios[i];
            string donde = $"episodio en la posición {i + 1}";

            if (e.Num < 0)
            {
                fallos.Add($"{donde}: «num» es {e.Num}; tiene que ser un entero mayor o igual que 0.");
            }
            else if (numerosVistos.TryGetValue(e.Num, out var primera))
            {
                // El índice se construye con «por número», así que un repetido borraría al
                // anterior sin decir nada y perderías un episodio entero.
                fallos.Add($"{donde}: el número {e.Num} ya lo usa el de la posición {primera}. " +
                           "Cada «num» debe ser único: si se repite, un episodio pisa al otro.");
            }
            else numerosVistos[e.Num] = i + 1;

            if (e.Fecha != null && !DateOnly.TryParseExact(e.Fecha, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _))
                fallos.Add($"{donde} (nº {e.Num}): la fecha «{e.Fecha}» no vale; " +
                           "tiene que ser una fecha real en formato AAAA-MM-DD.");

            if (e.Temporada is < 0)
                fallos.Add($"{donde} (nº {e.Num}): «temporada» es {e.Temporada}; no puede ser negativa.");

            if (fallos.Count >= 20)
            {
                fallos.Add("…y puede que haya más: se ha parado de comprobar en el fallo 20.");
                break;
            }
        }

        if (fallos.Count > 0)
            throw new ReindexCatalogException(
                (fallos.Count == 1 ? "El catálogo tiene un problema:" : $"El catálogo tiene {fallos.Count} problemas:")
                + "\n\n• " + string.Join("\n• ", fallos));
    }

    private void Index()
    {
        foreach (var e in Episodios) e.Precompute(IdiomasEfectivos);

        // NUNCA iterar 1..Total: la numeración salta valores (56/138/173 en Doraemon 2005)
        _porNum = new Dictionary<int, CatalogEpisode>();
        foreach (var e in Episodios) _porNum[e.Num] = e;

        Regulares = Episodios.Where(e => !e.Especial).ToList();
        Especiales = Episodios.Where(e => e.Especial).ToList();
        Advertencias = ConstruirAdvertencias();
    }

    /// <summary>
    /// Avisos derivados de los datos reales, no solo del texto de «notas»: así el usuario
    /// ve el peligro concreto de SU catálogo aunque las notas se queden cortas.
    /// </summary>
    private List<string> ConstruirAdvertencias()
    {
        var avisos = new List<string>();

        var huecos = HuecosDeNumeracion(8);
        if (huecos.Count > 0)
            avisos.Add($"La numeración salta el {string.Join(", ", huecos)} — los números vecinos no son consecutivos.");

        int sinFecha = Episodios.Count(e => e.FechaParsed == null);
        if (sinFecha == Episodios.Count)
            avisos.Add("Sin fechas de emisión: la identificación dependerá solo del título — espera más dudas.");
        else if (sinFecha > 0)
            avisos.Add($"{sinFecha} episodios no tienen fecha: se identificarán solo por título.");

        int sinTemporada = Episodios.Count(e => e.Temporada == null);
        if (sinTemporada > 0)
            avisos.Add($"{sinTemporada} episodios no tienen temporada asignada.");

        // Episodios que solo existen en japonés (nunca doblados): el emparejamiento por
        // título no puede alcanzarlos, así que si el fichero tampoco trae número o fecha
        // no hay por dónde cogerlo. Conviene saberlo ANTES, no descubrirlo fila a fila.
        int sinTitulo = Episodios.Count(e => e.TitulosNorm.Count == 0);
        if (sinTitulo > 0)
            avisos.Add($"{sinTitulo} episodios solo tienen título en japonés: a esos no se llega " +
                       "por título, solo por número o fecha.");

        // Remakes: mismo título normalizado en episodios distintos y lejanos en numeración
        if (TieneRemakes())
            avisos.Add("Hay remakes: episodios distintos con el mismo título años después.");

        if (Especiales.Count > 0)
            avisos.Add($"{Especiales.Count} especiales: se numeran aparte y necesitan confirmación manual.");

        return avisos;
    }

    /// <summary>Números que faltan dentro del rango: no son huecos reales, son saltos oficiales.</summary>
    public List<int> HuecosDeNumeracion(int maximoAMostrar = int.MaxValue)
    {
        var regulares = Regulares.Select(e => e.Num).Where(n => n > 0).OrderBy(n => n).ToList();
        if (regulares.Count < 2) return new List<int>();
        var faltan = new List<int>();
        for (int n = regulares[0]; n <= regulares[^1] && faltan.Count < maximoAMostrar; n++)
            if (!_porNum.ContainsKey(n)) faltan.Add(n);
        return faltan;
    }

    /// <summary>¿Hay títulos repetidos en episodios distintos? (la trampa del remake)</summary>
    public bool TieneRemakes()
    {
        var vistos = new Dictionary<string, int>();
        foreach (var e in Episodios)
            foreach (var t in e.TitulosNorm)
            {
                if (t.Length < 8) continue;             // títulos muy cortos coinciden por azar
                if (vistos.TryGetValue(t, out var otro) && Math.Abs(otro - e.Num) > 50) return true;
                vistos[t] = e.Num;
            }
        return false;
    }
}

/// <summary>El catálogo no se puede usar, con el motivo en un lenguaje que la UI puede enseñar.</summary>
public sealed class ReindexCatalogException : Exception
{
    public ReindexCatalogException(string mensaje) : base(mensaje) { }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ReindexCatalog))]
internal partial class ReindexJsonContext : JsonSerializerContext { }
