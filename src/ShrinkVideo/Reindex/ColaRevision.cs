using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShrinkVideo.Reindex;

/// <summary>Un fichero apartado para mirarlo con calma.</summary>
public sealed class ApartadoParaRevisar
{
    [JsonPropertyName("ruta")] public string Ruta { get; set; } = "";
    /// <summary>Por qué se apartó, en palabras de quien lo apartó. Puede ir vacío.</summary>
    [JsonPropertyName("nota")] public string Nota { get; set; } = "";
    [JsonPropertyName("fecha")] public string Fecha { get; set; } = "";
}

/// <summary>
/// La cola de revisión: ficheros que hay que arreglar y no da tiempo hoy.
///
/// Existe por una razón concreta: encontrar un fichero problemático cuesta más que
/// arreglarlo. Sin un sitio donde apartarlo, cerrar la app significa volver a buscarlo
/// mañana entre cientos.
///
/// Va por RUTA, y sabe seguir al fichero cuando se renombra — que es lo que esta app hace
/// todo el rato. Si guardara la ruta y nada más, el primer renombrado perdería la marca justo
/// del fichero que estabas arreglando.
///
/// Clase pura: no toca el disco. Serializa a texto y quien llama decide dónde ponerlo.
/// </summary>
public sealed class ColaRevision
{
    internal sealed class Archivo
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("apartados")] public List<ApartadoParaRevisar> Apartados { get; set; } = new();
    }

    private readonly Dictionary<string, ApartadoParaRevisar> _por =
        new(StringComparer.OrdinalIgnoreCase);

    public int Cuantos => _por.Count;

    /// <summary>En el orden en que se apartaron, que es el orden en que se van a mirar.</summary>
    public IReadOnlyList<ApartadoParaRevisar> Todos => _por.Values.ToList();

    public bool Tiene(string ruta) => _por.ContainsKey(ruta);

    public string Nota(string ruta) => _por.TryGetValue(ruta, out var a) ? a.Nota : "";

    /// <summary>Aparta un fichero. Marcarlo dos veces no lo duplica: actualiza la nota.</summary>
    public void Meter(string ruta, string nota = "", string? fecha = null)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return;
        if (_por.TryGetValue(ruta, out var ya)) { ya.Nota = nota; return; }
        _por[ruta] = new ApartadoParaRevisar
        {
            Ruta = ruta,
            Nota = nota,
            Fecha = fecha ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };
    }

    public void Sacar(string ruta) => _por.Remove(ruta);

    /// <summary>
    /// El fichero apartado ha cambiado de nombre: la marca se va con él. Sin esto, aplicar
    /// un renombrado borraría de la cola justo lo que estabas arreglando.
    /// </summary>
    public void Renombrado(string antes, string despues)
    {
        if (!_por.TryGetValue(antes, out var a)) return;
        _por.Remove(antes);
        a.Ruta = despues;
        _por[despues] = a;
    }

    // Por contexto generado, no por reflexión: la CLI se publica recortada y el recortador
    // se lleva los tipos que solo se alcanzan por reflexión — la cola volvería vacía y sin
    // decir nada.
    public string Escribir() =>
        JsonSerializer.Serialize(new Archivo { Apartados = _por.Values.ToList() },
                                 ColaRevisionJson.Default.Archivo);

    /// <summary>Lee una cola. Un fichero roto devuelve una cola vacía: no impide trabajar.</summary>
    public static ColaRevision Leer(string? json)
    {
        var cola = new ColaRevision();
        if (string.IsNullOrWhiteSpace(json)) return cola;
        try
        {
            var doc = JsonSerializer.Deserialize(json, ColaRevisionJson.Default.Archivo);
            foreach (var a in doc?.Apartados ?? new())
                if (!string.IsNullOrWhiteSpace(a.Ruta))
                    cola._por[a.Ruta] = a;
        }
        catch { /* una cola corrupta no debe impedir trabajar */ }
        return cola;
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true,
    UseStringEnumConverter = false)]
[JsonSerializable(typeof(ColaRevision.Archivo))]
internal partial class ColaRevisionJson : JsonSerializerContext { }
