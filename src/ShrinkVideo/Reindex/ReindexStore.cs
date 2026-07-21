using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShrinkVideo.Reindex;

/// <summary>Un catálogo ya importado, con lo justo para pintar su tarjeta sin releer el JSON.</summary>
public sealed class CatalogoGuardado
{
    public required string Ruta { get; init; }
    public required string Serie { get; init; }

    /// <summary>
    /// Fichero del que se importó, con su ruta completa. Importa enseñarlo: el catálogo que
    /// usa la app es una COPIA, así que si luego editas el original tu copia se queda vieja
    /// y no hay forma de notarlo si no se dice de dónde salió y cuándo.
    /// </summary>
    public string OrigenRuta { get; init; } = "";
    public string Importado { get; init; } = "";

    /// <summary>Solo el nombre del fichero de origen, para la tarjeta.</summary>
    public string Origen => OrigenRuta.Length == 0 ? "" : Path.GetFileName(OrigenRuta);

    /// <summary>«desde entrada.json · 21/07/2026» o vacío si viene de antes de guardarse esto.</summary>
    public string Procedencia => Origen.Length == 0
        ? "origen desconocido"
        : $"desde {Origen}" + (Importado.Length > 0 ? $" · {Importado}" : "");
    public int Episodios { get; init; }
    public int Especiales { get; init; }
    public int ConVariosSegmentos { get; init; }
    public IReadOnlyList<string> Advertencias { get; init; } = Array.Empty<string>();

    /// <summary>«769 episodios · 74 especiales · 464 con varios segmentos»</summary>
    public string Resumen
    {
        get
        {
            var partes = new List<string> { $"{Episodios} episodios" };
            partes.Add(Especiales > 0 ? $"{Especiales} especiales" : "sin especiales catalogados");
            if (ConVariosSegmentos > 0) partes.Add($"{ConVariosSegmentos} con varios segmentos");
            return string.Join(" · ", partes);
        }
    }
}

/// <summary>Un movimiento del diario: de dónde a dónde.</summary>
public sealed class MovimientoJournal
{
    [JsonPropertyName("de")] public string De { get; set; } = "";
    [JsonPropertyName("a")] public string A { get; set; } = "";
}

/// <summary>
/// El diario de un lote aplicado. Se escribe ANTES de tocar nada: si la app se cae a mitad
/// del renombrado, el diario ya está en disco y el «deshacer» sigue siendo posible.
/// </summary>
public sealed class LoteJournal
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("fecha")] public string Fecha { get; set; } = "";
    [JsonPropertyName("hora")] public string Hora { get; set; } = "";
    [JsonPropertyName("serie")] public string Serie { get; set; } = "";
    [JsonPropertyName("carpeta")] public string Carpeta { get; set; } = "";
    [JsonPropertyName("movimientos")] public List<MovimientoJournal> Movimientos { get; set; } = new();

    /// <summary>Texto del botón persistente: «Deshacer lote 14:32 (185)».</summary>
    public string Etiqueta => $"Deshacer lote {Hora} ({Movimientos.Count})";
}

/// <summary>Fichero de la memoria de decisiones.</summary>
public sealed class DecisionesArchivo
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("overrides")] public Dictionary<string, ReindexOverride> Overrides { get; set; } = new();
}

/// <summary>
/// Todo lo que «Organizar» guarda en disco: catálogos importados, memoria de decisiones y
/// diarios de lote. Separado del motor a propósito — el motor no toca disco.
/// </summary>
public static class ReindexStore
{
    /// <summary>Se puede redirigir en los tests para no ensuciar el perfil del usuario.</summary>
    public static string? RaizOverride { get; set; }

    public static string Raiz => RaizOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShrinkStudio");

    public static string DirCatalogos => Path.Combine(Raiz, "catalogos");
    public static string DirLotes => Path.Combine(Raiz, "lotes");
    public static string RutaDecisiones => Path.Combine(Raiz, "decisiones.json");

    /// <summary>De qué fichero salió cada catálogo. Clave = nombre del fichero guardado.</summary>
    public static string RutaProcedencia => Path.Combine(Raiz, "procedencia.json");

    /// <summary>Qué serie estaba elegida al cerrar.</summary>
    public static string RutaPreferencias => Path.Combine(Raiz, "organizar.json");

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ─────────────────────────── catálogos ───────────────────────────

    /// <summary>Los catálogos importados, ya validados. Los ilegibles se omiten en silencio.</summary>
    public static List<CatalogoGuardado> ListarCatalogos()
    {
        var lista = new List<CatalogoGuardado>();
        if (!Directory.Exists(DirCatalogos)) return lista;

        foreach (var ruta in Directory.EnumerateFiles(DirCatalogos, "*.json"))
        {
            try { lista.Add(Describir(ruta, ReindexCatalog.Load(ruta))); }
            catch { /* un catálogo corrupto no puede tumbar la lista entera */ }
        }
        return lista.OrderBy(c => c.Serie, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static CatalogoGuardado Describir(string ruta, ReindexCatalog cat)
    {
        var proc = LeerMapa(RutaProcedencia);
        proc.TryGetValue(Path.GetFileName(ruta), out var origen);
        var trozos = (origen ?? "").Split('|');

        return new CatalogoGuardado
        {
            Ruta = ruta,
            OrigenRuta = trozos.Length > 0 ? trozos[0] : "",
            Importado = trozos.Length > 1 ? trozos[1] : "",
            Serie = cat.Serie,
            Episodios = cat.Episodios.Count,
            Especiales = cat.Especiales.Count,
            ConVariosSegmentos = cat.Episodios.Count(e => e.TitulosSalida.Count > 1),
            Advertencias = cat.Advertencias,
        };
    }

    /// <summary>
    /// Importa un catálogo: lo VALIDA antes de copiarlo, para no dejar basura en la carpeta.
    /// Lanza <see cref="ReindexCatalogException"/> si el JSON no sirve.
    /// </summary>
    public static CatalogoGuardado ImportarCatalogo(string rutaOrigen)
    {
        var cat = ReindexCatalog.Load(rutaOrigen);   // si no vale, aquí revienta y no copiamos nada

        Directory.CreateDirectory(DirCatalogos);
        var destino = Path.Combine(DirCatalogos, NombreSeguro(cat.Serie) + ".reindex.json");
        File.Copy(rutaOrigen, destino, overwrite: true);

        var proc = LeerMapa(RutaProcedencia);
        proc[Path.GetFileName(destino)] = $"{Path.GetFullPath(rutaOrigen)}|{DateTime.Now:dd/MM/yyyy}";
        EscribirMapa(RutaProcedencia, proc);

        return Describir(destino, cat);
    }

    /// <summary>
    /// Quita un catálogo de la app. Solo borra LA COPIA: el JSON del que se importó es del
    /// usuario y no se toca nunca.
    /// </summary>
    /// <returns>false si ya no estaba.</returns>
    public static bool BorrarCatalogo(string ruta)
    {
        if (!File.Exists(ruta)) return false;
        File.Delete(ruta);

        var proc = LeerMapa(RutaProcedencia);
        if (proc.Remove(Path.GetFileName(ruta))) EscribirMapa(RutaProcedencia, proc);

        // Si era el elegido, dejarlo apuntado seria arrancar señalando a algo que ya no esta
        if (string.Equals(CargarUltimoCatalogo(), ruta, StringComparison.OrdinalIgnoreCase))
            GuardarUltimoCatalogo(null);

        return true;
    }

    // ───────────────── la serie elegida sobrevive al cierre ─────────────────

    /// <summary>
    /// La última serie elegida, o null si no hay o si el fichero ya no está. Comprobar que
    /// exista es la mitad del asunto: un puntero a un catálogo borrado a mano dejaría la
    /// página arrancando en un estado imposible.
    /// </summary>
    public static string? CargarUltimoCatalogo()
    {
        var mapa = LeerMapa(RutaPreferencias);
        if (!mapa.TryGetValue("ultimoCatalogo", out var ruta)) return null;
        return !string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta) ? ruta : null;
    }

    public static void GuardarUltimoCatalogo(string? ruta)
    {
        var mapa = LeerMapa(RutaPreferencias);
        if (string.IsNullOrWhiteSpace(ruta)) mapa.Remove("ultimoCatalogo");
        else mapa["ultimoCatalogo"] = ruta;
        EscribirMapa(RutaPreferencias, mapa);
    }

    // ── mapas de texto sueltos (procedencia, preferencias) ──
    // Ficheros minusculos y de forma libre; un fallo de lectura se traga y se sigue: son
    // comodidades, no datos que valga la pena defender con un error en pantalla.

    private static Dictionary<string, string> LeerMapa(string ruta)
    {
        try
        {
            if (!File.Exists(ruta)) return new();
            var json = File.ReadAllText(ruta, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize(json, ReindexStoreJson.Default.DictionaryStringString) ?? new();
        }
        catch { return new(); }
    }

    private static void EscribirMapa(string ruta, Dictionary<string, string> mapa)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.WriteAllText(ruta,
                JsonSerializer.Serialize(mapa, ReindexStoreJson.Default.DictionaryStringString),
                System.Text.Encoding.UTF8);
        }
        catch { /* ídem */ }
    }

    private static string NombreSeguro(string s)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '-');
        return s.Trim().Length == 0 ? "catalogo" : s.Trim();
    }

    // ───────────────────── memoria de decisiones ─────────────────────

    public static Dictionary<string, ReindexOverride> CargarDecisiones()
    {
        try
        {
            if (!File.Exists(RutaDecisiones)) return new();
            var json = File.ReadAllText(RutaDecisiones, System.Text.Encoding.UTF8);
            var doc = JsonSerializer.Deserialize(json, ReindexStoreJson.Default.DecisionesArchivo);
            return doc?.Overrides ?? new();
        }
        catch { return new(); }   // una memoria corrupta no debe impedir trabajar
    }

    public static void GuardarDecisiones(Dictionary<string, ReindexOverride> overrides)
    {
        Directory.CreateDirectory(Raiz);
        var doc = new DecisionesArchivo { Version = 1, Overrides = overrides };
        File.WriteAllText(RutaDecisiones,
            JsonSerializer.Serialize(doc, ReindexStoreJson.Default.DecisionesArchivo), System.Text.Encoding.UTF8);
    }

    public static void OlvidarDecisiones()
    {
        if (File.Exists(RutaDecisiones)) File.Delete(RutaDecisiones);
    }

    // ──────────────────────── diario de lotes ────────────────────────

    /// <summary>
    /// Escribe el diario ANTES de renombrar. Devuelve su ruta, que es lo que hará falta
    /// para deshacer.
    /// </summary>
    public static string EscribirJournal(LoteJournal lote)
    {
        Directory.CreateDirectory(DirLotes);
        var ruta = Path.Combine(DirLotes, $"{lote.Id}.json");
        File.WriteAllText(ruta, JsonSerializer.Serialize(lote, ReindexStoreJson.Default.LoteJournal),
            System.Text.Encoding.UTF8);
        return ruta;
    }

    /// <summary>Los lotes aplicados, del más reciente al más antiguo.</summary>
    public static List<LoteJournal> ListarLotes()
    {
        var lista = new List<LoteJournal>();
        if (!Directory.Exists(DirLotes)) return lista;

        foreach (var ruta in Directory.EnumerateFiles(DirLotes, "*.json"))
        {
            try
            {
                var lote = JsonSerializer.Deserialize(File.ReadAllText(ruta, System.Text.Encoding.UTF8),
                    ReindexStoreJson.Default.LoteJournal);
                if (lote != null) lista.Add(lote);
            }
            catch { /* ídem: un diario roto no esconde a los demás */ }
        }
        return lista.OrderByDescending(l => l.Id, StringComparer.Ordinal).ToList();
    }

    public static LoteJournal? UltimoLote() => ListarLotes().FirstOrDefault();

    /// <summary>
    /// Deshace un lote devolviendo cada fichero a su nombre anterior. Se recorre al revés
    /// por si dos movimientos encadenaron nombres. Devuelve cuántos volvieron y cuántos no
    /// se pudieron (porque el usuario ya los movió, o el nombre viejo está ocupado).
    /// </summary>
    public static (int devueltos, int fallidos) Deshacer(LoteJournal lote)
    {
        int ok = 0, mal = 0;
        for (int i = lote.Movimientos.Count - 1; i >= 0; i--)
        {
            var m = lote.Movimientos[i];
            try
            {
                if (!File.Exists(m.A)) { mal++; continue; }          // ya no está donde lo dejamos
                if (File.Exists(m.De)) { mal++; continue; }          // su nombre viejo está ocupado
                File.Move(m.A, m.De);
                ok++;
            }
            catch { mal++; }
        }
        return (ok, mal);
    }

    public static void OlvidarLote(LoteJournal lote)
    {
        var ruta = Path.Combine(DirLotes, $"{lote.Id}.json");
        if (File.Exists(ruta)) File.Delete(ruta);
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(DecisionesArchivo))]
[JsonSerializable(typeof(LoteJournal))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class ReindexStoreJson : JsonSerializerContext { }
