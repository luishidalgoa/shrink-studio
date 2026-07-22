using System.IO;

namespace ShrinkVideo.Reindex;

/// <summary>
/// Reconoce los ficheros que están en el disco solo de nombre.
///
/// Con «Archivos a petición» (OneDrive, Dropbox, iCloud…) lo que hay en la carpeta puede
/// ser un MARCADOR: ocupa cero y el contenido vive en el servidor. Leer un solo byte
/// dispara la descarga del fichero ENTERO, de forma síncrona y silenciosa.
///
/// Medido sobre la biblioteca real: sondear con ffprobe un marcador de 277 MB se lo bajó
/// completo en 18 segundos. Multiplicado por los ficheros de un lote, identificar una
/// carpeta puede llevarse gigabytes de tarifa y de disco sin haber pedido permiso. Por eso
/// esto se comprueba ANTES de abrir un vídeo para inspeccionarlo.
/// </summary>
public static class Nube
{
    /// <summary>
    /// FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS. No está en el enum de .NET: es la marca
    /// moderna de «Archivos a petición» (la clásica es OFFLINE, y conviven).
    /// </summary>
    public const int RecallOnDataAccess = 0x400000;

    public static bool EsMarcador(FileAttributes atributos) =>
        atributos.HasFlag(FileAttributes.Offline) ||
        ((int)atributos & RecallOnDataAccess) != 0;

    /// <summary>Igual, a partir de la ruta. Si no se puede leer, se asume que no lo es.</summary>
    public static bool EsMarcador(string ruta)
    {
        try { return EsMarcador(File.GetAttributes(ruta)); }
        catch { return false; }
    }
}
