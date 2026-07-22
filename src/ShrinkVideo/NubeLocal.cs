using System.IO;
using System.Runtime.InteropServices;
using ShrinkVideo.Reindex;

namespace ShrinkVideo;

/// <summary>
/// La parte de <see cref="Nube"/> que habla con Windows. Vive fuera de la carpeta del motor
/// porque esa se compila también para Linux y macOS (la suite de tests corre ahí).
/// </summary>
public static class NubeLocal
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode,
               EntryPoint = "SetFileAttributesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PonerAtributos(string ruta, int atributos);

    /// <summary>
    /// Pide al motor de sincronización que vuelva a dejar el fichero solo en la nube, como
    /// estaba. Se usa después de mirar un vídeo para identificarlo: verlo medio minuto no
    /// debería dejar 250 MB ocupados para siempre.
    ///
    /// Es una PETICIÓN, no una orden: quien decide cuándo liberar de verdad es el cliente de
    /// sincronización, y puede tardar. Por eso devuelve si se pudo dejar pedido, no si el
    /// fichero ya se ha ido.
    /// </summary>
    public static bool Liberar(string ruta)
    {
        try
        {
            var nuevos = Nube.AtributosParaLiberar((int)File.GetAttributes(ruta));
            return PonerAtributos(ruta, nuevos);
        }
        catch
        {
            // Sin permisos, fichero en uso o un sistema de ficheros que no lo soporta: no
            // pasa nada, simplemente se queda descargado.
            return false;
        }
    }
}
