using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using ShrinkVideo.Reindex;

namespace ShrinkVideo;

/// <summary>
/// Una opción del resolvedor de conflictos, ya con su consecuencia a la vista.
///
/// Incluye SIEMPRE al candidato que la app ha elegido, no solo a los descartados: antes se
/// enseñaban únicamente las alternativas, así que la propuesta buena no aparecía por ningún
/// lado y las dos opciones ofrecidas parecían —con razón— equivocadas.
/// </summary>
public sealed class CandidatoVista
{
    public required CatalogEpisode Episodio { get; init; }
    public required string Etiqueta { get; init; }
    public required string Titulo { get; init; }
    public required string NombreResultante { get; init; }
    public IReadOnlyList<string> Evidencia { get; init; } = Array.Empty<string>();
    public bool EsElegido { get; init; }

    /// <summary>«E318 · 2014»</summary>
    public string Cabecera => Episodio.Temporada.HasValue
        ? $"E{Episodio.Num} · {Episodio.Temporada}"
        : $"E{Episodio.Num}";

    public string TextoBoton => EsElegido ? $"Dejar E{Episodio.Num}" : $"Cambiar a E{Episodio.Num}";
}

/// <summary>
/// Una fila de la tabla de «Organizar»: traduce la resolución del motor a lo que se pinta.
/// El motor no sabe nada de colores ni de glifos, y así sigue siendo.
/// </summary>
public sealed class OrganizarRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void N([CallerMemberName] string? p = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    /// <summary>Formato del diseño: coma decimal y dos cifras («0,99»).</summary>
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public ReindexResolution Res { get; }
    public LibraryTemplate Plantilla { get; }
    public ReindexCatalog Catalogo { get; }

    /// <summary>Carpeta de temporada de la que salió, ya con nombre presentable.</summary>
    public string Grupo { get; }

    private bool _marcado = true;
    /// <summary>
    /// Si esta fila entra en el próximo «Aplicar». Solo pesa cuando la fila está lista: los
    /// conflictos y las dudas no se aplican jamás, marcados o no.
    /// </summary>
    public bool Marcado
    {
        get => _marcado;
        set { _marcado = value; N(); }
    }

    private bool _primeraDeGrupo;
    /// <summary>Esta fila abre su temporada: lleva encima la banda separadora.</summary>
    public bool PrimeraDeGrupo
    {
        get => _primeraDeGrupo;
        set { _primeraDeGrupo = value; N(); }
    }

    private string _grupoConteo = "";
    /// <summary>«32 ficheros» de esa temporada, contando solo los que el filtro deja ver.</summary>
    public string GrupoConteo
    {
        get => _grupoConteo;
        set { _grupoConteo = value; N(); }
    }

    /// <summary>Nombre final que se escribirá, o null si esta fila no se toca.</summary>
    public string? NombreNuevo { get; private set; }

    /// <summary>
    /// Dónde está el fichero AHORA: tras aplicar, el de disco ya es el nombre nuevo y abrir
    /// la ruta vieja fallaría.
    /// </summary>
    public string RutaActual => Aplicado && NombreNuevo != null
        ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Res.Archivo.Path) ?? "", NombreNuevo)
        : Res.Archivo.Path;

    private bool _aplicado;
    /// <summary>Ya renombrado en disco: la fila pasa a «Hecho ✓».</summary>
    public bool Aplicado
    {
        get => _aplicado;
        set { _aplicado = value; RefrescarTodo(); }
    }

    public OrganizarRow(ReindexResolution res, ReindexCatalog catalogo, LibraryTemplate plantilla,
                        string grupo = "")
    {
        Res = res;
        Catalogo = catalogo;
        Plantilla = plantilla;
        Grupo = grupo;
        Recalcular();
    }

    /// <summary>
    /// Recalcula el nombre propuesto. Se llama al crear la fila y cada vez que el usuario
    /// resuelve un conflicto o cambia la plantilla.
    /// </summary>
    public void Recalcular()
    {
        var archivo = SegElegido != null ? Res.Archivo.ConSegmento(SegElegido) : Res.Archivo;
        NombreNuevo = Res.Episodio != null && Res.Estado != ReindexEstado.Error
            ? Plantilla.Render(Catalogo, Res.Episodio, archivo)
            : null;
        RefrescarTodo();
    }

    private void RefrescarTodo()
    {
        foreach (var p in new[]
        {
            nameof(EstadoTexto), nameof(EstadoGlifo), nameof(EstadoFg), nameof(EstadoBg), nameof(EstadoBorde),
            nameof(Original), nameof(Propuesta), nameof(PropuestaDestacada), nameof(PropuestaFg),
            nameof(PorQue), nameof(Explicacion), nameof(TieneDetalle), nameof(Aplicado), nameof(Candidatos),
            // La casilla de aplicar se enseña según esto: al resolver un conflicto la fila
            // pasa a estar lista y su casilla tiene que aparecer en ese momento.
            nameof(ListoParaAplicar), nameof(EstadoTooltip),
        }) N(p);
    }

    // ───────────────────────── columna ESTADO ─────────────────────────

    /// <summary>
    /// Palabra + glifo + color. El glifo y la palabra van SIEMPRE: con daltonismo, o en una
    /// captura en blanco y negro, el color por sí solo no dice nada.
    /// </summary>
    public string EstadoTexto => Aplicado ? "Hecho" : Res.Estado switch
    {
        ReindexEstado.Limpio => "Limpio",
        ReindexEstado.Corregido => "Corregido",
        ReindexEstado.Especial => "Especial",
        ReindexEstado.Conflicto => "Conflicto",
        _ => "Error",
    };

    public string EstadoGlifo => Aplicado ? "✓" : Res.Estado switch
    {
        ReindexEstado.Limpio => "●",
        ReindexEstado.Corregido => "↻",
        ReindexEstado.Especial => "▲",
        ReindexEstado.Conflicto => "◆",
        _ => "✕",
    };

    /// <summary>
    /// El color sale de la CONFIANZA, no del estado. Así el reparto queda limpio: la palabra
    /// dice qué es («Limpio», «Especial»…) y el color dice si te puedes fiar. Atándolo al
    /// estado salían filas en verde que luego no se aplicaban —un «Limpio» sin fecha que lo
    /// confirme necesita revisión igual, y pintarlo de verde era mentir.
    /// </summary>
    private string Tono => Aplicado || SinCambios ? "Ok" : Res.Confianza switch
    {
        ReindexConfianza.Alta => "Ok",
        ReindexConfianza.Revisar => "Warn",
        _ => "Danger",
    };

    /// <summary>
    /// Explica por qué el color no siempre concuerda con la palabra. Un «Limpio» en ámbar
    /// desconcierta con razón: dice que el fichero YA se llama como toca, pero que no está
    /// confirmado que sea ese episodio. Sin decirlo, parece un fallo de la app.
    /// </summary>
    public string EstadoTooltip
    {
        get
        {
            if (Aplicado) return "Renombrado en este lote.";

            if (SinCambios)
                return "Ya se llama exactamente como debe: no hay nada que aplicar.\n\n" +
                       "Que el nombre coincida entero con el que produciría la plantilla es la " +
                       "confirmación más fuerte que hay de que el episodio es este.";

            var porque = string.IsNullOrWhiteSpace(Res.Motivo) ? "" : $" ({Res.Motivo})";
            return Res.Confianza switch
            {
                ReindexConfianza.Alta =>
                    $"{EstadoTexto} · identificación segura{porque}.",
                ReindexConfianza.Revisar =>
                    $"{EstadoTexto}, pero la identificación NO está confirmada{porque}.\n\n" +
                    "Va en ámbar por eso: el nombre puede estar ya bien y aun así no ser este episodio. " +
                    "Ábrela para ver contra qué ha casado.",
                _ =>
                    $"{EstadoTexto} · esto lo tienes que decidir tú{porque}.",
            };
        }
    }

    public Brush EstadoFg => Rec($"Org{Tono}");
    public Brush EstadoBg => Rec($"Org{Tono}Bg");
    public Brush EstadoBorde => Rec($"Org{Tono}Border");

    private static Brush Rec(string clave) =>
        Application.Current?.TryFindResource(clave) as Brush ?? Brushes.Transparent;

    // ───────────────────── columna FICHERO ORIGINAL ─────────────────────

    public string Original => Res.Archivo.NombreArchivo;

    // ─────────────────────── columna PROPUESTA ───────────────────────

    public string Propuesta
    {
        get
        {
            if (Aplicado) return NombreNuevo ?? Original;

            switch (Res.Estado)
            {
                case ReindexEstado.Error:
                    return "— nombre no parseable · elige episodio a mano";

                // Un duplicado no se explica con los candidatos de título: lo que hay que
                // contar es quién le ganó el sitio. Mezclar las dos cosas hacía una frase
                // larguísima que no decía ninguna de las dos bien.
                case ReindexEstado.Conflicto when Res.EsDuplicado:
                    return "— " + Res.Motivo;

                case ReindexEstado.Conflicto when Res.Alternativas.Count >= 2:
                    var a = Res.Alternativas[0].Episodio;
                    var b = Res.Alternativas[1].Episodio;
                    return $"¿E{a.Num} ({a.Temporada}) o E{b.Num} ({b.Temporada})? El título existe dos veces";

                case ReindexEstado.Conflicto:
                    return "— " + Res.Motivo;

                case ReindexEstado.Especial when Res.Episodio != null:
                    return $"¿Especial {Res.Episodio.Num} — {Res.Episodio.TituloPrincipal}? Confirmar";

                case ReindexEstado.Especial:
                    return "¿Qué especial es? Elegir a mano";
            }

            // Ya trae el nombre correcto: no hay nada que hacer, y decirlo es la información
            if (NombreNuevo == null) return "— sin propuesta";
            if (string.Equals(NombreNuevo, Original, StringComparison.OrdinalIgnoreCase))
                return "(sin cambios)";
            return NombreNuevo;
        }
    }

    /// <summary>Solo se destaca lo que de verdad va a cambiar; lo demás queda atenuado.</summary>
    public bool PropuestaDestacada =>
        !Aplicado && NombreNuevo != null && Res.Estado is ReindexEstado.Corregido or ReindexEstado.Especial
        && !string.Equals(NombreNuevo, Original, StringComparison.OrdinalIgnoreCase);

    public Brush PropuestaFg => PropuestaDestacada || Aplicado
        ? Rec("Text")
        : Res.Estado is ReindexEstado.Conflicto or ReindexEstado.Error
            ? Rec("Neutral400")
            : Rec("Neutral500");

    // ───────────────────────── columna POR QUÉ ─────────────────────────

    /// <summary>
    /// Versión corta: la evidencia y su puntuación. La explicación larga del motor vive en
    /// el tooltip, que es donde cabe sin romper la tabla.
    /// </summary>
    public string PorQue
    {
        get
        {
            var partes = new List<string>();

            string evidencia = Res.Hint switch
            {
                ReindexHint.Override => "decisión tuya",
                ReindexHint.IndiceFecha => "nº + fecha exacta",
                ReindexHint.OrdinalTemporada => "nº de temporada",
                ReindexHint.Titulo => Res.Archivo.Segmentos.Count > 1
                    ? $"{Res.Archivo.Segmentos.Count} segmentos · títulos"
                    : "título",
                ReindexHint.IndiceFechaAprox => "nº + fecha≈",
                ReindexHint.TituloDebil => "título débil",
                _ => "sin señales",
            };
            partes.Add($"{evidencia} {Res.Score.ToString("0.00", Es)}");

            if (Res.Alternativas.Count > 0)
                partes.Add(Res.Estado == ReindexEstado.Conflicto
                    ? $"{Res.Alternativas.Count} candidatos"
                    : $"{Res.Alternativas.Count} alternativas");

            return string.Join(" · ", partes);
        }
    }

    /// <summary>La prosa completa del motor, para el tooltip de la fila.</summary>
    public string Explicacion => Res.Motivo;

    /// <summary>¿Merece la pena desplegar el resolutor bajo esta fila?</summary>
    public bool TieneDetalle => Res.Alternativas.Count > 0 || Res.EsDuda;

    /// <summary>
    /// Las opciones del resolvedor: primero la que la app propone, después las descartadas.
    /// Cada una enseña el nombre que quedaría, porque «Elegir E318» sin ver la consecuencia
    /// obliga a decidir a ciegas.
    /// </summary>
    public IReadOnlyList<CandidatoVista> Candidatos
    {
        get
        {
            var lista = new List<CandidatoVista>();

            if (Res.Episodio != null)
                lista.Add(Ver(Res.Episodio, Res.Score, esElegido: true, Res.Alternativas.Count > 0
                    ? new[] { "＋ es la que propone la app ahora mismo" }
                    : Array.Empty<string>()));

            foreach (var alt in Res.Alternativas)
            {
                if (alt.Episodio.Num == Res.Episodio?.Num) continue;   // no repetir al elegido
                lista.Add(Ver(alt.Episodio, alt.Score, esElegido: false, alt.Evidencia));
            }
            return lista;
        }
    }

    private CandidatoVista Ver(CatalogEpisode ep, double score, bool esElegido, IReadOnlyList<string> evidencia)
    {
        var nombre = Plantilla.Render(Catalogo, ep, Res.Archivo);
        return new CandidatoVista
        {
            Episodio = ep,
            EsElegido = esElegido,
            Etiqueta = (esElegido ? "más probable" : "alternativa") + $" · {score.ToString("0.00", Es)}",
            Titulo = ep.TituloCompleto,
            NombreResultante = nombre ?? "(sin nombre que proponer)",
            Evidencia = evidencia,
        };
    }

    // ───────────────────────── clasificación ─────────────────────────

    /// <summary>
    /// Aplicar esta fila no cambiaría nada: ya se llama exactamente como la plantilla manda.
    ///
    /// Y eso, además de no dar trabajo, es la identificación MÁS FUERTE que hay. El nombre
    /// propuesto lleva serie, temporada, número y título; que coincida carácter a carácter
    /// con el que el fichero ya tiene no es un parecido de 0,81, es una confirmación —
    /// normalmente porque ya lo procesaste en su día—. Por eso una fila así va en verde
    /// aunque la cascada la resolviera con poca confianza: no hay nada que decidir.
    /// </summary>
    public bool SinCambios => !Aplicado && NombreNuevo != null
        && string.Equals(NombreNuevo, Original, StringComparison.OrdinalIgnoreCase);

    // Ni pendiente ni por despachar: no hay nada que hacerle.
    public bool EsDuda => !Aplicado && !SinCambios && Res.EsDuda;
    public bool ListoParaAplicar =>
        !Aplicado && !SinCambios && Res.AplicableEnBloque && NombreNuevo != null;

    /// <summary>
    /// El «es solo la historia X» decidido a mano. Vive en la fila y no en las señales
    /// porque las señales son inmutables: la decisión crea una variante al renderizar.
    /// </summary>
    public string? SegElegido { get; private set; }

    /// <summary>
    /// Fija a mano el episodio de esta fila (el usuario eligió en el resolutor). Deja de ser
    /// una duda: hay una persona detrás. Con <paramref name="seg"/>, el fichero es SOLO esa
    /// historia del episodio: E413b, con el título de esa historia.
    /// </summary>
    public void ElegirEpisodio(CatalogEpisode ep, string? seg = null)
    {
        SegElegido = seg;
        Res.Episodio = ep;
        Res.Hint = ReindexHint.Override;
        Res.Score = 1.0;
        Res.Confianza = ReindexConfianza.Alta;
        Res.Estado = ep.Especial ? ReindexEstado.Especial : ReindexEstado.Corregido;
        Res.Motivo = seg == null
            ? $"Lo elegiste tú: episodio {ep.Num}"
            : $"Lo elegiste tú: la historia «{seg}» del episodio {ep.Num}";
        Res.Alternativas = Array.Empty<ReindexCandidato>();
        Recalcular();
    }
}
