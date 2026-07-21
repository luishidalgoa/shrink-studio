using System.Text;

namespace ShrinkVideo.Reindex;

/// <summary>
/// Construye el encargo que se le pasa a una IA para que convierta un anexo de episodios
/// (Wikipedia, Fandom, lo que sea) en un catálogo <c>reindex/1.0</c>.
///
/// Existe porque cada anexo está montado a su manera: unos numeran por temporada y otros
/// en continuo, unos llaman a la columna «N.º» y otros «Episodio», unos separan «Título en
/// España» de «Título en Hispanoamérica» y otros solo traen el original. Pedirle a la IA
/// «hazme el JSON» sin más produce catálogos distintos cada vez; el valor está en fijar
/// por escrito las decisiones que si no se toman al azar.
///
/// El esquema va INCRUSTADO en el prompt a propósito: así funciona aunque la IA no pueda
/// abrir la documentación del repositorio.
/// </summary>
public static class CatalogPrompt
{
    /// <summary>Idiomas que se ofrecen, con su etiqueta para la interfaz.</summary>
    public static readonly (string Codigo, string Nombre)[] IdiomasConocidos =
    {
        ("es",  "Español (España)"),
        ("lat", "Español (Hispanoamérica)"),
        ("en",  "Inglés"),
        ("jp",  "Japonés (original)"),
        ("ca",  "Catalán"),
        ("gl",  "Gallego"),
        ("eu",  "Euskera"),
    };

    public static string Nombre(string codigo) =>
        IdiomasConocidos.FirstOrDefault(i => i.Codigo == codigo).Nombre ?? codigo;

    /// <summary>
    /// Redacta el encargo. <paramref name="comparar"/> son los idiomas que el catálogo debe
    /// incluir para PODER reconocer los ficheros; <paramref name="salida"/> es el que se
    /// escribirá en el nombre final.
    /// </summary>
    public static string Build(string serie, string fuente, string salida, IReadOnlyList<string> comparar)
    {
        serie = string.IsNullOrWhiteSpace(serie) ? "(escribe aquí el nombre de la serie)" : serie.Trim();
        fuente = string.IsNullOrWhiteSpace(fuente) ? "(pega aquí la dirección del anexo)" : fuente.Trim();
        salida = string.IsNullOrWhiteSpace(salida) ? "es" : salida.Trim();

        // El de salida SIEMPRE se incluye entre los comparables: sería absurdo escribir un
        // título que el motor no sabe reconocer.
        var idiomas = new List<string> { salida };
        foreach (var c in comparar)
            if (!idiomas.Contains(c, StringComparer.OrdinalIgnoreCase)) idiomas.Add(c);

        var listaIdiomas = string.Join(", ", idiomas.Select(c => $"`{c}` ({Nombre(c)})"));
        var jsonComparar = string.Join(", ", idiomas.Select(c => $"\"{c}\""));

        var sb = new StringBuilder();

        sb.AppendLine("Necesito que conviertas un anexo de episodios en un catálogo JSON. Sigue las");
        sb.AppendLine("instrucciones al pie de la letra: el archivo lo va a leer un programa, no una persona.");
        sb.AppendLine();
        sb.AppendLine($"SERIE:  {serie}");
        sb.AppendLine($"FUENTE: {fuente}");
        sb.AppendLine();
        sb.AppendLine("Lee esa página entera, incluidas TODAS las temporadas y sus tablas.");
        sb.AppendLine();

        sb.AppendLine("## Idiomas");
        sb.AppendLine();
        sb.AppendLine($"Incluye estos idiomas en cada episodio, siempre que la fuente los tenga: {listaIdiomas}.");
        sb.AppendLine();
        sb.AppendLine($"El idioma `{salida}` es el que se escribirá en el nombre del fichero. Los demás NO se");
        sb.AppendLine("escriben, pero hacen falta igual: sirven para reconocer ficheros cuyo nombre está en");
        sb.AppendLine("otro idioma. Es decir, un fichero titulado en inglés se identifica gracias al título");
        sb.AppendLine($"inglés y se renombra con el título en `{salida}`. Por eso conviene no escatimar idiomas.");
        sb.AppendLine();
        sb.AppendLine("Si la fuente no trae alguno, omite esa clave en ese episodio. No inventes traducciones");
        sb.AppendLine("ni rellenes con el título de otro idioma: un título inventado provoca renombrados");
        sb.AppendLine("equivocados, que es el peor resultado posible.");
        sb.AppendLine();

        sb.AppendLine("## Cómo interpretar la fuente");
        sb.AppendLine();
        sb.AppendLine("Cada anexo está montado a su manera. Antes de escribir nada, decide:");
        sb.AppendLine();
        sb.AppendLine("1. **Qué columna es el número. Esta es la decisión que más te puedes equivocar.**");
        sb.AppendLine("   Muchos anexos traen VARIAS numeraciones a la vez, y no dan el mismo resultado:");
        sb.AppendLine();
        sb.AppendLine("   - **Número de transmisión** (u «orden de emisión»): cuenta los pases en el orden");
        sb.AppendLine("     en que se emitieron, con los especiales ocupando su sitio en la secuencia.");
        sb.AppendLine("   - **Número de episodio** (u «oficial»): la numeración canónica de la serie, que");
        sb.AppendLine("     suele dejar los especiales fuera, salta números y no cuadra con el orden real.");
        sb.AppendLine();
        sb.AppendLine("   Usa el **número de TRANSMISIÓN** salvo que se te diga otra cosa: es el que suele");
        sb.AppendLine("   coincidir con cómo están numerados los ficheros de una colección, porque se");
        sb.AppendLine("   descargan y se guardan en el orden en que se emitieron.");
        sb.AppendLine();
        sb.AppendLine("   Ejemplo real de Doraemon (2005): el estreno del 15-04-2005 es la **transmisión 1**,");
        sb.AppendLine("   pero en la numeración oficial es un ESPECIAL, y el «episodio 1» oficial es en");
        sb.AppendLine("   realidad la transmisión 2. Elegir la numeración equivocada desplaza la serie");
        sb.AppendLine("   entera y hace que casi nada encaje.");
        sb.AppendLine();
        sb.AppendLine("   Si el anexo solo numera por temporada, numera tú en continuo desde 1 siguiendo");
        sb.AppendLine("   el orden de emisión. Escribe SIEMPRE en `clave` cuál has usado.");
        sb.AppendLine("2. **Qué columnas son títulos y de qué idioma.** «Título en España» → `es`;");
        sb.AppendLine("   «Título en Hispanoamérica» o «Latinoamérica» → `lat`; «Título original» suele ser");
        sb.AppendLine("   `jp` en anime y `en` en series estadounidenses — fíjate en el alfabeto, no en el");
        sb.AppendLine("   nombre de la columna. «Traducción literal» NO es un título de emisión: descártala.");
        sb.AppendLine("3. **Qué columna es la fecha.** Si hay varias (emisión original y estreno en España),");
        sb.AppendLine("   usa la de EMISIÓN ORIGINAL y déjalo escrito en `notas`. Conviértela a AAAA-MM-DD.");
        sb.AppendLine("   Si no hay fecha, omite el campo: es preferible a una fecha inventada.");
        sb.AppendLine("4. **Si un episodio contiene varias historias.** Es corriente en anime: una emisión");
        sb.AppendLine("   con dos o tres segmentos, a veces separados por saltos de línea o por «/» dentro");
        sb.AppendLine("   de la misma celda. Van como VARIOS elementos del array de ese idioma, en el mismo");
        sb.AppendLine("   episodio. No crees un episodio por segmento.");
        sb.AppendLine();

        sb.AppendLine("## Formato exacto");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"esquema\": \"reindex/1.0\",");
        sb.AppendLine($"  \"serie\": \"{serie}\",");
        sb.AppendLine("  \"clave\": \"oficial | segmento | continuo — explica qué significa «num» aquí\",");
        sb.AppendLine("  \"notas\": \"rarezas de esta serie: saltos de numeración, qué fecha usaste, etc.\",");
        sb.AppendLine($"  \"idiomas\": {{ \"salida\": \"{salida}\", \"comparar\": [{jsonComparar}] }},");
        sb.AppendLine("  \"total\": 0,");
        sb.AppendLine("  \"episodios\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"num\": 1,");
        sb.AppendLine("      \"temporada\": 2005,");
        sb.AppendLine("      \"fecha\": \"2005-04-22\",");
        sb.AppendLine("      \"especial\": false,");
        sb.AppendLine("      \"titulos\": {");
        foreach (var c in idiomas)
            sb.AppendLine($"        \"{c}\": [\"…\"],");
        sb.AppendLine("      },");
        sb.AppendLine("      \"aliases\": []");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Reglas que el programa comprueba (si fallan, rechaza el archivo)");
        sb.AppendLine();
        sb.AppendLine("- `num` es obligatorio, entero ≥ 0 y **único en todo el catálogo**. Si la fuente");
        sb.AppendLine("  repite un número, no lo dupliques: decide cuál va y anota el conflicto en `notas`.");
        sb.AppendLine("- **No rellenes los huecos de numeración.** Muchas series saltan números de forma");
        sb.AppendLine("  oficial. Si el anexo pasa del 55 al 57, tu catálogo también: no inventes un 56.");
        sb.AppendLine("- `fecha`, si está, debe ser una fecha real en `AAAA-MM-DD`.");
        sb.AppendLine("- `titulos` son SIEMPRE arrays, aunque solo haya un título.");
        sb.AppendLine("- **Los especiales solo van aparte si la numeración que has usado los deja aparte.**");
        sb.AppendLine("  Si numeras por transmisión, un especial es una emisión más: le toca su número en");
        sb.AppendLine("  la secuencia y va con `\"especial\": false`. Si numeras por episodio oficial y los");
        sb.AppendLine("  especiales quedan fuera de esa cuenta, entonces sí: `\"especial\": true` y un rango");
        sb.AppendLine("  propio (por convenio, a partir de 900).");
        sb.AppendLine("- Copia los títulos TAL CUAL, con sus tildes y su puntuación. No los normalices,");
        sb.AppendLine("  no los pongas en mayúsculas y no les quites los signos de interrogación.");
        sb.AppendLine("- Quita las referencias de la enciclopedia («[1]», «[nota 2]») de dentro del título.");
        sb.AppendLine();

        sb.AppendLine("## Antes de responder, comprueba tú mismo");
        sb.AppendLine();
        sb.AppendLine("1. ¿Hay algún `num` repetido? (es el fallo más frecuente)");
        sb.AppendLine("2. ¿Todas las fechas son AAAA-MM-DD y existen de verdad?");
        sb.AppendLine("3. ¿`total` coincide con la cantidad de episodios de la lista?");
        sb.AppendLine("4. ¿Están TODAS las temporadas de la página, no solo la primera?");
        sb.AppendLine();
        sb.AppendLine("Responde ÚNICAMENTE con el JSON, sin explicaciones ni texto alrededor. Si la serie es");
        sb.AppendLine("larga y no cabe de una vez, dilo y entrégalo por partes, pero sin resumir ni saltarte");
        sb.AppendLine("episodios: un catálogo incompleto deja ficheros sin identificar.");

        return sb.ToString();
    }
}
