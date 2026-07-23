using ShrinkVideo.Reindex;

namespace ShrinkVideo.Reindex.Tests;

/// <summary>
/// Arnés de tests del motor de reindexado. Sin dependencias externas a propósito: CI lo
/// compila y lo corre sin restaurar paquetes. Devuelve 1 si algo falla.
/// </summary>
public static class Program
{
    private static int _ok, _fallos;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("── Motor de reindexado ─────────────────────────────\n");

        Normalizacion();
        SimilitudContraDifflib();
        ExtraccionDeSeniales();
        CargaDeCatalogo();
        Cascada();
        ReglasDeLote();
        Desempate();
        Idiomas();
        Prompt();
        Plantilla();
        Almacen();
        ListaIso();
        BuscadorDeCatalogo();
        PrefijoDeSerie();
        BibliotecaPorTemporadas();
        CatalogosReales();
        ColaDeRevision();
        FicheroDeDosEpisodios();
        LeerLoQueEscribe();
        MarcadorManda();
        SepararHistorias();
        PeleaPorElMismoEpisodio();
        PartirEnTramos();
        ArrastrarLasJuntas();
        ArgsDelTramo();
        NombresDeLosTramos();
        MarcadorDeNube();
        TituloDelNfo();
        SegmentoRecordado();
        OrdinalDeTemporada();
        Sidecars();

        Console.WriteLine($"\n── {_ok} pasan · {_fallos} fallan ──");
        return _fallos == 0 ? 0 : 1;
    }

    // ───────────────────────────── Fase B: norm() ─────────────────────────────

    private static void Normalizacion()
    {
        Seccion("Normalización de títulos");

        Eq("una ciudad de sueno nobitaland", TitleMatch.Norm("Una ciudad de sueño, Nobitaland"),
            "quita acentos, ñ→n y puntuación");
        Eq("las galletas magicas", TitleMatch.Norm("Las galletas mágicas"), "diacríticos fuera");
        Eq("con calma y con prisa", TitleMatch.Norm("Con calma y con prisa (España)"), "sufijo (España)");
        Eq("el pueblo de nobita", TitleMatch.Norm("El pueblo de Nobita (Hispanoamérica)"), "sufijo (Hispanoamérica)");
        Eq("la mujer de nobita", TitleMatch.Norm("La mujer de Nobita (segundo doblaje)"), "sufijo (…doblaje)");
        Eq("la mujer de nobita", TitleMatch.Norm("La mujer de Nobita (primer doblaje)"), "sufijo (primer doblaje)");
        Eq("nobita 2 el regreso", TitleMatch.Norm("  Nobita   2:  el--regreso!!  "), "colapsa y recorta");
        Eq("", TitleMatch.Norm("   "), "solo espacios → vacío");
        Eq("", TitleMatch.Norm(null), "null → vacío");
        // Los dígitos sobreviven: distinguen «Parte 1» de «Parte 2»
        Eq("parte 2", TitleMatch.Norm("Parte 2"), "los números se conservan");
        // Los ordinales abreviados tienen que igualar a los escritos: el fichero dice
        // «(2.ª parte)» donde el catálogo dice «(segunda parte)», y sin esto se quedan
        // en «2 parte» vs «segunda parte» y el parecido baja justo lo que descalifica.
        Eq("segunda parte", TitleMatch.Norm("2.ª parte"), "«2.ª» = «segunda»");
        Eq("primera parte", TitleMatch.Norm("(1.ª parte)"), "«1.ª» = «primera»");
        Eq("tercera parte", TitleMatch.Norm("3ª parte"), "también sin punto");
        Eq("parte 2", TitleMatch.Norm("Parte 2"), "un 2 sin ordinal se queda como está");
    }

    // ─────────────────── Fase B: sim() == difflib de Python ───────────────────

    /// <summary>
    /// Valores generados con <c>difflib.SequenceMatcher(None, a, b).ratio()</c> de Python
    /// 3.12 sobre las cadenas ya normalizadas. Son la referencia con la que se calibraron
    /// los umbrales 0,78 / 0,86: si esta tabla deja de cuadrar, los umbrales dejan de
    /// significar lo que dice la epic.
    /// </summary>
    private static void SimilitudContraDifflib()
    {
        Seccion("Similitud idéntica a difflib (Ratcliff-Obershelp)");

        (string a, string b, double esperado)[] verdad =
        {
            ("una ciudad de sueno nobitaland", "una ciudad de sueno nobitaland", 1.0),
            ("la robochica me ama",            "la robotchica me ama",           0.974358974359),
            ("el interruptor del despotismo",  "el interruptor de despotismo",   0.982456140351),
            ("shin chan se va de compras",     "shin chan se va de compra",      0.980392156863),
            ("trabajo de madres",              "trabajos de madre",              0.941176470588),
            ("mira que dibujo",                "mira que dibujos",               0.967741935484),
            ("el interruptor del despotismo",  "el interruptor de la obediencia",0.733333333333),
            ("una ciudad de sueno nobitaland", "las galletas magicas",           0.28),
            ("trabajo de madres",              "el interruptor del despotismo",  0.434782608696),
            ("nobita",                         "doraemon",                       0.142857142857),
            ("a",                              "a",                              1.0),
            ("a",                              "b",                              0.0),
            ("ab",                             "ba",                             0.5),
            ("abcd",                           "abed",                           0.75),
            ("",                               "",                               1.0),
            ("nobita",                         "nobita y el reino",              0.521739130435),
            ("el regreso de nobita",           "nobita regresa",                 0.470588235294),
            ("galletas magicas las",           "las galletas magicas",           0.8),
        };

        foreach (var (a, b, esperado) in verdad)
        {
            double real = TitleMatch.Sim(a, b);
            Assert(Math.Abs(real - esperado) < 1e-9,
                $"sim(\"{Corta(a)}\", \"{Corta(b)}\") = {real:F6} (difflib: {esperado:F6})");
        }

        // La cota superior nunca puede mentir por debajo, o descartaría pares buenos
        foreach (var (a, b, _) in verdad)
        {
            var (ba, bb) = (new TitleBag(a), new TitleBag(b));
            Assert(TitleMatch.CotaSuperior(in ba, in bb) >= TitleMatch.Sim(a, b) - 1e-12,
                $"la cota superior acota de verdad: \"{Corta(a)}\" vs \"{Corta(b)}\"");
        }
    }

    // ───────────────────── Fase A: extracción de señales ─────────────────────

    private static void ExtraccionDeSeniales()
    {
        Seccion("Extracción de señales del nombre");

        var f = SignalExtractor.Extract(F("2005-04-22 Con calma y con prisa.mkv"), "Season 2005");
        Eq(new DateOnly(2005, 4, 22), f.Fecha, "fecha yyyy-mm-dd al inicio");
        Eq("Con calma y con prisa", f.TituloNombre, "el título es lo que queda");
        Eq(2005, f.Temporada, "temporada de la carpeta «Season 2005»");
        Assert(f.Indice is null, "la fecha no deja un «22» suelto haciéndose pasar por índice");

        f = SignalExtractor.Extract(F("[438] La robochica me ama.mkv"));
        Eq(438, f.Indice, "índice entre corchetes");
        Eq("La robochica me ama", f.TituloNombre, "título tras el índice");
        Assert(f.SubSegmento is null, "sin sub-segmento");

        f = SignalExtractor.Extract(F("[438a] Primera mitad.mkv"));
        Eq(438, f.Indice, "índice de sub-segmento");
        Eq("a", f.SubSegmento, "letra del sub-segmento");

        f = SignalExtractor.Extract(F("[S12] Especial de Navidad.mkv"));
        Assert(f.Especial, "«[S12]» marca especial");
        Eq(12, f.IndiceEspecial, "número del especial");
        Eq("Especial de Navidad", f.TituloNombre, "título del especial");

        f = SignalExtractor.Extract(F("[S] Especial sin número.mkv"));
        Assert(f.Especial, "«[S]» a secas también marca especial");
        Assert(f.IndiceEspecial is null, "especial sin número");

        f = SignalExtractor.Extract(F("S03E12 Un titulo.mkv"), "Serie");
        Eq(12, f.Indice, "SxxExx → episodio 12");

        f = SignalExtractor.Extract(F("E72 Otro titulo.mkv"));
        Eq(72, f.Indice, "«E72» suelto");

        f = SignalExtractor.Extract(F("72 - Otro titulo mas.mkv"));
        Eq(72, f.Indice, "número al principio");
        Eq("Otro titulo mas", f.TituloNombre, "título tras el número inicial");

        f = SignalExtractor.Extract(F("[10] Uno ┃ Dos ┃ Tres.mkv"));
        Eq(3, f.Segmentos.Count, "multi-segmento con ┃");
        Eq("Uno", f.Segmentos[0], "primer segmento limpio");
        Eq("Tres", f.Segmentos[2], "último segmento limpio");

        f = SignalExtractor.Extract(F("[10] Uno | Dos.mkv"));
        Eq(2, f.Segmentos.Count, "multi-segmento con |");

        f = SignalExtractor.Extract(F("[10] Titulo simple.mkv"));
        Eq(0, f.Segmentos.Count, "un solo título no se parte");

        foreach (var (carpeta, esperada) in new (string, int?)[]
                 { ("Season 2007", 2007), ("Temporada 3", 3), ("2007", 2007), ("S03", 3), ("Vídeos", null) })
        {
            f = SignalExtractor.Extract(Path.Combine("S", carpeta, "algo.mkv"), carpeta);
            Eq(esperada, f.Temporada, $"temporada de la carpeta «{carpeta}»");
        }

        f = SignalExtractor.Extract(F(".mkv"), "x");
        Assert(!f.TieneSeñales, "un nombre sin nada no tiene señales");

        f = SignalExtractor.Extract(F("[10] Con meta.mkv"), "S", tituloMeta: "  El titulo interno  ");
        Eq("El titulo interno", f.TituloMeta, "el metadato se recorta");
    }

    // ─────────────────────── Fase F1: carga del catálogo ───────────────────────

    private static void CargaDeCatalogo()
    {
        Seccion("Carga y validación del catálogo");

        var cat = ReindexCatalog.Parse(CatalogoDePrueba);
        Eq("Serie de prueba", cat.Serie, "lee la serie");
        Eq(6, cat.Episodios.Count, "lee todos los episodios");
        Eq(1, cat.Especiales.Count, "separa los especiales");
        Eq(5, cat.Regulares.Count, "separa los regulares");

        Assert(cat.PorNum(10) != null, "encuentra el episodio 10");
        Assert(cat.PorNum(13) is null, "el 13 no existe: la numeración salta");
        Assert(cat.HuecosDeNumeracion().Contains(13), "detecta el hueco del 13");
        Assert(cat.TieneRemakes(), "detecta el remake (mismo título, 445 números después)");
        Assert(cat.Advertencias.Any(a => a.Contains("remake", StringComparison.OrdinalIgnoreCase)),
            "avisa del remake antes de identificar nada");

        // Un catálogo del futuro no se abre a medias: se rechaza con un motivo legible
        Lanza<ReindexCatalogException>(() => ReindexCatalog.Parse(
            CatalogoDePrueba.Replace("reindex/1.0", "reindex/2.0")), "rechaza un esquema mayor no soportado");
        Lanza<ReindexCatalogException>(() => ReindexCatalog.Parse("{\"esquema\":\"otra-cosa/1.0\"}"),
            "rechaza un esquema desconocido");
        Lanza<ReindexCatalogException>(() => ReindexCatalog.Parse("{ esto no es json "),
            "rechaza un JSON roto");
        Lanza<ReindexCatalogException>(() => ReindexCatalog.Parse("{\"esquema\":\"reindex/1.0\",\"episodios\":[]}"),
            "rechaza un catálogo sin episodios");

        // ── Reglas para catálogos escritos a mano (documentadas en docs/catalogo-reindex.md) ──

        // Un «num» repetido perdía un episodio EN SILENCIO: el índice se construye por
        // número, así que el segundo pisaba al primero y nadie se enteraba.
        var repetido = Lanzado<ReindexCatalogException>(() => ReindexCatalog.Parse("""
            { "esquema": "reindex/1.0", "serie": "P", "episodios": [
              { "num": 7, "titulos": { "es": ["Uno"] } },
              { "num": 7, "titulos": { "es": ["Otro"] } } ] }
            """), "rechaza dos episodios con el mismo número");
        Assert(repetido?.Message.Contains("posición 1") == true, "y dice con cuál choca");

        Lanza<ReindexCatalogException>(() => ReindexCatalog.Parse("""
            { "esquema": "reindex/1.0", "serie": "P", "episodios": [
              { "num": -3, "titulos": { "es": ["Uno"] } } ] }
            """), "rechaza un número negativo");

        var fechaMala = Lanzado<ReindexCatalogException>(() => ReindexCatalog.Parse("""
            { "esquema": "reindex/1.0", "serie": "P", "episodios": [
              { "num": 1, "fecha": "2005-13-45", "titulos": { "es": ["Uno"] } } ] }
            """), "rechaza una fecha que no existe");
        Assert(fechaMala?.Message.Contains("2005-13-45") == true, "y cita la fecha culpable");

        Lanza<ReindexCatalogException>(() => ReindexCatalog.Parse("""
            { "esquema": "reindex/1.0", "serie": "", "episodios": [
              { "num": 1, "titulos": { "es": ["Uno"] } } ] }
            """), "rechaza un catálogo sin nombre de serie");

        // Los fallos se enseñan JUNTOS: corregir de uno en uno es un bucle sin final
        var varios = Lanzado<ReindexCatalogException>(() => ReindexCatalog.Parse("""
            { "esquema": "reindex/1.0", "serie": "P", "episodios": [
              { "num": 1, "fecha": "ayer" },
              { "num": 1 },
              { "num": -5 } ] }
            """), "rechaza un catálogo con varios problemas");
        Assert(varios?.Message.Contains("3 problemas") == true,
            "y los cuenta todos de una vez en vez de parar en el primero");

        // Una fecha válida sí pasa, y se entiende
        var conFecha = ReindexCatalog.Parse("""
            { "esquema": "reindex/1.0", "serie": "P", "episodios": [
              { "num": 1, "fecha": "2005-04-22", "titulos": { "es": ["Uno"] } } ] }
            """);
        Eq(new DateOnly(2005, 4, 22), conFecha.PorNum(1)!.FechaParsed, "una fecha correcta se lee bien");

        // El ejemplo que la app ofrece como punto de partida TIENE que importar sin fallos:
        // entregar una plantilla que no vale sería el peor recibimiento posible.
        var ejemplo = ReindexCatalog.Parse(ReindexCatalog.Ejemplo);
        Eq(3, ejemplo.Episodios.Count, "el catálogo de ejemplo es válido y se lee");
        Eq(1, ejemplo.Especiales.Count, "el ejemplo enseña cómo se marca un especial");
        Assert(ejemplo.Episodios.Any(e => e.TitulosSalida.Count > 1),
            "el ejemplo enseña un episodio con varios segmentos");
        Assert(ejemplo.Episodios.Any(e => e.FechaParsed != null), "el ejemplo enseña el formato de fecha");

        // Compatibilidad hacia delante: 1.9 sigue siendo mayor 1, y los campos de más se ignoran
        var futuro = ReindexCatalog.Parse(CatalogoDePrueba
            .Replace("reindex/1.0", "reindex/1.9")
            .Replace("\"serie\":", "\"campo_del_futuro\": {\"x\": [1,2]}, \"serie\":"));
        Eq(6, futuro.Episodios.Count, "un 1.9 con campos desconocidos se lee igual");
    }

    // ───────────────────────── Fase C: cascada P0-P4 ─────────────────────────

    private static void Cascada()
    {
        Seccion("Cascada de resolución");
        var cat = ReindexCatalog.Parse(CatalogoDePrueba);

        // P1 — número + fecha exacta, y el número ya era el bueno
        var r = Uno(cat, F("2005-01-10 [10] El interruptor del despotismo.mkv"));
        Eq(ReindexEstado.Limpio, r.Estado, "P1 con el número correcto → limpio");
        Eq(ReindexConfianza.Alta, r.Confianza, "P1 es verde");
        Eq(ReindexHint.IndiceFecha, r.Hint, "P1 lo resolvió número+fecha");

        // P1 — número + fecha exacta, pero el número del fichero era otro
        r = Uno(cat, F("2005-01-17 [11] La robochica me ama.mkv"));
        Eq(ReindexEstado.Limpio, r.Estado, "P1: el 11 con su fecha es correcto");

        // P2 — sin número, el título manda
        r = Uno(cat, F("Las galletas magicas.mkv"));
        Eq(ReindexEstado.Corregido, r.Estado, "P2 sin número → hay que corregir");
        Eq(12, r.Episodio?.Num, "P2 identifica el episodio 12 por título");
        Eq(ReindexConfianza.Alta, r.Confianza, "P2 por encima del umbral es verde");
        Eq(ReindexHint.Titulo, r.Hint, "P2 lo resolvió el título");

        // P2 — con acentos y sufijo de doblaje de por medio
        r = Uno(cat, F("Las galletas mágicas (España).mkv"));
        Eq(12, r.Episodio?.Num, "P2 aguanta acentos y sufijo de doblaje");

        // P3 — número correcto pero la fecha baila 2 días
        r = Uno(cat, F("2005-01-12 [10] Titulo que no dice nada.mkv"));
        Eq(ReindexConfianza.Revisar, r.Confianza, "P3 (fecha ±2 días) nunca es verde");
        Eq(ReindexHint.IndiceFechaAprox, r.Hint, "P3 lo resolvió número+fecha aproximada");
        Eq(10, r.Episodio?.Num, "P3 se queda con el episodio del número");

        // Fecha demasiado lejos: el número deja de valer como prueba
        r = Uno(cat, F("2005-06-30 [10] Titulo que no dice nada.mkv"));
        Assert(r.Confianza != ReindexConfianza.Alta, "una fecha a meses de distancia no se aplica sola");

        // P4 — el título se parece, pero poco
        r = Uno(cat, F("El interruptor de la obediencia.mkv"));
        Eq(ReindexConfianza.Revisar, r.Confianza, "P4 (título flojo) es sugerencia, no automático");
        Eq(ReindexHint.TituloDebil, r.Hint, "P4 lo marca como título débil");
        Assert(r.Score < TitleMatch.UmbralTitulo, "P4 va por debajo del umbral");

        // Sin ninguna señal → error, nunca una propuesta inventada
        r = Uno(cat, F(".mkv"));
        Eq(ReindexEstado.Error, r.Estado, "sin señales → error");
        Assert(r.Episodio is null, "un error no propone episodio");

        // P0 — el override gana a todo, incluso a un número+fecha perfectos
        var overrides = new Dictionary<string, ReindexOverride>
        {
            [F("2005-01-10 [10] El interruptor del despotismo.mkv")] = new() { Num = 12 },
        };
        r = ReindexEngine.Resolve(
            new[] { SignalExtractor.Extract(F("2005-01-10 [10] El interruptor del despotismo.mkv")) },
            cat, overrides)[0];
        Eq(12, r.Episodio?.Num, "P0 gana a P1");
        Eq(ReindexHint.Override, r.Hint, "P0 se marca como decisión del usuario");
        Eq(ReindexConfianza.Alta, r.Confianza, "una decisión humana es verde");
    }

    // ──────────────── Reglas transversales y de lote ────────────────

    private static void ReglasDeLote()
    {
        Seccion("Reglas transversales (las que vienen de bugs reales)");
        var cat = ReindexCatalog.Parse(CatalogoDePrueba);

        // Regla 1 — anti-remake: el título encaja igual de bien con el 10 y con el 455.
        // Elegir el primero de la lista en silencio sería justo el bug que la regla evita.
        var r = Uno(cat, F("[11] El interruptor del despotismo.mkv"));
        Eq(ReindexConfianza.Revisar, r.Confianza, "anti-remake: dos episodios empatan → a revisar");
        Assert(r.Alternativas.Count > 0, "anti-remake enseña el otro candidato");
        Assert(r.Motivo.Contains("título"), "el motivo explica el empate");

        // El mismo empate, pero con una fecha que desempata: vuelve a ser automático
        r = Uno(cat, F("2005-01-10 [10] El interruptor del despotismo.mkv"));
        Eq(ReindexConfianza.Alta, r.Confianza, "con fecha exacta el empate de título deja de importar");

        // …pero un remake BIEN numerado no puede salir como duda: comparten título a propósito
        r = Uno(cat, F("2012-05-05 [455] El interruptor del despotismo.mkv"));
        Eq(ReindexConfianza.Alta, r.Confianza, "un remake bien numerado sigue siendo verde");
        Eq(455, r.Episodio?.Num, "el remake se queda en su propio número");

        // Regla 3 — el nombre y el metadato llevan a episodios distintos
        var conChoque = SignalExtractor.Extract(F("Las galletas magicas.mkv"), "S",
            tituloMeta: "La robochica me ama");
        r = ReindexEngine.Resolve(new[] { conChoque }, cat)[0];
        Eq(ReindexEstado.Conflicto, r.Estado, "nombre vs metadato en desacuerdo → conflicto");
        Assert(r.Episodio is null, "un conflicto no elige en silencio");
        Eq(2, r.Alternativas.Count, "el conflicto enseña las dos lecturas");

        // …y si coinciden, no hay conflicto ninguno
        var sinChoque = SignalExtractor.Extract(F("Las galletas magicas.mkv"), "S",
            tituloMeta: "Las galletas mágicas");
        r = ReindexEngine.Resolve(new[] { sinChoque }, cat)[0];
        Eq(12, r.Episodio?.Num, "nombre y metadato de acuerdo → sin conflicto");

        // Regla 2 — dos ficheros que reclaman el mismo destino
        var lote = ReindexEngine.Resolve(new[]
        {
            SignalExtractor.Extract(F("Las galletas magicas.mkv")),
            SignalExtractor.Extract(F("Las galletas mágicas (España).mkv")),
        }, cat);
        Eq(1, lote.Count(x => x.Estado == ReindexEstado.Conflicto), "el duplicado cae en conflicto");
        Assert(lote.All(x => !x.AplicableEnBloque), "con pelea de por medio no se aplica nada a ciegas");
        Assert(lote.Any(x => x.EsDuplicado), "el perdedor queda marcado como duplicado");
        var perdedor = lote.First(x => x.EsDuplicado);
        Assert(perdedor.Motivo.Contains("Las galletas mágicas"),
            "y el motivo dice qué título espera el catálogo para ese número");
        Assert(perdedor.Motivo.Contains(".mkv"), "y con qué fichero compite");

        // …pero los sub-segmentos comparten número LEGÍTIMAMENTE
        lote = ReindexEngine.Resolve(new[]
        {
            SignalExtractor.Extract(F("2005-01-10 [10a] El interruptor del despotismo.mkv")),
            SignalExtractor.Extract(F("2005-01-10 [10b] El interruptor del despotismo.mkv")),
        }, cat);
        Eq(0, lote.Count(x => x.Estado == ReindexEstado.Conflicto),
            "«[10a]» y «[10b]» comparten número sin ser un duplicado");

        // Regla 4 — un especial jamás cae en la numeración regular
        r = Uno(cat, F("[S1] Especial de Navidad.mkv"));
        Eq(ReindexEstado.Especial, r.Estado, "un especial se queda en estado especial");
        Eq(ReindexConfianza.Revisar, r.Confianza, "un especial siempre se confirma a mano");
        Assert(r.Episodio?.Especial == true, "el especial apunta a un episodio especial del catálogo");
        Assert(r.Episodio?.Num != 12, "un especial no aterriza en la numeración regular");

        // Un especial recién identificado NO se aplica solo; confirmado (override) sí.
        // Es la regla «Aplicar toca verdes + confirmados» del diseño.
        r = Uno(cat, F("[S1] Especial de Navidad.mkv"));
        Assert(!r.AplicableEnBloque, "un especial sin confirmar no entra en el lote");
        r = ReindexEngine.Resolve(
            new[] { SignalExtractor.Extract(F("[S1] Especial de Navidad.mkv")) }, cat,
            new Dictionary<string, ReindexOverride>
            { [F("[S1] Especial de Navidad.mkv")] = new() { Num = 901 } })[0];
        Eq(ReindexEstado.Especial, r.Estado, "confirmado sigue siendo un especial");
        Assert(r.AplicableEnBloque, "un especial confirmado sí entra en el lote");

        // Un especial que el catálogo no contempla
        var catSinEsp = ReindexCatalog.Parse(CatalogoDePrueba.Replace("\"especial\": true", "\"especial\": false"));
        r = ReindexEngine.Resolve(new[] { SignalExtractor.Extract(F("[S1] Especial de Navidad.mkv")) },
            catSinEsp)[0];
        Eq(ReindexEstado.Conflicto, r.Estado, "especial sin especiales en el catálogo → conflicto, no invento");

        // Contadores del diseño: cada fichero cae en exactamente un estado
        var mezcla = ReindexEngine.Resolve(new[]
        {
            SignalExtractor.Extract(F("2005-01-10 [10] El interruptor del despotismo.mkv")),
            SignalExtractor.Extract(F("Las galletas magicas.mkv")),
            SignalExtractor.Extract(F("[S1] Especial de Navidad.mkv")),
            SignalExtractor.Extract(F(".mkv")),
        }, cat);
        Eq(4, mezcla.Count, "una resolución por fichero, ni más ni menos");
        Eq(1, mezcla.Count(x => x.Estado == ReindexEstado.Limpio), "1 limpio");
        Eq(1, mezcla.Count(x => x.Estado == ReindexEstado.Corregido), "1 corregido");
        Eq(1, mezcla.Count(x => x.Estado == ReindexEstado.Especial), "1 especial");
        Eq(1, mezcla.Count(x => x.Estado == ReindexEstado.Error), "1 error");
        Assert(mezcla.All(x => !string.IsNullOrWhiteSpace(x.Motivo)), "toda fila tiene su «por qué»");
    }

    // ─────────────────── Desempate: temporada y segmentos casados ───────────────────

    /// <summary>
    /// Caso real de Doraemon (2005). El fichero trae dos segmentos y declara su temporada
    /// (por el nombre y por la carpeta). El episodio 1 explica LOS DOS segmentos y es de
    /// 2005; el 318 comparte solo uno y es de 2014. Como la puntuación era el MÁXIMO, los
    /// dos daban 1,00 y la app preguntaba — enseñando además solo los descartados, así que
    /// las dos opciones que ofrecía estaban mal.
    /// </summary>
    private static void Desempate()
    {
        Seccion("Desempate por temporada y por segmentos casados");

        const string catalogo = """
        {
          "esquema": "reindex/1.0", "serie": "Doraemon (2005)",
          "episodios": [
            { "num": 1, "temporada": 2005,
              "titulos": { "es": ["Con calma y con prisa", "La mujer de Nobita"] } },
            { "num": 318, "temporada": 2014,
              "titulos": { "es": ["El gorro de la invisibilidad", "La mujer de Nobita"] } },
            { "num": 132, "temporada": 2009, "titulos": { "es": ["Los sueños de Nobita"] } }
          ]
        }
        """;
        var cat = ReindexCatalog.Parse(catalogo);

        var f = SignalExtractor.Extract(
            Path.Combine("Temporada 2005", "Doraemon (2005) S2005E002 - Con calma y con prisa | La mujer de Nobita.mkv"),
            "Temporada 2005");
        Eq(2005, f.Temporada, "el fichero declara su temporada");
        Eq(2, f.Segmentos.Count, "y trae dos segmentos");

        var r = ReindexEngine.Resolve(new[] { f }, cat)[0];
        Eq(1, r.Episodio?.Num, "gana el episodio que explica AMBOS segmentos, no el que comparte uno");
        Eq(ReindexConfianza.Alta, r.Confianza, "y deja de preguntar: la temporada y los segmentos lo resuelven");

        // Una fila resuelta al 100 % no debe ofrecer «alternativas» que van muy por detrás:
        // era ruido en una fila ya correcta, e invitaba a un clic equivocado.
        Eq(0, r.Alternativas.Count, "con el ganador al 100 %, los lejanos no se ofrecen");
        Assert(!r.EsDuda, "y la fila no pide decisión ninguna");

        // …pero en una fila que SÍ pide decisión, las alternativas siguen ahí: es donde
        // hacen falta.
        var catGemelas = ReindexCatalog.Parse("""
        {
          "esquema": "reindex/1.0", "serie": "S",
          "episodios": [
            { "num": 5,  "temporada": 2005, "titulos": { "es": ["El pañuelo del tiempo"] } },
            { "num": 90, "temporada": 2005, "titulos": { "es": ["El pañuelo del tiempo"] } }
          ]
        }
        """);
        var dudosa = ReindexEngine.Resolve(
            new[] { SignalExtractor.Extract(Path.Combine("Temporada 2005", "El pañuelo del tiempo.mkv"), "Temporada 2005") },
            catGemelas)[0];
        Assert(dudosa.EsDuda, "dos episodios idénticos sí dejan la fila en duda");
        Assert(dudosa.Alternativas.Count > 0, "y ahí las alternativas sí se ofrecen");

        // Sin la temporada del fichero, el empate por segmentos sigue decidiendo
        var sinTemp = SignalExtractor.Extract(
            Path.Combine("Videos", "Con calma y con prisa | La mujer de Nobita.mkv"), "Videos");
        Eq(1, ReindexEngine.Resolve(new[] { sinTemp }, cat)[0].Episodio?.Num,
            "sin temporada, «2 de 2 segmentos» todavía gana a «1 de 2»");

        // Pero cuando NADA desempata, se sigue preguntando: no se inventa una certeza
        var catGemelo = ReindexCatalog.Parse("""
        {
          "esquema": "reindex/1.0", "serie": "S",
          "episodios": [
            { "num": 10, "temporada": 2005, "titulos": { "es": ["El mismo título"] } },
            { "num": 455, "temporada": 2005, "titulos": { "es": ["El mismo título"] } }
          ]
        }
        """);
        var dudoso = ReindexEngine.Resolve(
            new[] { SignalExtractor.Extract(Path.Combine("Temporada 2005", "El mismo título.mkv"), "Temporada 2005") },
            catGemelo)[0];
        Eq(ReindexConfianza.Revisar, dudoso.Confianza,
            "con misma temporada y mismos segmentos, sigue siendo un empate de verdad");
        Assert(dudoso.Motivo.Contains("10") && dudoso.Motivo.Contains("455"),
            "y el motivo nombra a los dos implicados");
    }

    // ─────────────────── Idiomas: reconocer en uno, nombrar en otro ───────────────────

    private static void Idiomas()
    {
        Seccion("Idiomas (reconocer en uno, nombrar en otro)");

        // El caso real: los ficheros llegan titulados en inglés y se quieren en español.
        const string catalogo = """
        {
          "esquema": "reindex/1.0",
          "serie": "Bob Esponja",
          "idiomas": { "salida": "es" },
          "episodios": [
            { "num": 1, "temporada": 1999,
              "titulos": { "es": ["Ayudante de cocina"], "en": ["Help Wanted"] } },
            { "num": 2, "temporada": 1999,
              "titulos": { "es": ["La burbuja mascota"], "en": ["Bubblestand"] } }
          ]
        }
        """;
        var cat = ReindexCatalog.Parse(catalogo);
        var plantilla = new LibraryTemplate();

        var ingles = Uno(cat, F("Help Wanted.mkv"));
        Eq(1, ingles.Episodio?.Num, "identifica un fichero titulado en INGLÉS");
        Eq(ReindexConfianza.Alta, ingles.Confianza, "y lo hace con confianza");
        Eq("Bob Esponja - S1999E1 - Ayudante de cocina.mkv",
            plantilla.Render(cat, ingles.Episodio!, ingles.Archivo),
            "pero el nombre que propone va en ESPAÑOL");

        // Y al revés: el mismo catálogo sigue reconociendo el español
        var espanol = Uno(cat, F("La burbuja mascota.mkv"));
        Eq(2, espanol.Episodio?.Num, "el mismo catálogo reconoce también el español");

        // Salida en inglés: mismo catálogo, otra preferencia
        var catEn = ReindexCatalog.Parse(catalogo.Replace("\"salida\": \"es\"", "\"salida\": \"en\""));
        Eq("Bob Esponja - S1999E1 - Help Wanted.mkv",
            plantilla.Render(catEn, catEn.PorNum(1)!, ingles.Archivo),
            "cambiar el idioma de salida cambia el nombre, no la identificación");

        // Acotar la comparación deja fuera lo que no se listó
        var catSoloEs = ReindexCatalog.Parse(
            catalogo.Replace("\"salida\": \"es\"", "\"salida\": \"es\", \"comparar\": [\"es\"]"));
        var fallo = Uno(catSoloEs, F("Help Wanted.mkv"));
        Assert(fallo.Confianza != ReindexConfianza.Alta,
            "si limitas «comparar» a español, el fichero inglés deja de reconocerse");

        // El japonés no estorba aunque se compare: norm() lo deja en cadena vacía
        var catJp = ReindexCatalog.Parse("""
        { "esquema": "reindex/1.0", "serie": "S", "episodios": [
          { "num": 1, "titulos": { "es": ["Uno"], "jp": ["ゆめの町ノビタランド"] } } ] }
        """);
        Eq(1, catJp.PorNum(1)!.TitulosNorm.Count,
            "un título en japonés no añade ruido: al normalizar se queda en nada");

        // Si al episodio le falta el idioma de salida, se tira de lo que haya
        var catHueco = ReindexCatalog.Parse("""
        { "esquema": "reindex/1.0", "serie": "S", "idiomas": { "salida": "es" },
          "episodios": [ { "num": 1, "titulos": { "en": ["Only English"] } } ] }
        """);
        Eq("Only English", catHueco.PorNum(1)!.TituloPrincipal,
            "sin título en el idioma de salida, usa el que haya en vez de «Episodio 1»");
    }

    // ─────────────────── Encargo para la IA ───────────────────

    private static void Prompt()
    {
        Seccion("Generador del encargo para la IA");

        var p = CatalogPrompt.Build("Bob Esponja",
            "https://bobesponja.fandom.com/wiki/Lista_de_episodios", "es", new[] { "en" });

        Assert(p.Contains("Bob Esponja"), "lleva el nombre de la serie");
        Assert(p.Contains("bobesponja.fandom.com"), "lleva la dirección del anexo");
        Assert(p.Contains("reindex/1.0"), "fija el esquema");
        Assert(p.Contains("\"salida\": \"es\"") && p.Contains("\"en\""),
            "declara el idioma de salida y los de comparación");
        Assert(p.Contains("único en todo el catálogo"), "avisa de la regla del num duplicado");
        Assert(p.Contains("AAAA-MM-DD"), "fija el formato de fecha");
        Assert(p.Contains("no inventes un 56"), "prohíbe rellenar los huecos de numeración");
        Assert(p.Contains("ÚNICAMENTE con el JSON"), "pide solo el JSON, sin explicaciones");

        // La lección de Doraemon: elegir la numeración equivocada desplaza la serie entera.
        Assert(p.Contains("TRANSMISIÓN"), "manda usar el número de transmisión por defecto");
        Assert(p.Contains("15-04-2005"), "y lo justifica con el caso real que lo destapó");

        // Un anexo pobre no debe empujar a la IA a inventarse los campos que faltan.
        Assert(p.Contains("puedes OMITIR campos, nunca INVENTARLOS"), "prohíbe inventar campos");
        Assert(p.Contains("**Omite el campo.**"), "y dice qué hacer cuando un dato no está");
        Assert(p.Contains("solo número y título"), "enseña también un catálogo mínimo válido");
        Assert(p.Contains("valida el archivo"), "recuerda que al importar se valida");

        // La tabla de campos tiene que cubrir TODO lo que el programa entiende: si el prompt
        // se queda corto, la IA no puede saber que ese campo existe.
        foreach (var campo in new[] { "esquema", "serie", "episodios", "clave", "notas", "idiomas",
                                      "total", "num", "titulos", "temporada", "fecha", "especial", "aliases" })
            Assert(p.Contains($"`{campo}`"), $"el prompt documenta el campo «{campo}»");

        // El idioma de salida SIEMPRE entra entre los comparables: sería absurdo escribir un
        // título que luego el motor no sabe reconocer.
        var sinSalida = CatalogPrompt.Build("S", "http://x", "es", new[] { "en" });
        int i_es = sinSalida.IndexOf("\"es\"", StringComparison.Ordinal);
        Assert(i_es >= 0, "el idioma de salida se cuela solo entre los de comparación");

        var duplicado = CatalogPrompt.Build("S", "http://x", "es", new[] { "es", "en" });
        Assert(CuentaSubcadena(duplicado, "\"es\", \"en\"") == 1,
            "y no se duplica si ya estaba en la lista");

        // Sin datos sigue produciendo algo utilizable, con huecos marcados
        var vacio = CatalogPrompt.Build("", "", "", Array.Empty<string>());
        Assert(vacio.Contains("escribe aquí el nombre de la serie"), "sin serie, deja el hueco señalado");
        Assert(vacio.Contains("pega aquí la dirección"), "sin fuente, deja el hueco señalado");
        Assert(vacio.Contains("\"salida\": \"es\""), "sin idioma, cae al español");
    }

    private static int CuentaSubcadena(string texto, string aguja)
    {
        int n = 0, i = 0;
        while ((i = texto.IndexOf(aguja, i, StringComparison.Ordinal)) >= 0) { n++; i += aguja.Length; }
        return n;
    }

    // ─────────────────── Plantilla de biblioteca ───────────────────

    private static void Plantilla()
    {
        Seccion("Plantilla de biblioteca");
        var cat = ReindexCatalog.Parse(CatalogoDePrueba);
        var plantilla = new LibraryTemplate();
        var ep = cat.PorNum(12)!;

        var f = SignalExtractor.Extract(F("Las galletas magicas.mkv"));
        Eq("Serie de prueba - S2005E12 - Las galletas mágicas.mkv", plantilla.Render(cat, ep, f),
            "compone el nombre canónico del diseño");
        Assert(plantilla.Render(cat, ep, f)!.EndsWith(".mkv"), "conserva la extensión original");

        var avi = SignalExtractor.Extract(F("episodio_438.avi"));
        Assert(plantilla.Render(cat, ep, avi)!.EndsWith(".avi"), "conserva .avi, no lo cambia a .mkv");

        // Los títulos de estas series traen «:» y «?» a menudo; Windows no los admite
        var catRaro = ReindexCatalog.Parse(CatalogoDePrueba.Replace(
            "Las galletas mágicas", "¿Dónde está Nobita?: la película"));
        var epRaro = catRaro.PorNum(12)!;
        var nombre = plantilla.Render(catRaro, epRaro, f)!;
        Assert(!nombre.Contains(':') && !nombre.Contains('?'),
            "quita los caracteres que Windows prohíbe en un nombre");
        Assert(!nombre.Contains("  "), "no deja espacios dobles donde estaban los símbolos");

        // Un episodio con varios segmentos los junta, como en la propuesta del mockup
        var catSeg = ReindexCatalog.Parse(CatalogoDePrueba.Replace(
            "\"es\": [\"Las galletas mágicas\"]", "\"es\": [\"El cometa\", \"Nieve en agosto\"]"));
        Assert(plantilla.Render(catSeg, catSeg.PorNum(12)!, f)!.Contains("El cometa + Nieve en agosto"),
            "junta los segmentos de un episodio multi-historia");

        // ── Marcas con parámetro: «<marca:algo>» ──
        //
        // Sin esto la plantilla no puede describir una biblioteca ya ordenada con otra
        // convención, y entonces TODO sale como pendiente de renombrar aunque el trabajo
        // esté hecho. El caso que lo destapó: ficheros «S2005E001 - A ┃ B», que la app sabe
        // LEER (┃ es separador de segmentos) pero no sabía ESCRIBIR.

        Eq("Serie de prueba - S2005E012 - Las galletas mágicas.mkv",
            new LibraryTemplate("<serie> - S<temp>E<num:000> - <título>").Render(cat, ep, f),
            "«<num:000>» rellena con ceros hasta tres cifras");
        Eq("Serie de prueba - S2005E12 - Las galletas mágicas.mkv",
            new LibraryTemplate("<serie> - S<temp>E<num:00> - <título>").Render(cat, ep, f),
            "y no recorta si el número ya es más largo que el relleno");

        Eq("El cometa ┃ Nieve en agosto.mkv",
            new LibraryTemplate("<título: ┃ >").Render(catSeg, catSeg.PorNum(12)!, f),
            "«<título:sep>» une los segmentos con lo que le digas");
        Eq("El cometa, Nieve en agosto.mkv",
            new LibraryTemplate("<título:, >").Render(catSeg, catSeg.PorNum(12)!, f),
            "con cualquier separador, no solo el de barras");
        Assert(new LibraryTemplate("<título>").Render(catSeg, catSeg.PorNum(12)!, f)!
                   .Contains("El cometa + Nieve en agosto"),
            "sin parámetro sigue uniendo con «+»: las plantillas de siempre no cambian");

        // El caso real completo: reproducir un nombre que ya existe en la biblioteca
        Eq("Serie de prueba S2005E012 - El cometa ┃ Nieve en agosto.mkv",
            new LibraryTemplate("<serie> S<temp>E<num:000> - <título: ┃ >")
                .Render(catSeg, catSeg.PorNum(12)!, f),
            "reproduce exactamente la convención de una biblioteca ya ordenada");

        // ── Ficheros que son SOLO UNA HISTORIA de una transmisión ──
        //
        // El caso real: «S2015E423 - b] ¡En busca de una sonrisa!» es la segunda historia de
        // la transmisión 413, no el episodio entero. El estándar: la letra va pegada al
        // número (E413b) para no pisarse con el episodio completo ni con la otra mitad, y
        // el título es SOLO el de esa historia.
        var fB = SignalExtractor.Extract(F("Las galletas magicas.mkv")).ConSegmento("b");
        Eq("Serie de prueba - S2005E12b - Nieve en agosto.mkv",
            plantilla.Render(catSeg, catSeg.PorNum(12)!, fB),
            "la historia b lleva la letra en el número y solo su título");
        Eq("Serie de prueba - S2005E12a - El cometa.mkv",
            plantilla.Render(catSeg, catSeg.PorNum(12)!, fB.ConSegmento("a")),
            "y la a, la primera historia");
        Eq("Serie de prueba S2005E012b - Nieve en agosto.mkv",
            new LibraryTemplate("<serie> S<temp>E<num:000> - <título: ┃ >")
                .Render(catSeg, catSeg.PorNum(12)!, fB),
            "el relleno con ceros conserva la letra detrás");
        // Con una letra que va más allá de las historias que hay, no se inventa nada
        Eq("Serie de prueba - S2005E12z - El cometa ┃ Nieve en agosto.mkv",
            new LibraryTemplate("<serie> - S<temp>E<num> - <título: ┃ >")
                .Render(catSeg, catSeg.PorNum(12)!, fB.ConSegmento("z")),
            "una letra sin historia correspondiente deja el título completo");
        Eq(null, fB.ConSegmento(null).SubSegmento, "quitar el segmento también funciona");

        // Patrón a medida
        Eq("S2005E12.mkv", new LibraryTemplate("S<temp>E<num>").Render(cat, ep, f), "acepta un patrón propio");
        Eq("Serie de prueba - S2005E12 - Las galletas mágicas.mkv",
            new LibraryTemplate("   ").Render(cat, ep, f), "un patrón vacío cae al de por defecto");

        // Cada marca que se ofrece en la interfaz tiene que SUSTITUIRSE de verdad. Si la
        // lista y el código se separan, se acaba ofreciendo una marca que sale tal cual en el
        // nombre del fichero.
        foreach (var m in LibraryTemplate.Marcas)
        {
            var soloEsa = new LibraryTemplate("x" + m.Marca).Render(cat, ep, f);
            Assert(soloEsa != null && !soloEsa.Contains(m.Marca),
                $"la marca {m.Marca} se sustituye de verdad");
            Assert(!string.IsNullOrWhiteSpace(m.Nombre) && !string.IsNullOrWhiteSpace(m.Descripcion),
                $"la marca {m.Marca} viene explicada");
        }

        // Nunca un fichero sin nombre
        Assert(new LibraryTemplate("...").Render(cat, ep, f) is null,
            "un patrón que no deja nada devuelve null en vez de crear «.mkv»");

        // Nombres larguísimos: recorta sin acercarse al límite de ruta
        var catLargo = ReindexCatalog.Parse(CatalogoDePrueba.Replace(
            "Las galletas mágicas", new string('A', 400)));
        var largo = plantilla.Render(catLargo, catLargo.PorNum(12)!, f)!;
        Assert(largo.Length <= 155, $"recorta los títulos kilométricos ({largo.Length} caracteres)");
    }

    // ─────────────────── Almacén: catálogos, decisiones, diario ───────────────────

    private static void Almacen()
    {
        Seccion("Almacén en disco");

        var temporal = Path.Combine(Path.GetTempPath(), "shrinkstudio-test-" + Guid.NewGuid().ToString("N")[..8]);
        ReindexStore.RaizOverride = temporal;
        try
        {
            // — catálogos —
            var origen = Path.Combine(temporal, "entrada.json");
            Directory.CreateDirectory(temporal);
            File.WriteAllText(origen, CatalogoDePrueba, System.Text.Encoding.UTF8);

            var guardado = ReindexStore.ImportarCatalogo(origen);
            Eq("Serie de prueba", guardado.Serie, "importa y describe el catálogo");
            Eq(6, guardado.Episodios, "cuenta los episodios");
            Eq(1, guardado.Especiales, "cuenta los especiales");
            Assert(guardado.Advertencias.Count > 0, "arrastra las advertencias a la tarjeta");
            Eq(1, ReindexStore.ListarCatalogos().Count, "el catálogo importado aparece en la lista");

            // La app REFERENCIA el original en su sitio, no lo copia. Con copia, editar el
            // original dejaba a la app trabajando con una versión vieja sin forma de notarlo.
            Eq(origen, guardado.Ruta, "la ruta ES la del original: sin copia");
            Assert(guardado.Disponible, "el original está donde se dijo");
            Eq(0, Directory.Exists(ReindexStore.DirCatalogos)
                    ? Directory.GetFiles(ReindexStore.DirCatalogos).Length : 0,
                "no se dejó ninguna copia en la carpeta de la app");

            // Editar el original se nota SOLO: es toda la razón de referenciar
            File.WriteAllText(origen, CatalogoDePrueba.Replace("\"num\": 12,", "\"num\": 90,"),
                System.Text.Encoding.UTF8);
            Eq(6, ReindexStore.ListarCatalogos()[0].Episodios, "relee el original en cada listado");

            // Mover el original NO lo hace desaparecer en silencio: la tarjeta lo dice
            var movido = Path.Combine(temporal, "entrada-movida.json");
            File.Move(origen, movido);
            var perdida = ReindexStore.ListarCatalogos();
            Eq(1, perdida.Count, "el catálogo movido sigue listado — desaparecer sin decir nada sería peor");
            Assert(!perdida[0].Disponible, "pero avisa de que el fichero ya no está donde estaba");
            File.Move(movido, origen);
            Assert(ReindexStore.ListarCatalogos()[0].Disponible, "al volver el fichero, vuelve a estar disponible");

            // — la última serie elegida sobrevive al cierre —
            Eq(null, ReindexStore.CargarUltimoCatalogo(), "de entrada no hay ninguna elegida");
            ReindexStore.GuardarUltimoCatalogo(guardado.Ruta);
            Eq(guardado.Ruta, ReindexStore.CargarUltimoCatalogo(), "se recuerda la elegida");

            // Un JSON inválido no se registra
            var malo = Path.Combine(temporal, "malo.json");
            File.WriteAllText(malo, "{ no soy un catalogo ");
            Lanza<ReindexCatalogException>(() => ReindexStore.ImportarCatalogo(malo),
                "rechaza importar un JSON inválido");
            Eq(1, ReindexStore.ListarCatalogos().Count, "el JSON inválido no se registró");

            // — quitar —
            Assert(ReindexStore.BorrarCatalogo(guardado.Ruta), "quita el catálogo de la app");
            Eq(0, ReindexStore.ListarCatalogos().Count, "y desaparece de la lista");
            Eq(null, ReindexStore.CargarUltimoCatalogo(),
                "al quitar el elegido deja de estarlo: si no, arrancaría apuntando a un fichero que ya no está");
            Assert(!ReindexStore.BorrarCatalogo(guardado.Ruta), "quitar dos veces no revienta");
            Assert(File.Exists(origen), "el JSON del usuario NO se toca jamás");

            // — migración: una copia de las de antes, cuyo original sigue existiendo,
            //   pasa a referencia sola y la copia se retira —
            Directory.CreateDirectory(ReindexStore.DirCatalogos);
            var copiaVieja = Path.Combine(ReindexStore.DirCatalogos, "Serie de prueba.reindex.json");
            File.Copy(origen, copiaVieja);
            File.WriteAllText(Path.Combine(ReindexStore.Raiz, "procedencia.json"),
                $$"""{ "Serie de prueba.reindex.json": "{{origen.Replace("\\", "\\\\")}}|21/07/2026" }""",
                System.Text.Encoding.UTF8);
            var migrados = ReindexStore.ListarCatalogos();
            Eq(1, migrados.Count, "la copia vieja aparece una sola vez");
            Eq(origen, migrados[0].Ruta, "migrada a referencia: apunta al original");
            Assert(!File.Exists(copiaVieja), "y la copia interna se retiró");
            Assert(ReindexStore.BorrarCatalogo(origen), "se limpia el registro para lo que sigue");

            // — memoria de decisiones —
            Eq(0, ReindexStore.CargarDecisiones().Count, "sin decisiones al principio");
            ReindexStore.GuardarDecisiones(new Dictionary<string, ReindexOverride>
            {
                ["huella-1"] = new() { Num = 72, Temporada = 2007, Serie = "Serie de prueba",
                                       FechaDecision = "2026-07-21", NombreOriginal = "algo.mkv" },
            });
            var leidas = ReindexStore.CargarDecisiones();
            Eq(1, leidas.Count, "guarda y relee las decisiones");
            Eq(72, leidas["huella-1"].Num, "conserva el número decidido");
            Eq("2026-07-21", leidas["huella-1"].FechaDecision, "conserva la fecha de la decisión");
            Eq("algo.mkv", leidas["huella-1"].NombreOriginal, "conserva el nombre original (trazabilidad)");

            // — diario de lote y deshacer —
            var carpeta = Path.Combine(temporal, "videos");
            Directory.CreateDirectory(carpeta);
            var viejo = Path.Combine(carpeta, "viejo.mkv");
            var nuevo = Path.Combine(carpeta, "Serie - S2005E12 - Nuevo.mkv");
            File.WriteAllText(viejo, "contenido");

            var lote = new LoteJournal
            {
                Id = "20260721-143200", Fecha = "2026-07-21", Hora = "14:32",
                Serie = "Serie de prueba", Carpeta = carpeta,
                Movimientos = { new MovimientoJournal { De = viejo, A = nuevo } },
            };
            var rutaJournal = ReindexStore.EscribirJournal(lote);
            Assert(File.Exists(rutaJournal), "el diario se escribe ANTES de renombrar");

            File.Move(viejo, nuevo);   // el renombrado de verdad
            Assert(File.Exists(nuevo) && !File.Exists(viejo), "el fichero quedó renombrado");

            var recuperado = ReindexStore.UltimoLote();
            Assert(recuperado != null, "recupera el último lote del disco");
            Eq("Deshacer lote 14:32 (1)", recuperado!.Etiqueta, "la etiqueta del botón persistente");

            var (devueltos, fallidos) = ReindexStore.Deshacer(recuperado);
            Eq(1, devueltos, "deshacer devuelve el fichero");
            Eq(0, fallidos, "sin fallos al deshacer");
            Assert(File.Exists(viejo) && !File.Exists(nuevo), "el fichero recuperó su nombre original");

            // Deshacer dos veces no puede duplicar nada ni reventar
            var (dev2, fall2) = ReindexStore.Deshacer(recuperado);
            Eq(0, dev2, "deshacer otra vez no mueve nada");
            Eq(1, fall2, "y lo cuenta como no realizable, sin excepción");
            Assert(File.Exists(viejo), "el fichero sigue donde debía");

            // Si el nombre viejo está ocupado, NO se machaca
            File.WriteAllText(nuevo, "otro");
            var (dev3, fall3) = ReindexStore.Deshacer(recuperado);
            Eq(0, dev3, "no deshace si el destino está ocupado");
            Eq(1, fall3, "lo cuenta como fallido");
            Eq("contenido", File.ReadAllText(viejo), "el fichero original quedó intacto");

            // — la cola de revisión sobrevive al cierre de la app —
            Eq(0, ReindexStore.CargarRevision().Cuantos, "de entrada no hay nada apartado");

            var cola = new ColaRevision();
            cola.Meter(viejo, "no sé si es el 173 o el 174");
            ReindexStore.GuardarRevision(cola);

            var vuelta = ReindexStore.CargarRevision();
            Eq(1, vuelta.Cuantos, "el apartado se recupera del disco");
            Assert(vuelta.Tiene(viejo), "y con la ruta correcta");
            Eq("no sé si es el 173 o el 174", vuelta.Nota(viejo), "la nota entera, con tildes");

            // Un fichero corrupto no puede impedir abrir la página
            File.WriteAllText(ReindexStore.RutaRevision, "{ esto no es json");
            Eq(0, ReindexStore.CargarRevision().Cuantos, "una cola rota se lee como vacía, sin reventar");
        }
        finally
        {
            ReindexStore.RaizOverride = null;
            try { Directory.Delete(temporal, recursive: true); } catch { }
        }
    }

    // ─────────────────── Integración con los catálogos reales ───────────────────

    private static void CatalogosReales()
    {
        Seccion("Catálogos reales");

        var dir = Environment.GetEnvironmentVariable("REINDEX_DATA")
                  ?? @"C:\Users\luish\Projects\reindex-epic\data";
        if (!Directory.Exists(dir))
        {
            Console.WriteLine($"  — omitido: no está {dir} (define REINDEX_DATA para incluirlo)");
            return;
        }

        var doraemon2005 = Path.Combine(dir, "doraemon-2005.reindex.json");
        if (File.Exists(doraemon2005))
        {
            var cat = ReindexCatalog.Load(doraemon2005);
            Eq("Doraemon (2005)", cat.Serie, "carga Doraemon (2005)");
            Assert(cat.Episodios.Count > 700, $"trae {cat.Episodios.Count} episodios");

            // El aviso de la epic: 56/138/173 NO son huecos, son saltos oficiales
            var huecos = cat.HuecosDeNumeracion();
            foreach (var n in new[] { 56, 138, 173 })
                Assert(huecos.Contains(n), $"el {n} está saltado en la numeración oficial");
            Assert(cat.PorNum(57) != null, "el 57 sí existe (el salto no arrastra a sus vecinos)");

            // Identificación real de punta a punta
            var r = Uno(cat, F("2005-04-22 [1] Con calma y con prisa.mkv", "D"));
            Eq(1, r.Episodio?.Num, "identifica el episodio 1 real");
            Eq(ReindexConfianza.Alta, r.Confianza, "el episodio 1 se resuelve en verde");

            // El segundo segmento del mismo episodio también lo identifica
            r = Uno(cat, F("La mujer de Nobita.mkv", "D"));
            Eq(1, r.Episodio?.Num, "un segmento suelto identifica su episodio");

            // Rendimiento: el barrido global tiene que ser viable de verdad
            var lote = Enumerable.Range(1, 300)
                .Select(i => SignalExtractor.Extract(F($"[{i}] Titulo cualquiera numero {i}.mkv", "D")))
                .ToList();
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            var res = ReindexEngine.Resolve(lote, cat);
            reloj.Stop();
            Eq(300, res.Count, "resuelve el lote entero");
            Assert(reloj.ElapsedMilliseconds < 30_000,
                $"300 ficheros × {cat.Episodios.Count} episodios en {reloj.ElapsedMilliseconds} ms");
        }

        foreach (var (fichero, serie) in new[]
                 { ("doraemon-1979.reindex.json", "Doraemon (1979)"), ("shin-chan.reindex.json", "Crayon Shin-Chan") })
        {
            var ruta = Path.Combine(dir, fichero);
            if (!File.Exists(ruta)) continue;
            var cat = ReindexCatalog.Load(ruta);
            Eq(serie, cat.Serie, $"carga {serie}");

            // Cientos de episodios de estas series nunca se doblaron y solo tienen título
            // japonés. No es un fallo del catálogo: es la realidad, y hay que avisarla porque
            // a esos episodios no se llega emparejando títulos.
            int sinTitulo = cat.Episodios.Count(e => e.TitulosNorm.Count == 0);
            Assert(cat.Episodios.Count - sinTitulo > cat.Episodios.Count / 2,
                $"{serie}: la mayoría ({cat.Episodios.Count - sinTitulo}/{cat.Episodios.Count}) sí es comparable");
            if (sinTitulo > 0)
                Assert(cat.Advertencias.Any(a => a.Contains("japonés")),
                    $"{serie}: avisa de los {sinTitulo} episodios que solo existen en japonés");
        }

        // Shin-chan no trae fechas: el catálogo debe avisarlo, porque cambia la fiabilidad
        var shin = Path.Combine(dir, "shin-chan.reindex.json");
        if (File.Exists(shin))
        {
            var cat = ReindexCatalog.Load(shin);
            Assert(cat.Advertencias.Any(a => a.Contains("fecha", StringComparison.OrdinalIgnoreCase)),
                "Shin-chan avisa de que no hay fechas");
            var r = Uno(cat, F("[1] Shin-chan se va de compras.mkv"));
            Eq(1, r.Episodio?.Num, "identifica el episodio 1 de Shin-chan por título");
        }
    }

    // ──────────── Índice relativo a la temporada («S2018E01» = 1.º de 2018) ────────────

    /// <summary>
    /// El caso real: ficheros «S2018E01.mkv» sin título, numerados del 1 en adelante DENTRO
    /// de cada temporada. Leído como número global, el 1 es el estreno de 2005 — contradice
    /// a su propia carpeta y encima se pelea con el fichero del episodio 1 de verdad.
    /// </summary>
    private static void OrdinalDeTemporada()
    {
        Seccion("Índice relativo a la temporada");

        var cat = ReindexCatalog.Parse("""
        {
          "esquema": "reindex/1.0",
          "serie": "Doraemon (2005)",
          "episodios": [
            { "num": 1,   "temporada": 2005, "titulos": { "es": ["Pesca de andar por casa"] } },
            { "num": 2,   "temporada": 2005, "titulos": { "es": ["Con calma y con prisa"] } },
            { "num": 640, "temporada": 2018, "titulos": { "es": ["Primero de 2018"] } },
            { "num": 641, "temporada": 2018, "titulos": { "es": ["Segundo de 2018"] } },
            { "num": 643, "temporada": 2018, "titulos": { "es": ["Tercero de 2018"] } }
          ]
        }
        """);

        // «E01» en la carpeta de 2018: el episodio global 1 es de 2005 → se relee como
        // el PRIMERO de la temporada 2018
        var r = Uno(cat, F("Doraemon (2005) S2018E01.mkv", "Season 2018"));
        Eq(640, r.Episodio?.Num, "el 1 de la carpeta 2018 es el primero DE 2018, no el estreno de 2005");
        Eq(ReindexConfianza.Revisar, r.Confianza, "sin título ni fecha que lo confirme, se revisa");
        Assert(r.Motivo.Contains("temporada"), "el motivo explica la relectura");
        Assert(r.Alternativas.Any(a => a.Episodio.Num == 1),
            "la lectura global (el estreno de 2005) se ofrece como alternativa");

        var r3 = Uno(cat, F("Doraemon (2005) S2018E03.mkv", "Season 2018"));
        Eq(643, r3.Episodio?.Num, "el 3.º de 2018 aunque la numeración global salte (640, 641, 643)");

        // Con más ordinal que episodios tiene la temporada, no se inventa nada
        var r9 = Uno(cat, F("Doraemon (2005) S2018E09.mkv", "Season 2018"));
        Assert(r9.Episodio?.Num != 643, "un 9.º que no existe no cae en el último por las bravas");

        // Y si el número global YA es de esa temporada, no se toca
        var rOk = Uno(cat, F("Doraemon (2005) S2018E640.mkv", "Season 2018"));
        Eq(640, rOk.Episodio?.Num, "un número global que cuadra con su carpeta se queda como está");
    }

    // ─────────────── Los ficheros compañeros (.nfo, .srt) viajan con el vídeo ───────────────

    private static void Sidecars()
    {
        Seccion("Ficheros compañeros (.nfo, .srt…)");

        // Renombrar el vídeo y dejar su .nfo con el nombre viejo lo convierte en huérfano:
        // el reproductor de biblioteca deja de asociarlos. Los compañeros viajan juntos.
        var de = F("Doraemon S2005E020 - Animo.mkv");
        var a = F("Doraemon (2005) - S2005E20 - Animo.mkv");
        var enCarpeta = new[]
        {
            F("Doraemon S2005E020 - Animo.mkv"),
            F("Doraemon S2005E020 - Animo.nfo"),
            F("Doraemon S2005E020 - Animo.es.srt"),
            F("otra cosa.nfo"),
            F("Doraemon S2005E021 - Otro.nfo"),
        };

        var plan = SidecarPlanner.Planear(de, a, enCarpeta);
        Eq(2, plan.Count, "encuentra sus dos compañeros y solo los suyos");
        Assert(plan.Any(p => p.De.EndsWith("Animo.nfo") &&
                             p.A.EndsWith("Doraemon (2005) - S2005E20 - Animo.nfo")),
            "el .nfo se renombra igual que el vídeo");
        Assert(plan.Any(p => p.De.EndsWith("Animo.es.srt") &&
                             p.A.EndsWith("Doraemon (2005) - S2005E20 - Animo.es.srt")),
            "un subtítulo con idioma («.es.srt») conserva su sufijo completo");

        Eq(0, SidecarPlanner.Planear(de, de, enCarpeta).Count,
            "si el vídeo no cambia de nombre, no hay nada que mover");
    }

    // ─────────────── La cola de revisión ───────────────

    /// <summary>
    /// Apartar un fichero para mirarlo con calma otro día. Lo que tiene que aguantar: que la
    /// app se cierre, que el fichero se RENOMBRE (que es lo que hace esta app todo el rato)
    /// y que se marque dos veces sin duplicarse.
    /// </summary>
    private static void ColaDeRevision()
    {
        Seccion("Cola de revisión");

        var cola = new ColaRevision();
        Eq(0, cola.Cuantos, "empieza vacía");

        cola.Meter(F("Doraemon - S2005E1 - Un titulo.mkv"), "no sé qué episodio es");
        Eq(1, cola.Cuantos, "se aparta uno");
        Eq(true, cola.Tiene(F("Doraemon - S2005E1 - Un titulo.mkv")), "y consta");
        Eq("no sé qué episodio es", cola.Nota(F("Doraemon - S2005E1 - Un titulo.mkv")),
            "con la nota de por qué se apartó");

        cola.Meter(F("Doraemon - S2005E1 - Un titulo.mkv"), "otra vez");
        Eq(1, cola.Cuantos, "marcarlo dos veces no lo duplica");
        Eq("otra vez", cola.Nota(F("Doraemon - S2005E1 - Un titulo.mkv")), "y la nota se actualiza");

        // Lo que de verdad rompería esto: la app renombra ficheros. Si la cola guardara solo
        // la ruta, al renombrar se perdería la marca — justo del fichero que estabas
        // arreglando. Por eso sigue al fichero cuando cambia de nombre.
        cola.Renombrado(F("Doraemon - S2005E1 - Un titulo.mkv"), F("Doraemon - S2005E7 - Un titulo.mkv"));
        Eq(true, cola.Tiene(F("Doraemon - S2005E7 - Un titulo.mkv")), "sigue al fichero renombrado");
        Eq(false, cola.Tiene(F("Doraemon - S2005E1 - Un titulo.mkv")), "y suelta el nombre viejo");
        Eq(1, cola.Cuantos, "sin duplicarse por el camino");

        cola.Sacar(F("Doraemon - S2005E7 - Un titulo.mkv"));
        Eq(0, cola.Cuantos, "y se puede sacar");
        Eq(false, cola.Tiene(F("Doraemon - S2005E7 - Un titulo.mkv")), "ya no consta");

        // Ida y vuelta por JSON: es lo que la hace sobrevivir al cierre de la app.
        var otra = new ColaRevision();
        otra.Meter(F("A.mkv"), "una");
        otra.Meter(F("B.mkv"), "");
        var vuelta = ColaRevision.Leer(otra.Escribir());
        Eq(2, vuelta.Cuantos, "sobrevive a escribir y leer");
        Eq("una", vuelta.Nota(F("A.mkv")), "con sus notas");
        Eq(0, ColaRevision.Leer("{ esto no es json").Cuantos, "un fichero roto no impide trabajar");
    }

    // ─────────────── Un fichero con historias de dos episodios ───────────────

    /// <summary>
    /// Hay ficheros que emparejan dos historias que el catálogo cuenta como episodios
    /// DISTINTOS. Ahí no existe respuesta correcta: renombrarlo con el número de uno pierde
    /// al otro, en silencio y para siempre. Eso lo decide una persona, no el programa.
    ///
    /// Lo delicado es no confundirlo con un remake: en Doraemon la misma historia aparece en
    /// un episodio viejo y en uno moderno, y eso NO es un fichero de dos episodios. La
    /// diferencia es si el episodio elegido cubre esa historia o no.
    /// </summary>
    private static void FicheroDeDosEpisodios()
    {
        Seccion("Un fichero, dos episodios");

        var cat = ReindexCatalog.Parse("""
        {
          "esquema": "reindex/1.0",
          "serie": "Doraemon (2005)",
          "episodios": [
            { "num": 615, "temporada": 2020, "titulos": { "es": ["El robot pruebarreacciones"] } },
            { "num": 662, "temporada": 2021, "titulos": { "es": ["A por la marca perfecta", "El escaparate para recoger"] } },
            { "num": 364, "temporada": 2014, "titulos": { "es": ["El gorro de la suerte", "El cazamariposas"] } },
            { "num": 2,   "temporada": 2005, "titulos": { "es": ["El gorro de la suerte"] } },
            { "num": 588, "temporada": 2020, "titulos": { "es": ["La lanza de la consideración que va directa al corazón"] } },
            { "num": 589, "temporada": 2020, "titulos": { "es": ["Cuidado con los estornudos"] } }
          ]
        }
        """);

        // Lo peligroso de verdad: el número y la PRIMERA historia concuerdan, así que el
        // motor lo da por seguro... y la segunda historia es de otro episodio. Ese es el
        // fichero que se renombraría solo, perdiendo la mitad de lo que contiene.
        var dos = ReindexEngine.Resolve(new[] {
            SignalExtractor.Extract(
                F("Doraemon (2005) - S2020E615 - El robot pruebarreacciones + A por la marca perfecta.mkv"),
                "Season 2020"),
        }, cat)[0];
        Eq(true, dos.Confianza != ReindexConfianza.Alta,
            "un fichero que abarca dos episodios NUNCA se aplica solo");
        Eq(true, (dos.Motivo ?? "").Contains("662") && (dos.Motivo ?? "").Contains("615"),
            "y el motivo nombra los dos episodios que hay dentro");
        Eq(true, dos.TraeDosEpisodios,
            "queda MARCADO, no solo redactado: la interfaz ofrece partirlo mirando la marca");

        // El remake: la misma historia está en el 2 y en el 364, pero el 364 cubre las DOS
        // historias del fichero. Eso no es ambigüedad, es lo normal.
        var remake = ReindexEngine.Resolve(new[] {
            SignalExtractor.Extract(F("Doraemon (2005) - S2014E364 - El gorro de la suerte + El cazamariposas.mkv"),
                                    "Season 2014"),
        }, cat)[0];
        Eq(364, remake.Episodio?.Num, "el remake se identifica igual");
        Eq(ReindexConfianza.Alta, remake.Confianza, "y sigue siendo automático: no hay nada que decidir");
        Eq(false, remake.TraeDosEpisodios, "y NO se le ofrece partirlo: no hay nada que partir");

        // El caso que reportó el usuario: el número del fichero (1302) no existe en el
        // catálogo, así que la identificación cae al título — y las dos historias casan al
        // 100 % con DOS episodios distintos (588 y 589). La app lo tomaba por un empate de
        // «elige uno» (confianza Revisar) y, por ir a Revisar, el detector de dos episodios
        // lo saltaba. No se puede elegir uno: hay que partir.
        var empate = ReindexEngine.Resolve(new[] {
            SignalExtractor.Extract(
                F("1302 La lanza de la consideración que va directa al corazón ┃ Cuidado con los estornudos.mkv"),
                "Season 2020"),
        }, cat)[0];
        Eq(true, empate.Confianza != ReindexConfianza.Alta, "un empate de dos historias no se aplica solo");
        Eq(true, empate.TraeDosEpisodios,
            "aunque el empate lo deje en Revisar, sigue siendo un fichero de dos episodios → partir");
        Eq(true, (empate.Motivo ?? "").Contains("588") && (empate.Motivo ?? "").Contains("589"),
            "y el motivo nombra los dos episodios, no ofrece elegir uno");
    }

    // ─────────────── La app tiene que saber leer lo que ella misma escribe ───────────────

    /// <summary>
    /// Cuando un fichero es SOLO una historia de un episodio, la app escribe la letra pegada
    /// al número: «S2017E487b». Pero luego no sabía leerlo: el regex del marcador exigía que
    /// tras los dígitos viniera algo que no fuese letra, así que «S2017E487b» no casaba con
    /// NADA y el fichero se quedaba sin número ni segmento. Se reidentificaba solo por el
    /// título, casaba con el episodio entero y proponía deshacer la decisión del usuario.
    ///
    /// Que un programa no sepa releer su propia salida es la peor clase de fallo de datos:
    /// cada pasada deshace la anterior.
    /// </summary>
    private static void LeerLoQueEscribe()
    {
        Seccion("Leer su propia salida");

        var conLetra = SignalExtractor.Extract(F(
            "Doraemon (2005) - S2017E487b - La niña de los zapatos rojos.mkv"));
        Eq(487, conLetra.Indice, "«S2017E487b» es el episodio 487");
        Eq("b", conLetra.SubSegmento, "y la letra dice qué historia es");
        Eq("La niña de los zapatos rojos", conLetra.TituloNombre, "el título queda limpio");

        var sinLetra = SignalExtractor.Extract(F("Doraemon (2005) - S2017E487 - El traje nuevo.mkv"));
        Eq(487, sinLetra.Indice, "sin letra sigue siendo el 487");
        Eq(null, sinLetra.SubSegmento, "y no se inventa segmento");

        // La forma corta también: «E413b»
        var corta = SignalExtractor.Extract(F("Doraemon E413b - En busca de una sonrisa.mkv"));
        Eq(413, corta.Indice, "«E413b» también");
        Eq("b", corta.SubSegmento, "con su letra");

        // Y lo que NO debe partirse: una letra pegada que es parte de otra palabra
        var falsa = SignalExtractor.Extract(F("Doraemon - S01E02best of.mkv"));
        Eq(null, falsa.SubSegmento, "«E02best» no es un segmento");

        // Ida y vuelta completa: lo que escribe la plantilla se relee igual.
        var cat = ReindexCatalog.Parse("""
        {
          "esquema": "reindex/1.0",
          "serie": "Doraemon (2005)",
          "episodios": [
            { "num": 487, "temporada": 2017,
              "titulos": { "es": ["El traje nuevo del emperador", "La niña de los zapatos rojos"] } }
          ]
        }
        """);
        var plantilla = new LibraryTemplate(LibraryTemplate.PatronPorDefecto);
        var nombre = plantilla.Render(cat, cat.PorNum(487)!, conLetra)!;
        var releido = SignalExtractor.Extract(F(nombre));
        Eq(487, releido.Indice, "el nombre que escribe la app se relee con su número");
        Eq("b", releido.SubSegmento, "y con su letra: la decisión no se pierde");

        // Y el motor, con esa señal, se queda en la historia b — no propone el episodio entero
        var r = ReindexEngine.Resolve(new[] { conLetra }, cat)[0];
        Eq(487, r.Episodio?.Num, "sigue siendo el 487");
        Eq("b", r.Archivo.SubSegmento, "y sigue siendo solo su historia");
    }

    // ─────────────── Un marcador explícito manda sobre un número suelto ───────────────

    /// <summary>
    /// Salió auditando: hay catálogos cuyos títulos llevan números entre corchetes
    /// («Cuido de mamá (LA)[30] + Vamos a un restaurante (ES)[31]»). Al escribir el nombre
    /// propuesto, ese [30] acababa dentro del fichero, y al volver a leerlo ganaba al
    /// «S2005E536» que la propia app había puesto: la segunda simulación creía que era el
    /// episodio 30. Renombrar y re-simular tiene que dar SIEMPRE lo mismo, o los datos se
    /// van degradando solos sin que nadie lo vea.
    /// </summary>
    private static void MarcadorManda()
    {
        Seccion("El marcador explícito manda");

        var conRuido = SignalExtractor.Extract(F(
            "Crayon Shin-Chan - S2005E536 - Cuido de mamá (LA)[30] + Vamos a cenar (ES)[31].mkv"));
        Eq(536, conRuido.Indice, "el SxxExx gana a un [30] metido en el título");

        // La convención de corchetes sigue viva para los ficheros que NO traen marcador
        var soloCorchetes = SignalExtractor.Extract(F("[499b] La niña de los zapatos rojos.mkv"));
        Eq(499, soloCorchetes.Indice, "sin marcador, el corchete sigue mandando");
        Eq("b", soloCorchetes.SubSegmento, "y conserva su letra de historia");

        // La propiedad que de verdad importa: ida y vuelta sin deriva.
        var cat = ReindexCatalog.Parse("""
        {
          "esquema": "reindex/1.0",
          "serie": "Crayon Shin-Chan",
          "episodios": [
            { "num": 536, "temporada": 2005,
              "titulos": { "es": ["Cuido de mamá (LA)[30]", "Vamos a cenar (ES)[31]"] } }
          ]
        }
        """);
        var plantilla = new LibraryTemplate(LibraryTemplate.PatronPorDefecto);
        var ep = cat.PorNum(536)!;
        var nombre = plantilla.Render(cat, ep, conRuido)!;
        var releido = SignalExtractor.Extract(F(nombre));
        Eq(536, releido.Indice, "el nombre que escribe la app se relee como el mismo episodio");
    }

    // ─────────────── Otras formas de separar las historias ───────────────

    /// <summary>
    /// Cada web reparte las dos historias de un capítulo a su manera: «A ┃ B», «A + B» o
    /// «A - B», y muchas dejan pegada la etiqueta de la fuente («[Boing HD]»). Si no se
    /// reconocen, el título entero se compara contra uno solo del catálogo y sale un 58 %
    /// — por debajo del umbral, o sea trabajo manual para algo que era evidente.
    /// </summary>
    private static void SepararHistorias()
    {
        Seccion("Otras formas de separar las historias");

        var conMas = SignalExtractor.Extract(F("Doraemon (2005) - S2017E485 - El elixir duplicador + Gimnastiescuela a la fuerza.mkv"));
        Eq(true, conMas.TituloNombre?.EndsWith("El elixir duplicador + Gimnastiescuela a la fuerza") ?? false,
            "el título entero se conserva");
        Eq(2, conMas.Segmentos.Count, "«+» separa dos historias");
        Eq("El elixir duplicador", conMas.Segmentos[0], "primera historia");
        Eq("Gimnastiescuela a la fuerza", conMas.Segmentos[1], "segunda historia");

        var conGuion = SignalExtractor.Extract(F("1311 El Brazalete - Escapada A Una Isla Desierta [Boing HD].mkv"));
        Eq(2, conGuion.Segmentos.Count, "« - » también separa");
        Eq("El Brazalete", conGuion.Segmentos[0], "sin arrastrar el guion");
        Eq(false, conGuion.TituloNombre?.Contains("Boing") ?? false, "la etiqueta de la fuente se cae");
        Eq(false, conGuion.Segmentos[1].Contains("["), "y no se cuela en la última historia");

        // Un guion SIN espacios alrededor es parte del título, no un separador
        var guionPegado = SignalExtractor.Extract(F("Doraemon (2005) - S2005E12 - El súper-guante.mkv"));
        Eq("El súper-guante", guionPegado.TituloNombre, "«súper-guante» no se parte");
        Eq(0, guionPegado.Segmentos.Count, "y no inventa segmentos");
    }

    // ─────────────── Cuando dos ficheros reclaman el mismo episodio ───────────────

    /// <summary>
    /// Que dos ficheros apunten al mismo episodio no siempre es una pelea. Si uno lo clava
    /// por título y el otro solo lo rozaba, marcar a los DOS para revisión manual convierte
    /// un caso obvio en trabajo. Solo se baja al ganador cuando el rival era comparable.
    /// </summary>
    private static void PeleaPorElMismoEpisodio()
    {
        Seccion("Dos ficheros, un episodio");

        var cat = ReindexCatalog.Parse("""
        {
          "esquema": "reindex/1.0",
          "serie": "Doraemon (2005)",
          "episodios": [
            { "num": 100, "titulos": { "es": ["El pistolero Nobita"] } },
            { "num": 200, "titulos": { "es": ["La cuerda encantada"] } }
          ]
        }
        """);

        // Uno lo clava por título; el otro solo lleva el número y ningún título que ayude.
        var claro = ReindexEngine.Resolve(new[]
        {
            SignalExtractor.Extract(F("Doraemon (2005) - S2005E100 - El pistolero Nobita.mkv")),
            SignalExtractor.Extract(F("Doraemon (2005) S2005E100.mkv")),
        }, cat);
        var porTitulo = claro.First(r => r.Archivo.NombreArchivo.Contains("pistolero"));
        Eq(100, porTitulo.Episodio?.Num, "el que trae el título se queda el episodio");
        Eq(ReindexConfianza.Alta, porTitulo.Confianza,
            "y NO se le baja la confianza: el rival no era rival");

        // Aquí sí hay pelea: los dos traen el mismo título, uno de los dos sobra.
        var pelea = ReindexEngine.Resolve(new[]
        {
            SignalExtractor.Extract(F("Doraemon (2005) - S2005E100 - El pistolero Nobita.mkv")),
            SignalExtractor.Extract(F("Doraemon (2005) - S2005E100 - El pistolero Nobita (copia).mkv")),
        }, cat);
        Eq(1, pelea.Count(r => r.Confianza == ReindexConfianza.Alta ? false : true) >= 1 ? 1 : 0,
            "con dos títulos idénticos alguien tiene que mirarlo");
    }

    // ─────────────── Recortes: partir un vídeo en tramos ───────────────

    /// <summary>
    /// El modelo es UNO: una lista de tramos. Se arranca con el vídeo entero y cada corte
    /// parte en dos el tramo donde cae. Así «divídelo en dos episodios» y «quítale este
    /// trozo» son la misma herramienta, sin dos modos que mantener.
    /// </summary>
    private static void PartirEnTramos()
    {
        Seccion("Recortes · partir en tramos");

        var entero = Tramos.Entero(600);
        Eq(1, entero.Count, "se arranca con un tramo: el vídeo entero");
        Eq(0.0, entero[0].Inicio, "empieza en cero");
        Eq(600.0, entero[0].Fin, "y acaba en la duración");

        var dos = Tramos.Partir(entero, 250);
        Eq(2, dos.Count, "un corte deja dos tramos");
        Eq(250.0, dos[0].Fin, "el primero acaba en el corte");
        Eq(250.0, dos[1].Inicio, "y el segundo empieza ahí mismo, sin hueco");
        Eq(600.0, dos[1].Fin, "el segundo llega hasta el final");

        var tres = Tramos.Partir(dos, 400);
        Eq(3, tres.Count, "cortar en el segundo tramo lo parte a él");
        EqLista(new[] { 0.0, 250.0, 400.0 }, tres.Select(t => t.Inicio), "los inicios encadenan");

        // Cortes que no valen: en los bordes o fuera. Sin esto saldría un tramo de duración
        // cero, que ffmpeg acepta y produce un fichero vacío.
        Eq(2, Tramos.Partir(dos, 250).Count, "cortar justo en una junta no hace nada");
        Eq(2, Tramos.Partir(dos, 0).Count, "cortar en el inicio no hace nada");
        Eq(2, Tramos.Partir(dos, 600).Count, "cortar en el final no hace nada");
        Eq(2, Tramos.Partir(dos, 999).Count, "cortar fuera del vídeo no hace nada");

        // Quitar un trozo = borrar su tramo. Los demás NO se recolocan: cada uno guarda su
        // sitio en el vídeo original, que es de donde se va a extraer.
        var sinElMedio = Tramos.Quitar(tres, 1);
        Eq(2, sinElMedio.Count, "quitar un tramo lo descarta");
        EqLista(new[] { 0.0, 400.0 }, sinElMedio.Select(t => t.Inicio),
            "los que quedan conservan su posición en el original");
    }

    /// <summary>
    /// Arrastrar una junta en la línea de tiempo. Es la otra mitad de «cortar»: primero se
    /// parte por donde sea, luego se afina tirando del tirador hasta el fotograma exacto.
    ///
    /// Lo que no puede pasar bajo ningún concepto es que un tramo se dé la vuelta («desde el
    /// 400 hasta el 250») o se quede a cero: ffmpeg acepta ambos y saca ficheros vacíos o
    /// basura, y el usuario no se entera hasta que abre el resultado.
    /// </summary>
    private static void ArrastrarLasJuntas()
    {
        Seccion("Recortes · arrastrar las juntas");

        var dos = Tramos.Partir(Tramos.Entero(600), 250);

        // Una junta compartida es UNA junta, no dos bordes sueltos: mover el final del
        // primero arrastra con él el principio del segundo. Si no, quedaría un agujero en
        // medio del vídeo que nadie ha pedido.
        var movida = Tramos.MoverJunta(dos, 0, Extremo.Fin, 300, 600);
        Eq(2, movida.Count, "arrastrar no crea ni quita tramos");
        Eq(300.0, movida[0].Fin, "el final del primero se va a donde se suelta");
        Eq(300.0, movida[1].Inicio, "y el principio del segundo va con él, sin dejar hueco");
        Eq(0.0, movida[0].Inicio, "el extremo de fuera no se toca");
        Eq(600.0, movida[1].Fin, "ni el del final");

        // Da igual por qué lado se coja: es la misma junta.
        var porElOtroLado = Tramos.MoverJunta(dos, 1, Extremo.Inicio, 300, 600);
        EqLista(new[] { 0.0, 300.0 }, porElOtroLado.Select(t => t.Inicio),
            "cogerla por el tramo de la derecha hace exactamente lo mismo");
        EqLista(new[] { 300.0, 600.0 }, porElOtroLado.Select(t => t.Fin), "y deja los mismos finales");

        // Los vecinos son el tope.
        var pasada = Tramos.MoverJunta(dos, 0, Extremo.Fin, 5000, 600);
        Eq(true, pasada[0].Fin < 600.0, "la junta no se empuja más allá del tramo vecino");
        Eq(true, pasada[1].Fin > pasada[1].Inicio, "y el vecino nunca se queda del revés");
        var atras = Tramos.MoverJunta(dos, 0, Extremo.Fin, -50, 600);
        Eq(true, atras[0].Fin > atras[0].Inicio, "tirando hacia atrás tampoco se invierte el propio tramo");

        // Recortar la cabecera y la cola es mover una junta también, solo que el tope es el
        // vídeo en vez de un vecino.
        var entero = Tramos.Entero(600);
        Eq(30.0, Tramos.MoverJunta(entero, 0, Extremo.Inicio, 30, 600)[0].Inicio,
            "al primer tramo se le puede recortar la cabecera");
        Eq(500.0, Tramos.MoverJunta(entero, 0, Extremo.Fin, 500, 600)[0].Fin, "y al último la cola");
        Eq(0.0, Tramos.MoverJunta(entero, 0, Extremo.Inicio, -10, 600)[0].Inicio,
            "por delante no se pasa de cero");
        Eq(600.0, Tramos.MoverJunta(entero, 0, Extremo.Fin, 900, 600)[0].Fin,
            "por detrás no se pasa del final del vídeo");

        // Con un hueco en medio (alguien quitó un tramo) los dos bordes ya NO son la misma
        // junta: mover uno no puede arrastrar al otro al otro lado del agujero.
        var tres = Tramos.Partir(Tramos.Partir(Tramos.Entero(600), 200), 400);
        var conHueco = Tramos.Quitar(tres, 1);          // quedan 0–200 y 400–600
        var estirado = Tramos.MoverJunta(conHueco, 0, Extremo.Fin, 350, 600);
        Eq(350.0, estirado[0].Fin, "el borde suelto se estira por el hueco");
        Eq(400.0, estirado[1].Inicio, "y el del otro lado del hueco se queda donde estaba");
        Eq(400.0, Tramos.MoverJunta(conHueco, 0, Extremo.Fin, 550, 600)[0].Fin,
            "pero no se mete dentro del tramo siguiente");

        // Arrastrar una junta no es renombrar nada.
        var conNombre = new List<Tramo> { new(0, 250, "Primera historia"), new(250, 600, "Segunda") };
        EqLista(new[] { "Primera historia", "Segunda" },
            Tramos.MoverJunta(conNombre, 0, Extremo.Fin, 300, 600).Select(t => t.Nombre),
            "cada nombre se queda con su tramo");

        // Un índice que no existe no puede tumbar la app.
        Eq(2, Tramos.MoverJunta(dos, 9, Extremo.Fin, 100, 600).Count,
            "un índice fuera de la lista no hace nada");
        Eq(2, Tramos.MoverJunta(dos, -1, Extremo.Inicio, 100, 600).Count, "ni uno negativo");
    }

    /// <summary>
    /// Un fichero con dos historias suele decirlo en el nombre. Si los tramos coinciden con
    /// las historias, se nombran solos: es justo el caso de partir un capítulo doble.
    /// </summary>
    private static void NombresDeLosTramos()
    {
        Seccion("Recortes · nombres sugeridos");

        var dos = Tramos.Nombrar("1287 El diligente amuleto multiusos ┃ Los regalos de la manivela del tiempo", 2);
        Eq("1287 El diligente amuleto multiusos", dos[0], "la primera historia da nombre al primer tramo");
        Eq("Los regalos de la manivela del tiempo", dos[1], "y la segunda al segundo");

        EqLista(new[] { "A", "B" }, Tramos.Nombrar("A + B", 2), "el separador «+» también vale");
        EqLista(new[] { "A", "B" }, Tramos.Nombrar("A | B", 2), "y la barra vertical");

        // Si no cuadran, no se inventa: numerar es honesto, repartir mal no.
        EqLista(new[] { "capitulo - 1", "capitulo - 2", "capitulo - 3" }, Tramos.Nombrar("capitulo", 3),
            "sin historias en el nombre, se numera");
        EqLista(new[] { "A ┃ B - 1", "A ┃ B - 2", "A ┃ B - 3" }, Tramos.Nombrar("A ┃ B", 3),
            "dos historias y tres tramos: no cuadra, se numera");
        EqLista(new[] { "A ┃ B" }, Tramos.Nombrar("A ┃ B", 1),
            "un solo tramo conserva el nombre entero, sin sufijo");
    }

    /// <summary>
    /// Cómo se le dice a ffmpeg «solo este tramo». El «-ss» va ANTES del «-i» porque así
    /// salta por índice en vez de decodificar desde el principio: en un vídeo de 20 minutos
    /// es la diferencia entre empezar al instante o tras un rato.
    /// </summary>
    private static void ArgsDelTramo()
    {
        Seccion("Recortes · argumentos del tramo");

        var (antes, despues) = Tramos.ArgsFfmpeg(null, null);
        Eq(0, antes.Length + despues.Length, "sin tramo no se añade nada (el vídeo entero)");

        var (a1, d1) = Tramos.ArgsFfmpeg(90, 600);
        EqLista(new[] { "-ss", "90" }, a1, "el salto va antes del fichero de entrada");
        EqLista(new[] { "-t", "600" }, d1, "y la duración después");

        // El punto decimal SIEMPRE con punto: en configuración española «10,5» se escribiría
        // con coma y ffmpeg lo leería como otra cosa. Es un fallo que no daría la cara en
        // una máquina en inglés.
        var (a2, d2) = Tramos.ArgsFfmpeg(10.5, 30.25);
        EqLista(new[] { "-ss", "10.5" }, a2, "decimal con punto, no con coma");
        EqLista(new[] { "-t", "30.25" }, d2, "también en la duración");

        var (a3, _) = Tramos.ArgsFfmpeg(0, 100);
        Eq(0, a3.Length, "empezar en cero no necesita salto");
    }

    // ─────────────── Ficheros que están solo en la nube ───────────────

    /// <summary>
    /// Con «Archivos a petición» (OneDrive) un fichero del disco puede ser un MARCADOR:
    /// leer un solo byte lo descarga ENTERO. Medido: sondear con ffprobe un marcador de
    /// 277 MB se lo bajó completo en 18 s. Identificar una carpeta no puede vaciarle a
    /// nadie la tarifa de datos ni el disco, así que hay que reconocerlos antes de tocarlos.
    /// </summary>
    private static void MarcadorDeNube()
    {
        Seccion("Ficheros solo en la nube");

        Eq(true, Nube.EsMarcador(FileAttributes.Offline), "OFFLINE es un marcador");
        Eq(true, Nube.EsMarcador((FileAttributes)Nube.RecallOnDataAccess),
            "RECALL_ON_DATA_ACCESS es un marcador");
        Eq(true, Nube.EsMarcador(FileAttributes.Archive | FileAttributes.ReparsePoint |
                                 FileAttributes.Offline), "mezclado con otros, también");
        Eq(false, Nube.EsMarcador(FileAttributes.Archive), "un fichero normal no lo es");

        // El caso que de verdad importa: un fichero de OneDrive YA descargado conserva su
        // punto de reanálisis. Confundirlo con un marcador dejaría de sondear ficheros que
        // están en el disco — el error contrario, y también gratuito.
        Eq(false, Nube.EsMarcador(FileAttributes.Archive | FileAttributes.ReparsePoint),
            "descargado (Archive+ReparsePoint, sin OFFLINE) NO es un marcador");

        // ── Devolverlo a la nube después de mirarlo ──
        // Ver un vídeo para identificarlo no debería dejar 250 MB ocupados para siempre.
        // El aviso de «libera este fichero» es el mismo para todos los proveedores: lo
        // define Windows (Cloud Files API), no OneDrive — quitar ANCLADO y poner SOLTADO.
        const int anclado = Nube.Anclado, soltado = Nube.Soltado;
        const int archivo = (int)FileAttributes.Archive, reparse = (int)FileAttributes.ReparsePoint;

        Eq(archivo | soltado, Nube.AtributosParaLiberar(archivo | anclado),
            "quita ANCLADO y pone SOLTADO");
        Eq(archivo | reparse | soltado, Nube.AtributosParaLiberar(archivo | reparse),
            "conserva el resto de atributos intactos");
        Eq(archivo | soltado, Nube.AtributosParaLiberar(archivo | soltado),
            "si ya estaba soltado, no cambia nada");
        Eq(0, Nube.AtributosParaLiberar(anclado) & anclado, "ANCLADO nunca sobrevive");
    }

    // ─────────────── El título del .nfo compañero ───────────────

    /// <summary>
    /// Los ficheros sin título en el nombre suelen llevarlo en su .nfo (el XML de Kodi).
    /// Es la fuente ideal: leer un XML es instantáneo, sondear el vídeo en la nube no.
    /// </summary>
    private static void TituloDelNfo()
    {
        Seccion("Título del .nfo compañero");

        Eq("El pistolero Nobita", NfoTitulo.Extraer("""
            <?xml version="1.0" encoding="utf-8"?>
            <episodedetails><title>El pistolero Nobita</title><season>2018</season></episodedetails>
            """), "saca el <title> del XML de Kodi");
        Eq("A ┃ B", NfoTitulo.Extraer("<episodedetails><title>A ┃ B</title></episodedetails>"),
            "los separadores de historias sobreviven");
        Eq(null, NfoTitulo.Extraer("<episodedetails><season>2018</season></episodedetails>"),
            "sin <title> no hay título");
        Eq(null, NfoTitulo.Extraer("esto no es xml <"), "un XML roto no revienta: null");
        Eq(null, NfoTitulo.Extraer("<episodedetails><title>  </title></episodedetails>"),
            "un título en blanco no cuenta");

        // Y el motor casa por CADA historia del metadato, no solo por el texto entero
        var cat = ReindexCatalog.Parse("""
        {
          "esquema": "reindex/1.0",
          "serie": "Doraemon (2005)",
          "episodios": [
            { "num": 496, "temporada": 2018,
              "titulos": { "es": ["Los huevos de cuento", "Juegos de espías"] } }
          ]
        }
        """);
        var señal = SignalExtractor.Extract(F("Doraemon (2005) S2018E03.mkv", "Season 2018"),
            "Season 2018", tituloMeta: "Los huevos de cuento ┃ Juegos de espías");
        var r = ReindexEngine.Resolve(new[] { señal }, cat)[0];
        Eq(496, r.Episodio?.Num, "el título del metadato multi-historia identifica al episodio");
        Eq(ReindexConfianza.Alta, r.Confianza, "y con confianza: las dos historias casan");
    }

    // ─────────────── La decisión «es solo una historia» se recuerda ───────────────

    private static void SegmentoRecordado()
    {
        Seccion("Segmento elegido a mano y recordado");

        var cat = ReindexCatalog.Parse(CatalogoDePrueba);
        var ruta = F("algo raro 423.mkv");
        var señal = SignalExtractor.Extract(ruta);

        // El usuario decidió: este fichero es la historia «b» del episodio 12
        var overrides = new Dictionary<string, ReindexOverride>
        {
            [señal.Fingerprint] = new() { Num = 12, Seg = "b", Serie = "Serie de prueba" },
        };

        var r = ReindexEngine.Resolve(new[] { señal }, cat, overrides)[0];
        Eq(12, r.Episodio?.Num, "la decisión manda (P0)");
        Eq("b", r.Archivo.SubSegmento, "y trae el segmento pegado al fichero");
        Eq(ReindexEstado.Corregido, r.Estado, "renombrar a E12b es corregir");
        Eq(ReindexConfianza.Alta, r.Confianza, "una decisión humana no se duda");
    }

    // ─────────────────────────── Lista ISO 639-1 ───────────────────────────

    /// <summary>
    /// El selector de idiomas pasa de siete opciones fijas a la norma ISO entera. Con esa
    /// cantidad, encontrar el idioma DEPENDE del buscador: si buscar «japones» sin tilde no
    /// da nada, la lista larga es peor que la corta.
    /// </summary>
    private static void ListaIso()
    {
        Seccion("Idiomas ISO 639-1");

        Assert(IsoLanguages.Todos.Length > 150, $"la norma entera, no una muestra ({IsoLanguages.Todos.Length})");
        Assert(IsoLanguages.Todos.Select(i => i.Codigo).Distinct().Count() == IsoLanguages.Todos.Length,
            "ningún código repetido");
        Assert(IsoLanguages.Todos.All(i => i.Nombre.Length > 0), "todos tienen nombre");

        // ── buscar como se escribe de verdad: sin tildes y a medias ──
        string Primero(string q) => IsoLanguages.Buscar(q).FirstOrDefault()?.Codigo ?? "(nada)";

        Eq("ja", Primero("japones"), "«japones» sin tilde encuentra el japonés");
        Eq("ja", Primero("Japonés"), "y con tilde también");
        Eq("es", Primero("españ"), "a medio escribir");
        Eq("de", Primero("de"), "un código exacto gana a cualquier nombre que contenga «de»");
        Eq("ko", Primero("core"), "«coreano» por el principio del nombre");
        Assert(IsoLanguages.Buscar("zzzz").Count == 0, "lo que no existe no devuelve nada");

        // Sin escribir nada se ven primero los que se usan aquí, no los alfabéticos
        var vacio = IsoLanguages.Buscar("");
        Assert(vacio.Count > 150, "sin filtro están todos");
        Eq("es", vacio[0].Codigo, "y arriba los de andar por casa");

        // ── códigos viejos: los catálogos ya importados no pueden dejar de leerse ──
        Eq("ja", IsoLanguages.Normalizar("jp"), "«jp» era lo que usaba la app: se traduce al ISO «ja»");
        Eq("es-419", IsoLanguages.Normalizar("lat"), "«lat» era el hispanoamericano");
        Eq("es", IsoLanguages.Normalizar("ES"), "las mayúsculas no crean un idioma nuevo");
        Eq("qqq", IsoLanguages.Normalizar("qqq"), "un código desconocido se deja tal cual");

        Eq("Japonés", IsoLanguages.Nombre("ja"), "nombre en español");
        Eq("Japonés", IsoLanguages.Nombre("jp"), "y el código viejo llega al mismo sitio");
        Eq("qqq", IsoLanguages.Nombre("qqq"), "de un código que no conocemos se enseña el código");

        // El hispanoamericano no está en ISO 639-1 pero sí en la biblioteca del usuario
        Assert(IsoLanguages.Todos.Any(i => i.Codigo == "es-419"), "el español de Hispanoamérica existe");
    }

    // ─────────────────────── Buscador del catálogo ───────────────────────

    /// <summary>
    /// El explorador existe para verificar propuestas sin abrir el JSON a mano. Si su
    /// buscador no encuentra lo mismo que encontraría el motor, desinforma.
    /// </summary>
    private static void BuscadorDeCatalogo()
    {
        Seccion("Buscador del catálogo");

        var cat = ReindexCatalog.Parse("""
        {
          "esquema": "reindex/1.0",
          "serie": "Doraemon (2005)",
          "episodios": [
            { "num": 17,  "titulos": { "es": ["El espejo de la verdad"] } },
            { "num": 173, "titulos": { "es": ["El planeta espejo"] } },
            { "num": 175, "titulos": { "es": ["Aventura en el mundo de los insectos"] } },
            { "num": 300, "titulos": { "es": ["Bienvenidos (segunda parte)"] } },
            { "num": 400 }
          ]
        }
        """);

        string Nums(string q) => string.Join(",", CatalogSearch.Filtrar(cat, q).Select(e => e.Num));

        Eq("17,173,175,300,400", Nums(""), "sin consulta, el catálogo entero");
        Eq("17,173,175", Nums("17"), "por número: el exacto primero, luego los que empiezan igual");
        Eq("175", Nums("175"), "un número completo va directo");
        Eq("17,173", Nums("espejo"), "por título, en cualquier posición");
        Eq("173", Nums("planeta espejo"), "frase completa");
        Eq("173", Nums("PLANETA ESPEJO"), "sin distinguir mayúsculas");
        Eq("300", Nums("2.ª parte"), "con la misma normalización que el motor: «2.ª» = «segunda»");
        Eq("", Nums("zzzz"), "lo que no está no devuelve nada");
    }

    // ──────────── El nombre lleva la serie delante y no debe estorbar ────────────

    /// <summary>
    /// El caso real que lo destapó: «Doraemon (2005) S2009E175 - El planeta espejo.mkv».
    /// El planeta espejo es el E173 del catálogo, con su título en español — pero el trozo
    /// «Doraemon (2005)» que va dentro del nombre contaminaba el título (similitud 0,71,
    /// bajo el umbral), la vía por título moría y ganaba el NÚMERO equivocado del fichero.
    /// Los multi-historia se salvaban de rebote: su segundo segmento va limpio.
    /// </summary>
    private static void PrefijoDeSerie()
    {
        Seccion("El prefijo de la serie no estorba al título");

        var cat = ReindexCatalog.Parse("""
        {
          "esquema": "reindex/1.0",
          "serie": "Doraemon (2005)",
          "episodios": [
            { "num": 173, "temporada": 2009, "fecha": "2009-07-03",
              "titulos": { "es": ["El planeta espejo"] } },
            { "num": 175, "temporada": 2009, "fecha": "2009-07-17",
              "titulos": { "es": ["Aventura en el mundo de los insectos"] } },
            { "num": 165, "temporada": 2009, "fecha": "2009-05-08",
              "titulos": { "es": ["Bienvenidos al centro de la Tierra (segunda parte)"] } }
          ]
        }
        """);

        var r = Uno(cat, F("Doraemon (2005) S2009E175 - El planeta espejo.mkv", "Season 2009"));
        Eq(173, r.Episodio?.Num, "gana el título, no el número que trae el fichero");
        Eq(ReindexEstado.Corregido, r.Estado, "y propone corregir el 175 a 173");
        Assert(r.Score >= TitleMatch.UmbralTitulo, $"el parecido supera el umbral ({r.Score:0.00})");

        // Con ordinal abreviado, además del prefijo
        var r2 = Uno(cat, F("Doraemon (2005) S2009E167 - Bienvenidos al centro de la tierra (2.ª parte).mkv", "Season 2009"));
        Eq(165, r2.Episodio?.Num, "«(2.ª parte)» encuentra a «(segunda parte)»");
        Assert(r2.Score >= TitleMatch.UmbralTitulo, $"por encima del umbral ({r2.Score:0.00})");

        // Y el caso en que el título ES el nombre de la serie no se queda vacío
        var r3 = Uno(cat, F("El planeta espejo.mkv", "Season 2009"));
        Eq(173, r3.Episodio?.Num, "sin prefijo sigue funcionando igual");
    }

    // ──────────────── Biblioteca con subcarpetas de temporada ────────────────

    /// <summary>
    /// Una biblioteca de verdad no es una carpeta plana: «Doraemon (2005)» son nueve
    /// «Season 20xx» y ni un solo vídeo en la raíz. Mirando solo el primer nivel, esa
    /// carpeta con cientos de ficheros se veía como «no hay vídeos».
    /// </summary>
    private static void BibliotecaPorTemporadas()
    {
        Seccion("Biblioteca con subcarpetas de temporada");

        var raiz = Path.Combine("D", "Doraemon (2005)");
        string Bajo(params string[] tramos) => Path.Combine(new[] { raiz }.Concat(tramos).ToArray());

        // ── a qué temporada pertenece cada fichero ──
        Eq("Season 2005", LibraryScan.Grupo(raiz, Bajo("Season 2005", "a.mkv")),
            "la subcarpeta directa es el grupo");
        Eq("Season 2005", LibraryScan.Grupo(raiz, Bajo("Season 2005", "Extras", "b.mkv")),
            "lo que cuelga más abajo sigue perteneciendo a su temporada");
        Eq("", LibraryScan.Grupo(raiz, Bajo("suelto.mkv")), "un vídeo en la raíz no tiene temporada");
        Eq("", LibraryScan.Grupo(raiz, Path.Combine("D", "Otra serie", "x.mkv")),
            "algo de fuera de la raíz no se cuela en ningún grupo");

        Eq("Season 2005", LibraryScan.Etiqueta("Season 2005"), "la etiqueta es el nombre de la carpeta");
        Eq(LibraryScan.EtiquetaRaiz, LibraryScan.Etiqueta(""), "los sueltos se nombran aparte");

        // ── orden de lectura: temporadas en orden, lo raro al final ──
        var carpetas = new[] { "Season 2013", "", "Especiales", "Season 2005", "Temporada 3" };
        var ordenadas = carpetas.OrderBy(LibraryScan.Orden)
                                .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
                                .ToArray();
        Eq("Temporada 3|Season 2005|Season 2013|Especiales|",
            string.Join("|", ordenadas),
            "temporadas por número; lo que no lleva número después; los sueltos, últimos");

        Eq(2005, SignalExtractor.TemporadaDeCarpeta("Season 2005"), "«Season 2005»");
        Eq(3, SignalExtractor.TemporadaDeCarpeta("Temporada 3"), "«Temporada 3»");
        Eq(3, SignalExtractor.TemporadaDeCarpeta("S03"), "«S03»");
        Eq(2007, SignalExtractor.TemporadaDeCarpeta("2007"), "un año a secas");
        Eq(null, SignalExtractor.TemporadaDeCarpeta("Especiales"), "«Especiales» no es un número");

        // ── el recorrido de verdad, sobre disco: es el que se rompió ──
        var tmp = Path.Combine(Path.GetTempPath(), "shrinkvideo-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "Season 2005"));
            Directory.CreateDirectory(Path.Combine(tmp, "Season 2006"));
            foreach (var r in new[]
                     {
                         Path.Combine(tmp, "Season 2005", "b.mkv"),
                         Path.Combine(tmp, "Season 2005", "a.mkv"),
                         Path.Combine(tmp, "Season 2005", "leeme.txt"),
                         Path.Combine(tmp, "Season 2006", "c.mp4"),
                         Path.Combine(tmp, "suelto.mkv"),
                     })
                File.WriteAllText(r, "");

            var extensiones = new[] { ".mkv", ".mp4" };
            var hallados = LibraryScan.Escanear(tmp, extensiones);

            Eq(4, hallados.Length, "encuentra los vídeos de las subcarpetas, no solo los del primer nivel");
            Eq("a.mkv|b.mkv|c.mp4|suelto.mkv",
                string.Join("|", hallados.Select(Path.GetFileName)),
                "2005 antes que 2006, alfabético dentro, y el suelto al final");
            Assert(hallados.All(h => Path.GetExtension(h) != ".txt"), "lo que no es vídeo se queda fuera");

            Eq(0, LibraryScan.Escanear(Path.Combine(tmp, "no-existe"), extensiones).Length,
                "una carpeta que no está no revienta el escaneo");
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { /* limpieza */ } }
    }

    // ───────────────────────────── utilidades ─────────────────────────────

    /// <summary>
    /// Ruta de fixture portable. En Linux la barra invertida NO separa directorios, asi
    /// que una ruta «C:\S\x.mkv» literal deja a GetFileName devolviendo la cadena entera
    /// y el extractor no encuentra ni el nombre ni la extension. Componerla con el
    /// separador del sistema hace que los tests digan lo mismo aqui y en el CI de Linux.
    /// </summary>
    private static string F(string nombre, string carpeta = "S") => Path.Combine(carpeta, nombre);

    private static ReindexResolution Uno(ReindexCatalog cat, string ruta) =>
        ReindexEngine.Resolve(new[] { SignalExtractor.Extract(ruta) }, cat)[0];

    /// <summary>
    /// Catálogo mínimo con las trampas de verdad: numeración con hueco (falta el 13),
    /// un remake 445 números después con el título idéntico, y un especial.
    /// </summary>
    private const string CatalogoDePrueba = """
    {
      "esquema": "reindex/1.0",
      "serie": "Serie de prueba",
      "clave": "oficial",
      "notas": "catálogo de test",
      "total": 6,
      "episodios": [
        { "num": 10, "temporada": 2005, "fecha": "2005-01-10", "especial": false,
          "titulos": { "es": ["El interruptor del despotismo"] }, "aliases": [] },
        { "num": 11, "temporada": 2005, "fecha": "2005-01-17", "especial": false,
          "titulos": { "es": ["La robochica me ama"] }, "aliases": [] },
        { "num": 12, "temporada": 2005, "fecha": "2005-01-24", "especial": false,
          "titulos": { "es": ["Las galletas mágicas"] }, "aliases": [] },
        { "num": 20, "temporada": 2005, "fecha": "2005-02-28", "especial": false,
          "titulos": { "es": ["Una ciudad de sueño, Nobitaland"] }, "aliases": [] },
        { "num": 455, "temporada": 2012, "fecha": "2012-05-05", "especial": false,
          "titulos": { "es": ["El interruptor del despotismo"] }, "aliases": [] },
        { "num": 901, "temporada": 2005, "fecha": null, "especial": true,
          "titulos": { "es": ["Especial de Navidad"] }, "aliases": [] }
      ]
    }
    """;

    private static void Seccion(string titulo) => Console.WriteLine($"\n▸ {titulo}");

    private static void Assert(bool condicion, string descripcion)
    {
        if (condicion) { _ok++; Console.WriteLine($"  ✓ {descripcion}"); }
        else { _fallos++; Console.WriteLine($"  ✗ {descripcion}"); }
    }

    /// <summary>
    /// Igualdad de secuencias. Hace falta aparte porque el comparador por defecto compara
    /// los arrays por REFERENCIA, y ahí todo assert sobre una lista fallaría siempre.
    /// </summary>
    private static void EqLista<T>(IEnumerable<T> esperado, IEnumerable<T> real, string descripcion)
    {
        var a = esperado.ToList();
        var b = real.ToList();
        if (a.SequenceEqual(b)) { _ok++; Console.WriteLine($"  ✓ {descripcion}"); }
        else
        {
            _fallos++;
            Console.WriteLine($"  ✗ {descripcion}");
            Console.WriteLine($"      esperado: [{string.Join(" · ", a)}]");
            Console.WriteLine($"      real:     [{string.Join(" · ", b)}]");
        }
    }

    private static void Eq<T>(T esperado, T real, string descripcion)
    {
        bool igual = EqualityComparer<T>.Default.Equals(esperado, real);
        if (igual) { _ok++; Console.WriteLine($"  ✓ {descripcion}"); }
        else { _fallos++; Console.WriteLine($"  ✗ {descripcion}\n      esperado: «{esperado}»\n      real:     «{real}»"); }
    }

    /// <summary>Como <see cref="Lanza{T}"/>, pero devuelve la excepción para mirarle el mensaje.</summary>
    private static T? Lanzado<T>(Action accion, string descripcion) where T : Exception
    {
        try { accion(); _fallos++; Console.WriteLine($"  ✗ {descripcion} (no lanzó nada)"); return null; }
        catch (T ex) { _ok++; Console.WriteLine($"  ✓ {descripcion}"); return ex; }
        catch (Exception ex)
        {
            _fallos++;
            Console.WriteLine($"  ✗ {descripcion} (lanzó {ex.GetType().Name}, se esperaba {typeof(T).Name})");
            return null;
        }
    }

    private static void Lanza<T>(Action accion, string descripcion) where T : Exception
    {
        try { accion(); _fallos++; Console.WriteLine($"  ✗ {descripcion} (no lanzó nada)"); }
        catch (T) { _ok++; Console.WriteLine($"  ✓ {descripcion}"); }
        catch (Exception ex)
        {
            _fallos++;
            Console.WriteLine($"  ✗ {descripcion} (lanzó {ex.GetType().Name}, se esperaba {typeof(T).Name})");
        }
    }

    private static string Corta(string s) => s.Length <= 24 ? s : s[..21] + "…";
}
