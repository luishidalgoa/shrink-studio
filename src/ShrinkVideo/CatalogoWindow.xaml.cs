using System.Windows;
using ShrinkVideo.Reindex;

namespace ShrinkVideo;

/// <summary>Una fila del explorador, ya lista para pintar.</summary>
public sealed class EpisodioVista
{
    public required CatalogEpisode Ep { get; init; }

    public string Codigo => $"E{Ep.Num}";
    public string Titulos => Ep.TitulosSalida.Count > 0
        ? string.Join("  ┃  ", Ep.TitulosSalida)
        : "(sin título en español: se identifica por número o fecha)";

    /// <summary>«2009 · 03/07/2009» — lo que confirma o desmiente una sospecha.</summary>
    public string Detalle
    {
        get
        {
            var partes = new List<string>();
            if (Ep.Temporada.HasValue) partes.Add(Ep.Temporada.Value.ToString());
            if (Ep.FechaParsed.HasValue) partes.Add(Ep.FechaParsed.Value.ToString("dd/MM/yyyy"));
            return string.Join(" · ", partes);
        }
    }

    public Visibility EsEspecial => Ep.Especial ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// Explorador de solo lectura del catálogo elegido, con buscador por número o título.
///
/// Existe para verificar una propuesta sin abrir el JSON a mano: dudar de una sugerencia
/// («¿de verdad el planeta espejo es el 175?») obligaba a buscar entre cientos de episodios
/// fuera de la app, y esa fricción deja dudas razonables sin comprobar.
/// </summary>
public partial class CatalogoWindow : Window
{
    private readonly ReindexCatalog _cat;

    public CatalogoWindow(ReindexCatalog cat, string? consultaInicial = null)
    {
        InitializeComponent();
        _cat = cat;

        lblTitulo.Text = $"Explorar el catálogo — {cat.Serie}";
        txtBuscar.TextChanged += (_, _) => Refrescar();
        btnCerrar.Click += (_, _) => Close();

        // Se puede llegar con una consulta puesta (p. ej. desde una fila): el buscador
        // arranca apuntando a lo que se estaba mirando en vez de a la lista entera.
        txtBuscar.Text = consultaInicial ?? "";
        Refrescar();
        Loaded += (_, _) => txtBuscar.Focus();

        lista.SelectionChanged += (_, _) => MostrarJson();
        btnCopiarJson.Click += (_, _) =>
        {
            try { Clipboard.SetText(txtJson.Text); lblJsonTitulo.Text += "  · copiado"; }
            catch { /* portapapeles ocupado por otro proceso: se reintenta a mano */ }
        };
    }

    private static readonly System.Text.Json.JsonSerializerOptions OpcionesJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Un episodio sin temporada no debe enseñar «"temporada": null»: en el fichero del
        // usuario esa clave sencillamente no está, y esto pretende ser LO QUE HAY.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// El JSON del episodio elegido, serializado desde el MISMO modelo que usa el motor: lo
    /// que se ve aquí es lo que el identificador está leyendo, no una reconstrucción.
    /// </summary>
    private void MostrarJson()
    {
        if (lista.SelectedItem is not EpisodioVista v)
        {
            colJson.Width = new GridLength(0);
            return;
        }

        colJson.Width = new GridLength(300);
        lblJsonTitulo.Text = $"E{v.Ep.Num} · JSON del catálogo";
        txtJson.Text = System.Text.Json.JsonSerializer.Serialize(v.Ep, OpcionesJson);
    }

    private void Refrescar()
    {
        var encontrados = CatalogSearch.Filtrar(_cat, txtBuscar.Text);
        lista.ItemsSource = encontrados.Select(e => new EpisodioVista { Ep = e }).ToList();
        lblCuenta.Text = encontrados.Count == _cat.Episodios.Count
            ? $"{encontrados.Count} episodios"
            : $"{encontrados.Count} de {_cat.Episodios.Count}";
    }
}
