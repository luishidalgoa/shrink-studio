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

    /// <summary>
    /// «Llévate este fichero a Recortes». Lo pide la fila y lo resuelve la ventana: esta
    /// página no sabe que existe una pestaña, y así sigue sin saberlo.
    /// </summary>
    public event Action<string>? AbrirEnRecortes;

    private readonly PasosVisual _pasos;

    public OrganizarView()
    {
        InitializeComponent();

        tabla.ItemsSource = _filas;
        listaCatalogos.ItemsSource = new ObservableCollection<CatalogoCard>();

        txtPlantilla.Text = LibraryTemplate.PatronPorDefecto;

        btnCarpeta.Click += (_, _) => ElegirCarpeta();
        btnImportar.Click += (_, _) => ImportarCatalogo();
        btnCatalogos.Click += (_, _) => ImportarCatalogo();
        btnExplorar.Click += (_, _) => AbrirExplorador();
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

        txtBuscarTabla.TextChanged += (_, _) => AplicarFiltro();
        txtBuscarTabla.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { txtBuscarTabla.Text = ""; tabla.Focus(); e.Handled = true; }
        };
        // Ctrl+K desde cualquier punto de la página: el estándar de «buscar aquí dentro»
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control &&
                vistaRevision.Visibility == Visibility.Visible)
            {
                txtBuscarTabla.Focus();
                txtBuscarTabla.SelectAll();
                e.Handled = true;
            }
        };

        // Menú contextual: se resuelve la fila bajo el puntero y se selecciona, para que
        // no quede duda de sobre cuál se va a actuar. Fuera de una fila no se abre: un menú
        // que aparece sobre el vacío y actúa sobre «lo último seleccionado» es una trampa.
        tabla.ContextMenuOpening += (_, e) =>
        {
            // Con la tecla Menú no hay puntero al que preguntar: WPF lo indica poniendo la
            // posición en negativo, y entonces manda la fila seleccionada. Sin esto el menú
            // quedaría muerto para quien no use el ratón.
            bool conTeclado = e.CursorLeft < 0 && e.CursorTop < 0;
            var r = conTeclado
                ? tabla.SelectedItem as OrganizarRow
                : Ascender<DataGridRow>(Mouse.DirectlyOver as DependencyObject)?.Item as OrganizarRow;
            if (r == null) { e.Handled = true; return; }
            tabla.SelectedItem = r;
            miReproducir.IsEnabled = File.Exists(r.RutaActual);
            miRecortar.IsEnabled = miReproducir.IsEnabled;
            miUbicacion.IsEnabled = miReproducir.IsEnabled;
        };
        miReproducir.Click += (_, _) => ReproducirFila(tabla.SelectedItem as OrganizarRow);
        miRecortar.Click += (_, _) =>
        {
            if (tabla.SelectedItem is OrganizarRow f && File.Exists(f.RutaActual))
                AbrirEnRecortes?.Invoke(f.RutaActual);
        };
        miUbicacion.Click += (_, _) => AbrirUbicacion(tabla.SelectedItem as OrganizarRow);

        tabla.PreviewKeyDown += OnTablaKeyDown;
        tabla.PreviewMouseLeftButtonDown += OnTablaClic;
        tabla.PreviewMouseMove += OnTablaArrastre;
        tabla.PreviewMouseLeftButtonUp += (_, _) => _pintando = null;
        tabla.MouseLeave += (_, _) => _pintando = null;

        // Con la ventana estrecha, el rótulo de la sección se recortaba a un par de puntos
        // suspensivos, que queda peor que no estar: los botones de al lado ya dicen de qué
        // va la columna. Por debajo de ese ancho se retira entero.
        //
        // El umbral sale de la cuenta real, no a ojo: el panel de catálogos ocupa la mitad
        // de la página, los tres botones piden unos 330 px y el rótulo necesita ~140 para
        // leerse entero. Media página ≥ 470 ⇒ página ≥ 990.
        SizeChanged += (_, _) =>
            lblTituloCatalogos.Visibility = ActualWidth >= 990 ? Visibility.Visible : Visibility.Collapsed;

        // Las tres etapas de la identificación, en el panel de ficheros. Son las fases
        // REALES del trabajo, no decorado: cada una se enciende cuando su fase corre.
        _pasos = new PasosVisual(
            "Leyendo los nombres de los ficheros",
            "Identificándolos contra el catálogo",
            "Preparando la revisión");
        panelEtapas.Children.Add(_pasos.Raiz);

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

    /// <summary>Abre el explorador del catálogo elegido, para verificar propuestas.</summary>
    private void AbrirExplorador()
    {
        if (_catalogoCargado == null)
        {
            Aviso("Primero elige o importa un catálogo.");
            return;
        }
        new CatalogoWindow(_catalogoCargado) { Owner = Window.GetWindow(this) }.Show();
    }

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
            // Importar TAMBIÉN es elegir: sin esto, quitar un catálogo y reimportarlo dejaba
            // la preferencia vacía y el siguiente arranque caía al primero por alfabeto.
            ReindexStore.GuardarUltimoCatalogo(guardado.Ruta);
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
        // Se RE-ESCANEA siempre: tras aplicar, la lista vieja apunta a nombres que ya no
        // existen, y simular sobre ella re-resolvía el pasado — la tabla enseñaba los
        // mismos «Corregido» de antes como si aplicar no hubiera hecho nada.
        var carpetaActual = txtCarpeta.Text?.Trim() ?? "";
        try { _ficheros = LibraryScan.Escanear(carpetaActual, Engine.VideoExtensions); }
        catch { /* la carpeta puede haber desaparecido; el guard de abajo lo dice */ }

        if (_catalogoCargado == null || _ficheros.Length == 0) return;

        // Las etapas viven en la pantalla de inicio: al re-simular desde la revisión (botón
        // de abajo) no hay dónde pintarlas y se va directo al resultado.
        bool animar = vistaInicio.Visibility == Visibility.Visible;

        btnSimular.IsEnabled = btnSimularGrande.IsEnabled = btnCarpeta.IsEnabled = false;
        lblEstadoOrg.Text = "Identificando…";

        if (animar)
        {
            panelReposo.Visibility = Visibility.Collapsed;
            panelEtapas.Visibility = Visibility.Visible;
            _pasos.Reiniciar();
            EncenderHaz();
        }

        var catalogo = _catalogoCargado;
        var ficheros = _ficheros;
        var decisiones = _decisiones;

        try
        {
            // ── Etapa 1: leer las señales de los nombres ──
            if (animar) _pasos.EnCurso(0);
            var señales = await ConTiempoDeVerse(Task.Run(() =>
                // Nota: el título del metadato del contenedor (titulo_meta) todavía no se lee.
                // Exigiría un ffprobe por fichero y «Simular» dejaría de ser inmediato en
                // bibliotecas de cientos. El motor ya lo admite cuando se enganche.
                ficheros
                    .Select(f => SignalExtractor.Extract(f, new DirectoryInfo(Path.GetDirectoryName(f)!).Name))
                    .ToList()), animar);
            if (animar) _pasos.Hecha(0, señales.Count == 1 ? "1 nombre leído" : $"{señales.Count} nombres leídos");

            // ── Etapa 2: el motor, fuera del hilo de interfaz ──
            if (animar) _pasos.EnCurso(1);
            var resoluciones = await ConTiempoDeVerse(
                Task.Run(() => ReindexEngine.Resolve(señales, catalogo, decisiones)), animar);

            // ── Etapa 2b: metadatos SOLO de los dudosos ──
            // El contenedor suele llevar el título grabado («title» del MKV) aunque el
            // nombre no lo traiga — el caso de los S2018E01 pelados. Se lee únicamente de
            // los que quedaron en duda, y de esos solo los que están en el disco: abrir un
            // fichero sincronizado «bajo demanda» lo descarga entero. Tope de 80.
            _dudososEnNube = 0;   // cada simulación parte de cero
            var dudosos = resoluciones
                .Where(x => x.EsDuda && string.IsNullOrEmpty(x.Archivo.TituloMeta) &&
                            string.IsNullOrEmpty(x.Archivo.Error))
                .Select(x => x.Archivo.Path)
                .Take(80)
                .ToList();
            if (dudosos.Count > 0)
            {
                Escribir($"Buscando el título de {dudosos.Count} dudosos…");
                var metadatos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int enNube = 0;
                await Task.Run(async () =>
                {
                    // De ocho en ocho: cada sondeo levanta un proceso de ffprobe, y ochenta
                    // a la vez castigan el disco sin acabar antes.
                    using var turno = new SemaphoreSlim(8);
                    var tareas = dudosos.Select(async ruta =>
                    {
                        await turno.WaitAsync();
                        try
                        {
                            // El .nfo compañero primero: es un XML minúsculo y trae el título
                            // en limpio.
                            var nfo = Path.ChangeExtension(ruta, ".nfo");
                            string? t = null;
                            if (File.Exists(nfo))
                                try { t = NfoTitulo.Extraer(File.ReadAllText(nfo)); } catch { }

                            // Y si no lo hay, el contenedor... salvo que el vídeo sea un
                            // marcador de «Archivos a petición»: abrirlo para mirar una
                            // etiqueta se lo descargaría ENTERO (medido: 277 MB en 18 s).
                            // Identificar una carpeta no puede gastarle a nadie gigabytes
                            // sin avisar, así que ahí se para.
                            if (t == null)
                            {
                                if (Nube.EsMarcador(ruta)) Interlocked.Increment(ref enNube);
                                else t = await Engine.LeerTituloAsync(ruta);
                            }
                            if (t != null) lock (metadatos) metadatos[ruta] = t;
                        }
                        finally { turno.Release(); }
                    });
                    await Task.WhenAll(tareas);
                });

                _dudososEnNube = enNube;
                if (enNube > 0)
                    Escribir($"{enNube} están solo en la nube: no se abren para no descargarlos enteros.");

                if (metadatos.Count > 0)
                {
                    // Con los títulos en la mano, se re-resuelve TODO el lote: las reglas de
                    // deduplicación miran al conjunto y parchear filas sueltas las esquivaría.
                    for (int i = 0; i < señales.Count; i++)
                        if (metadatos.TryGetValue(señales[i].Path, out var titulo))
                            señales[i] = SignalExtractor.Extract(señales[i].Path, señales[i].Carpeta, titulo);
                    resoluciones = await Task.Run(() => ReindexEngine.Resolve(señales, catalogo, decisiones));
                    Escribir($"{metadatos.Count} títulos encontrados en los metadatos.");
                }
            }

            if (animar) _pasos.Hecha(1, $"contra «{catalogo.Serie}»");

            // ── Etapa 3: montar la tabla ──
            // El respiro de antes deja al arco pintarse: montar filas bloquea el hilo de
            // interfaz y sin él esta etapa pasaría de pendiente a hecha sin verse en curso.
            if (animar) { _pasos.EnCurso(2); await Task.Delay(220); }

            var raiz = txtCarpeta.Text?.Trim() ?? "";
            _filas.Clear();
            foreach (var r in resoluciones)
                _filas.Add(new OrganizarRow(r, catalogo, _plantilla,
                    LibraryScan.Etiqueta(LibraryScan.Grupo(raiz, r.Archivo.Path))));

            int temporadas = RecalcularSeparadores();
            int listos = _filas.Count(f => f.ListoParaAplicar);
            if (animar)
            {
                _pasos.Hecha(2);
                // La fusión final: los tres pasos se funden en un solo check con destello.
                // Merece verse entera antes de saltar a la tabla — es la recompensa.
                _pasos.Terminado("Identificación lista",
                    listos == 1 ? "1 listo para aplicar" : $"{listos} listos para aplicar");
                await Task.Delay(1100);
            }

            MostrarRevision();
            ActualizarContadores();
            Escribir($"Simulación: {_filas.Count} ficheros contra «{catalogo.Serie}»" +
                     (temporadas > 0 ? $", repartidos en {temporadas} temporadas." : "."));
        }
        catch (Exception ex) { Aviso($"La simulación falló: {ex.Message}"); }
        finally
        {
            if (animar)
            {
                ApagarHaz();
                panelEtapas.Visibility = Visibility.Collapsed;
                panelReposo.Visibility = Visibility.Visible;
            }
            btnCarpeta.IsEnabled = true;
            ActualizarEstado();
        }
    }

    /// <summary>
    /// Espera la tarea, y con animación le garantiza un mínimo en pantalla: una etapa que
    /// entra y sale en 40 ms no informa, parpadea.
    /// </summary>
    private static async Task<T> ConTiempoDeVerse<T>(Task<T> tarea, bool animar)
    {
        if (animar) await Task.WhenAll(tarea, Task.Delay(300));
        return await tarea;
    }

    // ── el haz que rodea el panel mientras se identifica ──

    private void EncenderHaz()
    {
        hazFicheros.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        if (hazFicheros.BorderBrush is System.Windows.Media.LinearGradientBrush b &&
            b.RelativeTransform is System.Windows.Media.RotateTransform rt)
            rt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.8))
                { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
    }

    private void ApagarHaz()
    {
        hazFicheros.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        if (hazFicheros.BorderBrush is System.Windows.Media.LinearGradientBrush b &&
            b.RelativeTransform is System.Windows.Media.RotateTransform rt)
            rt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
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
        int marcados = _filas.Count(f => f.ListoParaAplicar && f.Marcado);
        int dudas = _filas.Count(f => f.EsDuda);

        // El botón dice EXACTAMENTE cuántos va a tocar. Si hay listos sin marcar, se nota
        // en el propio texto («12 de 400»): aplicar nunca lleva sorpresa dentro.
        lblAplicar.Text = marcados == 0 ? "Aplicar"
            : marcados == listos ? $"Aplicar {marcados} marcados"
            : $"Aplicar {marcados} de {listos}";
        btnAplicar.IsEnabled = marcados > 0;
        btnAplicar.ToolTip = "Renombra SOLO los ficheros en verde que estén marcados. " +
                             "Los conflictos y las dudas nunca se tocan, estén como estén.";
        btnAceptarVerdes.IsEnabled = listos > 0;

        // Los que ya estaban bien se dicen aparte: si no, «383 listos · 165 por despachar» sobre
        // 548 deja 0 sin explicar y parece que se han perdido por el camino.
        int hechos = _filas.Count(f => f.SinCambios);
        lblEstadoOrg.Text = $"{_filas.Count} ficheros · {listos} listos para aplicar · {dudas} por despachar"
                            + (hechos > 0 ? $" · {hechos} ya estaban bien" : "")
                            + (_dudososEnNube > 0 ? $" · {_dudososEnNube} solo en la nube (no se abren)" : "");

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
        // Doble clic = ver el video en el reproductor del sistema: ante la duda de que
        // capitulo es, mirarlo gana a cualquier metadato. Va antes que el cierre de fila
        // para que el segundo clic no la recoja.
        if (e.ClickCount == 2 &&
            Ascender<CheckBox>(e.OriginalSource as DependencyObject) == null &&
            Ascender<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is OrganizarRow filaVideo)
        {
            // Reproductor INTEGRADO en modo focus, no una ventana del sistema: la pregunta
            // es «¿qué capítulo es?» y la respuesta debe estar a un Esc de distancia. Si el
            // códec no está soportado, la propia ventana ofrece el reproductor del sistema.
            ReproducirFila(filaVideo);
            e.Handled = true;
            return;
        }

        // Un clic que nace en una casilla es PARA la casilla. Este manejador oye el clic
        // antes que ella (los Preview van de fuera adentro), y sin esta salida se lo comía
        // cuando la fila estaba seleccionada: la casilla parecía muerta con el ratón — y la
        // verificación por accesibilidad no lo cazó porque conmuta sin pasar por el ratón.
        // De paso, aquí arranca el marcado por arrastre: se anota el valor del primer
        // toque y el movimiento lo va contagiando a las filas que cruces.
        if (Ascender<CheckBox>(e.OriginalSource as DependencyObject) is
            { DataContext: OrganizarRow filaMarca } && filaMarca.ListoParaAplicar)
        {
            _pintando = !filaMarca.Marcado;
            filaMarca.Marcado = _pintando.Value;
            ActualizarContadores();
            e.Handled = true;   // que el CheckBox no vuelva a conmutar lo ya conmutado
            return;
        }

        var celda = Ascender<DataGridCell>(e.OriginalSource as DependencyObject);
        if (celda == null) return;

        var fila = Ascender<DataGridRow>(celda);
        if (fila is not { IsSelected: true }) return;

        tabla.SelectedItem = null;
        e.Handled = true;
    }

    /// <summary>Valor que se está «pintando» al arrastrar sobre las casillas. Null = no hay arrastre.</summary>
    private bool? _pintando;

    /// <summary>
    /// Marca en tanda: con el botón pulsado, cada fila que cruces recibe el valor del primer
    /// toque (no se alterna fila a fila, que dejaría un patrón de ajedrez si pasas dos veces).
    /// </summary>
    private void OnTablaArrastre(object sender, MouseEventArgs e)
    {
        if (_pintando is not bool valor) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _pintando = null; return; }

        var fila = Ascender<DataGridRow>(e.OriginalSource as DependencyObject);
        if (fila?.Item is OrganizarRow f && f.ListoParaAplicar && f.Marcado != valor)
        {
            f.Marcado = valor;
            ActualizarContadores();
        }
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

        // El texto filtra con la normalización del identificador: «sonrisa» encuentra
        // «¡En busca de una sonrisa!» aunque el nombre lleve signos y tildes.
        var q = TitleMatch.Norm(txtBuscarTabla.Text);
        bool PasaTexto(OrganizarRow f)
        {
            if (q.Length == 0) return true;
            // Una fila YA APLICADA solo se encuentra por su nombre nuevo: el viejo ya no
            // existe en disco, y que siguiera apareciendo al buscarlo hacía dudar de si el
            // renombrado había ocurrido de verdad.
            if (f.Aplicado)
                return TitleMatch.Norm(f.NombreNuevo ?? f.Original).Contains(q, StringComparison.Ordinal);
            return TitleMatch.Norm(f.Original).Contains(q, StringComparison.Ordinal)
                || TitleMatch.Norm(f.Propuesta).Contains(q, StringComparison.Ordinal);
        }

        if (estados.Count == 0 && !soloDudas && q.Length == 0)
        { vista.Filter = null; RecalcularSeparadores(); return; }

        vista.Filter = o =>
        {
            if (o is not OrganizarRow f) return false;
            if (soloDudas && !f.EsDuda) return false;
            if (!PasaTexto(f)) return false;
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

    /// <summary>
    /// Abre el explorador en modo elegir, arrancando con el título del fichero ya buscado:
    /// lo normal es que el episodio correcto esté a un golpe de vista.
    /// </summary>
    private void OnElegirAMano(object sender, RoutedEventArgs e)
    {
        if (tabla.SelectedItem is not OrganizarRow fila || _catalogoCargado == null) return;

        var win = new CatalogoWindow(_catalogoCargado, fila.Res.Archivo.TituloNombre, modoElegir: true)
        { Owner = Window.GetWindow(this) };

        if (win.ShowDialog() != true || win.Elegido is not { } ep) return;

        fila.ElegirEpisodio(ep, win.SegElegido);
        RecordarDecision(fila, ep, win.SegElegido);
        ActualizarContadores();
        Escribir(win.SegElegido == null
            ? $"«{fila.Original}» → episodio {ep.Num} (elegido en el explorador)."
            : $"«{fila.Original}» → historia «{win.SegElegido}» del episodio {ep.Num}.");
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

    private void RecordarDecision(OrganizarRow fila, CatalogEpisode ep, string? seg = null)
    {
        _decisiones[fila.Res.Archivo.Fingerprint] = new ReindexOverride
        {
            Num = ep.Num,
            Seg = seg,
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

    // OJO en ambos: la casilla de la cabecera nace con IsChecked="True", así que su Checked
    // dispara DURANTE InitializeComponent, cuando el resto de controles aún no existe.
    // Sin la guarda, la página revienta al construirse — se aprendió a las malas.
    private void OnMarcarFila(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        ActualizarContadores();
    }

    /// <summary>La casilla de la cabecera marca o desmarca todos los listos de golpe.</summary>
    private void OnMarcarTodos(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        if (sender is not CheckBox chk) return;
        bool valor = chk.IsChecked == true;
        foreach (var f in _filas.Where(f => f.ListoParaAplicar)) f.Marcado = valor;
        ActualizarContadores();
    }

    private void PedirConfirmacion()
    {
        var listos = _filas.Where(f => f.ListoParaAplicar && f.Marcado).ToList();
        if (listos.Count == 0) return;

        int dudas = _filas.Count(f => f.EsDuda);
        int desmarcados = _filas.Count(f => f.ListoParaAplicar && !f.Marcado);

        lblConfSeRenombra.Text = listos.Count == 1
            ? "Se renombra 1 fichero identificado con confianza."
            : $"Se renombran {listos.Count} ficheros identificados con confianza.";

        if (dudas > 0 || desmarcados > 0)
        {
            // Contar tambien lo desmarcado a proposito: el miedo de «aplicar» es no saber
            // qué toca, y este cuadro existe para que no quede nada sin contar.
            var trozos = new List<string>();
            if (dudas > 0) trozos.Add(dudas == 1 ? "1 fichero con dudas" : $"{dudas} ficheros con dudas");
            if (desmarcados > 0) trozos.Add(desmarcados == 1 ? "1 que has desmarcado" : $"{desmarcados} que has desmarcado");
            lblConfNoSeToca.Text = $"Se quedan exactamente como están: {string.Join(" y ", trozos)}.";
            filaNoSeToca.Visibility = Visibility.Visible;
        }
        else filaNoSeToca.Visibility = Visibility.Collapsed;

        overlayConfirmar.Visibility = Visibility.Visible;
    }

    private async void Aplicar()
    {
        var listos = _filas.Where(f => f.ListoParaAplicar && f.Marcado).ToList();
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
        var companeros = new List<(string de, string a)>();
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

            // Sus compañeros (.nfo, .srt…) viajan con él: un .nfo con el nombre viejo queda
            // huérfano y el reproductor de biblioteca deja de asociarlo. Van al MISMO diario,
            // así que «Deshacer» también los devuelve.
            try
            {
                var vecinos = Directory.EnumerateFiles(carpeta);
                foreach (var (de, a) in SidecarPlanner.Planear(f.Res.Archivo.Path, destino, vecinos))
                {
                    if (ocupados.Contains(a) || File.Exists(a)) continue;
                    ocupados.Add(a);
                    companeros.Add((de, a));
                    lote.Movimientos.Add(new MovimientoJournal { De = de, A = a });
                }
            }
            catch { /* una carpeta ilegible no impide renombrar el vídeo */ }
        }

        if (planeados.Count == 0) { Aviso("No quedó nada que renombrar."); return; }

        // El diario va a disco ANTES del primer renombrado: si esto se corta a la mitad,
        // el «deshacer» sigue existiendo.
        try { ReindexStore.EscribirJournal(lote); }
        catch (Exception ex) { Aviso($"No se pudo guardar el registro del lote, no se renombra nada: {ex.Message}"); return; }

        // Los movimientos van FUERA del hilo de interfaz: 462 renombrados en OneDrive
        // tardan, y con la ventana congelada parecía que aplicar no hacía nada.
        btnAplicar.IsEnabled = btnSimular.IsEnabled = false;
        lblEstadoOrg.Text = $"Renombrando {planeados.Count} ficheros…";

        var companerosMovidos = 0;
        var resultados = await Task.Run(() =>
        {
            var lista = new List<(OrganizarRow fila, string? error)>();
            foreach (var (fila, destino) in planeados)
            {
                try { File.Move(fila.Res.Archivo.Path, destino); lista.Add((fila, null)); }
                catch (Exception ex) { lista.Add((fila, ex.Message)); }
            }
            foreach (var (de, a) in companeros)
                try { File.Move(de, a); companerosMovidos++; } catch { /* se cuenta abajo */ }
            return lista;
        });

        int hechos = 0, fallos = 0;
        foreach (var (fila, error) in resultados)
        {
            if (error == null) { fila.Aplicado = true; hechos++; }
            else { fallos++; Escribir($"No se pudo renombrar «{fila.Original}»: {error}"); }
        }
        btnAplicar.IsEnabled = btnSimular.IsEnabled = true;

        _ultimoLote = lote;
        RefrescarUltimoLote();
        ActualizarContadores();

        var extra = companerosMovidos > 0
            ? $" (+{companerosMovidos} compañeros .nfo/.srt)" : "";
        lblBannerAplicado.Text = fallos == 0
            ? $"{hechos} ficheros renombrados{extra}."
            : $"{hechos} ficheros renombrados{extra} · {fallos} no se pudieron.";
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

        var lote = _ultimoLote;
        ReindexStore.OlvidarLote(_ultimoLote);
        _ultimoLote = null;
        bannerAplicado.Visibility = Visibility.Collapsed;
        RefrescarUltimoLote();

        // Deshacer NO te saca del contexto. Si la tabla está a la vista, las filas del lote
        // vuelven de «Hecho» a su estado anterior EN EL SITIO — con su casilla y su
        // propuesta intactas, listas para re-aplicar si era eso lo que se quería.
        if (vistaRevision.Visibility == Visibility.Visible && _filas.Count > 0)
        {
            var deshechos = new HashSet<string>(
                lote.Movimientos.Select(m => m.De), StringComparer.OrdinalIgnoreCase);
            foreach (var f in _filas)
                if (f.Aplicado && deshechos.Contains(f.Res.Archivo.Path))
                    f.Aplicado = false;
            ActualizarContadores();

            // La lista de disco vuelve a los nombres de antes; se refresca sin tocar la vista
            try { _ficheros = LibraryScan.Escanear(txtCarpeta.Text?.Trim() ?? "", Engine.VideoExtensions); }
            catch { /* si la carpeta no se puede releer, la próxima simulación lo dirá */ }
        }
        else RevisarCarpeta();   // desde la pantalla de inicio, solo refrescar el recuento
    }

    /// <summary>
    /// Reproductor INTEGRADO en modo focus, no una ventana del sistema: la pregunta es
    /// «¿qué capítulo es?» y la respuesta debe estar a un Esc de distancia.
    /// </summary>
    private void ReproducirFila(OrganizarRow? fila)
    {
        if (fila == null || !File.Exists(fila.RutaActual)) return;
        new ReproductorWindow(fila.RutaActual) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    /// <summary>
    /// Abre el explorador con el fichero ya seleccionado. No lo abre: seleccionarlo no
    /// descarga nada, así que sirve igual para los que están solo en la nube.
    /// </summary>
    private void AbrirUbicacion(OrganizarRow? fila)
    {
        if (fila == null) return;
        var ruta = fila.RutaActual;
        try
        {
            if (File.Exists(ruta))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", $"/select,\"{ruta}\"") { UseShellExecute = true });
            else
            {
                // Si el fichero ya no está (renombrado fuera, movido), al menos la carpeta
                var carpeta = Path.GetDirectoryName(ruta);
                if (Directory.Exists(carpeta))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(carpeta)
                        { UseShellExecute = true });
            }
        }
        catch (Exception ex) { Escribir($"No se pudo abrir la ubicación: {ex.Message}"); }
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

    /// <summary>
    /// Dudosos que no se llegaron a sondear porque el vídeo está solo en la nube. Sale en
    /// el resumen, no en el registro: el panel del registro va plegado, así que contarlo
    /// solo ahí es no contarlo.
    /// </summary>
    private int _dudososEnNube;

    private void Aviso(string mensaje)
    {
        Escribir(mensaje);
        DialogWindow.Aviso(Window.GetWindow(this), "Organizar", mensaje);
    }
}
