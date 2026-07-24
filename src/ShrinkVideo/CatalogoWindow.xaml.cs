using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private readonly bool _modoElegir;

    /// <summary>En modo elegir: el episodio escogido y, si es solo una historia, su letra.</summary>
    public CatalogEpisode? Elegido { get; private set; }
    public string? SegElegido { get; private set; }

    public CatalogoWindow(ReindexCatalog cat, string? consultaInicial = null, bool modoElegir = false)
    {
        InitializeComponent();
        _cat = cat;
        _modoElegir = modoElegir;

        if (modoElegir)
        {
            lblPie.Text = "Doble clic para elegir el episodio de este fichero. Si el episodio " +
                          "tiene varias historias, podrás decir si el fichero es solo una de ellas.";
            lista.MouseDoubleClick += (_, _) => ElegirSeleccionado();
        }

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
            try { Clipboard.SetText(_jsonActual); lblJsonTitulo.Text += "  · copiado"; }
            catch { /* portapapeles ocupado por otro proceso: se reintenta a mano */ }
        };
        // Cerrar también deselecciona: si la fila siguiera elegida, volver a pincharla no
        // dispararía el cambio de selección y el panel no reaparecería.
        btnCerrarJson.Click += (_, _) => lista.SelectedItem = null;
    }

    /// <summary>El JSON tal cual, sin colores, para el botón de copiar.</summary>
    private string _jsonActual = "";

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
            bordeJson.Visibility = Visibility.Collapsed;   // sin esto quedaba una tira del borde
            return;
        }

        colJson.Width = new GridLength(300);
        bordeJson.Visibility = Visibility.Visible;
        lblJsonTitulo.Text = $"E{v.Ep.Num} · JSON del catálogo";
        _jsonActual = System.Text.Json.JsonSerializer.Serialize(v.Ep, OpcionesJson);

        // Se VACÍA y RELLENA el documento existente en vez de asignar uno nuevo: reemplazar
        // Document desengancha al lector de accesibilidad, que se queda leyendo el documento
        // original (vacío) para siempre. Con el mismo documento, lo que se pinta y lo que se
        // lee son la misma cosa — y además se puede verificar.
        var doc = rtbJson.Document;
        doc.Blocks.Clear();
        doc.PageWidth = 2000;   // sin renglones artificiales: las líneas son las del JSON
        doc.Blocks.Add(Colorear(_jsonActual));
    }

    // ── coloreado de sintaxis ──
    // Los colores son los del tema, no los de VS: claves en el acento, cadenas en el verde
    // del semáforo, números en su ámbar y la puntuación apagada. Así el panel es de esta
    // app y no un pegote de otro editor.
    private static readonly System.Windows.Media.Brush ColClave = Pincel("Accent300");
    private static readonly System.Windows.Media.Brush ColCadena = Pincel("OrgOk");
    private static readonly System.Windows.Media.Brush ColNumero = Pincel("OrgWarn");
    private static readonly System.Windows.Media.Brush ColBool = Pincel("OrgDanger");
    private static readonly System.Windows.Media.Brush ColSigno = Pincel("Neutral500");

    private static System.Windows.Media.Brush Pincel(string clave) =>
        Application.Current?.TryFindResource(clave) as System.Windows.Media.Brush
        ?? System.Windows.Media.Brushes.Gray;

    /// <summary>
    /// Un JSON como documento coloreado. El recorrido es un autómata mínimo, no un parser:
    /// este JSON lo acaba de producir el serializador, así que siempre está bien formado y
    /// basta con distinguir cadena / número / palabra / signo. La única decisión con miga:
    /// una cadena es CLAVE si lo siguiente (saltando espacios) son los dos puntos.
    /// </summary>
    private static System.Windows.Documents.Paragraph Colorear(string json)
    {
        var parrafo = new System.Windows.Documents.Paragraph { Margin = new Thickness(0) };
        int i = 0;

        void Trozo(string t, System.Windows.Media.Brush b) =>
            parrafo.Inlines.Add(new System.Windows.Documents.Run(t) { Foreground = b });

        while (i < json.Length)
        {
            char c = json[i];

            if (c == '"')
            {
                int j = i + 1;
                while (j < json.Length && (json[j] != '"' || json[j - 1] == '\\')) j++;
                var cadena = json[i..Math.Min(j + 1, json.Length)];

                int k = j + 1;
                while (k < json.Length && char.IsWhiteSpace(json[k])) k++;
                bool esClave = k < json.Length && json[k] == ':';

                Trozo(cadena, esClave ? ColClave : ColCadena);
                i = j + 1;
            }
            else if (char.IsDigit(c) || c == '-')
            {
                int j = i;
                while (j < json.Length && (char.IsDigit(json[j]) || json[j] is '-' or '+' or '.' or 'e' or 'E')) j++;
                Trozo(json[i..j], ColNumero);
                i = j;
            }
            else if (char.IsLetter(c))   // true / false (null no llega: se omite al serializar)
            {
                int j = i;
                while (j < json.Length && char.IsLetter(json[j])) j++;
                Trozo(json[i..j], ColBool);
                i = j;
            }
            else
            {
                int j = i;
                while (j < json.Length && json[j] is not ('"' or '-') &&
                       !char.IsLetterOrDigit(json[j])) j++;
                Trozo(json[i..j], ColSigno);
                i = j;
            }
        }

        return parrafo;
    }

    // ── modo elegir ──

    private void ElegirSeleccionado()
    {
        if (lista.SelectedItem is not EpisodioVista v) return;

        // Con una sola historia no hay nada que preguntar; con varias, sí: el fichero puede
        // ser el episodio entero o solo uno de sus trozos.
        if (v.Ep.TitulosSalida.Count <= 1) { Terminar(v.Ep, null); return; }

        int n = v.Ep.TitulosSalida.Count;
        lblHistoriaTitulo.Text = $"El episodio {v.Ep.Num} tiene {n} historias. ¿Cuáles trae este fichero?";
        panelHistorias.Children.Clear();

        Button Boton(string texto, Action accion)
        {
            var b = new Button
            {
                Content = texto,
                Style = (Style)FindResource("BtnSecondary"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 12.5,
            };
            b.Click += (_, _) => accion();
            return b;
        }

        // El caso más común, destacado: el episodio entero.
        panelHistorias.Children.Add(Boton("El episodio completo", () => Terminar(v.Ep, null)));

        // O SOLO ALGUNAS: un checkbox por historia. Se pueden marcar varias (p. ej. la a y la c
        // de tres), no solo una. Las letras de las marcadas van pegadas al número (E413ac).
        panelHistorias.Children.Add(new TextBlock
        {
            Text = "O marca solo las historias que trae:",
            FontSize = 11.5, Foreground = (Brush)FindResource("Neutral500"),
            Margin = new Thickness(2, 6, 0, 6),
        });

        var casillas = new List<CheckBox>();
        for (int i = 0; i < n && i < 6; i++)
        {
            char letra = (char)('a' + i);
            var chk = new CheckBox
            {
                Content = $"«{v.Ep.TitulosSalida[i]}»  →  E{v.Ep.Num}{letra}",
                Foreground = (Brush)FindResource("Text"),
                FontSize = 12.5,
                Margin = new Thickness(2, 0, 0, 7),
            };
            casillas.Add(chk);
            panelHistorias.Children.Add(chk);
        }

        var aceptar = Boton("Usar las historias marcadas", () =>
        {
            var letras = new string(Enumerable.Range(0, casillas.Count)
                .Where(i => casillas[i].IsChecked == true)
                .Select(i => (char)('a' + i))
                .ToArray());
            if (letras.Length == 0) return;                          // nada marcado: no hace nada
            Terminar(v.Ep, letras.Length == n ? null : letras);      // todas marcadas = el completo
        });
        aceptar.Style = (Style)FindResource("BtnPrimary");
        panelHistorias.Children.Add(aceptar);

        var cancelar = Boton("Cancelar", () => overlayHistoria.Visibility = Visibility.Collapsed);
        cancelar.Style = (Style)FindResource("BtnGhostMuted");
        panelHistorias.Children.Add(cancelar);

        overlayHistoria.Visibility = Visibility.Visible;
    }

    private void Terminar(CatalogEpisode ep, string? seg)
    {
        Elegido = ep;
        SegElegido = seg;
        DialogResult = true;
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
