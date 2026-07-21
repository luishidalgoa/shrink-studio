using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ShrinkVideo;

/// <summary>Una fila de la vista previa: nombre original → nombre resultante.</summary>
public sealed class RenamePreviewRow
{
    public string Original { get; init; } = "";
    public string Nuevo { get; init; } = "";
    public Brush Color { get; init; } = Brushes.Gray;
}

/// <summary>
/// Diálogo de renombrado al estilo PowerRename, con vista previa en vivo.
/// La regla se aplica al nombre del archivo de salida de cada vídeo al procesarlo.
/// </summary>
public partial class RenameWindow : Window
{
    private readonly IReadOnlyList<(string name, DateTime created)> _items;
    private readonly ObservableCollection<RenamePreviewRow> _preview = new();
    private bool _loaded;

    /// <summary>Regla resultante tras pulsar Aplicar/Quitar (null si se cancela).</summary>
    public RenameRule? Result { get; private set; }

    public RenameWindow(RenameRule current, IReadOnlyList<(string name, DateTime created)> items)
    {
        InitializeComponent();
        _items = items;
        lstPrev.ItemsSource = _preview;

        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        btnX.Click += (_, _) => Close();
        btnCancel.Click += (_, _) => Close();
        btnApply.Click += (_, _) => { Result = Build(enabled: true); DialogResult = true; };
        btnClear.Click += (_, _) => { Result = new RenameRule(); DialogResult = true; };

        // cargar la regla actual
        txtSearch.Text = current.Search;
        txtReplace.Text = current.Replace;
        chkRegex.IsChecked = current.UseRegex;
        chkCase.IsChecked = current.CaseSensitive;
        chkAll.IsChecked = current.MatchAll;
        chkEnum.IsChecked = current.Enumerate;
        chkRand.IsChecked = current.RandomStrings;
        cboTarget.SelectedIndex = (int)current.Target;
        cboCase.SelectedIndex = (int)current.Case;

        // vista previa en vivo
        txtSearch.TextChanged += (_, _) => Refresh();
        txtReplace.TextChanged += (_, _) => Refresh();
        foreach (var c in new[] { chkRegex, chkCase, chkAll, chkEnum, chkRand })
        {
            c.Checked += (_, _) => Refresh();
            c.Unchecked += (_, _) => Refresh();
        }
        cboTarget.SelectionChanged += (_, _) => Refresh();
        cboCase.SelectionChanged += (_, _) => Refresh();

        _loaded = true;
        Refresh();
    }

    private RenameRule Build(bool enabled) => new()
    {
        Enabled = enabled,
        Search = txtSearch.Text,
        Replace = txtReplace.Text,
        UseRegex = chkRegex.IsChecked == true,
        CaseSensitive = chkCase.IsChecked == true,
        MatchAll = chkAll.IsChecked == true,
        Enumerate = chkEnum.IsChecked == true,
        RandomStrings = chkRand.IsChecked == true,
        Target = (RenameTarget)Math.Max(0, cboTarget.SelectedIndex),
        Case = (TextCase)Math.Max(0, cboCase.SelectedIndex),
    };

    private void Refresh()
    {
        if (!_loaded) return;
        var rule = Build(enabled: true);
        lblExtWarn.Visibility = rule.Target == RenameTarget.NameOnly ? Visibility.Collapsed : Visibility.Visible;

        var changed = (Brush)FindResource("Accent300");
        var same = (Brush)FindResource("Neutral700");

        _preview.Clear();
        int counter = 0, n = 0;
        foreach (var (name, created) in _items)
        {
            // ${rstring…}/${ruuidv4} cambian en cada evaluación: la vista previa es orientativa
            var nuevo = rule.Apply(name, counter, created);
            if (nuevo != null) { counter++; n++; }
            _preview.Add(new RenamePreviewRow
            {
                Original = name,
                Nuevo = nuevo ?? "— sin cambio —",
                Color = nuevo != null ? changed : same,
            });
        }
        lblCount.Text = _items.Count == 0
            ? "no hay vídeos seleccionados"
            : $"{n} de {_items.Count} se renombran";
    }
}
