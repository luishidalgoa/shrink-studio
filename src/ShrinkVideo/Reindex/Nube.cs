using System.IO;

namespace ShrinkVideo.Reindex;

/// <summary>
/// Reconoce los ficheros que están en el disco solo de nombre, y sabe devolverlos a su sitio.
///
/// Con la sincronización «bajo demanda» lo que hay en la carpeta puede ser un MARCADOR:
/// ocupa cero y el contenido vive en el servidor. Leer un solo byte dispara la descarga del
/// fichero ENTERO, de forma síncrona y silenciosa.
///
/// Nada de esto es de un proveedor concreto: lo define Windows (Cloud Files API) y lo usan
/// igual OneDrive, Nextcloud, Dropbox, Google Drive o iCloud. Por eso aquí no se nombra a
/// ninguno — se miran los atributos del fichero, que son los mismos para todos.
///
/// Medido sobre una biblioteca real: abrir con ffprobe un marcador de 277 MB se lo bajó
/// completo en 18 segundos.
/// </summary>
public static class Nube
{
    /// <summary>
    /// FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS. No está en el enum de .NET: es la marca
    /// moderna de «bajo demanda» (la clásica es OFFLINE, y conviven).
    /// </summary>
    public const int RecallOnDataAccess = 0x400000;

    /// <summary>FILE_ATTRIBUTE_PINNED: «mantener siempre en este dispositivo».</summary>
    public const int Anclado = 0x00080000;

    /// <summary>FILE_ATTRIBUTE_UNPINNED: «liberar espacio», devuélvelo a la nube.</summary>
    public const int Soltado = 0x00100000;

    public static bool EsMarcador(FileAttributes atributos) =>
        atributos.HasFlag(FileAttributes.Offline) ||
        ((int)atributos & RecallOnDataAccess) != 0;

    /// <summary>Igual, a partir de la ruta. Si no se puede leer, se asume que no lo es.</summary>
    public static bool EsMarcador(string ruta)
    {
        try { return EsMarcador(File.GetAttributes(ruta)); }
        catch { return false; }
    }

    /// <summary>
    /// Los atributos que dejan pedido «vuelve a la nube»: fuera ANCLADO, dentro SOLTADO, y
    /// el resto tal cual estaba. Es exactamente lo que hace `attrib +U -P`, que es la forma
    /// documentada de liberar espacio sin borrar nada.
    /// </summary>
    public static int AtributosParaLiberar(int actuales) => (actuales & ~Anclado) | Soltado;
}
