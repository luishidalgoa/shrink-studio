using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ShrinkVideo;

/// <summary>A qué parte del nombre se aplica la regla (como en PowerRename).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RenameTarget { NameOnly, ExtensionOnly, NameAndExtension }

/// <summary>Formato del texto resultante (como en PowerRename).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextCase { None, Lower, Upper, Title, CapitalizeEachWord }

/// <summary>
/// Regla de renombrado al estilo PowerRename, aplicada al nombre del archivo de SALIDA
/// de cada vídeo según se va procesando: buscar/reemplazar (texto o regex), formato del
/// texto, enumeración y cadenas aleatorias, más variables de fecha del archivo original.
/// </summary>
public sealed class RenameRule
{
    public bool Enabled { get; set; }
    public string Search { get; set; } = "";
    public string Replace { get; set; } = "";
    public bool UseRegex { get; set; }
    public bool MatchAll { get; set; } = true;
    public bool CaseSensitive { get; set; }
    public RenameTarget Target { get; set; } = RenameTarget.NameOnly;
    public TextCase Case { get; set; } = TextCase.None;
    public bool Enumerate { get; set; }
    public bool RandomStrings { get; set; }

    /// <summary>La regla hace algo: hay algo que buscar, o al menos un cambio de formato.</summary>
    [JsonIgnore]
    public bool HasEffect => Enabled && (Search.Length > 0 || Case != TextCase.None);

    public RenameRule Clone() => (RenameRule)MemberwiseClone();

    /// <summary>
    /// Aplica la regla a un nombre de archivo completo (nombre + extensión).
    /// Devuelve el nombre nuevo, o <c>null</c> si la regla no afecta a este archivo
    /// (sin coincidencia). <paramref name="counterIndex"/> es la posición entre los
    /// archivos que SÍ se renombran (0 para el primero), como el contador de PowerRename.
    /// </summary>
    public string? Apply(string fileName, int counterIndex, DateTime created)
    {
        if (!HasEffect || string.IsNullOrEmpty(fileName)) return null;

        string result;
        if (Target == RenameTarget.NameAndExtension)
        {
            // como PowerRename: el patrón se aplica a la cadena completa "nombre.ext"
            if (Transform(fileName, counterIndex, created) is not { } whole) return null;
            result = whole;
        }
        else
        {
            string namePart = Path.GetFileNameWithoutExtension(fileName);
            string extPart = Path.GetExtension(fileName);        // incluye el punto
            if (Target == RenameTarget.NameOnly)
            {
                if (Transform(namePart, counterIndex, created) is not { } r) return null;
                result = r + extPart;
            }
            else   // solo la extensión (se transforma sin el punto)
            {
                string bare = extPart.StartsWith('.') ? extPart[1..] : extPart;
                if (Transform(bare, counterIndex, created) is not { } r) return null;
                result = namePart + (r.Length == 0 ? "" : "." + r);
            }
        }

        var final = Sanitize(result);
        // no dejamos un archivo sin nombre (p. ej. ".mkv"): en ese caso no se renombra
        if (final.Length == 0 || Path.GetFileNameWithoutExtension(final).Length == 0) return null;
        return final;
    }

    /// <summary>Buscar/reemplazar + formato sobre un trozo del nombre. null = no aplica.</summary>
    private string? Transform(string input, int idx, DateTime created)
    {
        string result = input;

        if (Search.Length > 0)
        {
            var opts = CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            Regex rx;
            try { rx = new Regex(UseRegex ? Search : Regex.Escape(Search), opts); }
            catch (ArgumentException) { return null; }   // patrón inválido: no tocamos nada
            if (!rx.IsMatch(input)) return null;

            string Eval(Match m) => ExpandReplacement(Replace, UseRegex ? m : null, idx, created);
            result = MatchAll ? rx.Replace(input, Eval) : rx.Replace(input, Eval, 1);
        }
        else if (Case == TextCase.None) return null;   // nada que hacer

        return ApplyCase(result);
    }

    // ---------- expansión de variables del campo "Reemplazar por" ----------
    private string ExpandReplacement(string tpl, Match? m, int idx, DateTime d)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < tpl.Length; i++)
        {
            char c = tpl[i];
            if (c != '$') { sb.Append(c); continue; }
            if (i + 1 >= tpl.Length) { sb.Append('$'); break; }

            char n = tpl[i + 1];
            if (n == '$') { sb.Append('$'); i++; continue; }          // $$ = un $ literal

            if (n == '{')                                             // ${...}
            {
                int close = tpl.IndexOf('}', i + 2);
                if (close > 0 && TryExpandBraced(tpl[(i + 2)..close], idx, out var braced))
                {
                    sb.Append(braced); i = close; continue;
                }
                sb.Append('$'); continue;
            }

            if (m != null && char.IsDigit(n))                          // grupos de captura $1..$99
            {
                int g = n - '0', len = 1;
                if (i + 2 < tpl.Length && char.IsDigit(tpl[i + 2]))
                {
                    int g2 = g * 10 + (tpl[i + 2] - '0');
                    if (g2 < m.Groups.Count) { g = g2; len = 2; }
                }
                if (g < m.Groups.Count) { sb.Append(m.Groups[g].Value); i += len; continue; }
            }

            if (MatchDateToken(tpl, i + 1, d) is var (text, len2))     // $YYYY, $MM, $DD…
            {
                sb.Append(text); i += len2; continue;
            }

            sb.Append('$');
        }
        return sb.ToString();
    }

    // Tokens de fecha/hora, del más largo al más corto dentro de cada familia.
    private static readonly (string tok, Func<DateTime, string> fmt)[] DateTokens =
    {
        ("YYYY", d => d.ToString("yyyy", CultureInfo.CurrentCulture)),
        ("YY",   d => d.ToString("yy",   CultureInfo.CurrentCulture)),
        ("Y",    d => (d.Year % 10).ToString(CultureInfo.InvariantCulture)),
        ("MMMM", d => d.ToString("MMMM", CultureInfo.CurrentCulture)),
        ("MMM",  d => d.ToString("MMM",  CultureInfo.CurrentCulture)),
        ("MM",   d => d.ToString("MM",   CultureInfo.InvariantCulture)),
        ("M",    d => d.Month.ToString(CultureInfo.InvariantCulture)),
        ("DDDD", d => d.ToString("dddd", CultureInfo.CurrentCulture)),
        ("DDD",  d => d.ToString("ddd",  CultureInfo.CurrentCulture)),
        ("DD",   d => d.ToString("dd",   CultureInfo.InvariantCulture)),
        ("D",    d => d.Day.ToString(CultureInfo.InvariantCulture)),
        ("hh",   d => d.ToString("HH",   CultureInfo.InvariantCulture)),
        ("h",    d => d.Hour.ToString(CultureInfo.InvariantCulture)),
        ("mm",   d => d.ToString("mm",   CultureInfo.InvariantCulture)),
        ("m",    d => d.Minute.ToString(CultureInfo.InvariantCulture)),
        ("ss",   d => d.ToString("ss",   CultureInfo.InvariantCulture)),
        ("s",    d => d.Second.ToString(CultureInfo.InvariantCulture)),
        ("fff",  d => d.Millisecond.ToString("000", CultureInfo.InvariantCulture)),
        ("ff",   d => (d.Millisecond / 10).ToString("00", CultureInfo.InvariantCulture)),
        ("f",    d => (d.Millisecond / 100).ToString(CultureInfo.InvariantCulture)),
    };

    private static (string text, int len)? MatchDateToken(string tpl, int pos, DateTime d)
    {
        foreach (var (tok, fmt) in DateTokens)
            if (string.CompareOrdinal(tpl, pos, tok, 0, tok.Length) == 0 && pos + tok.Length <= tpl.Length)
                return (fmt(d), tok.Length);
        return null;
    }

    /// <summary>Contadores ${...} y cadenas aleatorias ${rstring…}/${ruuidv4}.</summary>
    private bool TryExpandBraced(string inner, int idx, out string val)
    {
        val = "";
        string s = inner.Trim();

        if (RandomStrings)
        {
            if (s.Equals("ruuidv4", StringComparison.OrdinalIgnoreCase))
            {
                val = Guid.NewGuid().ToString();
                return true;
            }
            var rm = Regex.Match(s, @"^rstring(alnum|alpha|digit)\s*=\s*(\d{1,4})$", RegexOptions.IgnoreCase);
            if (rm.Success)
            {
                val = RandomString(rm.Groups[1].Value.ToLowerInvariant(), int.Parse(rm.Groups[2].Value));
                return true;
            }
        }

        if (!Enumerate) return false;
        if (s.Length == 0) { val = idx.ToString(CultureInfo.InvariantCulture); return true; }

        int start = 0, inc = 1, pad = 0;
        bool any = false;
        foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return false;
            switch (kv[0].Trim().ToLowerInvariant())
            {
                case "start": start = v; any = true; break;
                case "increment": inc = v; any = true; break;
                case "padding": pad = Math.Clamp(v, 0, 20); any = true; break;
                default: return false;
            }
        }
        if (!any) return false;

        long value = start + (long)idx * inc;
        string txt = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        if (pad > 0) txt = txt.PadLeft(pad, '0');
        val = (value < 0 ? "-" : "") + txt;
        return true;
    }

    private const string Alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private static string RandomString(string kind, int len)
    {
        string pool = kind switch { "alpha" => Alpha, "digit" => Digits, _ => Alpha + Digits };
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++) sb.Append(pool[Random.Shared.Next(pool.Length)]);
        return sb.ToString();
    }

    // ---------- formato del texto ----------
    private string ApplyCase(string s) => Case switch
    {
        TextCase.Lower => s.ToLower(CultureInfo.CurrentCulture),
        TextCase.Upper => s.ToUpper(CultureInfo.CurrentCulture),
        TextCase.Title => s.Length == 0 ? s
            : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..].ToLower(CultureInfo.CurrentCulture),
        TextCase.CapitalizeEachWord => CapitalizeWords(s),
        _ => s,
    };

    private static string CapitalizeWords(string s)
    {
        var a = s.ToLower(CultureInfo.CurrentCulture).ToCharArray();
        bool wordStart = true;
        for (int i = 0; i < a.Length; i++)
        {
            if (char.IsLetter(a[i]))
            {
                if (wordStart) { a[i] = char.ToUpper(a[i], CultureInfo.CurrentCulture); wordStart = false; }
            }
            else if (!char.IsDigit(a[i])) wordStart = true;
        }
        return new string(a);
    }

    /// <summary>Deja un nombre de archivo válido en Windows (sin caracteres prohibidos ni puntos/espacios finales).</summary>
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString().TrimEnd(' ', '.');
    }
}
