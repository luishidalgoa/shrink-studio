using System.Windows;
using System.Windows.Controls;
using ShrinkVideo.Reindex;

namespace ShrinkVideo;

/// <summary>
/// Arma el encargo para que una IA convierta un anexo de episodios en un catálogo.
/// La redacción vive en <see cref="CatalogPrompt"/>, que es código puro y con tests;
/// aquí solo se recogen los datos y se copia el resultado.
/// </summary>
public partial class PromptWindow : Window
{
    private readonly List<CheckBox> _idiomas = new();

    public PromptWindow(string serieSugerida)
    {
        InitializeComponent();

        txtSerie.Text = serieSugerida;

        cboSalida.ItemsSource = CatalogPrompt.IdiomasConocidos.Select(i => i.Nombre).ToList();
        cboSalida.SelectedIndex = 0;   // español de España

        foreach (var (codigo, nombre) in CatalogPrompt.IdiomasConocidos)
        {
            var chk = new CheckBox
            {
                Content = nombre,
                Tag = codigo,
                Margin = new Thickness(0, 2, 14, 2),
                FontSize = 12,
                // Español e inglés marcados de salida: cubren la inmensa mayoría de los
                // nombres con los que llegan los ficheros.
                IsChecked = codigo is "es" or "en",
            };
            chk.Checked += (_, _) => Refrescar();
            chk.Unchecked += (_, _) => Refrescar();
            _idiomas.Add(chk);
        }
        listaIdiomas.ItemsSource = _idiomas;

        txtSerie.TextChanged += (_, _) => Refrescar();
        txtFuente.TextChanged += (_, _) => Refrescar();
        cboSalida.SelectionChanged += (_, _) => Refrescar();

        btnCerrar.Click += (_, _) => Close();
        btnCopiar.Click += (_, _) => Copiar();
        btnAbrirFuente.Click += (_, _) => AbrirFuente();

        Refrescar();
    }

    private string IdiomaSalida =>
        CatalogPrompt.IdiomasConocidos[Math.Max(0, cboSalida.SelectedIndex)].Codigo;

    private List<string> IdiomasMarcados =>
        _idiomas.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();

    private void Refrescar()
    {
        txtPrompt.Text = CatalogPrompt.Build(txtSerie.Text, txtFuente.Text, IdiomaSalida, IdiomasMarcados);

        // Avisar del error que más caro sale: no incluir el idioma en el que vienen tus
        // ficheros hoy, y descubrirlo cuando ya no reconoce ninguno.
        lblAviso.Text = IdiomasMarcados.Count <= 1
            ? "Con un solo idioma, solo reconocerá los ficheros titulados en ese idioma."
            : $"{IdiomasMarcados.Count} idiomas para reconocer · el nombre se escribirá en {CatalogPrompt.Nombre(IdiomaSalida)}";
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
