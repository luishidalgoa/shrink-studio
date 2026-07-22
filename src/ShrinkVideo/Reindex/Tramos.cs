namespace ShrinkVideo.Reindex;

/// <summary>
/// Un trozo del vídeo original, en segundos. Guarda SU sitio en el original: al quitar un
/// tramo los demás no se recolocan, porque de ahí es de donde se va a extraer.
/// </summary>
public sealed record Tramo(double Inicio, double Fin, string Nombre = "")
{
    public double Duracion => Fin - Inicio;
}

/// <summary>Cuál de los dos extremos de un tramo se está agarrando.</summary>
public enum Extremo { Inicio, Fin }

/// <summary>
/// El modelo de «Recortes», entero: una lista de tramos. Se arranca con el vídeo completo y
/// cada corte parte en dos el tramo donde cae; quitar un trozo es borrar su tramo.
///
/// Con un solo concepto salen los dos usos: partir un capítulo doble en dos episodios, y
/// recortarle un cacho a un vídeo. Dos modos distintos serían dos cosas que mantener.
/// </summary>
public static class Tramos
{
    /// <summary>Margen para no crear tramos de duración ~cero, que dan ficheros vacíos.</summary>
    private const double Minimo = 0.05;

    /// <summary>Dos bordes en el mismo instante son la misma junta, sin fiarse del último bit.</summary>
    private static bool MismoSitio(double a, double b) => Math.Abs(a - b) < 1e-6;

    public static List<Tramo> Entero(double duracion) => new() { new Tramo(0, duracion) };

    /// <summary>
    /// Parte en dos el tramo que contiene <paramref name="segundo"/>. Si el corte cae en una
    /// junta, en un extremo o fuera, no hace nada: devolver un tramo vacío sería peor que
    /// ignorar un corte que no significa nada.
    /// </summary>
    public static List<Tramo> Partir(IReadOnlyList<Tramo> tramos, double segundo)
    {
        var fuera = new List<Tramo>();
        bool partido = false;
        foreach (var t in tramos)
        {
            if (!partido && segundo > t.Inicio + Minimo && segundo < t.Fin - Minimo)
            {
                fuera.Add(t with { Fin = segundo });
                fuera.Add(t with { Inicio = segundo });
                partido = true;
            }
            else fuera.Add(t);
        }
        return fuera;
    }

    /// <summary>
    /// Arrastra un extremo de un tramo hasta <paramref name="segundo"/>. Es la otra mitad de
    /// «cortar»: se parte a ojo y luego se afina tirando del tirador.
    ///
    /// Si el tramo de al lado empieza justo donde este acaba, los dos bordes son UNA junta y
    /// se mueven juntos: separarlos abriría en medio del vídeo un agujero que nadie ha
    /// pedido. Si hay hueco (alguien quitó un tramo), son dos bordes independientes y cada
    /// uno se estira por su lado hasta topar con el otro.
    ///
    /// El punto queda siempre encerrado entre lo que hay a los lados, así que ningún tramo
    /// se da la vuelta ni se queda a cero. Un tramo invertido o vacío ffmpeg lo acepta sin
    /// rechistar y saca un fichero basura del que nadie se entera hasta abrirlo.
    /// </summary>
    public static List<Tramo> MoverJunta(
        IReadOnlyList<Tramo> tramos, int indice, Extremo extremo, double segundo, double duracion)
    {
        var fuera = new List<Tramo>(tramos);
        if (indice < 0 || indice >= fuera.Count) return fuera;

        var yo = fuera[indice];
        int vecino = extremo == Extremo.Fin ? indice + 1 : indice - 1;
        bool hayVecino = vecino >= 0 && vecino < fuera.Count;
        // ¿Comparten instante? Entonces es una sola junta y el vecino viene detrás.
        bool pegados = hayVecino && MismoSitio(
            extremo == Extremo.Fin ? fuera[vecino].Inicio : fuera[vecino].Fin,
            extremo == Extremo.Fin ? yo.Fin : yo.Inicio);

        double minimo, maximo;
        if (extremo == Extremo.Fin)
        {
            minimo = yo.Inicio + Minimo;
            maximo = !hayVecino ? duracion
                   : pegados ? fuera[vecino].Fin - Minimo
                   : fuera[vecino].Inicio;
        }
        else
        {
            maximo = yo.Fin - Minimo;
            minimo = !hayVecino ? 0
                   : pegados ? fuera[vecino].Inicio + Minimo
                   : fuera[vecino].Fin;
        }

        // Sin sitio donde ponerla (dos tramos ya minúsculos) es mejor no tocar nada que
        // recolocar a la fuerza: Math.Clamp además revienta si el mínimo pasa al máximo.
        if (maximo < minimo) return fuera;
        double donde = Math.Clamp(segundo, minimo, maximo);

        if (extremo == Extremo.Fin)
        {
            fuera[indice] = yo with { Fin = donde };
            if (pegados) fuera[vecino] = fuera[vecino] with { Inicio = donde };
        }
        else
        {
            fuera[indice] = yo with { Inicio = donde };
            if (pegados) fuera[vecino] = fuera[vecino] with { Fin = donde };
        }
        return fuera;
    }

    public static List<Tramo> Quitar(IReadOnlyList<Tramo> tramos, int indice)
    {
        var fuera = new List<Tramo>(tramos);
        if (indice >= 0 && indice < fuera.Count) fuera.RemoveAt(indice);
        return fuera;
    }

    /// <summary>
    /// Cómo se le pide a ffmpeg un tramo. El salto va ANTES del «-i»: así busca por índice
    /// en vez de decodificar desde el principio, que en un vídeo largo es la diferencia
    /// entre empezar al instante o tras un buen rato. La duración va después, con «-t»,
    /// que siempre se mide sobre la salida y no depende de dónde se puso el salto.
    ///
    /// Los números salen SIEMPRE con punto decimal: en una máquina en español «10,5» se
    /// escribiría con coma y ffmpeg leería otra cosa. Es un fallo que no daría la cara en
    /// una máquina en inglés.
    /// </summary>
    public static (string[] Antes, string[] Despues) ArgsFfmpeg(double? desde, double? duracion)
    {
        static string N(double v) =>
            v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        var antes = desde is > 0 ? new[] { "-ss", N(desde.Value) } : Array.Empty<string>();
        var despues = duracion is > 0 ? new[] { "-t", N(duracion.Value) } : Array.Empty<string>();
        return (antes, despues);
    }

    /// <summary>
    /// Nombres por defecto. Si el nombre del fichero trae tantas historias como tramos hay,
    /// cada tramo se queda con la suya — que es exactamente el caso de partir un capítulo
    /// doble. Si no cuadra no se inventa nada: se numera.
    /// </summary>
    public static string[] Nombrar(string nombreOriginal, int cuantos)
    {
        if (cuantos <= 1) return new[] { nombreOriginal };

        var historias = nombreOriginal
            .Split(new[] { '┃', '|', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(h => h.Length > 0)
            .ToArray();

        if (historias.Length == cuantos) return historias;

        return Enumerable.Range(1, cuantos).Select(i => $"{nombreOriginal} - {i}").ToArray();
    }
}
