using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ShrinkVideo;

/// <summary>Una entrada del desplegable de autocompletado.</summary>
public sealed class SuggestionItem
{
    public string Text { get; init; } = "";     // lo que se inserta
    public string Desc { get; init; } = "";     // explicación breve
    /// <summary>Opción que hay que activar para que esto funcione: "regex", "enum" o "rand".</summary>
    public string? Enables { get; init; }
}

/// <summary>Catálogos de sugerencias para los campos Buscar y Reemplazar por.</summary>
public static class Suggestions
{
    /// <summary>Patrones frecuentes para el campo «Buscar» (todos son regex).</summary>
    public static readonly IReadOnlyList<SuggestionItem> Search = new List<SuggestionItem>
    {
        new() { Text = "^",                          Desc = "principio del nombre (para anteponer texto)", Enables = "regex" },
        new() { Text = "$",                          Desc = "final del nombre (para añadir texto al final)", Enables = "regex" },
        new() { Text = ".*",                         Desc = "todo el texto del nombre", Enables = "regex" },
        new() { Text = "(.*)",                       Desc = "captura el nombre entero como grupo $1", Enables = "regex" },
        new() { Text = "^.*$",                       Desc = "el nombre completo (para sustituirlo del todo)", Enables = "regex" },
        new() { Text = @"\d+",                       Desc = "uno o más dígitos", Enables = "regex" },
        new() { Text = @"\s+",                       Desc = "uno o más espacios", Enables = "regex" },
        new() { Text = "[._-]+",                     Desc = "separadores: punto, guion bajo o guion", Enables = "regex" },
        new() { Text = @"\[.*?\]",                   Desc = "texto entre corchetes, p. ej. [1080p]", Enables = "regex" },
        new() { Text = @"\(.*?\)",                   Desc = "texto entre paréntesis, p. ej. (2019)", Enables = "regex" },
        new() { Text = "^.{3}",                      Desc = "los 3 primeros caracteres (para recortarlos)", Enables = "regex" },
        new() { Text = ".{3}$",                      Desc = "los 3 últimos caracteres (para recortarlos)", Enables = "regex" },
        new() { Text = "^foo",                       Desc = "lo que empieza por «foo»", Enables = "regex" },
        new() { Text = "bar$",                       Desc = "lo que termina en «bar»", Enables = "regex" },
        new() { Text = "^foo.*bar$",                 Desc = "empieza por «foo» y acaba en «bar»", Enables = "regex" },
        new() { Text = ".+?(?=bar)",                 Desc = "todo lo anterior a «bar»", Enables = "regex" },
        new() { Text = @"foo[\s\S]*bar",             Desc = "todo entre «foo» y «bar», incluidos", Enables = "regex" },
        new() { Text = @"(\d{2})-(\d{2})-(\d{4})",   Desc = "fecha dd-mm-aaaa en 3 grupos ($1 $2 $3)", Enables = "regex" },
        new() { Text = @"[Ss](\d{1,2})[Ee](\d{1,2})", Desc = "temporada y episodio: S01E02 → $1 y $2", Enables = "regex" },
        new() { Text = "1080p|720p|480p|2160p|4K",   Desc = "marcas de resolución en el nombre", Enables = "regex" },
        new() { Text = "x264|x265|HEVC|WEB-DL|BluRay", Desc = "marcas de codec/fuente en el nombre", Enables = "regex" },
    };

    /// <summary>Variables disponibles para el campo «Reemplazar por».</summary>
    public static readonly IReadOnlyList<SuggestionItem> Replace = new List<SuggestionItem>
    {
        // grupos de captura
        new() { Text = "$1", Desc = "primer grupo de captura de la búsqueda", Enables = "regex" },
        new() { Text = "$2", Desc = "segundo grupo de captura", Enables = "regex" },
        new() { Text = "$3", Desc = "tercer grupo de captura", Enables = "regex" },
        new() { Text = "$$", Desc = "un símbolo $ literal" },
        // contadores
        new() { Text = "${}",                                  Desc = "contador simple: 0, 1, 2…", Enables = "enum" },
        new() { Text = "${start=1}",                           Desc = "contador que empieza en 1", Enables = "enum" },
        new() { Text = "${padding=2;start=1}",                 Desc = "contador 01, 02, 03…", Enables = "enum" },
        new() { Text = "${padding=3;start=1}",                 Desc = "contador 001, 002, 003…", Enables = "enum" },
        new() { Text = "${increment=2}",                       Desc = "contador de 2 en 2", Enables = "enum" },
        new() { Text = "${padding=4;increment=2;start=10}",    Desc = "combinado: 0010, 0012, 0014…", Enables = "enum" },
        // fecha del archivo original
        new() { Text = "$YYYY", Desc = "año con 4 dígitos (2026)" },
        new() { Text = "$YY",   Desc = "año con 2 dígitos (26)" },
        new() { Text = "$Y",    Desc = "último dígito del año" },
        new() { Text = "$MMMM", Desc = "nombre del mes (julio)" },
        new() { Text = "$MMM",  Desc = "mes abreviado (jul)" },
        new() { Text = "$MM",   Desc = "mes con cero delante (07)" },
        new() { Text = "$M",    Desc = "mes sin cero (7)" },
        new() { Text = "$DDDD", Desc = "día de la semana (martes)" },
        new() { Text = "$DDD",  Desc = "día de la semana abreviado (mar)" },
        new() { Text = "$DD",   Desc = "día con cero delante (05)" },
        new() { Text = "$D",    Desc = "día sin cero (5)" },
        new() { Text = "$hh",   Desc = "hora con cero delante" },
        new() { Text = "$h",    Desc = "hora sin cero" },
        new() { Text = "$mm",   Desc = "minutos con cero delante" },
        new() { Text = "$m",    Desc = "minutos sin cero" },
        new() { Text = "$ss",   Desc = "segundos con cero delante" },
        new() { Text = "$s",    Desc = "segundos sin cero" },
        new() { Text = "$fff",  Desc = "milisegundos (3 dígitos)" },
        new() { Text = "$ff",   Desc = "milisegundos (2 dígitos)" },
        new() { Text = "$f",    Desc = "milisegundos (1 dígito)" },
        // aleatorios
        new() { Text = "${rstringalnum=8}", Desc = "8 caracteres aleatorios (letras y dígitos)", Enables = "rand" },
        new() { Text = "${rstringalpha=8}", Desc = "8 letras aleatorias", Enables = "rand" },
        new() { Text = "${rstringdigit=6}", Desc = "6 dígitos aleatorios", Enables = "rand" },
        new() { Text = "${ruuidv4}",        Desc = "identificador único (UUID v4)", Enables = "rand" },
    };
}

/// <summary>
/// Desplegable de autocompletado reactivo para un TextBox: se abre al enfocar/pulsar,
/// filtra según se escribe y permite elegir con ratón o teclado (↑ ↓ Entrar Esc).
/// </summary>
internal sealed class SuggestionBox
{
    private readonly TextBox _box;
    private readonly Popup _pop;
    private readonly ListBox _list;
    private readonly IReadOnlyList<SuggestionItem> _catalog;
    private readonly Func<IEnumerable<string>> _history;
    private readonly Action<SuggestionItem>? _onAccept;
    private bool _suppress;

    public SuggestionBox(TextBox box, Popup pop, ListBox list,
                         IReadOnlyList<SuggestionItem> catalog,
                         Func<IEnumerable<string>> history,
                         Action<SuggestionItem>? onAccept = null)
    {
        _box = box; _pop = pop; _list = list; _catalog = catalog; _history = history; _onAccept = onAccept;

        _box.GotKeyboardFocus += (_, _) => Show();
        _box.PreviewMouseLeftButtonUp += (_, _) => Show();
        _box.TextChanged += (_, _) => { if (!_suppress) Show(); };
        _box.LostKeyboardFocus += (_, _) => { if (!_pop.IsMouseOver) Hide(); };
        _box.PreviewKeyDown += OnKeyDown;

        // clic en un elemento: lo aceptamos antes de que el foco se mueva
        _list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (ItemUnder(e.OriginalSource as DependencyObject) is { } it) { Accept(it); e.Handled = true; }
        };
    }

    private static SuggestionItem? ItemUnder(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is ListBoxItem { DataContext: SuggestionItem it }) return it;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _pop.IsOpen) { Hide(); e.Handled = true; return; }

        if (e.Key == Key.Down)
        {
            if (!_pop.IsOpen) { Show(); e.Handled = true; return; }
            Move(1); e.Handled = true; return;
        }
        if (e.Key == Key.Up && _pop.IsOpen) { Move(-1); e.Handled = true; return; }

        if ((e.Key == Key.Enter || e.Key == Key.Tab) && _pop.IsOpen && _list.SelectedItem is SuggestionItem it)
        {
            Accept(it); e.Handled = true;
        }
    }

    private void Move(int delta)
    {
        if (_list.Items.Count == 0) return;
        int i = _list.SelectedIndex + delta;
        _list.SelectedIndex = Math.Clamp(i, 0, _list.Items.Count - 1);
        _list.ScrollIntoView(_list.SelectedItem);
    }

    /// <summary>El "trozo" que se está escribiendo: desde el último $ si lo hay, o todo hasta el cursor.</summary>
    private (int start, string token) CurrentToken()
    {
        string t = _box.Text ?? "";
        int caret = Math.Clamp(_box.CaretIndex, 0, t.Length);
        if (caret > 0)
        {
            int dollar = t.LastIndexOf('$', caret - 1);
            if (dollar >= 0)
            {
                string seg = t[dollar..caret];
                if (!seg.Any(char.IsWhiteSpace)) return (dollar, seg);
            }
        }
        return (0, t[..caret]);
    }

    private void Show()
    {
        var (_, token) = CurrentToken();
        var items = Filter(_catalog, _history(), token);
        _list.ItemsSource = items;
        if (items.Count == 0) { Hide(); return; }
        _list.SelectedIndex = 0;
        _pop.IsOpen = true;
    }

    /// <summary>
    /// Filtra catálogo + historial según lo escrito. Si el token empieza por '$' se
    /// filtra por prefijo (estás escribiendo una variable); si no, por contenido en
    /// el texto o en la descripción. Vacío = todo. Lógica pura, testeable.
    /// </summary>
    internal static List<SuggestionItem> Filter(
        IReadOnlyList<SuggestionItem> catalog, IEnumerable<string> history, string token)
    {
        bool isVar = token.StartsWith('$');
        var hist = history.Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => new SuggestionItem { Text = h, Desc = "usado recientemente" });

        var all = hist.Concat(catalog);
        var filtered = token.Length == 0
            ? all
            : all.Where(i => isVar
                ? i.Text.StartsWith(token, StringComparison.OrdinalIgnoreCase)
                : i.Text.Contains(token, StringComparison.OrdinalIgnoreCase)
                  || i.Desc.Contains(token, StringComparison.OrdinalIgnoreCase));

        return filtered.Take(60).ToList();
    }

    private void Hide() => _pop.IsOpen = false;

    private void Accept(SuggestionItem it)
    {
        var (start, token) = CurrentToken();
        string t = _box.Text ?? "";
        _suppress = true;
        _box.Text = t.Remove(start, token.Length).Insert(start, it.Text);
        _box.CaretIndex = start + it.Text.Length;
        _suppress = false;
        Hide();
        _box.Focus();
        _onAccept?.Invoke(it);
    }
}
