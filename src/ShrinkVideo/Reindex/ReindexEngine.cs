namespace ShrinkVideo.Reindex;

/// <summary>Estado final de un fichero. Cada uno cae en exactamente uno.</summary>
public enum ReindexEstado
{
    /// <summary>El número que ya trae es correcto: no hay que tocarlo.</summary>
    Limpio,
    /// <summary>Identificado con confianza, pero el número era otro → renombrar.</summary>
    Corregido,
    /// <summary>Es un especial: siempre pide confirmación de a cuál corresponde.</summary>
    Especial,
    /// <summary>Ambigüedad (duplicado, señales contradictorias, sin match) → decisión humana.</summary>
    Conflicto,
    /// <summary>Nombre no parseable o fichero ilegible.</summary>
    Error,
}

/// <summary>Semáforo de confianza, ortogonal al estado: qué se puede aplicar sin mirar.</summary>
public enum ReindexConfianza
{
    /// <summary>🟢 evidencia sólida: se puede aplicar en bloque.</summary>
    Alta,
    /// <summary>🟡 hay que echarle un ojo antes de aplicar.</summary>
    Revisar,
    /// <summary>🔴 no se aplica sin que una persona decida.</summary>
    Ninguna,
}

/// <summary>Qué evidencia resolvió el fichero (se enseña en la columna «POR QUÉ»).</summary>
public enum ReindexHint
{
    Ninguno,
    /// <summary>Decisión que el usuario ya tomó antes.</summary>
    Override,
    /// <summary>Número + fecha exacta: la más fuerte.</summary>
    IndiceFecha,
    /// <summary>Coincidencia de título por encima del umbral.</summary>
    Titulo,
    /// <summary>Número + fecha cercana (≤ 4 días).</summary>
    IndiceFechaAprox,
    /// <summary>Título por debajo del umbral: sugerencia, nunca automático.</summary>
    TituloDebil,
}

/// <summary>Un episodio propuesto, con por qué se propone.</summary>
public sealed class ReindexCandidato
{
    public required CatalogEpisode Episodio { get; init; }
    public double Score { get; init; }
    public ReindexHint Hint { get; init; }
    /// <summary>Frases «＋ …» / «－ …» que sostienen o restan a este candidato.</summary>
    public IReadOnlyList<string> Evidencia { get; init; } = Array.Empty<string>();
}

/// <summary>Veredicto para un fichero: lo que la tabla de «Organizar» pinta en una fila.</summary>
public sealed class ReindexResolution
{
    public required FileSignals Archivo { get; init; }
    public ReindexEstado Estado { get; set; }
    public ReindexConfianza Confianza { get; set; }
    public ReindexHint Hint { get; set; }
    public CatalogEpisode? Episodio { get; set; }
    public double Score { get; set; }
    /// <summary>Explicación corta en español para la columna «POR QUÉ».</summary>
    public string Motivo { get; set; } = "";
    /// <summary>Otros episodios plausibles, para corregir con un clic.</summary>
    public IReadOnlyList<ReindexCandidato> Alternativas { get; set; } = Array.Empty<ReindexCandidato>();

    /// <summary>
    /// ¿Se puede aplicar sin más intervención? «Verdes + confirmados»: los especiales entran
    /// solo cuando alguien los ha confirmado, porque nacen en Revisar y únicamente una
    /// decisión humana (o un override guardado) los sube a Alta.
    /// </summary>
    public bool AplicableEnBloque => Confianza == ReindexConfianza.Alta
        && Estado is ReindexEstado.Limpio or ReindexEstado.Corregido or ReindexEstado.Especial;
    /// <summary>¿Necesita a una persona? Es el filtro «Solo dudas» del diseño.</summary>
    public bool EsDuda => Confianza != ReindexConfianza.Alta;
}

/// <summary>
/// Una decisión que el usuario ya tomó y que no se le vuelve a preguntar. Se guarda con el
/// nombre original al lado por trazabilidad: dentro de un año, «episodio 72» sin más no le
/// dirá nada a nadie.
/// </summary>
public sealed class ReindexOverride
{
    [System.Text.Json.Serialization.JsonPropertyName("num")]
    public required int Num { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("temporada")]
    public int? Temporada { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("serie")]
    public string Serie { get; init; } = "";
    /// <summary>«usuario» o «auto-confirmado».</summary>
    [System.Text.Json.Serialization.JsonPropertyName("origen")]
    public string Origen { get; init; } = "usuario";
    [System.Text.Json.Serialization.JsonPropertyName("fecha_decision")]
    public string FechaDecision { get; init; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("nombre_original")]
    public string NombreOriginal { get; init; } = "";
}

/// <summary>
/// Motor de identificación. Librería PURA: entra (señales + catálogo + overrides), sale una
/// lista de resoluciones. Sin tocar disco y sin UI, para poder testear las reglas con
/// fixtures y cambiar la interfaz sin rozarlas.
/// </summary>
public static class ReindexEngine
{
    /// <summary>Tolerancia de P3: fecha «casi» igual.</summary>
    public const int ToleranciaDiasAprox = 4;

    /// <summary>
    /// Por debajo de esta diferencia, dos candidatos se consideran indistinguibles y la
    /// elección deja de ser automática.
    /// </summary>
    public const double MargenDesempate = 0.02;

    // Nota sobre la «ventana de candidatos» (±12 nº / ±12 días) de la epic: era un truco de
    // rendimiento de los scripts para no comparar contra 1800 episodios. Aquí no se usa, y a
    // propósito: la regla anti-remake EXIGE mirar la serie entera (el remake está a cientos de
    // números de distancia), y la cota superior de TitleBag ya hace ese barrido barato. Acotar
    // la búsqueda reintroduciría justo el fallo que la regla 1 existe para evitar.

    public static List<ReindexResolution> Resolve(
        IReadOnlyList<FileSignals> archivos,
        ReindexCatalog catalogo,
        IReadOnlyDictionary<string, ReindexOverride>? overrides = null)
    {
        overrides ??= new Dictionary<string, ReindexOverride>();
        var indice = new IndiceTitulos(catalogo);

        // Cada fichero se resuelve contra un índice de solo lectura, sin mirar a los demás:
        // se puede repartir entre núcleos tal cual. Las reglas de LOTE (deduplicación) van
        // después, cuando ya están todas. AsOrdered mantiene el orden de entrada, que es el
        // que verá el usuario en la tabla.
        var resoluciones = archivos.AsParallel().AsOrdered()
            .Select(a => ResolverUno(a, catalogo, overrides, indice))
            .ToList();
        Deduplicar(resoluciones);
        return resoluciones;
    }

    // ────────────────────────── resolución de un fichero ──────────────────────────

    private static ReindexResolution ResolverUno(FileSignals f, ReindexCatalog cat,
        IReadOnlyDictionary<string, ReindexOverride> overrides, IndiceTitulos indice)
    {
        var r = new ReindexResolution { Archivo = f };

        if (!string.IsNullOrEmpty(f.Error))
            return Fallo(r, ReindexEstado.Error, f.Error!);

        if (!f.TieneSeñales)
            return Fallo(r, ReindexEstado.Error, "El nombre no da ninguna pista (ni número, ni fecha, ni título).");

        // ── P0: el usuario ya decidió esto ──
        if (overrides.TryGetValue(f.Fingerprint, out var ov))
        {
            var epOv = cat.PorNum(ov.Num);
            if (epOv != null)
            {
                r.Episodio = epOv;
                r.Hint = ReindexHint.Override;
                r.Score = 1.0;
                r.Confianza = ReindexConfianza.Alta;
                r.Estado = epOv.Especial ? ReindexEstado.Especial
                         : (f.Indice == epOv.Num ? ReindexEstado.Limpio : ReindexEstado.Corregido);
                r.Motivo = "Lo decidiste tú antes";
                return r;
            }
        }

        // ── Regla 4: los especiales van por su rama, nunca a la numeración regular ──
        if (f.Especial) return ResolverEspecial(r, f, cat, indice);

        // ── títulos del fichero, ya normalizados ──
        var titulosArchivo = TitulosDe(f);
        var (mejorTituloEp, mejorTituloScore) = indice.MejorPorTitulo(titulosArchivo, cat.Episodios);

        // ── Regla 3: metadato y nombre apuntan a episodios distintos → nunca elegir en silencio ──
        var choque = ChoqueMetaVsNombre(f, cat, indice);
        if (choque != null)
        {
            r.Estado = ReindexEstado.Conflicto;
            r.Confianza = ReindexConfianza.Ninguna;
            r.Episodio = null;
            r.Motivo = "El título del fichero y el del metadato apuntan a episodios distintos";
            r.Alternativas = choque;
            return r;
        }

        var epPorIndice = f.Indice.HasValue ? cat.PorNum(f.Indice.Value) : null;

        // ── P1: número existe en la serie Y la fecha coincide exacta ──
        if (epPorIndice != null && f.Fecha.HasValue && epPorIndice.FechaParsed == f.Fecha)
            return PorNumero(r, f, epPorIndice, ReindexHint.IndiceFecha, indice, titulosArchivo,
                mejorTituloEp, mejorTituloScore,
                "El número y la fecha de emisión cuadran exactos");

        // ── P2: título por encima del umbral ──
        if (mejorTituloEp != null && mejorTituloScore >= TitleMatch.UmbralTitulo)
        {
            r.Episodio = mejorTituloEp;
            r.Score = mejorTituloScore;
            r.Hint = ReindexHint.Titulo;
            r.Confianza = ReindexConfianza.Alta;
            r.Estado = f.Indice == mejorTituloEp.Num ? ReindexEstado.Limpio : ReindexEstado.Corregido;
            r.Motivo = $"El título coincide al {Pct(mejorTituloScore)}";
            r.Alternativas = OtrosCandidatos(indice, titulosArchivo, cat.Episodios, mejorTituloEp);

            // Otra cara del remake: DOS episodios distintos encajan igual de bien y ganó el
            // primero por orden de lista. Por título no hay manera de separarlos, así que no
            // se aplica solo — es exactamente el caso que el resolvedor de conflictos existe
            // para resolver (en el diseño, «E210 vs E655»).
            var rival = r.Alternativas.FirstOrDefault();
            if (rival != null && rival.Score >= mejorTituloScore - MargenDesempate)
            {
                r.Confianza = ReindexConfianza.Revisar;
                r.Motivo = $"El título encaja igual de bien con dos episodios " +
                           $"({mejorTituloEp.Num} y {rival.Episodio.Num}) — elige tú";
            }
            return r;
        }

        // ── P3: número existe y la fecha queda a ≤ 4 días ──
        if (epPorIndice != null && f.Fecha.HasValue && epPorIndice.FechaParsed.HasValue)
        {
            int dias = Math.Abs(epPorIndice.FechaParsed.Value.DayNumber - f.Fecha.Value.DayNumber);
            if (dias <= ToleranciaDiasAprox)
            {
                var res = PorNumero(r, f, epPorIndice, ReindexHint.IndiceFechaAprox, indice, titulosArchivo,
                    mejorTituloEp, mejorTituloScore,
                    $"El número cuadra y la fecha baila {dias} día{(dias == 1 ? "" : "s")}");
                // P3 nunca es verde por sí sola: la fecha no encaja del todo
                if (res.Confianza == ReindexConfianza.Alta) res.Confianza = ReindexConfianza.Revisar;
                return res;
            }
        }

        // Número que existe pero sin fecha con la que confirmarlo: vale como pista, no como prueba
        if (epPorIndice != null && !f.Fecha.HasValue)
        {
            var res = PorNumero(r, f, epPorIndice, ReindexHint.IndiceFechaAprox, indice, titulosArchivo,
                    mejorTituloEp, mejorTituloScore,
                "El número existe en la serie, pero no hay fecha que lo confirme");
            if (res.Confianza == ReindexConfianza.Alta) res.Confianza = ReindexConfianza.Revisar;
            return res;
        }

        // ── P4: título flojo → sugerencia, jamás automático ──
        if (mejorTituloEp != null && mejorTituloScore > 0)
        {
            r.Episodio = mejorTituloEp;
            r.Score = mejorTituloScore;
            r.Hint = ReindexHint.TituloDebil;
            r.Confianza = ReindexConfianza.Revisar;
            r.Estado = ReindexEstado.Corregido;
            r.Motivo = $"Se parece al {Pct(mejorTituloScore)}, por debajo de lo fiable";
            r.Alternativas = OtrosCandidatos(indice, titulosArchivo, cat.Episodios, mejorTituloEp);
            return r;
        }

        // ── nada ──
        r.Estado = ReindexEstado.Conflicto;
        r.Confianza = ReindexConfianza.Ninguna;
        r.Motivo = "No se ha podido identificar con ninguna pista";
        return r;
    }

    /// <summary>
    /// Resolución por número, aplicando el cross-check anti-remake: si el título apunta con
    /// fuerza a OTRO episodio, el número deja de ser suficiente.
    /// </summary>
    private static ReindexResolution PorNumero(ReindexResolution r, FileSignals f, CatalogEpisode ep,
        ReindexHint hint, IndiceTitulos indice, IReadOnlyList<TitleBag> titulos,
        CatalogEpisode? otroEp, double otroScore, string motivo)
    {
        r.Episodio = ep;
        r.Hint = hint;
        r.Score = 1.0;
        r.Confianza = ReindexConfianza.Alta;
        r.Estado = f.Indice == ep.Num ? ReindexEstado.Limpio : ReindexEstado.Corregido;
        r.Motivo = motivo;

        // Regla 1 (anti-remake): el mejor título viene de barrer TODA la serie, no una ventana
        // alrededor del número — un remake vive a cientos de episodios de distancia y jamás
        // caería cerca. Si ese título apunta a otro episodio, el número deja de bastar.
        //
        // Exigimos que el otro encaje ESTRICTAMENTE mejor que el propio episodio asignado: en
        // un remake ambos comparten título, y con un empate el número sigue siendo la mejor
        // prueba que hay. Sin esta condición, todo remake bien numerado saldría como duda.
        double scoreAsignado = indice.ScoreDe(titulos, ep);
        if (otroEp != null && otroEp.Num != ep.Num
            && otroScore >= TitleMatch.UmbralTitulo && otroScore > scoreAsignado)
        {
            r.Confianza = ReindexConfianza.Revisar;
            r.Motivo = $"{motivo}, pero el título encaja al {Pct(otroScore)} con el episodio {otroEp.Num}";
            r.Alternativas = new[]
            {
                new ReindexCandidato
                {
                    Episodio = otroEp, Score = otroScore, Hint = ReindexHint.Titulo,
                    Evidencia = new[]
                    {
                        $"＋ el título coincide al {Pct(otroScore)}",
                        "－ el número del fichero apunta a otro episodio",
                    },
                },
            };
        }
        return r;
    }

    private static ReindexResolution ResolverEspecial(ReindexResolution r, FileSignals f,
        ReindexCatalog cat, IndiceTitulos indice)
    {
        r.Estado = ReindexEstado.Especial;
        // Los especiales SIEMPRE se confirman a mano: su numeración es aparte y poco fiable.
        r.Confianza = ReindexConfianza.Revisar;

        if (cat.Especiales.Count == 0)
        {
            r.Estado = ReindexEstado.Conflicto;
            r.Confianza = ReindexConfianza.Ninguna;
            r.Motivo = "Viene marcado como especial, pero el catálogo no tiene especiales";
            return r;
        }

        var titulos = TitulosDe(f);
        var (ep, score) = indice.MejorPorTitulo(titulos, cat.Especiales);

        if (ep != null && score > 0)
        {
            r.Episodio = ep;
            r.Score = score;
            r.Hint = score >= TitleMatch.UmbralTitulo ? ReindexHint.Titulo : ReindexHint.TituloDebil;
            r.Motivo = $"Especial: el título encaja al {Pct(score)} — confírmalo";
            r.Alternativas = OtrosCandidatos(indice, titulos, cat.Especiales, ep);
        }
        else if (f.IndiceEspecial.HasValue)
        {
            r.Episodio = cat.Especiales.FirstOrDefault(e => e.Num == f.IndiceEspecial.Value);
            r.Hint = ReindexHint.IndiceFechaAprox;
            r.Motivo = $"Especial nº {f.IndiceEspecial} — confírmalo";
        }
        else
        {
            r.Motivo = "Especial sin título reconocible — elige a cuál corresponde";
        }
        return r;
    }

    /// <summary>Regla 3: ¿el metadato y el nombre llevan a episodios distintos?</summary>
    private static IReadOnlyList<ReindexCandidato>? ChoqueMetaVsNombre(FileSignals f, ReindexCatalog cat,
        IndiceTitulos indice)
    {
        if (string.IsNullOrWhiteSpace(f.TituloMeta) || string.IsNullOrWhiteSpace(f.TituloNombre)) return null;

        var porNombre = indice.MejorPorTitulo(new[] { new TitleBag(TitleMatch.Norm(f.TituloNombre)) }, cat.Episodios);
        var porMeta = indice.MejorPorTitulo(new[] { new TitleBag(TitleMatch.Norm(f.TituloMeta)) }, cat.Episodios);

        // Solo es choque si AMBOS son fuertes y discrepan: uno flojo no contradice a nadie.
        if (porNombre.ep == null || porMeta.ep == null) return null;
        if (porNombre.score < TitleMatch.UmbralTitulo || porMeta.score < TitleMatch.UmbralTitulo) return null;
        if (porNombre.ep.Num == porMeta.ep.Num) return null;

        return new[]
        {
            new ReindexCandidato
            {
                Episodio = porNombre.ep, Score = porNombre.score, Hint = ReindexHint.Titulo,
                Evidencia = new[] { $"＋ el nombre del fichero coincide al {Pct(porNombre.score)}",
                                    "－ el metadato del vídeo dice otra cosa" },
            },
            new ReindexCandidato
            {
                Episodio = porMeta.ep, Score = porMeta.score, Hint = ReindexHint.Titulo,
                Evidencia = new[] { $"＋ el título interno del vídeo coincide al {Pct(porMeta.score)}",
                                    "－ el nombre del fichero dice otra cosa" },
            },
        };
    }

    // ────────────────────────── reglas de lote ──────────────────────────

    /// <summary>
    /// Regla 2: dos ficheros que apuntan al MISMO destino no pueden estar los dos bien.
    /// Gana el de mayor score; los demás pasan a conflicto. Sin esto, aplicar el lote
    /// machacaría un fichero con otro.
    /// </summary>
    private static void Deduplicar(List<ReindexResolution> resoluciones)
    {
        var porDestino = resoluciones
            .Where(r => r.Episodio != null && r.Estado is ReindexEstado.Limpio or ReindexEstado.Corregido)
            // Los sub-segmentos («[438a]», «[438b]») comparten número a propósito: no chocan
            // entre sí, solo con otro fichero que reclame el mismo trozo.
            .GroupBy(r => (r.Episodio!.Num, r.Archivo.SubSegmento ?? ""));

        foreach (var grupo in porDestino)
        {
            if (grupo.Count() < 2) continue;

            var ganador = grupo.OrderByDescending(r => r.Confianza == ReindexConfianza.Alta)
                               .ThenByDescending(r => r.Score)
                               .ThenBy(r => r.Archivo.NombreArchivo, StringComparer.OrdinalIgnoreCase)
                               .First();

            foreach (var r in grupo)
            {
                if (ReferenceEquals(r, ganador)) continue;
                r.Estado = ReindexEstado.Conflicto;
                r.Confianza = ReindexConfianza.Ninguna;
                r.Motivo = $"Otro fichero reclama el mismo episodio {grupo.Key.Num} con más fuerza " +
                           $"(«{ganador.Archivo.NombreArchivo}»)";
            }

            // El ganador tampoco se aplica a ciegas: hubo pelea, que se vea.
            if (ganador.Confianza == ReindexConfianza.Alta)
            {
                ganador.Confianza = ReindexConfianza.Revisar;
                ganador.Motivo += $" · otro fichero reclamaba este mismo episodio";
            }
        }
    }

    // ────────────────────────── utilidades ──────────────────────────

    /// <summary>Los títulos comparables del fichero: el completo, sus segmentos y el metadato.</summary>
    private static IReadOnlyList<TitleBag> TitulosDe(FileSignals f)
    {
        var lista = new List<TitleBag>();
        void Add(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            var n = TitleMatch.Norm(s);
            if (n.Length > 0 && !lista.Any(b => b.Text == n)) lista.Add(new TitleBag(n));
        }
        Add(f.TituloNombre);
        Add(f.TituloMeta);
        foreach (var seg in f.Segmentos) Add(seg);
        return lista;
    }

    private static IReadOnlyList<ReindexCandidato> OtrosCandidatos(IndiceTitulos indice,
        IReadOnlyList<TitleBag> titulos, IReadOnlyList<CatalogEpisode> candidatos, CatalogEpisode elegido)
    {
        return indice.Ranking(titulos, candidatos, excluir: elegido, cuantos: 2)
            .Select(x => new ReindexCandidato
            {
                Episodio = x.ep,
                Score = x.score,
                Hint = x.score >= TitleMatch.UmbralTitulo ? ReindexHint.Titulo : ReindexHint.TituloDebil,
                Evidencia = new[] { $"＋ el título coincide al {Pct(x.score)}" },
            })
            .ToList();
    }

    private static ReindexResolution Fallo(ReindexResolution r, ReindexEstado estado, string motivo)
    {
        r.Estado = estado;
        r.Confianza = ReindexConfianza.Ninguna;
        r.Motivo = motivo;
        return r;
    }

    internal static string Pct(double score) =>
        (score * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " %";
}

/// <summary>
/// Índice de títulos del catálogo con los recuentos precalculados. Existe solo por
/// velocidad: sin la cota superior, comparar 600 ficheros contra 1800 episodios es
/// inviable, y el cross-check anti-remake obliga a barrer la serie entera cada vez.
/// </summary>
internal sealed class IndiceTitulos
{
    private readonly Dictionary<CatalogEpisode, TitleBag[]> _bolsas = new();

    public IndiceTitulos(ReindexCatalog cat)
    {
        foreach (var e in cat.Episodios)
            _bolsas[e] = e.TitulosNorm.Select(t => new TitleBag(t)).ToArray();
    }

    /// <summary>Mejor episodio y su score sobre los títulos dados.</summary>
    public (CatalogEpisode? ep, double score) MejorPorTitulo(IReadOnlyList<TitleBag> titulos,
                                                            IReadOnlyList<CatalogEpisode> candidatos)
    {
        CatalogEpisode? mejor = null;
        double mejorScore = 0;
        foreach (var ep in candidatos)
        {
            double s = Score(titulos, ep, mejorScore);
            if (s > mejorScore) { mejorScore = s; mejor = ep; }
        }
        return (mejor, mejorScore);
    }

    /// <summary>Score de UN episodio concreto contra los títulos dados.</summary>
    public double ScoreDe(IReadOnlyList<TitleBag> titulos, CatalogEpisode ep) => Score(titulos, ep, 0);

    /// <summary>Los siguientes mejores, para ofrecer alternativas de un clic.</summary>
    public List<(CatalogEpisode ep, double score)> Ranking(IReadOnlyList<TitleBag> titulos,
        IReadOnlyList<CatalogEpisode> candidatos, CatalogEpisode? excluir, int cuantos)
    {
        var lista = new List<(CatalogEpisode, double)>();
        foreach (var ep in candidatos)
        {
            if (ReferenceEquals(ep, excluir)) continue;
            double s = Score(titulos, ep, 0);
            if (s > 0) lista.Add((ep, s));
        }
        return lista.OrderByDescending(x => x.Item2).Take(cuantos).ToList();
    }

    /// <summary>
    /// Score de un episodio = MÁXIMO sim sobre todos sus títulos (segmentos es/lat + alias).
    /// <paramref name="minimoUtil"/> permite abandonar pronto: si ni la cota superior supera
    /// lo ya encontrado, no hace falta calcular la similitud real.
    /// </summary>
    private double Score(IReadOnlyList<TitleBag> titulos, CatalogEpisode ep, double minimoUtil)
    {
        if (!_bolsas.TryGetValue(ep, out var bolsas)) return 0;
        double mejor = 0;
        foreach (var t in titulos)
            foreach (var b in bolsas)
            {
                double cota = TitleMatch.CotaSuperior(in t, in b);
                if (cota <= mejor || cota <= minimoUtil) continue;   // imposible que mejore: se salta
                double s = TitleMatch.Sim(t.Text, b.Text);
                if (s > mejor) mejor = s;
            }
        return mejor;
    }
}
