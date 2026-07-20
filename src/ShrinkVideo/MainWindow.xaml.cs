using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ShrinkVideo;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<VideoRow> _rows = new();
    private readonly Engine _engine = new();
    private readonly string _thumbDir = Path.Combine(Path.GetTempPath(), "shrinkvideo_thumbs");
    private CancellationTokenSource? _cts;
    private bool _running;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(_thumbDir);
        lst.ItemsSource = _rows;
        lblVersion.Text = "v" + Updater.Current;

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
        btnMarkAll.Click += (_, _) => { foreach (var r in _rows) r.Sel = true; };
        btnMarkNone.Click += (_, _) => { foreach (var r in _rows) r.Sel = false; };
        btnDelSel.Click += (_, _) => DeleteSelected();
        btnDelDir.Click += (_, _) => DeleteFolders();
        btnCheckUpdate.Click += async (_, _) => await CheckUpdateAsync(manual: true);
        btnUpdateLater.Click += (_, _) => updateBar.Visibility = Visibility.Collapsed;

        Loaded += async (_, _) =>
        {
            cboLang.Text = "spa";
            if (!await Engine.ToolsAvailableAsync())
                MessageBox.Show(this,
                    "No se encuentra FFmpeg. Instálalo (por ejemplo con:  winget install Gyan.FFmpeg) y vuelve a abrir la app.",
                    "Falta FFmpeg", MessageBoxButton.OK, MessageBoxImage.Warning);
            await CheckUpdateAsync(manual: false);
        };
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
        lblLangHint.Text = " detectando…";
        await ProbeRowsAsync(nuevos);
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
            MessageBox.Show(this, "Indica un archivo o carpeta de origen válido.", "Origen", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            row.Estado = "listo";
            foreach (var l in info.AudioLangs) if (l != "?") EnsureLangChip(pnlALang, l);
            foreach (var l in info.SubLangs) if (l != "?") EnsureLangChip(pnlSLang, l);
        }
        lblProg.Text = $"{_rows.Count} vídeo(s) en la lista.";
    }

    private void EnsureLangChip(WrapPanel panel, string code)
    {
        if (panel.Children.OfType<CheckBox>().Any(c => (string)c.Content == code)) return;
        AddLangChip(panel, code);
    }

    private void AddLangChip(WrapPanel panel, string code)
    {
        panel.Children.Add(new CheckBox
        {
            Content = code,
            IsChecked = true,
            Style = (Style)FindResource("ChipStyle"),
        });
    }
    private static List<string> CheckedLangs(WrapPanel panel) =>
        panel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToList();

    // ---------- previsualización ----------
    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lst.SelectedItem is not VideoRow r) return;
        lblPrevName.Text = r.Name;
        lblPrevInfo.Text = $"{r.Dir}  ·  {r.SizeMB}  ·  {r.Dur}\n{r.Codec}  ·  audio: {r.Audio}" +
                           (string.IsNullOrEmpty(r.Subs) ? "" : $"  ·  subs: {r.Subs}");

        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(r.Path)));
        var thumb = Path.Combine(_thumbDir, hash + ".jpg");
        if (!File.Exists(thumb))
        {
            if (!await Engine.MakeThumbnailAsync(r.Path, thumb, 120))
                await Engine.MakeThumbnailAsync(r.Path, thumb, 3);
        }
        if (File.Exists(thumb))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(thumb);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            imgPrev.Source = bmp;
        }
        else imgPrev.Source = null;
    }

    // ---------- eliminar (papelera) ----------
    private void DeleteSelected()
    {
        var sel = _rows.Where(r => r.Sel).ToList();
        if (sel.Count == 0) { MessageBox.Show(this, "No hay vídeos marcados.", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        double mb = sel.Sum(r => r.Bytes) / 1048576.0;
        if (MessageBox.Show(this, $"¿Enviar {sel.Count} vídeo(s) ({mb:n0} MB) a la Papelera de reciclaje?",
                "Eliminar marcados", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var r in sel)
        {
            if (RecycleBin.Send(r.Path)) _rows.Remove(r);
            else r.Estado = "error al borrar";
        }
        lblProg.Text = "Enviados a la Papelera.";
    }
    private void DeleteFolders()
    {
        var dirs = _rows.Where(r => r.Sel).Select(r => Path.GetDirectoryName(r.Path)!).Distinct().ToList();
        if (dirs.Count == 0)
        {
            var s = txtSrc.Text.Trim();
            if (!string.IsNullOrEmpty(s) && Directory.Exists(s)) dirs = new() { s };
        }
        if (dirs.Count == 0) { MessageBox.Show(this, "Marca algún vídeo o indica una carpeta de origen.", "Eliminar carpeta", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show(this, $"¿Enviar estas {dirs.Count} carpeta(s) COMPLETAS a la Papelera?\n\n{string.Join("\n", dirs)}",
                "Eliminar carpeta", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
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
            Container = cboFmt.SelectedIndex == 1 ? "mp4" : "mkv",
            VideoCodec = cboCodec.SelectedIndex switch { 1 => "h264", 2 => "av1", _ => "hevc" },
        };
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
        var sel = _rows.Where(r => r.Sel).Select(r => r.Path).ToList();
        if (sel.Count == 0) { MessageBox.Show(this, "Analiza y marca al menos un vídeo.", "Comprimir", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        var opt = BuildOptions();
        _cts = new CancellationTokenSource();
        _running = true;
        btnRun.IsEnabled = false; btnCancel.IsEnabled = true;
        progRow.Visibility = Visibility.Visible; bar.Value = 0;
        tglLog.IsChecked = true;
        txtLog.Clear();
        lblProg.Text = $"Procesando {sel.Count} vídeo(s)…";

        var reporter = new Reporter(this);
        try
        {
            var results = await _engine.CompressAsync(sel, opt, reporter, _cts.Token);
            var ok = results.Where(r => r.OutBytes != null).ToList();
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
            _running = false;
            btnRun.IsEnabled = true; btnCancel.IsEnabled = false;
            progRow.Visibility = Visibility.Collapsed;
            _cts?.Dispose(); _cts = null;
        }
    }

    private void AppendLog(string line)
    {
        txtLog.AppendText(line + "\n");
        txtLog.ScrollToEnd();
    }

    // reporter que marshaliza el avance del motor al hilo de la UI
    private sealed class Reporter : IEngineReporter
    {
        private readonly MainWindow _w;
        public Reporter(MainWindow w) => _w = w;
        public void Log(string line) => _w.Dispatcher.BeginInvoke(() => _w.AppendLog(line));
        public void FileStart(int i, int t, string name, double dur) =>
            _w.Dispatcher.BeginInvoke(() => { _w.lblProg.Text = $"[{i}/{t}] {name}"; _w.bar.Value = 0; });
        public void FileProgress(double frac, string raw) =>
            _w.Dispatcher.BeginInvoke(() => _w.bar.Value = frac);
        public void FileDone(FileResult r) { }
    }

    // ---------- actualizaciones ----------
    private async Task CheckUpdateAsync(bool manual)
    {
        if (manual) lblProg.Text = "Buscando actualizaciones…";
        var info = await Updater.CheckAsync();
        if (info == null)
        {
            if (manual) lblProg.Text = $"Ya tienes la última versión (v{Updater.Current}).";
            return;
        }
        lblUpdate.Text = $"Versión nueva disponible: {info.Tag}  (tienes v{Updater.Current}).";
        updateBar.Visibility = Visibility.Visible;
        btnUpdateNow.Click -= OnUpdateNow;   // evitar suscripción doble
        btnUpdateNow.Click += OnUpdateNow;
        _pendingUpdate = info;
    }

    private UpdateInfo? _pendingUpdate;
    private async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        btnUpdateNow.IsEnabled = false;
        lblUpdate.Text = "Descargando actualización…";
        try
        {
            var progress = new Progress<double>(p => lblUpdate.Text = $"Descargando actualización… {p * 100:n0}%");
            var installer = await Updater.DownloadAsync(_pendingUpdate, progress);
            lblUpdate.Text = "Iniciando el instalador…";
            Updater.LaunchInstallerAndExit(installer);
        }
        catch (Exception ex)
        {
            btnUpdateNow.IsEnabled = true;
            MessageBox.Show(this, "No se pudo descargar la actualización:\n" + ex.Message,
                "Actualizar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- cierre ----------
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_running)
        {
            if (MessageBox.Show(this, "Hay una compresión en curso. ¿Cerrar y cancelarla?",
                    "Salir", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                _cts?.Cancel();
            else { e.Cancel = true; return; }
        }
        base.OnClosing(e);
    }
}
