// El SDK de consola trae System.IO en los usings implícitos y el de WPF no, así que este
// fichero compila en los tests pero no en la app si no se pone a mano.
using System.IO;

namespace ShrinkVideo.Reindex;

/// <summary>
/// Recorre la carpeta de una serie ENTERA, subcarpetas de temporada incluidas, y reparte
/// los ficheros por la temporada a la que pertenecen.
///
/// Existe porque una biblioteca de verdad no es una carpeta plana: «Doraemon (2005)» son
/// nueve «Season 20xx» y ni un solo vídeo en la raíz. Mirando solo el primer nivel, esa
/// carpeta con cientos de ficheros se veía como «No hay vídeos en esta carpeta», que es
/// justo lo contrario de lo que pasaba.
/// </summary>
public static class LibraryScan
{
    /// <summary>Carpetas cuyo nombre no dice ninguna temporada: van tras las que sí.</summary>
    private const int OrdenSinNumero = int.MaxValue - 1;

    /// <summary>Y los vídeos sueltos de la raíz, los últimos: son la excepción.</summary>
    private const int OrdenRaiz = int.MaxValue;

    public const string EtiquetaRaiz = "Sueltos en la carpeta principal";

    /// <summary>
    /// Los vídeos de <paramref name="raiz"/> y de todo lo que cuelga de ella, ya en el orden
    /// en que se van a leer: temporada por temporada y alfabético dentro de cada una.
    ///
    /// Las extensiones entran por parámetro en vez de leerse de <c>Engine</c> para que este
    /// fichero siga compilando —y testeándose— fuera del proyecto de Windows.
    /// </summary>
    public static string[] Escanear(string raiz, IReadOnlyCollection<string> extensiones)
    {
        if (!Directory.Exists(raiz)) return Array.Empty<string>();

        var opciones = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            // Una subcarpeta sin permisos no puede tumbar el escaneo de la biblioteca entera
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };

        return Directory.EnumerateFiles(raiz, "*", opciones)
            .Where(f => extensiones.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => (Ruta: f, Grupo: Grupo(raiz, f)))
            .OrderBy(x => Orden(x.Grupo))
            .ThenBy(x => x.Grupo, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => Path.GetFileName(x.Ruta), StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Ruta)
            .ToArray();
    }

    /// <summary>
    /// A qué carpeta de temporada pertenece un fichero: el PRIMER tramo bajo la raíz. Se
    /// queda con el primero y no con el padre inmediato para que un «Season 2005/Extras»
    /// siga contando como 2005 en vez de abrir un grupo suelto.
    /// Cadena vacía = está en la propia raíz.
    /// </summary>
    public static string Grupo(string raiz, string ruta)
    {
        var dir = Path.GetDirectoryName(ruta);
        if (string.IsNullOrEmpty(dir)) return "";

        var rel = Path.GetRelativePath(raiz, dir);
        if (rel.Length == 0 || rel == ".") return "";
        if (rel.StartsWith("..", StringComparison.Ordinal)) return "";   // ni siquiera cuelga de la raíz

        return rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
    }

    /// <summary>Cómo se llama el grupo en la tabla.</summary>
    public static string Etiqueta(string grupo) => grupo.Length == 0 ? EtiquetaRaiz : grupo;

    /// <summary>Por dónde va cada grupo al ordenar. Ver las constantes de arriba.</summary>
    public static int Orden(string grupo)
    {
        if (grupo.Length == 0) return OrdenRaiz;
        return SignalExtractor.TemporadaDeCarpeta(grupo) ?? OrdenSinNumero;
    }
}
