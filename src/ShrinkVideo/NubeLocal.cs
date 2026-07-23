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

    /// <summary>¿Este fichero está (total o parcialmente) solo en la nube?</summary>
    public static bool EsMarcador(string ruta)
    {
        try { return Nube.EsMarcador(File.GetAttributes(ruta)); }
        catch { return false; }
    }

    /// <summary>
    /// Descarga el fichero ENTERO leyéndolo de punta a punta: con «Archivos a petición»,
    /// leer un marcador obliga al proveedor a traerlo. Da el progreso en 0..1 y se puede
    /// cancelar — el trozo ya bajado se queda, no se pierde nada.
    ///
    /// Existe porque trabajar sobre un marcador a medias es la fuente de dos males: las
    /// miniaturas y la codificación van a velocidad de red (la app parece ahogada), y las
    /// comprobaciones de «¿está libre?» tropiezan con el propio motor de sincronización.
    /// Mejor pagar la descarga UNA vez, al principio y con una barra honesta.
    /// </summary>
    public static async Task DescargarAsync(string ruta, IProgress<double>? progreso,
                                            CancellationToken ct)
    {
        long total = new FileInfo(ruta).Length;
        await Task.Run(() =>
        {
            using var s = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read,
                                         1 << 20, FileOptions.SequentialScan);
            var buf = new byte[1 << 20];
            long leido = 0;
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                leido += n;
                progreso?.Report(total > 0 ? (double)leido / total : 0);
            }
        }, ct);
    }
}
