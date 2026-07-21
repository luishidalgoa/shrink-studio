namespace ShrinkVideo.Reindex;

/// <summary>
/// El buscador del explorador de catálogo: por número o por título.
///
/// Existe para poder VERIFICAR una propuesta sin salir de la app. Antes, dudar de una
/// sugerencia («¿de verdad el planeta espejo es el 175?») obligaba a abrir el JSON a mano
/// y buscar entre cientos de episodios — y esa fricción es exactamente lo que hace que
/// una duda razonable se quede sin comprobar.
/// </summary>
public static class CatalogSearch
{
    /// <summary>
    /// Filtra episodios. Dígitos → por número (exacto primero, luego los que empiezan
    /// igual). Texto → por título, con la misma normalización que usa el identificador:
    /// así «japones» sin tilde o «2.ª parte» encuentran lo mismo que encontraría el motor.
    /// Vacío → el catálogo entero, en su orden.
    /// </summary>
    public static IReadOnlyList<CatalogEpisode> Filtrar(ReindexCatalog cat, string? consulta)
    {
        var q = (consulta ?? "").Trim();
        if (q.Length == 0) return cat.Episodios;

        if (q.All(char.IsDigit))
        {
            var exactos = cat.Episodios.Where(e => e.Num.ToString() == q);
            var empiezan = cat.Episodios.Where(e =>
                e.Num.ToString() != q && e.Num.ToString().StartsWith(q, StringComparison.Ordinal));
            return exactos.Concat(empiezan).ToList();
        }

        var qNorm = TitleMatch.Norm(q);
        if (qNorm.Length == 0) return Array.Empty<CatalogEpisode>();

        return cat.Episodios
            .Where(e => e.TitulosNorm.Any(t => t.Contains(qNorm, StringComparison.Ordinal)))
            .ToList();
    }
}
