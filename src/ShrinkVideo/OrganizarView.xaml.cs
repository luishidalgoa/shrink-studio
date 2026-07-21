using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ShrinkVideo.Reindex;

namespace ShrinkVideo;

/// <summary>Tarjeta de catálogo de la pantalla de inicio.</summary>
public sealed class CatalogoCard
{
    public required CatalogoGuardado Cat { get; init; }
    public bool Seleccionado { get; init; }
    public bool NoSeleccionado => !Seleccionado;
    public Brush Fondo => Seleccionado
        ? (Brush)Application.Current.FindResource("Accent900")
        : (Brush)Application.Current.FindResource("Surface");
    public Brush Borde => Seleccionado
        ? (Brush)Application.Current.FindResource("Accent700")
        : (Brush)Application.Current.FindResource("Divider");
}

/// <summary>
/// Página «Organizar»: identifica los ficheros de una carpeta contra un catálogo y propone
/// el nombre canónico. La inteligencia vive en <see cref="ReindexEngine"/>; aquí solo se
/// orquesta y se pinta.
/// </summary>
public partial class OrganizarView : UserControl
{
    private readonly ObservableCollection<OrganizarRow> _filas = new();
    private readonly List<CatalogoGuardado> _catalogos = new();
    private CatalogoGuardado? _catalogoElegido;
    private ReindexCatalog? _catalogoCargado;
    private LibraryTemplate _plantilla = new();
    private Dictionary<string, ReindexOverride> _decisiones = new();
    private LoteJournal? _ultimoLote;
    private string[] _ficheros = Array.Empty<string>();
    private bool _cargando;

    /// <summary>Se avisa al anfitrión para que lo escriba en el registro compartido.</summary>
    public event Action<string>? Log;

    public OrganizarView()
    {
        InitializeComponent();

        tabla.ItemsSource = _filas;
        listaCatalogos.ItemsSource = new ObservableCollection<CatalogoCard>();

        txtPlantilla.Text = LibraryTemplate.PatronPorDefecto;

        btnCarpeta.Click += (_, _) => ElegirCarpeta();
        btnImportar.Click += (_, _) => ImportarCatalogo();
        btnCatalogos.Click += (_, _) => ImportarCatalogo();
        btnFormato.Click += (_, _) => AbrirEspecificacion();
        btnEjemplo.Click += (_, _) => GuardarEjemplo();
        btnPrompt.Click += (_, _) => AbrirGeneradorDePrompt();
        btnVolver.Click += (_, _) => VolverAlInicio();
        btnSimular.Click += (_, _) => Simular();
        btnSimularGrande.Click += (_, _) => Simular();
        btnAplicar.Click += (_, _) => PedirConfirmacion();
        btnDeshacer.Click += (_, _) => DeshacerUltimoLote();
        btnDeshacerBanda.Click += (_, _) => DeshacerUltimoLote();
        btnMemoria.Click += (_, _) => AbrirMemoria();
        btnAceptarVerdes.Click += (_, _) => AceptarVerdes();
        btnConfirmarEspeciales.Click += (_, _) => FiltrarSolo(ReindexEstado.Especial);
        listaMarcas.ItemsSource = LibraryTemplate.Marcas;

        btnConfCancelar.Click += (_, _) => overlayConfirmar.Visibility = Visibility.Collapsed;
        btnConfAceptar.Click += (_, _) => { overlayConfirmar.Visibility = Visibility.Collapsed; Aplicar(); };

        txtCarpeta.LostFocus += (_, _) => RevisarCarpeta();
        txtCarpeta.KeyDown += (_, e) => { if (e.Key == Key.Enter) RevisarCarpeta(); };
        txtPlantilla.LostFocus += (_, _) => CambiarPlantilla();
        txtPlantilla.KeyDown += (_, e) => { if (e.Key == Key.Enter) CambiarPlantilla(); };
        // La vista previa se refresca mientras escribes, no al confirmar: es lo que hace que
        // se entienda qué produce cada marca.
        txtPlantilla.TextChanged += (_, _) => RefrescarVistaPrevia();

        cboSerie.SelectionChanged += (_, _) => ElegirCatalogo(cboSerie.SelectedItem as CatalogoGuardado);

        foreach (var chip in new[] { chipLimpios, chipCorregidos, chipEspeciales, chipConflictos, chipErrores, chipDudas })
        {
            chip.Checked += (_, _) => AplicarFiltro();
            chip.Unchecked += (_, _) => AplicarFiltro();
        }

        tabla.PreviewKeyDown += OnTablaKeyDown;
        tabla.PreviewMouseLeftButtonDown += OnTablaClic;

        // Con la ventana estrecha, el rótulo de la sección se recortaba a un par de puntos
        // suspensivos, que queda peor que no estar: los botones de al lado ya dicen de qué
        // va la columna. Por debajo de ese ancho se retira entero.
        //
        // El umbral sale de la cuenta real, no a ojo: el panel de catálogos ocupa la mitad
        // de la página, los tres botones piden unos 330 px y el rótulo necesita ~140 para
        // leerse entero. Media página ≥ 470 ⇒ página ≥ 990.
        SizeChanged += (_, _) =>
            lblTituloCatalogos.Visibility = ActualWidth >= 990 ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) => { if (!_cargando) Recargar(); };
    }

    // ─────────────────────────── arranque ───────────────────────────

    private void Recargar()
    {
        _cargando = true;
        try
        {
            _decisiones = ReindexStore.CargarDecisiones();
            CargarCatalogos();
            RefrescarUltimoLote();
        }
        finally { _cargando = false; }
        ActualizarEstado();
        RefrescarVistaPrevia();
    }

    private void CargarCatalogos()
    {
        _catalogos.Clear();
        _catalogos.AddRange(ReindexStore.ListarCatalogos());

        cboSerie.ItemsSource = null;
        cboSerie.ItemsSource = _catalogos;

        if (_catalogoElegido != null)
            _catalogoElegido = _catalogos.FirstOrDefault(c => c.Ruta == _catalogoElegido.Ruta);

        // La de la última vez antes que «la primera de la lista»: con dos catálogos, arrancar
        // siempre en el alfabéticamente primero significa elegir a mano en cada arranque.
        if (_catalogoElegido == null)
        {
            var ultima = ReindexStore.CargarUltimoCatalogo();
            _catalogoElegido = _catalogos.FirstOrDefault(c => c.Ruta == ultima);
        }
        _catalogoElegido ??= _catalogos.FirstOrDefault();

        cboSerie.SelectedItem = _catalogoElegido;
        panelSinCatalogos.Visibility = _catalogos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PintarTarjetas();
        CargarCatalogoElegido();
    }

    private void PintarTarjetas()
    {
        listaCatalogos.ItemsSource = _catalogos
            .Select(c => new CatalogoCard { Cat = c, Seleccionado = c.Ruta == _catalogoElegido?.Ruta })
            .ToList();
    }

    private void CargarCatalogoElegido()
    {
        _catalogoCargado = null;
        if (_catalogoElegido == null) return;
        try { _catalogoCargado = ReindexCatalog.Load(_catalogoElegido.Ruta); }
        catch (Exception ex) { Aviso($"No se pudo leer el catálogo: {ex.Message}"); }
    }

    private void ElegirCatalogo(CatalogoGuardado? cat)
    {
        if (cat == null || cat.Ruta == _catalogoElegido?.Ruta) return;
        _catalogoElegido = cat;
        ReindexStore.GuardarUltimoCatalogo(cat.Ruta);
        cboSerie.SelectedItem = cat;
        PintarTarjetas();
        CargarCatalogoElegido();
        ActualizarEstado();
        RefrescarVistaPrevia();
    }

    private void OnUsarCatalogo(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string ruta)
            ElegirCatalogo(_catalogos.FirstOrDefault(c => c.Ruta == ruta));
    }

    /// <summary>
    /// Saca un catálogo de la app. Se pregunta antes porque no hay «deshacer» para esto, y se
    /// dice de dónde salió: si el JSON original sigue en su sitio, volver a importarlo es
    /// trivial, y saberlo cambia por completo lo que cuesta decir que sí.
    /// </summary>
    private void OnQuitarCatalogo(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string ruta) return;

        var cat = _catalogos.FirstOrDefault(c => c.Ruta == ruta);
        if (cat == null) return;

        var deDonde = cat.OrigenRuta.Length > 0
            ? $"\n\nSe importó de:\n{cat.OrigenRuta}\n\nEse fichero NO se borra: podrás volver a importarlo."
            : "\n\nNo consta de qué fichero se importó, así que para recuperarlo tendrás que buscarlo tú.";

        if (!DialogWindow.Confirmar(Window.GetWindow(this), "Quitar catálogo",
                $"¿Quitar «{cat.Serie}» de la app?{deDonde}")) return;

        if (ReindexStore.BorrarCatalogo(ruta))
        {
            Escribir($"Catálogo quitado: «{cat.Serie}».");
            if (_catalogoElegido?.Ruta == ruta) _catalogoElegido = null;
            Recargar();
        }
        else Aviso("Ese catálogo ya no estaba.");
    }

    // ─────────────────────────── carpeta ───────────────────────────

    private void ElegirCarpeta()
    {
        var dlg = new OpenFolderDialog { Title = "Carpeta a organizar" };
        if (!string.IsNullOrWhiteSpace(txtCarpeta.Text) && Directory.Exists(txtCarpeta.Text))
            dlg.InitialDirectory = txtCarpeta.Text;
        if (dlg.ShowDialog() == true)
        {
            txtCarpeta.Text = dlg.FolderName;
            RevisarCarpeta();
        }
    }

    /// <summary>
    /// Cuenta los vídeos de la carpeta Y DE SUS SUBCARPETAS. No lee metadatos: eso es «Simular».
    ///
    /// El recorrido baja porque así está montada una biblioteca: se apunta a la carpeta de la
    /// serie y las temporadas cuelgan dentro. Quedándose en el primer nivel, una serie entera
    /// se veía como «no hay vídeos».
    /// </summary>
    private void RevisarCarpeta()
    {
        var carpeta = txtCarpeta.Text?.Trim() ?? "";
        _ficheros = Array.Empty<string>();

        try { _ficheros = LibraryScan.Escanear(carpeta, Engine.VideoExtensions); }
        catch (Exception ex) { Aviso($"No se pudo leer la carpeta: {ex.Message}"); }

        int carpetas = _ficheros.Select(f => LibraryScan.Grupo(carpeta, f)).Distinct().Count();

        lblFicheros.Text = _ficheros.Length switch
        {
            0 when !Directory.Exists(carpeta) => "Elige una carpeta para empezar",
            0 => "No hay vídeos en esta carpeta ni en sus subcarpetas",
            1 => $"1 fichero en {carpeta}",
            // Decir en cuántas carpetas están confirma que el recorrido ha bajado: si sale «1»
            // sobre una serie con temporadas, se sabe al momento que se apuntó demasiado adentro.
            _ when carpetas > 1 => $"{_ficheros.Length} ficheros en {carpetas} carpetas de {carpeta}",
            _ => $"{_ficheros.Length} ficheros en {carpeta}",
        };

        // Volver a elegir carpeta invalida lo que hubiera en la tabla
        MostrarInicio();
        ActualizarEstado();
    }

    private void CambiarPlantilla()
    {
        var nueva = new LibraryTemplate(txtPlantilla.Text);
        if (nueva.Patron == _plantilla.Patron) return;
        _plantilla = nueva;
        txtPlantilla.Text = nueva.Patron;

        // La plantilla cambia el nombre propuesto de cada fila ya calculada
        foreach (var f in _filas) f.Recalcular();
        ActualizarContadores();
        RefrescarVistaPrevia();
    }

    /// <summary>Inserta la marca donde esté el cursor, no al final: se está editando un patrón.</summary>
    private void OnInsertarMarca(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string marca) return;

        int pos = txtPlantilla.SelectionStart;
        txtPlantilla.Text = txtPlantilla.Text.Remove(pos, txtPlantilla.SelectionLength).Insert(pos, marca);
        txtPlantilla.SelectionStart = pos + marca.Length;

        btnMarcas.IsChecked = false;
        txtPlantilla.Focus();
        CambiarPlantilla();
    }

    /// <summary>
    /// Enseña cómo queda la plantilla con un episodio DE VERDAD del catálogo elegido. Con un
    /// ejemplo inventado no se ve el problema de siempre: que el título real trae dos puntos,
    /// interrogaciones o tres segmentos encadenados.
    /// </summary>
    private void RefrescarVistaPrevia()
    {
        if (lblVistaPrevia == null) return;

        var ejemplo = _catalogoCargado?.Episodios.FirstOrDefault(x => x.TitulosSalida.Count > 0)
                      ?? _catalogoCargado?.Episodios.FirstOrDefault();

        if (_catalogoCargado == null || ejemplo == null)
        {
            lblVistaPrevia.Text = "Elige un catálogo para ver un ejemplo";
            return;
        }

        var muestra = _filas.FirstOrDefault()?.Res.Archivo ?? SignalExtractor.Extract("ejemplo.mkv", "");
        var nombre = new LibraryTemplate(txtPlantilla.Text).Render(_catalogoCargado, ejemplo, muestra);
        lblVistaPrevia.Text = nombre == null
            ? "⚠ Esa plantilla no deja nombre: añade alguna marca o texto"
            : "Quedaría: " + nombre;

        // El «Quedaría:» se corta casi siempre —estos títulos son larguísimos— así que el
        // ejemplo entero va también al globo, que es donde cabe entero.
        var globo = nombre == null
            ? ExplicacionPlantilla
            : $"{ExplicacionPlantilla}\n\nCon «{_catalogoCargado.Serie}» quedaría:\n{nombre}";

        txtPlantilla.ToolTip = Globo(globo);
        lblVistaPrevia.ToolTip = Globo(globo);
    }

    private const string ExplicacionPlantilla =
        "Cómo se compone el nombre final. No es el «Renombrado libre» de Herramientas: " +
        "aquí el nombre se construye desde el catálogo.";

    /// <summary>
    /// Un globo con el texto ajustado. Cada llamada crea el suyo: un mismo elemento visual no
    /// puede colgar de dos sitios, así que compartirlo dejaría el segundo en blanco.
    /// </summary>
    private static TextBlock Globo(string texto) => new()
    {
        Text = texto,
        MaxWidth = 460,
        TextWrapping = TextWrapping.Wrap,
    };

    // ─────────────────────────── catálogos ───────────────────────────

    private void ImportarCatalogo()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Importar catálogo de referencia",
            Filter = "Catálogo de reindexado (*.json)|*.json|Todos los archivos|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var guardado = ReindexStore.ImportarCatalogo(dlg.FileName);
            _catalogoElegido = guardado;
            CargarCatalogos();
            Escribir($"Catálogo importado: {guardado.Serie} ({guardado.Episodios} episodios).");
            ActualizarEstado();
        }
        catch (ReindexCatalogException ex) { Aviso(ex.Message); }
        catch (Exception ex) { Aviso($"No se pudo importar: {ex.Message}"); }
    }

    /// <summary>
    /// Especificación del formato. Va al repositorio y no a un texto embebido a propósito:
    /// así se corrige sin publicar una versión, y siempre se lee la vigente.
    /// </summary>
    private const string UrlEspecificacion =
        "https://github.com/luishidalgoa/shrink-studio/blob/main/docs/catalogo-reindex.md";

    private void AbrirEspecificacion()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UrlEspecificacion)
            { UseShellExecute = true });
        }
        catch (Exception ex) { Aviso($"No se pudo abrir la documentación: {ex.Message}\n\n{UrlEspecificacion}"); }
    }

    /// <summary>
    /// Guarda un catálogo de ejemplo VÁLIDO para editar. Partir de algo que ya funciona
    /// evita el peor arranque posible: escribir el JSON a ciegas y que el primer intento
    /// de importar sea una lista de errores.
    /// </summary>
    /// <summary>
    /// Abre el generador del encargo para la IA. Se le sugiere el nombre de la serie que
    /// tengas elegida, que es el caso más común: ampliar un catálogo que ya usas.
    /// </summary>
    private void AbrirGeneradorDePrompt()
    {
        var ventana = new PromptWindow(_catalogoElegido?.Serie ?? "")
        { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }

    private void GuardarEjemplo()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Guardar catálogo de ejemplo",
            Filter = "Catálogo de reindexado (*.json)|*.json",
            FileName = "mi-serie.reindex.json",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, ReindexCatalog.Ejemplo, new System.Text.UTF8Encoding(false));
            Escribir($"Catálogo de ejemplo guardado en {dlg.FileName}.");

            var r = DialogWindow.Confirmar(Window.GetWindow(this), "Organizar", "Ejemplo guardado.\n\nEdítalo con tus episodios y luego impórtalo. " +
                "Si algo no encaja, al importar se te dirá exactamente qué corregir.\n\n" +
                "¿Quieres abrir la especificación del formato?");
            if (r) AbrirEspecificacion();
        }
        catch (Exception ex) { Aviso($"No se pudo guardar el ejemplo: {ex.Message}"); }
    }

    // ─────────────────────────── simulación ───────────────────────────

    private async void Simular()
    {
        if (_catalogoCargado == null || _ficheros.Length == 0) return;

        btnSimular.IsEnabled = btnSimularGrande.IsEnabled = false;
        lblEstadoOrg.Text = "Identificando…";

        var catalogo = _catalogoCargado;
        var ficheros = _ficheros;
        var decisiones = _decisiones;

        try
        {
            // El motor es puro y puede tardar en lotes grandes: fuera del hilo de interfaz.
            var resoluciones = await Task.Run(() =>
            {
                // Nota: el título del metadato del contenedor (titulo_meta) todavía no se lee.
                // Exigiría un ffprobe por fichero y «Simular» dejaría de ser inmediato en
                // bibliotecas de cientos. El motor ya lo admite cuando se enganche.
                var señales = ficheros
                    .Select(f => SignalExtractor.Extract(f, new DirectoryInfo(Path.GetDirectoryName(f)!).Name))
                    .ToList();
                return ReindexEngine.Resolve(señales, catalogo, decisiones);
            });

            var raiz = txtCarpeta.Text?.Trim() ?? "";
            _filas.Clear();
            foreach (var r in resoluciones)
                _filas.Add(new OrganizarRow(r, catalogo, _plantilla,
                    LibraryScan.Etiqueta(LibraryScan.Grupo(raiz, r.Archivo.Path))));

            int temporadas = RecalcularSeparadores();
            MostrarRevision();
            ActualizarContadores();
            Escribir($"Simulación: {_filas.Count} ficheros contra «{catalogo.Serie}»" +
                     (temporadas > 0 ? $", repartidos en {temporadas} temporadas." : "."));
        }
        catch (Exception ex) { Aviso($"La simulación falló: {ex.Message}"); }
        finally { ActualizarEstado(); }
    }

    /// <summary>
    /// Descarta la simulación y vuelve a la pantalla de inicio.
    ///
    /// No se pregunta nada porque no se pierde nada: las decisiones que hayas tomado a mano
    /// se guardan en disco en cuanto las tomas, y al volver a simular se reaplican solas
    /// («Lo decidiste tú antes»). Lo único que se tira es el cálculo, que se rehace en
    /// segundos.
    /// </summary>
    private void VolverAlInicio()
    {
        MostrarInicio();
        ActualizarContadores();
        ActualizarEstado();
    }

    private void MostrarInicio()
    {
        _filas.Clear();
        vistaInicio.Visibility = Visibility.Visible;
        vistaRevision.Visibility = Visibility.Collapsed;
        filaChips.Visibility = Visibility.Collapsed;
        bannerAplicado.Visibility = Visibility.Collapsed;
    }

    private void MostrarRevision()
    {
        vistaInicio.Visibility = Visibility.Collapsed;
        vistaRevision.Visibility = Visibility.Visible;
        filaChips.Visibility = Visibility.Visible;
        bannerAplicado.Visibility = Visibility.Collapsed;
    }

    // ─────────────────────────── contadores y filtro ───────────────────────────

    private void ActualizarContadores()
    {
        int limpios = _filas.Count(f => f.Res.Estado == ReindexEstado.Limpio);
        int corregidos = _filas.Count(f => f.Res.Estado == ReindexEstado.Corregido);
        int especiales = _filas.Count(f => f.Res.Estado == ReindexEstado.Especial);
        int conflictos = _filas.Count(f => f.Res.Estado == ReindexEstado.Conflicto);
        int errores = _filas.Count(f => f.Res.Estado == ReindexEstado.Error);

        runLimpios.Text = $" {limpios} limpios";
        runCorregidos.Text = $" {corregidos} corregidos";
        runEspeciales.Text = $" {especiales} especiales";
        runConflictos.Text = $" {conflictos} conflictos";
        runErrores.Text = $" {errores} errores";

        chipEspeciales.IsEnabled = especiales > 0;
        chipConflictos.IsEnabled = conflictos > 0;
        chipErrores.IsEnabled = errores > 0;
        btnConfirmarEspeciales.IsEnabled = especiales > 0;

        int listos = _filas.Count(f => f.ListoParaAplicar);
        int dudas = _filas.Count(f => f.EsDuda);

        lblAplicar.Text = listos > 0 ? $"Aplicar {listos} listos" : "Aplicar";
        btnAplicar.IsEnabled = listos > 0;
        btnAceptarVerdes.IsEnabled = listos > 0;

        // Los que ya estaban bien se dicen aparte: si no, «383 listos · 165 por despachar» sobre
        // 548 deja 0 sin explicar y parece que se han perdido por el camino.
        int hechos = _filas.Count(f => f.SinCambios);
        lblEstadoOrg.Text = $"{_filas.Count} ficheros · {listos} listos para aplicar · {dudas} por despachar"
                            + (hechos > 0 ? $" · {hechos} ya estaban bien" : "");

        // Si la mayoría son dudas, se dice de frente en vez de dejar que lo descubra fila a fila
        if (_filas.Count > 0 && dudas > _filas.Count / 2)
        {
            lblBannerAviso.Text = $"{dudas} de {_filas.Count} ficheros necesitan que decidas tú. " +
                                  ExplicarPorQueTantasDudas();
            bannerAviso.Visibility = Visibility.Visible;
        }
        else bannerAviso.Visibility = Visibility.Collapsed;
    }

    private string ExplicarPorQueTantasDudas()
    {
        var avisos = _catalogoCargado?.Advertencias ?? Array.Empty<string>();
        var sinFechas = avisos.FirstOrDefault(a => a.Contains("Sin fechas"));
        if (sinFechas != null) return "Este catálogo no trae fechas de emisión, así que solo se puede tirar del título.";
        return "Revisa los conflictos y los especiales antes de aplicar.";
    }

    /// <summary>
    /// Marca qué fila abre cada temporada, que es la que lleva encima la banda separadora.
    ///
    /// Se calcula sobre la vista YA FILTRADA, no sobre <c>_filas</c>: si escondes las limpias,
    /// la banda tiene que salir sobre la primera que quede —no sobre una que no se ve— y el
    /// recuento tiene que ser el de las visibles.
    ///
    /// Solo se separa si hay más de una carpeta: con una sola, la banda repetiría lo que ya
    /// dice el cuadro de la carpeta y se comería una fila por nada.
    /// </summary>
    /// <summary>
    /// Volver a pinchar una fila ya abierta la cierra. Sin esto, el resolutor se queda
    /// desplegado y la única forma de recogerlo es abrir otra fila.
    ///
    /// Solo cuenta el clic sobre una CELDA: dentro del desplegable hay botones («Cambiar a
    /// E318»), y si el clic ahí también cerrara la fila, elegir un candidato sería imposible.
    /// </summary>
    private void OnTablaClic(object sender, MouseButtonEventArgs e)
    {
        var celda = Ascender<DataGridCell>(e.OriginalSource as DependencyObject);
        if (celda == null) return;

        var fila = Ascender<DataGridRow>(celda);
        if (fila is not { IsSelected: true }) return;

        tabla.SelectedItem = null;
        e.Handled = true;
    }

    private static T? Ascender<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    /// <returns>Cuántas temporadas han quedado separadas. 0 = no hay nada que separar.</returns>
    private int RecalcularSeparadores()
    {
        var vista = CollectionViewSource.GetDefaultView(_filas);
        if (vista == null) return 0;

        var visibles = vista.Cast<OrganizarRow>().ToList();
        foreach (var f in visibles) { f.PrimeraDeGrupo = false; f.GrupoConteo = ""; }

        if (visibles.Select(f => f.Grupo).Distinct().Count() <= 1) return 0;

        int bandas = 0;
        for (int i = 0; i < visibles.Count;)
        {
            int j = i;
            while (j < visibles.Count && visibles[j].Grupo == visibles[i].Grupo) j++;

            visibles[i].PrimeraDeGrupo = true;
            visibles[i].GrupoConteo = j - i == 1 ? "· 1 fichero" : $"· {j - i} ficheros";
            bandas++;
            i = j;
        }
        return bandas;
    }

    private void AplicarFiltro()
    {
        var vista = CollectionViewSource.GetDefaultView(_filas);
        if (vista == null) return;

        bool soloDudas = chipDudas.IsChecked == true;
        var estados = new List<ReindexEstado>();
        if (chipLimpios.IsChecked == true) estados.Add(ReindexEstado.Limpio);
        if (chipCorregidos.IsChecked == true) estados.Add(ReindexEstado.Corregido);
        if (chipEspeciales.IsChecked == true) estados.Add(ReindexEstado.Especial);
        if (chipConflictos.IsChecked == true) estados.Add(ReindexEstado.Conflicto);
        if (chipErrores.IsChecked == true) estados.Add(ReindexEstado.Error);

        if (estados.Count == 0 && !soloDudas) { vista.Filter = null; RecalcularSeparadores(); return; }

        vista.Filter = o =>
        {
            if (o is not OrganizarRow f) return false;
            if (soloDudas && !f.EsDuda) return false;
            return estados.Count == 0 || estados.Contains(f.Res.Estado);
        };

        // Las bandas dependen de lo que quede visible, así que se rehacen tras cada filtro
        RecalcularSeparadores();
    }

    private void FiltrarSolo(ReindexEstado estado)
    {
        chipLimpios.IsChecked = chipCorregidos.IsChecked = chipConflictos.IsChecked = chipErrores.IsChecked = false;
        chipDudas.IsChecked = false;
        chipEspeciales.IsChecked = estado == ReindexEstado.Especial;
        AplicarFiltro();
    }

    /// <summary>Da por buenas las filas verdes: no cambia nada, solo confirma lo evidente.</summary>
    private void AceptarVerdes()
    {
        int n = _filas.Count(f => f.ListoParaAplicar);
        Escribir($"{n} filas verdes aceptadas; listas para aplicar.");
        chipDudas.IsChecked = true;   // lo interesante ya es solo lo que falta por decidir
    }

    // ─────────────────────────── resolutor ───────────────────────────

    private void OnElegirCandidato(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not CatalogEpisode ep) return;
        if (tabla.SelectedItem is not OrganizarRow fila) return;

        fila.ElegirEpisodio(ep);
        RecordarDecision(fila, ep);
        ActualizarContadores();
        Escribir($"«{fila.Original}» → episodio {ep.Num} (elegido a mano).");
    }

    private void OnElegirAMano(object sender, RoutedEventArgs e)
    {
        if (tabla.SelectedItem is not OrganizarRow fila || _catalogoCargado == null) return;
        Aviso($"Elegir un episodio cualquiera del catálogo para «{fila.Original}» todavía no está montado. " +
              "Por ahora puedes escoger entre los candidatos propuestos.");
    }

    private void OnDejarComoEsta(object sender, RoutedEventArgs e)
    {
        if (tabla.SelectedItem is not OrganizarRow fila) return;
        fila.Res.Estado = ReindexEstado.Limpio;
        fila.Res.Confianza = ReindexConfianza.Alta;
        fila.Res.Episodio = null;
        fila.Res.Motivo = "Lo dejaste como estaba";
        fila.Res.Alternativas = Array.Empty<ReindexCandidato>();
        fila.Recalcular();
        ActualizarContadores();
    }

    private void RecordarDecision(OrganizarRow fila, CatalogEpisode ep)
    {
        _decisiones[fila.Res.Archivo.Fingerprint] = new ReindexOverride
        {
            Num = ep.Num,
            Temporada = ep.Temporada,
            Serie = _catalogoCargado?.Serie ?? "",
            Origen = "usuario",
            FechaDecision = DateTime.Now.ToString("yyyy-MM-dd"),
            NombreOriginal = fila.Original,
        };
        try { ReindexStore.GuardarDecisiones(_decisiones); }
        catch (Exception ex) { Escribir($"No se pudo guardar la decisión: {ex.Message}"); }
    }

    /// <summary>Triaje por teclado: Enter abre el resolutor, 1/2 eligen candidato.</summary>
    private void OnTablaKeyDown(object sender, KeyEventArgs e)
    {
        if (tabla.SelectedItem is not OrganizarRow fila) return;

        if (e.Key is Key.D1 or Key.NumPad1 or Key.D2 or Key.NumPad2)
        {
            int idx = e.Key is Key.D1 or Key.NumPad1 ? 0 : 1;
            if (fila.Res.Alternativas.Count > idx)
            {
                var ep = fila.Res.Alternativas[idx].Episodio;
                fila.ElegirEpisodio(ep);
                RecordarDecision(fila, ep);
                ActualizarContadores();
                e.Handled = true;
            }
        }
    }

    // ─────────────────────────── aplicar ───────────────────────────

    private void PedirConfirmacion()
    {
        var listos = _filas.Where(f => f.ListoParaAplicar).ToList();
        if (listos.Count == 0) return;

        int dudas = _filas.Count(f => f.EsDuda);

        lblConfSeRenombra.Text = listos.Count == 1
            ? "Se renombra 1 fichero identificado con confianza."
            : $"Se renombran {listos.Count} ficheros identificados con confianza.";

        if (dudas > 0)
        {
            lblConfNoSeToca.Text = dudas == 1
                ? "1 fichero con dudas se queda exactamente como está."
                : $"{dudas} ficheros con dudas se quedan exactamente como están.";
            filaNoSeToca.Visibility = Visibility.Visible;
        }
        else filaNoSeToca.Visibility = Visibility.Collapsed;

        overlayConfirmar.Visibility = Visibility.Visible;
    }

    private void Aplicar()
    {
        var listos = _filas.Where(f => f.ListoParaAplicar).ToList();
        if (listos.Count == 0) return;

        var ahora = DateTime.Now;
        var lote = new LoteJournal
        {
            Id = ahora.ToString("yyyyMMdd-HHmmss"),
            Fecha = ahora.ToString("yyyy-MM-dd"),
            Hora = ahora.ToString("HH:mm"),
            Serie = _catalogoCargado?.Serie ?? "",
            Carpeta = txtCarpeta.Text?.Trim() ?? "",
        };

        // Se resuelven los destinos ANTES de mover nada, para detectar colisiones sin
        // haber tocado el disco.
        var ocupados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var planeados = new List<(OrganizarRow fila, string destino)>();
        foreach (var f in listos)
        {
            var carpeta = Path.GetDirectoryName(f.Res.Archivo.Path)!;
            var destino = Path.Combine(carpeta, f.NombreNuevo!);
            if (string.Equals(destino, f.Res.Archivo.Path, StringComparison.OrdinalIgnoreCase)) continue;
            if (ocupados.Contains(destino) || File.Exists(destino))
            {
                Escribir($"Se omite «{f.Original}»: «{f.NombreNuevo}» ya existe.");
                continue;
            }
            ocupados.Add(destino);
            planeados.Add((f, destino));
            lote.Movimientos.Add(new MovimientoJournal { De = f.Res.Archivo.Path, A = destino });
        }

        if (planeados.Count == 0) { Aviso("No quedó nada que renombrar."); return; }

        // El diario va a disco ANTES del primer renombrado: si esto se corta a la mitad,
        // el «deshacer» sigue existiendo.
        try { ReindexStore.EscribirJournal(lote); }
        catch (Exception ex) { Aviso($"No se pudo guardar el registro del lote, no se renombra nada: {ex.Message}"); return; }

        int hechos = 0, fallos = 0;
        foreach (var (fila, destino) in planeados)
        {
            try
            {
                File.Move(fila.Res.Archivo.Path, destino);
                fila.Aplicado = true;
                hechos++;
            }
            catch (Exception ex)
            {
                fallos++;
                Escribir($"No se pudo renombrar «{fila.Original}»: {ex.Message}");
            }
        }

        _ultimoLote = lote;
        RefrescarUltimoLote();
        ActualizarContadores();

        lblBannerAplicado.Text = fallos == 0
            ? $"{hechos} ficheros renombrados."
            : $"{hechos} ficheros renombrados · {fallos} no se pudieron.";
        bannerAplicado.Visibility = Visibility.Visible;
        Escribir(lblBannerAplicado.Text);
    }

    private void RefrescarUltimoLote()
    {
        _ultimoLote ??= ReindexStore.UltimoLote();
        btnDeshacer.IsEnabled = _ultimoLote is { Movimientos.Count: > 0 };
        lblDeshacer.Text = _ultimoLote is { Movimientos.Count: > 0 }
            ? _ultimoLote.Etiqueta
            : "Deshacer último lote";
    }

    private void DeshacerUltimoLote()
    {
        if (_ultimoLote == null) return;

        var (devueltos, fallidos) = ReindexStore.Deshacer(_ultimoLote);
        Escribir(fallidos == 0
            ? $"Lote deshecho: {devueltos} ficheros devueltos a su nombre anterior."
            : $"Lote deshecho: {devueltos} devueltos · {fallidos} no se pudieron.");

        ReindexStore.OlvidarLote(_ultimoLote);
        _ultimoLote = null;
        bannerAplicado.Visibility = Visibility.Collapsed;
        RefrescarUltimoLote();

        // Los nombres del disco han cambiado: lo que hubiera en la tabla ya no vale
        RevisarCarpeta();
    }

    private void AbrirMemoria()
    {
        if (_decisiones.Count == 0) { Aviso("Todavía no has tomado ninguna decisión que recordar."); return; }

        var r = DialogWindow.Confirmar(Window.GetWindow(this), "Memoria de decisiones", $"Tienes {_decisiones.Count} decisiones recordadas.\n\n¿Quieres olvidarlas todas?");
        if (!r) return;

        _decisiones.Clear();
        ReindexStore.OlvidarDecisiones();
        Escribir("Memoria de decisiones vaciada.");
    }

    // ─────────────────────────── varios ───────────────────────────

    private void ActualizarEstado()
    {
        bool puede = _catalogoCargado != null && _ficheros.Length > 0;
        btnSimular.IsEnabled = btnSimularGrande.IsEnabled = puede;

        if (_filas.Count > 0) { ActualizarContadores(); return; }

        lblEstadoOrg.Text = (_catalogoCargado, _ficheros.Length) switch
        {
            (null, _) => "Importa un catálogo para empezar",
            (_, 0) => "Elige una carpeta con vídeos",
            var (c, n) => $"Catálogo {c!.Serie} · {n} ficheros listos para simular",
        };
    }

    private void Escribir(string linea) => Log?.Invoke(linea);

    private void Aviso(string mensaje)
    {
        Escribir(mensaje);
        DialogWindow.Aviso(Window.GetWindow(this), "Organizar", mensaje);
    }
}
