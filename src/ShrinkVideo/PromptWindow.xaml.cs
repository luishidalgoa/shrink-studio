using System.Windows;
using System.Windows.Controls;
using ShrinkVideo.Reindex;

namespace ShrinkVideo;

/// <summary>
/// Arma el encargo para que una IA convierta un anexo de episodios en un catálogo.
/// La redacción vive en <see cref="CatalogPrompt"/>, que es código puro y con tests;
/// aquí solo se recogen los datos y se copia el resultado.
/// </summary>
/// <summary>Un idioma tal y como se pinta en la lista de resultados.</summary>
public sealed class IdiomaFila
{
    public required string Codigo { get; init; }
    public required string Nombre { get; init; }
    public required bool Elegido { get; init; }
    /// <summary>Un visto si ya está elegido. Ocupa sitio fijo para que la columna no baile.</summary>
    public string Marca => Elegido ? "✓" : "";
}

public partial class PromptWindow : Window
{
    /// <summary>Códigos elegidos, en el orden en que se fueron marcando.</summary>
    private readonly List<string> _elegidos = new() { "es", "en" };

    public PromptWindow(string serieSugerida)
    {
        InitializeComponent();

        txtSerie.Text = serieSugerida;

        // La lista de salida sale ya ordenada con los de andar por casa arriba. Va con
        // ItemTemplate y no con DisplayMemberPath: el tema le pone su propia plantilla a lo
        // seleccionado, y con DisplayMemberPath se acaba viendo el nombre del tipo.
        cboSalida.ItemsSource = IsoLanguages.Buscar("");
        cboSalida.SelectedIndex = 0;   // español de España

        txtSerie.TextChanged += (_, _) => Refrescar();
        txtFuente.TextChanged += (_, _) => Refrescar();
        cboSalida.SelectionChanged += (_, _) => Refrescar();
        txtBuscarIdioma.TextChanged += (_, _) => RefrescarIdiomas();

        // Al abrir: lista limpia y cursor dentro. Si conservara lo escrito, reabrir enseñaría
        // cuatro resultados de la búsqueda anterior y parecería que no hay más idiomas.
        // El foco va aplazado porque durante Opened el emergente aún no puede recibirlo.
        popIdiomas.Opened += (_, _) =>
        {
            txtBuscarIdioma.Text = "";
            Dispatcher.BeginInvoke(new Action(() => txtBuscarIdioma.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
        };

        btnCerrar.Click += (_, _) => Close();
        btnCopiar.Click += (_, _) => Copiar();
        btnAbrirFuente.Click += (_, _) => AbrirFuente();

        RefrescarIdiomas();
        Refrescar();
    }

    private string IdiomaSalida => (cboSalida.SelectedItem as IsoLanguage)?.Codigo ?? "es";

    private List<string> IdiomasMarcados => _elegidos.ToList();

    // ─────────────────────────── idiomas ───────────────────────────

    /// <summary>Repinta badges y resultados. La lista es de 183: rehacerla entera no se nota.</summary>
    private void RefrescarIdiomas()
    {
        listaSeleccionados.ItemsSource = _elegidos
            .Select(c => new IdiomaFila { Codigo = c, Nombre = IsoLanguages.Nombre(c), Elegido = true })
            .ToList();

        var encontrados = IsoLanguages.Buscar(txtBuscarIdioma.Text)
            .Select(i => new IdiomaFila { Codigo = i.Codigo, Nombre = i.Nombre, Elegido = _elegidos.Contains(i.Codigo) })
            .ToList();

        listaResultados.ItemsSource = encontrados;
        lblSinResultados.Visibility = encontrados.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAlternarIdioma(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string codigo) return;

        if (!_elegidos.Remove(codigo)) _elegidos.Add(codigo);
        RefrescarIdiomas();
        Refrescar();
    }

    private void OnQuitarIdioma(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string codigo) return;

        _elegidos.Remove(codigo);
        RefrescarIdiomas();
        Refrescar();
    }

    private void Refrescar()
    {
        txtPrompt.Text = CatalogPrompt.Build(txtSerie.Text, txtFuente.Text, IdiomaSalida, IdiomasMarcados);

        // Avisar del error que más caro sale: no incluir el idioma en el que vienen tus
        // ficheros hoy, y descubrirlo cuando ya no reconoce ninguno.
        lblAviso.Text = IdiomasMarcados.Count switch
        {
            // Quitarlos todos no rompe nada —el de salida siempre entra— pero conviene decirlo
            0 => $"Sin ninguno marcado solo se compara contra {IsoLanguages.Nombre(IdiomaSalida)}, el de salida.",
            1 => "Con un solo idioma, solo reconocerá los ficheros titulados en ese idioma.",
            _ => $"{IdiomasMarcados.Count} idiomas para reconocer · el nombre se escribirá en {IsoLanguages.Nombre(IdiomaSalida)}",
        };
    }

    private void Copiar()
    {
        try
        {
            Clipboard.SetText(txtPrompt.Text);
            lblAviso.Text = "Copiado. Pégaselo a la IA junto con la dirección del anexo.";
        }
        catch (Exception ex)
        {
            // El portapapeles lo puede tener bloqueado otro proceso: se dice y ya está,
            // el texto sigue en pantalla para copiarlo a mano.
            lblAviso.Text = "No se pudo copiar: " + ex.Message;
        }
    }

    private void AbrirFuente()
    {
        var url = txtFuente.Text?.Trim() ?? "";
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            lblAviso.Text = "Escribe primero la dirección del anexo.";
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) { lblAviso.Text = "No se pudo abrir: " + ex.Message; }
    }
}
