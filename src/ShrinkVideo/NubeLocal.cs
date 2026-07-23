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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode,
               EntryPoint = "GetCompressedFileSizeW")]
    private static extern uint TamañoEnDisco(string ruta, out uint altos);

    /// <summary>
    /// Bytes que hay YA en disco de un fichero. Para un marcador de nube a medio hidratar
    /// esto crece según baja el contenido — es lo que enseña «tamaño en disco» del Explorador
    /// —, mientras que el tamaño lógico es siempre el total. Devuelve -1 si no se puede saber.
    /// </summary>
    private static long BytesEnDisco(string ruta)
    {
        try
        {
            uint bajos = TamañoEnDisco(ruta, out uint altos);
            if (bajos == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0) return -1;
            return ((long)altos << 32) | bajos;
        }
        catch { return -1; }
    }

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

        // La lectura de punta a punta es lo que OBLIGA al proveedor a traer el fichero, pero
        // OneDrive suele hidratarlo entero en el primer Read: contar bytes leídos deja la
        // barra clavada en 0 y luego la salta a 100. El progreso REAL sale de los bytes que
        // ya hay en disco, que se sondean en paralelo mientras la lectura está bloqueada.
        var lectura = Task.Run(() =>
        {
            using var s = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read,
                                         1 << 20, FileOptions.SequentialScan);
            var buf = new byte[1 << 20];
            while (s.Read(buf, 0, buf.Length) > 0) ct.ThrowIfCancellationRequested();
        }, ct);

        while (!lectura.IsCompleted)
        {
            var cumplida = await Task.WhenAny(lectura, Task.Delay(250, CancellationToken.None));
            if (cumplida == lectura) break;
            if (progreso != null && total > 0)
            {
                long enDisco = BytesEnDisco(ruta);
                // Solo se informa si de verdad va por debajo del total: si el sondeo no sirve
                // en esta máquina, la barra se queda como estaba en vez de saltar a 100 en falso.
                if (enDisco > 0 && enDisco < total) progreso.Report((double)enDisco / total);
            }
        }

        await lectura;             // propaga cancelación o error de la lectura
        progreso?.Report(1);       // al terminar, la descarga está completa por definición
    }
}
