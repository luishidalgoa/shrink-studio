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
    /// <summary>
    /// La lista de idiomas vive en <see cref="IsoLanguages"/>, que es la norma ISO entera.
    /// Aquí había siete a mano, y dos ni siquiera eran códigos de idioma.
    /// </summary>
    public static string Nombre(string codigo) => IsoLanguages.Nombre(codigo);

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
        // Se normalizan aquí para que el encargo pida siempre códigos ISO: si alguien trae un
        // «jp» de los de antes, la IA no debe aprenderlo y perpetuarlo en el catálogo nuevo.
        var idiomas = new List<string> { IsoLanguages.Normalizar(salida) };
        foreach (var c in comparar)
        {
            var n = IsoLanguages.Normalizar(c);
            if (n.Length > 0 && !idiomas.Contains(n, StringComparer.OrdinalIgnoreCase)) idiomas.Add(n);
        }

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

        sb.AppendLine("## Formato: estos campos y solo estos");
        sb.AppendLine();
        sb.AppendLine("Esta es la lista COMPLETA de campos que el programa entiende. No todos los anexos");
        sb.AppendLine("traen la misma información, así que se espera que uses **solo los que puedas sacar**");
        sb.AppendLine("de la fuente. Un catálogo con menos campos es perfectamente válido.");
        sb.AppendLine();
        sb.AppendLine("**La regla que no se salta:** puedes OMITIR campos, nunca INVENTARLOS. Ni inventar");
        sb.AppendLine("nombres de campo nuevos, ni anidar cosas de otra manera, ni rellenar un hueco con un");
        sb.AppendLine("valor plausible. Un dato inventado provoca renombrados equivocados, que es el peor");
        sb.AppendLine("resultado posible; un dato ausente solo hace que ese fichero se revise a mano.");
        sb.AppendLine();

        sb.AppendLine("### En la raíz");
        sb.AppendLine();
        sb.AppendLine("| Campo | ¿Va siempre? | Qué es |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine("| `esquema` | **sí** | Literalmente `\"reindex/1.0\"`. |");
        sb.AppendLine("| `serie` | **sí** | Nombre de la serie; se escribirá en los ficheros. |");
        sb.AppendLine("| `episodios` | **sí** | La lista. No puede ir vacía. |");
        sb.AppendLine("| `clave` | recomendado | Qué numeración usaste: `transmision`, `oficial`, `continuo`… |");
        sb.AppendLine("| `notas` | recomendado | Rarezas de la serie y decisiones que tomaste. |");
        sb.AppendLine("| `idiomas` | recomendado | `{ \"salida\": \"…\", \"comparar\": [\"…\"] }`. |");
        sb.AppendLine("| `total` | opcional | Cuántos episodios hay. Solo informativo. |");
        sb.AppendLine();

        sb.AppendLine("### En cada episodio");
        sb.AppendLine();
        sb.AppendLine("| Campo | ¿Va siempre? | Si la fuente no lo trae |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine("| `num` | **sí** | No hay excepción: sin número no se puede construir el catálogo. |");
        sb.AppendLine("| `titulos` | casi siempre | Omítelo solo si ese episodio no tiene ningún título. |");
        sb.AppendLine("| `temporada` | si existe | **Omite el campo.** No lo deduzcas del año de la fecha. |");
        sb.AppendLine("| `fecha` | si existe | **Omite el campo.** Nunca pongas una fecha aproximada. |");
        sb.AppendLine("| `especial` | si aplica | Omítelo o pon `false`; son equivalentes. |");
        sb.AppendLine("| `aliases` | si existen | Omítelo o pon `[]`; son equivalentes. |");
        sb.AppendLine();

        sb.AppendLine("### Con todo lo que se puede tener");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"esquema\": \"reindex/1.0\",");
        sb.AppendLine($"  \"serie\": \"{serie}\",");
        sb.AppendLine("  \"clave\": \"transmision\",");
        sb.AppendLine("  \"notas\": \"qué numeración usaste, qué fecha, y cualquier rareza de la serie\",");
        sb.AppendLine($"  \"idiomas\": {{ \"salida\": \"{salida}\", \"comparar\": [{jsonComparar}] }},");
        sb.AppendLine("  \"total\": 768,");
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

        sb.AppendLine("### Con un anexo pobre (solo número y título)");
        sb.AppendLine();
        sb.AppendLine("Igual de válido. Se omite lo que no hay, en vez de rellenarlo:");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"esquema\": \"reindex/1.0\",");
        sb.AppendLine($"  \"serie\": \"{serie}\",");
        sb.AppendLine("  \"clave\": \"continuo\",");
        sb.AppendLine("  \"notas\": \"el anexo no trae fechas ni temporadas\",");
        sb.AppendLine($"  \"idiomas\": {{ \"salida\": \"{salida}\" }},");
        sb.AppendLine("  \"episodios\": [");
        sb.AppendLine($"    {{ \"num\": 1, \"titulos\": {{ \"{salida}\": [\"…\"] }} }},");
        sb.AppendLine($"    {{ \"num\": 2, \"titulos\": {{ \"{salida}\": [\"…\"] }} }}");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Si al catálogo le faltan fechas, el programa lo detecta al importarlo y avisa de que");
        sb.AppendLine("habrá más dudas que resolver a mano. Eso es correcto y esperable: es mejor que");
        sb.AppendLine("identificar mal por una fecha que te has inventado.");
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
        sb.AppendLine("5. ¿Hay algún campo que no esté en las tablas de arriba? Quítalo.");
        sb.AppendLine("6. ¿Has rellenado alguna fecha, temporada o título que la fuente no daba? Quítalo");
        sb.AppendLine("   también: omitir es correcto, inventar no.");
        sb.AppendLine();
        sb.AppendLine("No hace falta que aciertes a la primera con lo dudoso: al importar, el programa");
        sb.AppendLine("valida el archivo y, si algo no encaja, dice **exactamente** qué corregir y en qué");
        sb.AppendLine("episodio. Lo que no puede detectar es un dato inventado que parezca correcto — por");
        sb.AppendLine("eso esa es la única regla que no admite excepción.");
        sb.AppendLine();
        sb.AppendLine("Responde ÚNICAMENTE con el JSON, sin explicaciones ni texto alrededor. Si la serie es");
        sb.AppendLine("larga y no cabe de una vez, dilo y entrégalo por partes, pero sin resumir ni saltarte");
        sb.AppendLine("episodios: un catálogo incompleto deja ficheros sin identificar.");

        return sb.ToString();
    }
}
