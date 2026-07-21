using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ShrinkVideo;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<VideoRow> _rows = new();

    private readonly Engine _engine = new();
    private readonly string _thumbDir = Path.Combine(Path.GetTempPath(), "shrinkvideo_thumbs");
    private readonly string _previewDir = Path.Combine(Path.GetTempPath(), "shrinkvideo_preview");
    private readonly System.Windows.Threading.DispatcherTimer _scrubTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private CancellationTokenSource? _scrubCts;
    private CancellationTokenSource? _previewCts;
    private object? _previewBtnContent;
    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _paused;
    private bool _applyingPreset;
    private Settings _settings = new();

    // banda de selección (rubber-band)
    private Point _marqueeStart;
    private bool _marqueeActive;
    private bool _marqueeDragging;
    private readonly HashSet<object> _marqueeBase = new();

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(_thumbDir);
        lst.ItemsSource = _rows;
        lblVersion.Text = "v" + Updater.Current;
        _previewBtnContent = btnPreview.Content;   // para restaurar tras "Cancelar"

        _settings = SettingsStore.Load();
        ApplySettings();

        // el estado vacío solo se ve cuando no hay nada en la lista
        _rows.CollectionChanged += (_, _) =>
            emptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // barra de título propia
        btnMin.Click += (_, _) => WindowState = WindowState.Minimized;
        btnMax.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        btnClose.Click += (_, _) => Close();
        StateChanged += (_, _) => rootBorder.Padding = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);

        btnSrc.Click += (_, _) => PickFolder(txtSrc);
        btnOut.Click += (_, _) => PickFolder(txtOut);
        btnSrcFile.Click += async (_, _) => await AddFilesDialogAsync();
        btnOpen.Click += (_, _) => OpenDestination();
        btnScan.Click += async (_, _) => await ScanAsync();
        btnRun.Click += async (_, _) => await RunAsync();
        btnCancel.Click += (_, _) => _cts?.Cancel();
        btnPause.Click += (_, _) => TogglePause();
        btnMarkAll.Click += (_, _) => lst.SelectAll();
        btnMarkNone.Click += (_, _) => lst.UnselectAll();
        btnDelSel.Click += (_, _) => DeleteSelected();
        btnDelDir.Click += (_, _) => DeleteFolders();
        btnCheckUpdate.Click += async (_, _) => await CheckUpdateAsync(manual: true);
        btnUpdateLater.Click += (_, _) => updateBar.Visibility = Visibility.Collapsed;

        // barra de menú
        miPickSrc.Click += (_, _) => PickFolder(txtSrc);
        miAddFiles.Click += async (_, _) => await AddFilesDialogAsync();
        miOpenDest.Click += (_, _) => OpenDestination();
        miExit.Click += (_, _) => Close();
        miSelAll.Click += (_, _) => lst.SelectAll();
        miSelNone.Click += (_, _) => lst.UnselectAll();
        miSelInvert.Click += (_, _) => InvertSelection();
        miDelSel.Click += (_, _) => DeleteSelected();
        miRename.Click += (_, _) => OpenRenameDialog();
        btnRename.Click += (_, _) => OpenRenameDialog();
        miPrefs.Click += (_, _) => OpenPreferences();
        miCheckUpd.Click += async (_, _) => await CheckUpdateAsync(manual: true);
        miAbout.Click += (_, _) => ShowAbout();

        // «Subcarpetas» es el mismo ajuste que el de Preferencias: se mantienen en sync
        chkRec.Checked += (_, _) => PersistRecurse();
        chkRec.Unchecked += (_, _) => PersistRecurse();

        // arrastrar vídeos (o carpetas) desde el Explorador y soltarlos en la ventana
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDropFiles;

        // quitar de la lista con Supr + menú contextual de la tabla
        lst.PreviewKeyDown += Lst_KeyDown;
        lst.PreviewMouseRightButtonDown += Lst_RightButtonDown;
        ctxTable.Opened += (_, _) => UpdateContextMenu();
        miCtxRemove.Click += (_, _) => RemoveSelectedRows();
        miCtxRecycle.Click += (_, _) => DeleteSelected();
        miCtxOpenFolder.Click += (_, _) => OpenContainingFolder();
        miCtxCopyPath.Click += (_, _) => CopySelectedPaths();
        miCtxSelectAll.Click += (_, _) => lst.SelectAll();
        miCtxInvert.Click += (_, _) => InvertSelection();

        // banda de selección estilo explorador
        lst.PreviewMouseLeftButtonDown += Lst_MouseDown;
        lst.PreviewMouseMove += Lst_MouseMove;
        lst.PreviewMouseLeftButtonUp += Lst_MouseUp;

        // El panel lateral se pliega solo cuando la ventana se queda estrecha
        SizeChanged += (_, _) => AjustarAAncho();

        // conmutador de páginas «Comprimir | Organizar»
        tabComprimir.Checked += (_, _) => CambiarPagina(organizar: false);
        tabOrganizar.Checked += (_, _) => CambiarPagina(organizar: true);
        pageOrganizar.Log += AppendLog;
        pillFondo.MouseLeftButtonUp += (_, _) => tabComprimir.IsChecked = true;

        tabDetalle.MouseLeftButtonUp += (_, _) => ShowSideTab("detalle");
        tabEstim.MouseLeftButtonUp += (_, _) => ShowSideTab("estim");

        foreach (var c in new[] { cboFmt, cboCodec, cboQ, cboRes, cboAud })
            c.SelectionChanged += (_, _) => UpdateEstimate();

        btnPreview.Click += async (_, _) => await OnPreviewAsync();
        btnMeasure.Click += async (_, _) => await OnMeasureAsync();
        _scrubTimer.Tick += async (_, _) => { _scrubTimer.Stop(); await ShowScrubFrameAsync(); };
        sldPreview.ValueChanged += (_, _) =>
        {
            lblPreviewAt.Text = $"Previsualizar desde {FmtTime((int)sldPreview.Value)}";
            _scrubTimer.Stop(); _scrubTimer.Start();   // debounce: muestra el fotograma al soltar/pausar
        };

        cboFmt.SelectionChanged += (_, _) =>
        {
            bool audioOnly = cboFmt.SelectedIndex >= 3;   // MP3/M4A/FLAC/Opus
            cboCodec.IsEnabled = cboQ.IsEnabled = cboRes.IsEnabled = !audioOnly;
        };
        btnSavePreset.Click += (_, _) => SavePreset();
        cboPreset.SelectionChanged += (_, _) => ApplyPreset();
        ReloadPresets(_settings.DefaultPreset);   // aplica el preset por defecto si lo hay
        if (string.IsNullOrEmpty(_settings.DefaultPreset)) cboLang.Text = _settings.DefaultLang;

        Loaded += async (_, _) =>
        {
            if (cboPreset.SelectedItem == null) cboLang.Text = _settings.DefaultLang;
            if (!await Engine.ToolsAvailableAsync())
                DialogWindow.Aviso(this, "Falta FFmpeg", "No se encuentra FFmpeg. Instálalo (por ejemplo con:  winget install Gyan.FFmpeg) y vuelve a abrir la app.");
            if (_settings.CheckUpdatesOnStart) await CheckUpdateAsync(manual: false);
        };
    }

    // ---------- preferencias / ajustes ----------
    private void ApplySettings()
    {
        Engine.MinFreeBytes = _settings.MinFreeMb * 1024L * 1024;
        Engine.AllowHardware = _settings.UseHardware;
        Estimator.ComplexityFactor = Math.Clamp(_settings.ComplexityFactor, 0.15, 4.0);
        chkRec.IsChecked = _settings.Recurse;
        UpdateRenameStatus();
    }

    /// <summary>La casilla «Subcarpetas» y la preferencia son lo mismo: al cambiarla, se guarda.</summary>
    private void PersistRecurse()
    {
        bool v = chkRec.IsChecked == true;
        if (v == _settings.Recurse) return;   // evita reescribir al aplicar los ajustes
        _settings.Recurse = v;
        SettingsStore.Save(_settings);
    }

    private void OpenPreferences()
    {
        var names = (cboPreset.ItemsSource as IEnumerable<Preset>)?.Select(p => p.Name) ?? Enumerable.Empty<string>();
        var dlg = new PreferencesWindow(_settings, names) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            _settings = dlg.Result;
            SettingsStore.Save(_settings);
            ApplySettings();
            lblProg.Text = "Preferencias guardadas.";
        }
    }

    // ---------- renombrado de la salida (estilo PowerRename) ----------
    private void OpenRenameDialog()
    {
        string ext = Engine.OutputExtension(BuildOptions());
        var rows = SelectedRows();
        if (rows.Count == 0) rows = _rows.ToList();   // sin selección: previsualiza con toda la lista

        var items = rows
            .Select(r => (name: Path.GetFileNameWithoutExtension(r.Name) + ext, created: SafeCreated(r.Path)))
            .ToList();

        var dlg = new RenameWindow(_settings.Rename, items,
                                   _settings.RenameSearchHistory, _settings.RenameReplaceHistory) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            _settings.Rename = dlg.Result;   // los historiales se mutan dentro del diálogo
            SettingsStore.Save(_settings);
            UpdateRenameStatus();
            lblProg.Text = _settings.Rename.HasEffect
                ? "Regla de renombrado activa."
                : "Renombrado desactivado: la salida conserva el nombre original.";
        }
    }

    private static DateTime SafeCreated(string path)
    {
        try { return File.GetCreationTime(path); } catch { return DateTime.Now; }
    }

    /// <summary>Refleja en el botón si hay una regla de renombrado activa (para que nunca sea silenciosa).</summary>
    private void UpdateRenameStatus()
    {
        bool on = _settings.Rename.HasEffect;
        lblRename.Text = on ? "Renombrar salida ✓" : "Renombrar salida";
        lblRename.Foreground = on ? (Brush)FindResource("Accent300") : (Brush)FindResource("Text");
    }

    private void ShowAbout() => DialogWindow.Aviso(this, "Acerca de ShrinkStudio", $"ShrinkStudio v{Updater.Current}\n\nCompresor de vídeo con foco en el ahorro de almacenamiento.\n" +
        "Usa FFmpeg por debajo. Nunca toca los originales salvo que lo pidas explícitamente.");

    /// <summary>Modal antes de comprimir: ¿enviar cada original a la Papelera al terminar? Devuelve (proceder, borrar, recordar).</summary>
    private (bool proceed, bool delete, bool remember) AskAfterCompress(int count)
    {
        var win = new Window
        {
            Title = "Comprimir", Width = 470, SizeToContent = SizeToContent.Height, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None, Background = Brushes.Transparent, AllowsTransparency = true,
        };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Vas a comprimir {count} archivo(s).", FontSize = 14, FontWeight = FontWeights.Medium,
            Foreground = (Brush)FindResource("Text"), Margin = new Thickness(0, 0, 0, 14),
        });
        var chkDel = new CheckBox
        {
            Content = "Enviar cada original a la Papelera cuando su comprimido esté listo",
            Foreground = (Brush)FindResource("Neutral300"), Margin = new Thickness(0, 0, 0, 9),
        };
        var chkRemember = new CheckBox
        {
            Content = "No volver a preguntar (se cambia en Preferencias)",
            Foreground = (Brush)FindResource("Neutral500"),
        };
        panel.Children.Add(chkDel);
        panel.Children.Add(chkRemember);
        panel.Children.Add(new TextBlock
        {
            Text = "Los originales van a la Papelera de reciclaje (recuperables), archivo por archivo según van terminando — nunca al final ni de forma definitiva.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Neutral600"),
            Margin = new Thickness(0, 12, 0, 16),
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancelar", Width = 100, Style = (Style)FindResource("BtnSecondary"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var okb = new Button { Content = "Comprimir", Width = 110, Style = (Style)FindResource("BtnPrimary"), IsDefault = true };
        row.Children.Add(cancel);
        row.Children.Add(okb);
        panel.Children.Add(row);
        win.Content = new Border
        {
            Background = (Brush)FindResource("Bg"), BorderBrush = (Brush)FindResource("Divider"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = panel,
        };
        bool proceed = false;
        okb.Click += (_, _) => { proceed = true; win.DialogResult = true; };
        win.ShowDialog();
        return (proceed, chkDel.IsChecked == true, chkRemember.IsChecked == true);
    }

    // ---------- selección de rutas ----------
    private void PickFolder(TextBox target)
    {
        var d = new OpenFolderDialog { Title = "Elige la carpeta" };
        if (d.ShowDialog(this) == true) target.Text = d.FolderName;
    }
    private async Task AddFilesDialogAsync()
    {
        var d = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Elige uno o varios vídeos (Ctrl/Shift para seleccionar varios)",
            Filter = "Vídeos|*.mkv;*.mp4;*.avi;*.m4v;*.mov;*.wmv;*.webm;*.mpg;*.mpeg;*.flv|Todos|*.*"
        };
        if (d.ShowDialog(this) == true) await AddFilesAsync(d.FileNames);
    }

    /// <summary>Añade archivos sueltos a la lista (sin reemplazarla) y analiza los nuevos.</summary>
    private async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var nuevos = new List<VideoRow>();
        foreach (var f in paths.Where(File.Exists))
        {
            if (_rows.Any(r => string.Equals(r.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
            var fi = new FileInfo(f);
            var row = new VideoRow
            {
                Name = fi.Name,
                Dir = fi.Directory?.Name ?? "",
                Path = fi.FullName,
                Bytes = fi.Length,
                SizeMB = $"{fi.Length / 1048576.0:n0} MB",
            };
            _rows.Add(row);
            nuevos.Add(row);
        }
        if (nuevos.Count == 0) return;
        foreach (var row in nuevos) lst.SelectedItems.Add(row);   // los recién añadidos entran seleccionados
        lblLangHint.Text = " detectando…";
        await ProbeRowsAsync(nuevos);
    }
    // ---------- arrastrar y soltar desde el Explorador ----------
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDropFiles(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var vids = ShellIntegration.ExpandVideos(paths, chkRec.IsChecked == true);
        if (vids.Count == 0) { lblProg.Text = "Ahí no había vídeos que añadir."; return; }
        AddFilesFromShell(vids);
    }

    /// <summary>
    /// Añade a la tabla los vídeos que llegan desde el Explorador (menú contextual,
    /// «Enviar a», arrastrar y soltar), y deja la ventana lista para trabajar.
    /// </summary>
    public async void AddFilesFromShell(IReadOnlyList<string> paths)
    {
        var nuevos = paths.Where(File.Exists).ToList();
        if (nuevos.Count == 0) return;

        // si aún no hay origen, se toma la carpeta del primer archivo (para «Abrir destino»)
        if (string.IsNullOrWhiteSpace(txtSrc.Text))
            txtSrc.Text = Path.GetDirectoryName(nuevos[0]) ?? "";

        int antes = _rows.Count;
        lblProg.Text = $"Añadiendo {nuevos.Count} vídeo(s) desde el Explorador…";
        await AddFilesAsync(nuevos);

        int añadidos = _rows.Count - antes;
        lblProg.Text = añadidos == 0
            ? "Esos vídeos ya estaban en la lista."
            : $"{añadidos} vídeo(s) añadidos desde el Explorador.";
    }

    private string EffectiveOutput()
    {
        if (!string.IsNullOrWhiteSpace(txtOut.Text)) return txtOut.Text.Trim();
        var src = txtSrc.Text.Trim();
        if (string.IsNullOrEmpty(src)) return "";
        try
        {
            var baseDir = Directory.Exists(src) ? src : Path.GetDirectoryName(src) ?? "";
            return Path.Combine(baseDir, "comprimido");
        }
        catch { return ""; }
    }
    private void OpenDestination()
    {
        var o = EffectiveOutput();
        if (string.IsNullOrEmpty(o)) return;
        Directory.CreateDirectory(o);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{o}\"") { UseShellExecute = true });
    }

    // ---------- analizar ----------
    private async Task ScanAsync()
    {
        var src = txtSrc.Text.Trim();
        if (string.IsNullOrEmpty(src) || (!Directory.Exists(src) && !File.Exists(src)))
        {
            DialogWindow.Aviso(this, "Origen", "Indica un archivo o carpeta de origen válido.");
            return;
        }
        _rows.Clear(); pnlALang.Children.Clear(); pnlSLang.Children.Clear();
        lblLangHint.Text = " detectando…";

        var outDir = EffectiveOutput();
        List<string> files;
        if (Directory.Exists(src))
        {
            var opt = chkRec.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            files = Directory.EnumerateFiles(src, "*.*", opt)
                .Where(f => Engine.VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !string.Equals(Path.GetDirectoryName(f), outDir, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f).ToList();
        }
        else files = new() { src };

        foreach (var f in files)
        {
            var fi = new FileInfo(f);
            _rows.Add(new VideoRow
            {
                Name = fi.Name,
                Dir = fi.Directory?.Name ?? "",
                Path = fi.FullName,
                Bytes = fi.Length,
                SizeMB = $"{fi.Length / 1048576.0:n0} MB",
            });
        }
        lblProg.Text = $"{_rows.Count} vídeo(s) encontrados. Leyendo pistas…";
        if (_rows.Count == 0) { lblLangHint.Text = "(nada que analizar)"; return; }
        lst.SelectAll();   // por defecto se procesan todos; el usuario acota con la selección

        await ProbeRowsAsync(_rows.ToList());
        lblLangHint.Text = pnlSLang.Children.Count == 0 ? "(sin subtítulos detectados)" : "";
    }

    /// <summary>Lee las pistas de cada fila con ffprobe y va poblando los idiomas detectados.</summary>
    private async Task ProbeRowsAsync(IReadOnlyList<VideoRow> rows)
    {
        foreach (var row in rows)
        {
            var info = await _engine.ProbeAsync(row.Path);
            row.Codec = info.Codec;
            row.Dur = $"{info.DurationSec / 3600:D1}:{info.DurationSec % 3600 / 60:D2}:{info.DurationSec % 60:D2}";
            row.Audio = string.Join("+", info.AudioLangs);
            row.Subs = string.Join("+", info.SubLangs);
            // El estado dice algo útil desde el análisis: si ya está bien comprimido, se
            // avisa aquí y no se preselecciona, en vez de descubrirlo al lanzar la tanda.
            int totalKbps = row.DurationSec > 0 ? (int)(row.Bytes * 8.0 / row.DurationSec / 1000.0) : 0;
            row.YaComprimido = Engine.AlreadyCompressed(info.Codec, totalKbps);
            row.Estado = row.YaComprimido ? $"Ya en {info.Codec.ToUpperInvariant()}" : "Pendiente";
            row.Width = info.Width; row.Height = info.Height; row.Fps = info.Fps;
            row.DurationSec = info.DurationSec;
            row.VideoBitrateKbps = info.VideoBitrateKbps; row.AudioBitrateKbps = info.AudioBitrateKbps;
            row.Channels = info.Channels; row.AudioCodec = info.AudioCodec; row.Probed = true;
            foreach (var l in info.AudioLangs) if (l != "?") EnsureLangChip(pnlALang, l);
            foreach (var l in info.SubLangs) if (l != "?") EnsureLangChip(pnlSLang, l);
        }
        UpdateLangCombo();

        // Preselección inteligente: se marcan solo los que conviene comprimir. Los que ya
        // están en un códec eficiente se dejan fuera, que era justo lo que prometía el
        // texto de la lista vacía y no cumplía nadie.
        int utiles = _rows.Count(r => !r.YaComprimido);
        if (utiles > 0 && utiles < _rows.Count)
        {
            lst.UnselectAll();
            foreach (var r in _rows.Where(r => !r.YaComprimido)) lst.SelectedItems.Add(r);
            lblProg.Text = $"{_rows.Count} vídeo(s) · {utiles} por comprimir · {_rows.Count - utiles} ya comprimidos (sin marcar).";
        }
        else lblProg.Text = utiles == 0 && _rows.Count > 0
            ? $"{_rows.Count} vídeo(s): todos están ya bien comprimidos."
            : $"{_rows.Count} vídeo(s) en la lista.";
    }

    /// <summary>Llena el combo de idioma principal con los idiomas de audio realmente detectados.</summary>
    private void UpdateLangCombo()
    {
        var detected = pnlALang.Children.OfType<CheckBox>().Select(c => (string)c.Content).Distinct().ToList();
        if (detected.Count == 0) return;
        var current = cboLang.Text;
        cboLang.ItemsSource = detected;
        cboLang.Text = detected.Contains(current) ? current : (detected.Contains("spa") ? "spa" : detected[0]);
    }

    private void EnsureLangChip(WrapPanel panel, string code)
    {
        if (panel.Children.OfType<CheckBox>().Any(c => (string)c.Content == code)) return;
        AddLangChip(panel, code);
    }

    private void AddLangChip(WrapPanel panel, string code)
    {
        var cb = new CheckBox { Content = code, IsChecked = true, Style = (Style)FindResource("ChipStyle") };
        cb.Checked += (_, _) => UpdateEstimate();
        cb.Unchecked += (_, _) => UpdateEstimate();
        panel.Children.Add(cb);
    }
    private static List<string> CheckedLangs(WrapPanel panel) =>
        panel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToList();

    // ---------- previsualización ----------
    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_marqueeDragging) return;   // durante el arrastre no recalculamos la vista (una vez al soltar)
        await UpdateForSelectionAsync();
    }

    private async Task UpdateForSelectionAsync()
    {
        if (lst.SelectedItem is not VideoRow r) return;
        UpdateEstimate();
        sldPreview.Maximum = Math.Max(0, r.DurationSec - 10);
        if (sldPreview.Value > sldPreview.Maximum) sldPreview.Value = 0;
        lblPreviewAt.Text = $"Previsualizar desde {FmtTime((int)sldPreview.Value)}";
        lblPrevName.Text = r.Name;
        lblPrevInfo.Text = $"{r.Dir}  ·  {r.SizeMB}  ·  {r.Dur}\n{r.Codec}  ·  audio: {r.Audio}" +
                           (string.IsNullOrEmpty(r.Subs) ? "" : $"  ·  subs: {r.Subs}");

        await ShowScrubFrameAsync();   // muestra el fotograma del punto de previsualización
    }

    // ---------- selección estilo explorador (rubber-band) ----------
    private void Lst_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // ignorar si el click es sobre la barra de desplazamiento
        if (FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject) != null) return;
        _marqueeStart = e.GetPosition(lst);
        _marqueeActive = true;
        _marqueeDragging = false;
        // no capturamos ni marcamos manejado aún: un click simple debe seleccionar la fila con normalidad
    }

    private void Lst_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_marqueeActive || e.LeftButton != MouseButtonState.Pressed) return;
        var cur = e.GetPosition(lst);
        if (!_marqueeDragging)
        {
            if (Math.Abs(cur.X - _marqueeStart.X) < 5 && Math.Abs(cur.Y - _marqueeStart.Y) < 5) return;   // umbral
            _marqueeDragging = true;
            _marqueeBase.Clear();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)   // Ctrl = añadir a lo ya seleccionado
                foreach (var it in lst.SelectedItems) _marqueeBase.Add(it);
            lst.CaptureMouse();
            marquee.Visibility = Visibility.Visible;
        }
        var band = new Rect(_marqueeStart, cur);
        Canvas.SetLeft(marquee, band.X);
        Canvas.SetTop(marquee, band.Y);
        marquee.Width = band.Width;
        marquee.Height = band.Height;
        ApplyMarqueeSelection(band);
        e.Handled = true;
    }

    private void Lst_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_marqueeDragging)
        {
            marquee.Visibility = Visibility.Collapsed;
            lst.ReleaseMouseCapture();
            _marqueeDragging = false;
            _marqueeActive = false;
            _ = UpdateForSelectionAsync();   // refresca la vista de detalle una sola vez
            e.Handled = true;
            return;
        }
        _marqueeActive = false;   // fue un click simple: lo gestiona el ListView
    }

    /// <summary>Selecciona las filas cuyo rectángulo intersecta la banda (unión con la base si venía con Ctrl).</summary>
    private void ApplyMarqueeSelection(Rect band)
    {
        for (int i = 0; i < lst.Items.Count; i++)
        {
            if (lst.ItemContainerGenerator.ContainerFromIndex(i) is not ListViewItem lvi) continue;
            Rect r;
            try
            {
                var tl = lvi.TranslatePoint(new Point(0, 0), lst);
                r = new Rect(tl, new Size(lvi.ActualWidth, lvi.ActualHeight));
            }
            catch { continue; }
            bool want = r.IntersectsWith(band) || _marqueeBase.Contains(lst.Items[i]);
            if (lvi.IsSelected != want) lvi.IsSelected = want;
        }
    }

    // ---------- quitar de la lista / menú contextual ----------
    private void Lst_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        RemoveSelectedRows();
        e.Handled = true;
    }

    /// <summary>Con el botón derecho, si la fila no estaba seleccionada pasa a serlo (como el explorador).</summary>
    private void Lst_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is not { } item) return;
        if (!item.IsSelected) { lst.UnselectAll(); item.IsSelected = true; }
    }

    /// <summary>
    /// Quita las filas de la lista. NO toca los archivos: solo deja de tenerlos en cuenta.
    /// Para borrar de verdad está «Enviar el archivo a la Papelera», que además pregunta.
    /// </summary>
    private void RemoveSelectedRows()
    {
        var sel = SelectedRows();
        if (sel.Count == 0) return;
        foreach (var r in sel) _rows.Remove(r);
        lblProg.Text = sel.Count == 1
            ? "1 vídeo quitado de la lista (el archivo sigue en su sitio)."
            : $"{sel.Count} vídeos quitados de la lista (los archivos siguen en su sitio).";
    }

    private void UpdateContextMenu()
    {
        int n = SelectedRows().Count;
        miCtxRemove.IsEnabled = miCtxRecycle.IsEnabled = miCtxCopyPath.IsEnabled = n > 0;
        miCtxOpenFolder.IsEnabled = n > 0;
        miCtxInvert.IsEnabled = _rows.Count > 0;
        miCtxSelectAll.IsEnabled = _rows.Count > 0;
        miCtxRemove.Header = n > 1 ? $"Quitar {n} de la lista" : "Quitar de la lista";
        miCtxRecycle.Header = n > 1 ? $"Enviar {n} archivos a la Papelera…" : "Enviar el archivo a la Papelera…";
    }

    private void OpenContainingFolder()
    {
        if (SelectedRows().FirstOrDefault() is not { } r) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{r.Path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { lblProg.Text = "No se pudo abrir la carpeta: " + ex.Message; }
    }

    private void CopySelectedPaths()
    {
        var sel = SelectedRows();
        if (sel.Count == 0) return;
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(r => r.Path)));
            lblProg.Text = sel.Count == 1 ? "Ruta copiada." : $"{sel.Count} rutas copiadas.";
        }
        catch (Exception ex) { lblProg.Text = "No se pudo copiar: " + ex.Message; }
    }

    private void InvertSelection()
    {
        for (int i = 0; i < lst.Items.Count; i++)
            if (lst.ItemContainerGenerator.ContainerFromIndex(i) is ListViewItem lvi)
                lvi.IsSelected = !lvi.IsSelected;
    }

    /// <summary>Las filas seleccionadas, en el orden de la lista.</summary>
    private List<VideoRow> SelectedRows() => _rows.Where(r => lst.SelectedItems.Contains(r)).ToList();

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>Genera y muestra el fotograma del vídeo seleccionado en el punto del timeline.</summary>
    private async Task ShowScrubFrameAsync()
    {
        if (lst.SelectedItem is not VideoRow r || !r.Probed) return;
        _scrubCts?.Cancel();
        var cts = _scrubCts = new CancellationTokenSource();
        var token = cts.Token;

        Directory.CreateDirectory(_thumbDir);
        var scrub = Path.Combine(_thumbDir, $"frame_{Guid.NewGuid():N}.jpg");   // archivo único: sin carreras
        bool ok = await Engine.MakeThumbnailAsync(r.Path, scrub, (int)sldPreview.Value);
        if (token.IsCancellationRequested || !ok || !File.Exists(scrub)) { TryDelete(scrub); return; }
        try
        {
            var bytes = await File.ReadAllBytesAsync(scrub, token);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            if (!token.IsCancellationRequested) imgPrev.Source = bmp;
        }
        catch { }
        TryDelete(scrub);
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    // ---------- estimación de ahorro ----------
    /// <summary>
    /// Enseña una de las pestañas laterales. Va por nombre y no por booleano —como estaba—
    /// porque un <c>bool</c> que significa «la segunda» solo se entiende si hay exactamente
    /// dos, y añadir una tercera obligaba a reescribirlo entero.
    /// </summary>
    private void ShowSideTab(string cual)
    {
        var paneles = new (string Clave, Border Pestaña, UIElement Panel)[]
        {
            ("detalle", tabDetalle, panelDetalle),
            ("estim",   tabEstim,   panelEstim),
        };

        foreach (var (clave, pestaña, panel) in paneles)
        {
            bool activa = clave == cual;
            panel.Visibility = activa ? Visibility.Visible : Visibility.Collapsed;
            pestaña.BorderBrush = activa ? (Brush)FindResource("Accent") : Brushes.Transparent;
            ((TextBlock)pestaña.Child).Foreground = (Brush)FindResource(activa ? "Accent300" : "Neutral400");
        }
    }

    private void UpdateEstimate()
    {
        if (lst.SelectedItem is not VideoRow r || !r.Probed) { ClearEstimate(); return; }
        var est = Estimator.Compute(r, BuildOptions());
        if (!est.Valid) { ClearEstimate(); return; }
        lblEstSize.Text = "≈ " + Human(est.EstBytes);
        lblEstSaving.Text = $"−{est.SavedPct}% · ahorras {Human(est.SavedBytes)}";
        SetBar(barVQ, est.VideoQuality); SetBar(barVS, est.VideoSaving);
        SetBar(barAQ, est.AudioQuality); SetBar(barAS, est.AudioSaving);
        lblEstDetail.Text = $"Vídeo ≈ {est.EstVideoKbps} kbps · audio ≈ {est.EstAudioKbps} kbps. " +
                            "Estimación aproximada; el resultado real depende del contenido.";
    }

    private void ClearEstimate()
    {
        lblEstSize.Text = "—";
        lblEstSaving.Text = "Selecciona un vídeo analizado";
        foreach (var b in new[] { barVQ, barVS, barAQ, barAS }) SetBar(b, 0);
        lblEstDetail.Text = "";
    }

    private void SetBar(StackPanel panel, int value)
    {
        panel.Children.Clear();
        var on = (Brush)FindResource("Accent");
        var off = (Brush)FindResource("Neutral800");
        for (int i = 0; i < 5; i++)
            panel.Children.Add(new Border
            {
                Width = 20, Height = 6, CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 3, 0),
                Background = i < value ? on : off,
            });
    }

    private static string Human(long bytes) => bytes switch
    {
        >= (1L << 30) => $"{bytes / (double)(1L << 30):n2} GB",
        >= (1L << 20) => $"{bytes / (double)(1L << 20):n0} MB",
        _ => $"{bytes / 1024.0:n0} KB",   // por debajo de 1 MB, «0 MB» no dice nada
    };

    private static string FmtTime(int sec) => $"{sec / 60}:{sec % 60:D2}";

    // ---------- medición real del tamaño (muestreo) ----------
    private async Task OnMeasureAsync()
    {
        if (lst.SelectedItem is not VideoRow r || !r.Probed)
        {
            DialogWindow.Aviso(this, "Medir", "Selecciona un vídeo analizado.");
            return;
        }
        if (_running) { DialogWindow.Aviso(this, "Medir", "Espera a que termine la compresión en curso."); return; }

        var opt = BuildOptions();
        if (opt.AudioOnly) { DialogWindow.Aviso(this, "Medir", "En modo «solo audio» el tamaño ya es exacto: no hace falta medir."); return; }

        btnMeasure.IsEnabled = false;
        progRow.Visibility = Visibility.Visible; bar.Value = 0;
        lblProg.Text = "Midiendo con muestras reales…";
        lblMeasure.Text = "Codificando 3 fragmentos con estos ajustes…";
        var cts = new CancellationTokenSource();
        try
        {
            int kbps = await _engine.MeasureVideoBitrateAsync(r.Path, opt, new PreviewReporter(this), cts.Token);
            if (kbps <= 0)
            {
                lblMeasure.Text = "No se pudo medir este vídeo.";
                lblProg.Text = "No se pudo medir.";
                return;
            }
            // el contenido manda: deducimos su factor de complejidad y recalibramos
            double factor = Estimator.FactorFromMeasurement(r, opt, kbps);
            Estimator.ComplexityFactor = factor;
            _settings.ComplexityFactor = factor;
            SettingsStore.Save(_settings);
            UpdateEstimate();
            lblMeasure.Text = $"Medido: vídeo ≈ {kbps} kbps. Contenido {(factor < 0.85 ? "más fácil" : factor > 1.15 ? "más exigente" : "normal")} " +
                              $"de lo típico (×{factor:0.00}); el resto de la lista ya usa esta calibración.";
            lblProg.Text = "Medición aplicada a la estimación.";
        }
        catch (OperationCanceledException) { lblMeasure.Text = "Medición cancelada."; }
        catch (Exception ex) { lblMeasure.Text = "Error al medir: " + ex.Message; }
        finally
        {
            cts.Dispose();
            btnMeasure.IsEnabled = true;
            progRow.Visibility = Visibility.Collapsed;
        }
    }

    // ---------- previsualización de 10 s ----------
    private async Task OnPreviewAsync()
    {
        if (_previewCts != null) { _previewCts.Cancel(); return; }   // ya generando → el botón cancela
        if (lst.SelectedItem is not VideoRow r || !r.Probed)
        {
            DialogWindow.Aviso(this, "Previsualizar", "Selecciona un vídeo analizado.");
            return;
        }
        _previewCts = new CancellationTokenSource();
        btnPreview.Content = "■  Cancelar previsualización";
        progRow.Visibility = Visibility.Visible; bar.Value = 0;
        lblProg.Text = "Generando previsualización (10 s)…";
        try
        {
            Directory.CreateDirectory(_previewDir);
            foreach (var old in Directory.GetFiles(_previewDir)) TryDelete(old);   // borra la anterior
            var dest = Path.Combine(_previewDir, $"preview_{Guid.NewGuid():N}.mkv");
            var path = await _engine.PreviewAsync(r.Path, BuildOptions(), (int)sldPreview.Value, dest,
                                                  new PreviewReporter(this), _previewCts.Token);
            if (path != null)
            {
                lblProg.Text = "Previsualización lista (se borra sola).";
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            else lblProg.Text = "No se pudo generar la previsualización.";
        }
        catch (OperationCanceledException) { lblProg.Text = "Previsualización cancelada."; }
        catch (Exception ex) { lblProg.Text = "Error en la previsualización: " + ex.Message; }
        finally
        {
            progRow.Visibility = Visibility.Collapsed;
            _previewCts?.Dispose(); _previewCts = null;
            btnPreview.Content = _previewBtnContent;
        }
    }

    // reportero de progreso para la preview (actualiza la barra)
    private sealed class PreviewReporter : IEngineReporter
    {
        private readonly MainWindow _w;
        public PreviewReporter(MainWindow w) => _w = w;
        public void Log(string l) { }
        public void FileStart(int i, int t, string n, double d) { }
        public void FileProgress(double f, string raw) => _w.Dispatcher.BeginInvoke(() => _w.bar.Value = f);
        public void FileDone(FileResult r) { }
    }

    // ---------- eliminar (papelera) ----------
    private void DeleteSelected()
    {
        var sel = SelectedRows();
        if (sel.Count == 0) { DialogWindow.Aviso(this, "Eliminar", "No hay vídeos seleccionados."); return; }
        double mb = sel.Sum(r => r.Bytes) / 1048576.0;
        if (!DialogWindow.Confirmar(this, "Eliminar marcados", $"¿Enviar {sel.Count} vídeo(s) ({mb:n0} MB) a la Papelera de reciclaje?")) return;
        foreach (var r in sel)
        {
            if (RecycleBin.Send(r.Path)) _rows.Remove(r);
            else r.Estado = "error al borrar";
        }
        lblProg.Text = "Enviados a la Papelera.";
    }
    private void DeleteFolders()
    {
        var dirs = SelectedRows().Select(r => Path.GetDirectoryName(r.Path)!).Distinct().ToList();
        if (dirs.Count == 0)
        {
            var s = txtSrc.Text.Trim();
            if (!string.IsNullOrEmpty(s) && Directory.Exists(s)) dirs = new() { s };
        }
        if (dirs.Count == 0) { DialogWindow.Aviso(this, "Eliminar carpeta", "Marca algún vídeo o indica una carpeta de origen."); return; }
        if (!DialogWindow.Confirmar(this, "Eliminar carpeta", $"¿Enviar estas {dirs.Count} carpeta(s) COMPLETAS a la Papelera?\n\n{string.Join("\n", dirs)}")) return;
        foreach (var d in dirs) RecycleBin.Send(d);
        foreach (var r in _rows.Where(r => Path.GetDirectoryName(r.Path) is { } d && dirs.Contains(d)).ToList()) _rows.Remove(r);
        lblProg.Text = "Carpeta(s) enviadas a la Papelera.";
    }

    // ---------- comprimir ----------
    private EncodeOptions BuildOptions()
    {
        var opt = new EncodeOptions
        {
            Output = EffectiveOutput() is { Length: > 0 } o ? o : null,
            Lang = string.IsNullOrWhiteSpace(cboLang.Text) ? "spa" : cboLang.Text.Trim(),
            KeepLangs = CheckedLangs(pnlALang),
            Force = chkForce.IsChecked == true,
            DryRun = chkDry.IsChecked == true,
            NameRule = _settings.Rename,
            VideoCodec = cboCodec.SelectedIndex switch { 1 => "h264", 2 => "av1", _ => "hevc" },
        };
        switch (cboFmt.SelectedIndex)
        {
            case 1: opt.Container = "mp4"; break;
            case 2: opt.Container = "webm"; break;
            case 3: opt.AudioOnly = true; opt.AudioFormat = "mp3"; break;
            case 4: opt.AudioOnly = true; opt.AudioFormat = "m4a"; break;
            case 5: opt.AudioOnly = true; opt.AudioFormat = "flac"; break;
            case 6: opt.AudioOnly = true; opt.AudioFormat = "opus"; break;
            default: opt.Container = "mkv"; break;
        }
        opt.Quality = cboQ.SelectedIndex switch { 1 => 22, 2 => 24, 3 => 27, 4 => 30, _ => 0 };
        opt.MaxHeight = cboRes.SelectedIndex switch { 1 => 1080, 2 => 720, 3 => 480, _ => 0 };
        opt.AudioBitrate = cboAud.SelectedIndex switch { 1 => 192, 2 => 160, 3 => 128, 4 => 96, _ => 0 };

        var sChips = pnlSLang.Children.OfType<CheckBox>().ToList();
        var sChecked = CheckedLangs(pnlSLang);
        if (sChips.Count > 0 && sChecked.Count == 0) opt.NoSubs = true;
        else if (sChecked.Count > 0 && sChecked.Count < sChips.Count) opt.SubLangs = sChecked;
        return opt;
    }

    private async Task RunAsync()
    {
        var selRows = SelectedRows();
        if (selRows.Count == 0) { DialogWindow.Aviso(this, "Comprimir", "Analiza y selecciona al menos un vídeo (arrastra o Ctrl/Shift+click)."); return; }
        var sel = selRows.Select(r => r.Path).ToList();

        var opt = BuildOptions();

        // ¿enviar cada original a la Papelera tras comprimirlo? (según preferencias / modal)
        bool deleteOriginals = false;
        if (!opt.DryRun)
        {
            if (_settings.AfterCompress == AfterCompress.Ask)
            {
                var (proceed, del, remember) = AskAfterCompress(selRows.Count);
                if (!proceed) return;   // canceló el modal
                deleteOriginals = del;
                if (remember)
                {
                    _settings.AfterCompress = del ? AfterCompress.RecycleOriginal : AfterCompress.Keep;
                    SettingsStore.Save(_settings);
                }
            }
            else deleteOriginals = _settings.AfterCompress == AfterCompress.RecycleOriginal;
        }

        _cts = new CancellationTokenSource();
        _running = true;
        btnRun.IsEnabled = false; btnCancel.IsEnabled = true; btnPause.IsEnabled = true;
        _paused = false; btnPause.Content = "Pausar";
        progRow.Visibility = Visibility.Visible; bar.Value = 0;
        tglLog.IsChecked = true;
        txtLog.Clear();
        lblProg.Text = $"Procesando {sel.Count} vídeo(s)…";
        ActualizarTextoPildora(0, sel.Count, 0);
        ActualizarPildoraFondo();

        foreach (var r in selRows) r.Estado = "En cola";
        var reporter = new Reporter(this, deleteOriginals, selRows);
        // «ok» vive fuera del try porque el resumen de subtítulos lo consulta al terminar
        var ok = new List<FileResult>();
        try
        {
            var results = await _engine.CompressAsync(sel, opt, reporter, _cts.Token);
            ok = results.Where(r => r.OutBytes != null).ToList();
            if (ok.Count > 0)
            {
                double inGb = ok.Sum(r => r.InBytes) / 1073741824.0;
                double outGb = ok.Sum(r => r.OutBytes!.Value) / 1073741824.0;
                int pct = (int)Math.Round(100 - (outGb / Math.Max(inGb, 1e-9) * 100));
                lblProg.Text = $"Terminado ✓  {inGb:n2} GB → {outGb:n2} GB  (-{pct}%)  ·  {ok.Count} archivo(s)";
            }
            else lblProg.Text = "Terminado ✓";
        }
        catch (OperationCanceledException) { lblProg.Text = "Cancelado."; }
        catch (Exception ex) { lblProg.Text = "Error: " + ex.Message; AppendLog("ERROR: " + ex); }
        finally
        {
            _running = false; _paused = false;
            btnRun.IsEnabled = true; btnCancel.IsEnabled = false;
            btnPause.IsEnabled = false; btnPause.Content = "Pausar";
            progRow.Visibility = Visibility.Collapsed;
            _cts?.Dispose(); _cts = null;
            // La píldora pasa a «✓ N hechos» un momento y se retira sola: si desapareciera
            // de golpe, quien estuviera en «Organizar» no llegaría a saber que terminó.
            AnunciarFinEnPildora(sel.Count);
        }

        // Con la ventana ya en reposo: si se han quedado subtítulos fuera, decirlo.
        WarnAboutLostSubtitles(ok);
    }

    // ---------- presets ----------
    private void ReloadPresets(string? select)
    {
        var all = PresetStore.Factory().Concat(PresetStore.LoadUser()).ToList();
        _applyingPreset = true;
        cboPreset.ItemsSource = all;
        _applyingPreset = false;
        if (select != null) cboPreset.SelectedItem = all.FirstOrDefault(p => p.Name == select);
    }

    private void ApplyPreset()
    {
        if (_applyingPreset || cboPreset.SelectedItem is not Preset p) return;
        _applyingPreset = true;
        cboFmt.SelectedIndex = p.Fmt; cboCodec.SelectedIndex = p.Codec;
        cboQ.SelectedIndex = p.Quality; cboRes.SelectedIndex = p.Res; cboAud.SelectedIndex = p.Audio;
        cboLang.Text = p.Lang;
        // «Subcarpetas» NO se toca aquí: es un ajuste de exploración del disco que manda
        // el usuario desde Preferencias, no parte de la receta de codificación. Antes el
        // preset lo reactivaba y pisaba la preferencia.
        _applyingPreset = false;
        UpdateEstimate();
    }

    private void SavePreset()
    {
        var name = InputName("Nombre del preset:", "Guardar preset");
        if (string.IsNullOrWhiteSpace(name)) return;
        var user = PresetStore.LoadUser();
        user.RemoveAll(x => x.Name == name);
        user.Add(new Preset
        {
            Name = name, Fmt = cboFmt.SelectedIndex, Codec = cboCodec.SelectedIndex,
            Quality = cboQ.SelectedIndex, Res = cboRes.SelectedIndex, Audio = cboAud.SelectedIndex,
            Lang = cboLang.Text,
        });
        PresetStore.SaveUser(user);
        ReloadPresets(name);
        lblProg.Text = $"Preset «{name}» guardado.";
    }

    private string? InputName(string prompt, string title)
    {
        var win = new Window
        {
            Title = title, Width = 380, SizeToContent = SizeToContent.Height, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
            Background = (Brush)FindResource("Surface"),
        };
        var tb = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
        var ok = new Button { Content = "Guardar", Width = 90, Style = (Style)FindResource("BtnPrimary"), IsDefault = true };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt, Foreground = (Brush)FindResource("Text"), Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(tb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(ok);
        panel.Children.Add(row);
        win.Content = panel;
        string? result = null;
        ok.Click += (_, _) => { result = tb.Text.Trim(); win.DialogResult = true; };
        win.ContentRendered += (_, _) => tb.Focus();
        win.ShowDialog();
        return result;
    }

    /// <summary>
    /// Si algún vídeo ha salido sin los subtítulos que el usuario tenía marcados, decírselo
    /// a la cara al terminar. Antes solo constaba en el registro y se daba por hecho que iban.
    /// </summary>
    private void WarnAboutLostSubtitles(List<FileResult> done)
    {
        var afectados = done.Where(r => !string.IsNullOrEmpty(r.SubtitleWarning)).ToList();
        if (afectados.Count == 0) return;

        lblProg.Text += "  ·  ⚠ sin algunos subtítulos";

        // Un motivo por línea; casi siempre será uno solo repetido en varios archivos.
        var motivos = afectados.Select(r => r.SubtitleWarning!).Distinct().ToList();
        string cuerpo = afectados.Count == 1
            ? $"«{afectados[0].Name}»:\n\n{motivos[0]}"
            : $"{afectados.Count} de los {done.Count} vídeos han salido sin parte de sus subtítulos:\n\n"
              + string.Join("\n\n", motivos)
              + "\n\nAfectados:\n· " + string.Join("\n· ", afectados.Take(8).Select(r => r.Name))
              + (afectados.Count > 8 ? $"\n… y {afectados.Count - 8} más" : "");

        DialogWindow.Aviso(this, "Subtítulos no incluidos", cuerpo);
    }

    private void TogglePause()
    {
        if (!_running) return;
        _paused = !_paused;
        if (_paused) { _engine.Pause(); btnPause.Content = "Reanudar"; lblProg.Text = "En pausa — FFmpeg suspendido"; }
        else { _engine.Resume(); btnPause.Content = "Pausar"; lblProg.Text = "Comprimiendo… (mira la barra)"; }
    }

    private void AppendLog(string line)
    {
        txtLog.AppendText(line + "\n");
        txtLog.ScrollToEnd();
    }

    /// <summary>
    /// Ancho por debajo del cual el panel lateral estorba más de lo que aporta. Sale de
    /// sumar lo que la tabla necesita para que sus columnas se lean (≈620) más los 262 del
    /// panel y los márgenes: por debajo, el panel se estaría quedando con espacio que la
    /// tabla necesita más.
    /// </summary>
    private const double AnchoMinimoConLateral = 940;

    /// <summary>
    /// WPF no tiene consultas de medios, así que la adaptación al ancho se hace aquí. Es un
    /// solo umbral a propósito: cuantos más puntos de corte, más difícil es que el resultado
    /// siga siendo coherente en todos ellos.
    /// </summary>
    private void AjustarAAncho()
    {
        bool cabeElLateral = ActualWidth >= AnchoMinimoConLateral;

        colLateral.Width = cabeElLateral ? new GridLength(262) : new GridLength(0);
        if (sideCol != null)
            sideCol.Visibility = cabeElLateral ? Visibility.Visible : Visibility.Collapsed;

        // El texto del botón de renombrar sobra antes que el botón: se queda el icono, que
        // con su descripción emergente sigue diciendo lo que hace.
        if (lblRename != null)
            lblRename.Visibility = ActualWidth >= 1080 ? Visibility.Visible : Visibility.Collapsed;

        // La versión junto al nombre es lo primero que sobra en la barra de título.
        if (lblVersion != null)
            lblVersion.Visibility = ActualWidth >= 900 ? Visibility.Visible : Visibility.Collapsed;

        // Y lo segundo, el texto del conmutador: sin esto el menú y el conmutador se pisan y
        // «Ayuda» desaparecía debajo. Los iconos se quedan, y cada pestaña tiene su
        // descripción emergente, así que no se pierde qué es cada una.
        var textoPestanas = ActualWidth >= 1000 ? Visibility.Visible : Visibility.Collapsed;
        if (lblTabComprimir != null) lblTabComprimir.Visibility = textoPestanas;
        if (lblTabOrganizar != null) lblTabOrganizar.Visibility = textoPestanas;
    }

    // ─────────────────────── páginas de oficio ───────────────────────

    /// <summary>
    /// Cambia entre «Comprimir» y «Organizar». Las dos páginas comparten ventana, barra de
    /// título y registro, pero cada una tiene su lista y su ciclo de vida: cambiar de página
    /// NO detiene lo que estuviera corriendo en la otra.
    /// </summary>
    private void CambiarPagina(bool organizar)
    {
        var comprimir = organizar ? Visibility.Collapsed : Visibility.Visible;
        rowOrigen.Visibility = comprimir;
        rowOpciones.Visibility = comprimir;
        rowTabla.Visibility = comprimir;
        rowAcciones.Visibility = comprimir;
        // La barra de progreso solo reaparece si de verdad hay algo comprimiendo
        progRow.Visibility = organizar ? Visibility.Collapsed
                                       : (_running ? Visibility.Visible : Visibility.Collapsed);

        pageOrganizar.Visibility = organizar ? Visibility.Visible : Visibility.Collapsed;
        ActualizarPildoraFondo();
    }

    /// <summary>
    /// La píldora de la barra de título solo tiene sentido cuando hay compresión en marcha y
    /// el usuario NO la está mirando: si estás en «Comprimir» ya ves la barra de progreso, y
    /// repetirlo sería ruido.
    /// </summary>
    private void ActualizarPildoraFondo()
    {
        bool enOtraPagina = pageOrganizar.Visibility == Visibility.Visible;
        pillFondo.Visibility = _running && enOtraPagina ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Texto de la píldora: «Comprimiendo 3/8 · 31 %».</summary>
    private void ActualizarTextoPildora(int hecho, int total, double fraccion)
    {
        lblPill.Text = total > 0
            ? $"Comprimiendo {hecho}/{total} · {fraccion * 100:0} %"
            : "Comprimiendo";
    }

    /// <summary>
    /// Al acabar, la píldora dice «✓ N hechos» unos segundos y se retira. Quitarla de golpe
    /// dejaría a quien esté en «Organizar» sin enterarse de que terminó.
    /// </summary>
    private void AnunciarFinEnPildora(int total)
    {
        if (pillFondo.Visibility != Visibility.Visible) return;

        pillDot.Visibility = Visibility.Collapsed;
        lblPill.Text = $"✓ {total} hecho{(total == 1 ? "" : "s")}";

        var reloj = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        reloj.Tick += (s, _) =>
        {
            reloj.Stop();
            pillFondo.Visibility = Visibility.Collapsed;
            pillDot.Visibility = Visibility.Visible;
        };
        reloj.Start();
    }

    // reporter que marshaliza el avance del motor al hilo de la UI
    private sealed class Reporter : IEngineReporter
    {
        private readonly MainWindow _w;
        private readonly bool _del;
        private readonly IReadOnlyList<VideoRow> _queue;   // mismo orden que las rutas del motor
        private VideoRow? _current;
        private int _lastPct = -1;

        public Reporter(MainWindow w, bool deleteOriginals, IReadOnlyList<VideoRow> queue)
        { _w = w; _del = deleteOriginals; _queue = queue; }

        private VideoRow? RowOf(string path) =>
            _w._rows.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

        public void Log(string line) => _w.Dispatcher.BeginInvoke(() => _w.AppendLog(line));
        // El índice y el total se guardan para la píldora de la barra de título: cuando el
        // usuario se va a «Organizar» deja de ver la barra de progreso, y esa píldora es lo
        // único que le dice que la compresión sigue viva.
        private int _idx, _total;

        public void FileStart(int i, int t, string name, double dur) =>
            _w.Dispatcher.BeginInvoke(() =>
            {
                _idx = i; _total = t;
                _w.lblProg.Text = $"[{i}/{t}] {name}";
                _w.bar.Value = 0;
                _lastPct = -1;
                // el motor numera del 1 al total en el mismo orden en que se le pasó la cola
                _current = i >= 1 && i <= _queue.Count ? _queue[i - 1] : null;
                if (_current != null) _current.Estado = "Comprimiendo…";
                _w.ActualizarTextoPildora(i, t, 0);
            });

        public void FileProgress(double frac, string raw)
        {
            int pct = (int)Math.Round(Math.Clamp(frac, 0, 1) * 100);
            _w.Dispatcher.BeginInvoke(() =>
            {
                _w.bar.Value = frac;
                // solo se reescribe la fila al cambiar de entero: si no, repinta sin parar
                if (_current != null && pct != _lastPct) { _lastPct = pct; _current.Estado = $"Comprimiendo… {pct}%"; }
                _w.ActualizarTextoPildora(_idx, _total, frac);
            });
        }

        /// <summary>Un archivo que el motor se salta: la fila cuenta el motivo.</summary>
        public void FileSkipped(string sourcePath, string reason) =>
            _w.Dispatcher.BeginInvoke(() =>
            {
                if (RowOf(sourcePath) is { } row) row.Estado = reason;
            });

        // Borrado iteración a iteración: en cuanto un archivo se comprime OK, su original
        // se envía a la Papelera (en segundo plano, sin bloquear la codificación siguiente).
        public void FileDone(FileResult r)
        {
            // el resultado se queda escrito en la fila: cuánto se ahorró, o que falló
            _w.Dispatcher.BeginInvoke(() =>
            {
                if (RowOf(r.SourcePath) is { } row)
                    row.Estado = r.Ok ? $"{r.Status} · {Human(r.OutBytes!.Value)}" : "Error";
                if (_current != null && string.Equals(_current.Path, r.SourcePath, StringComparison.OrdinalIgnoreCase))
                    _current = null;
            });

            if (!Engine.ShouldRecycleSource(_del, r)) return;
            Task.Run(() =>
            {
                bool ok = RecycleBin.Send(r.SourcePath);
                _w.Dispatcher.BeginInvoke(() =>
                {
                    if (ok)
                    {
                        var row = _w._rows.FirstOrDefault(x => string.Equals(x.Path, r.SourcePath, StringComparison.OrdinalIgnoreCase));
                        if (row != null) _w._rows.Remove(row);
                        _w.AppendLog($"    original a la Papelera: {r.Name}");
                    }
                    else _w.AppendLog($"    no se pudo enviar a la Papelera: {r.Name}");
                });
            });
        }

        public void DiskFull(bool paused) => _w.Dispatcher.BeginInvoke(() =>
        {
            _w.lblProg.Text = paused
                ? "⛔ Disco lleno — pausado. Libera espacio y continuará solo."
                : "Comprimiendo… (mira la barra)";
        });
    }

    // ---------- actualizaciones ----------
    private async Task CheckUpdateAsync(bool manual)
    {
        if (manual)
        {
            btnCheckUpdate.IsEnabled = false;         // evita repetir la búsqueda a lo tonto
            miCheckUpd.IsEnabled = false;
            lblProg.Text = "Buscando actualizaciones…";
        }
        try
        {
            var res = await Updater.CheckAsync();

            if (res.Failed)
            {
                // antes esto se confundía con «estás al día»: se decía que no había nada
                // nuevo aunque en realidad no hubiera habido conexión
                if (manual) lblProg.Text = "No se pudo comprobar: " + res.Error;
                return;
            }
            if (!res.Available)
            {
                if (manual) lblProg.Text = $"Ya tienes la última versión (v{Updater.Current}).";
                return;
            }

            var info = res.Info!;
            lblUpdate.Text = $"Versión nueva disponible: {info.Tag}  (tienes v{Updater.Current}).";
            updateBar.Visibility = Visibility.Visible;
            btnUpdateNow.Click -= OnUpdateNow;   // evitar suscripción doble
            btnUpdateNow.Click += OnUpdateNow;
            _pendingUpdate = info;
            // faltaba: sin esto se quedaba «Buscando actualizaciones…» para siempre
            lblProg.Text = $"Versión {info.Tag} disponible — mira el aviso de arriba.";
        }
        finally
        {
            btnCheckUpdate.IsEnabled = true;
            miCheckUpd.IsEnabled = true;
        }
    }

    private UpdateInfo? _pendingUpdate;
    private async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        btnUpdateNow.IsEnabled = false;
        lblUpdate.Text = $"Descargando {_pendingUpdate.AssetName}…";
        progRow.Visibility = Visibility.Visible; bar.Value = 0;
        lblProg.Text = "Descargando la actualización…";
        try
        {
            var progress = new Progress<double>(p =>
            {
                lblUpdate.Text = $"Descargando {_pendingUpdate.AssetName}…  {p * 100:n0}%";
                bar.Value = p;
            });
            var installer = await Updater.DownloadAsync(_pendingUpdate, progress);
            lblUpdate.Text = "Descarga lista. Abriendo el instalador; la app se cerrará para actualizarse.";
            lblProg.Text = "Abriendo el instalador…";
            Updater.LaunchInstallerAndExit(installer);
        }
        catch (Exception ex)
        {
            btnUpdateNow.IsEnabled = true;
            progRow.Visibility = Visibility.Collapsed;
            lblUpdate.Text = "No se pudo descargar la actualización.";
            lblProg.Text = "Fallo al descargar la actualización.";
            DialogWindow.Aviso(this, "Actualizar", "No se pudo descargar la actualización:\n" + ex.Message);
        }
    }

    // ---------- cierre ----------
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_running)
        {
            if (DialogWindow.Confirmar(this, "Salir", "Hay una compresión en curso. ¿Cerrar y cancelarla?"))
                _cts?.Cancel();
            else { e.Cancel = true; return; }
        }
        // liberar miniaturas y previsualizaciones cacheadas en %TEMP% (foco: ahorro de almacenamiento)
        try { if (Directory.Exists(_thumbDir)) Directory.Delete(_thumbDir, true); } catch { }
        try { if (Directory.Exists(_previewDir)) Directory.Delete(_previewDir, true); } catch { }
        base.OnClosing(e);
    }
}
