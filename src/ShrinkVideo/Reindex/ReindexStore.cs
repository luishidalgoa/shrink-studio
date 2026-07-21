using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShrinkVideo.Reindex;

/// <summary>Un catálogo ya importado, con lo justo para pintar su tarjeta sin releer el JSON.</summary>
public sealed class CatalogoGuardado
{
    public required string Ruta { get; init; }
    public required string Serie { get; init; }
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

    private static CatalogoGuardado Describir(string ruta, ReindexCatalog cat) => new()
    {
        Ruta = ruta,
        Serie = cat.Serie,
        Episodios = cat.Episodios.Count,
        Especiales = cat.Especiales.Count,
        ConVariosSegmentos = cat.Episodios.Count(e => e.TitulosVisibles.Count > 1),
        Advertencias = cat.Advertencias,
    };

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
        return Describir(destino, cat);
    }

    public static void EliminarCatalogo(string ruta)
    {
        if (File.Exists(ruta)) File.Delete(ruta);
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
internal partial class ReindexStoreJson : JsonSerializerContext { }
