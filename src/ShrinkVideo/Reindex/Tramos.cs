namespace ShrinkVideo.Reindex;

/// <summary>
/// Un trozo del vídeo original, en segundos. Guarda SU sitio en el original: al quitar un
/// tramo los demás no se recolocan, porque de ahí es de donde se va a extraer.
/// </summary>
public sealed record Tramo(double Inicio, double Fin, string Nombre = "")
{
    public double Duracion => Fin - Inicio;
}

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

    public static List<Tramo> Quitar(IReadOnlyList<Tramo> tramos, int indice)
    {
        var fuera = new List<Tramo>(tramos);
        if (indice >= 0 && indice < fuera.Count) fuera.RemoveAt(indice);
        return fuera;
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
