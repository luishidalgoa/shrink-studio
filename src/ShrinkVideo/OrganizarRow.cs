using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using ShrinkVideo.Reindex;

namespace ShrinkVideo;

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

    /// <summary>Nombre final que se escribirá, o null si esta fila no se toca.</summary>
    public string? NombreNuevo { get; private set; }

    private bool _aplicado;
    /// <summary>Ya renombrado en disco: la fila pasa a «Hecho ✓».</summary>
    public bool Aplicado
    {
        get => _aplicado;
        set { _aplicado = value; RefrescarTodo(); }
    }

    public OrganizarRow(ReindexResolution res, ReindexCatalog catalogo, LibraryTemplate plantilla)
    {
        Res = res;
        Catalogo = catalogo;
        Plantilla = plantilla;
        Recalcular();
    }

    /// <summary>
    /// Recalcula el nombre propuesto. Se llama al crear la fila y cada vez que el usuario
    /// resuelve un conflicto o cambia la plantilla.
    /// </summary>
    public void Recalcular()
    {
        NombreNuevo = Res.Episodio != null && Res.Estado != ReindexEstado.Error
            ? Plantilla.Render(Catalogo, Res.Episodio, Res.Archivo)
            : null;
        RefrescarTodo();
    }

    private void RefrescarTodo()
    {
        foreach (var p in new[]
        {
            nameof(EstadoTexto), nameof(EstadoGlifo), nameof(EstadoFg), nameof(EstadoBg), nameof(EstadoBorde),
            nameof(Original), nameof(Propuesta), nameof(PropuestaDestacada), nameof(PropuestaFg),
            nameof(PorQue), nameof(Explicacion), nameof(TieneDetalle), nameof(Aplicado),
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
    private string Tono => Aplicado ? "Ok" : Res.Confianza switch
    {
        ReindexConfianza.Alta => "Ok",
        ReindexConfianza.Revisar => "Warn",
        _ => "Danger",
    };

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
                case ReindexEstado.Conflicto when Res.Motivo.StartsWith("Otro fichero"):
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

    // ───────────────────────── clasificación ─────────────────────────

    public bool EsDuda => !Aplicado && Res.EsDuda;
    public bool ListoParaAplicar =>
        !Aplicado && Res.AplicableEnBloque && NombreNuevo != null
        && !string.Equals(NombreNuevo, Original, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fija a mano el episodio de esta fila (el usuario eligió en el resolutor). Deja de ser
    /// una duda: hay una persona detrás.
    /// </summary>
    public void ElegirEpisodio(CatalogEpisode ep)
    {
        Res.Episodio = ep;
        Res.Hint = ReindexHint.Override;
        Res.Score = 1.0;
        Res.Confianza = ReindexConfianza.Alta;
        Res.Estado = ep.Especial ? ReindexEstado.Especial : ReindexEstado.Corregido;
        Res.Motivo = $"Lo elegiste tú: episodio {ep.Num}";
        Res.Alternativas = Array.Empty<ReindexCandidato>();
        Recalcular();
    }
}
