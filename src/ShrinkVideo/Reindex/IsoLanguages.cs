namespace ShrinkVideo.Reindex;

/// <summary>Un idioma de la lista: el código que va en el JSON y cómo se llama en español.</summary>
public sealed record IsoLanguage(string Codigo, string Nombre);

/// <summary>
/// La norma ISO 639-1 entera, con los nombres en español.
///
/// Antes había siete idiomas fijos elegidos a ojo, y dos de ellos ni siquiera eran ISO:
/// «jp» (el código de Japón, no del japonés — el idioma es «ja») y «lat». Un catálogo que
/// los use sigue leyéndose: <see cref="Normalizar"/> los traduce en vez de dejarlos tirados.
/// </summary>
public static class IsoLanguages
{
    /// <summary>
    /// Los que salen primero antes de escribir nada. No es un ranking mundial: son los
    /// idiomas en los que llegan los ficheros de una biblioteca de anime en España.
    /// </summary>
    public static readonly string[] Frecuentes =
        { "es", "es-419", "en", "ja", "ca", "gl", "eu", "fr", "it", "de", "pt", "ko", "zh" };

    /// <summary>
    /// Códigos que la app usó antes de pasarse a ISO, y los que la gente escribe de memoria.
    /// Sin esto, un catálogo con «jp» dejaría de reconocer sus propios títulos.
    /// </summary>
    private static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jp"] = "ja",        // lo que usaba la app: es el código del país, no del idioma
        ["lat"] = "es-419",   // «español latino»
        ["cast"] = "es",
        ["esp"] = "es",
        ["ing"] = "en",
        ["jpn"] = "ja",       // ISO 639-2, por si alguien lo escribe
        ["spa"] = "es",
        ["eng"] = "en",
        ["cat"] = "ca",
        ["glg"] = "gl",
        ["baq"] = "eu",
        ["eus"] = "eu",
        ["por"] = "pt",
        ["fra"] = "fr",
        ["fre"] = "fr",
        ["deu"] = "de",
        ["ger"] = "de",
        ["ita"] = "it",
        ["kor"] = "ko",
        ["chi"] = "zh",
        ["zho"] = "zh",
        ["rus"] = "ru",
        ["ara"] = "ar",
    };

    /// <summary>ISO 639-1 completa, más el hispanoamericano, que no está en la norma pero sí en las bibliotecas.</summary>
    public static readonly IsoLanguage[] Todos =
    {
        new("es",     "Español (España)"),
        new("es-419", "Español (Hispanoamérica)"),
        new("en",     "Inglés"),
        new("ja",     "Japonés"),
        new("ca",     "Catalán"),
        new("gl",     "Gallego"),
        new("eu",     "Euskera"),
        new("fr",     "Francés"),
        new("it",     "Italiano"),
        new("de",     "Alemán"),
        new("pt",     "Portugués"),
        new("ko",     "Coreano"),
        new("zh",     "Chino"),
        new("ru",     "Ruso"),
        new("ar",     "Árabe"),
        new("ab",     "Abjasio"),
        new("aa",     "Afar"),
        new("af",     "Afrikáans"),
        new("ak",     "Akan"),
        new("sq",     "Albanés"),
        new("am",     "Amárico"),
        new("an",     "Aragonés"),
        new("hy",     "Armenio"),
        new("as",     "Asamés"),
        new("av",     "Avar"),
        new("ae",     "Avéstico"),
        new("ay",     "Aimara"),
        new("az",     "Azerí"),
        new("bm",     "Bambara"),
        new("ba",     "Baskir"),
        new("be",     "Bielorruso"),
        new("bn",     "Bengalí"),
        new("bh",     "Bhojpuri"),
        new("bi",     "Bislama"),
        new("bs",     "Bosnio"),
        new("br",     "Bretón"),
        new("bg",     "Búlgaro"),
        new("my",     "Birmano"),
        new("ch",     "Chamorro"),
        new("ce",     "Checheno"),
        new("ny",     "Chichewa"),
        new("cu",     "Eslavo eclesiástico"),
        new("cv",     "Chuvasio"),
        new("kw",     "Córnico"),
        new("co",     "Corso"),
        new("cr",     "Cree"),
        new("hr",     "Croata"),
        new("cs",     "Checo"),
        new("da",     "Danés"),
        new("dv",     "Maldivo"),
        new("nl",     "Neerlandés"),
        new("dz",     "Dzongkha"),
        new("eo",     "Esperanto"),
        new("et",     "Estonio"),
        new("ee",     "Ewé"),
        new("fo",     "Feroés"),
        new("fj",     "Fiyiano"),
        new("fi",     "Finés"),
        new("ff",     "Fula"),
        new("ka",     "Georgiano"),
        new("el",     "Griego"),
        new("gn",     "Guaraní"),
        new("gu",     "Guyaratí"),
        new("ht",     "Criollo haitiano"),
        new("ha",     "Hausa"),
        new("he",     "Hebreo"),
        new("hz",     "Herero"),
        new("hi",     "Hindi"),
        new("ho",     "Hiri motu"),
        new("hu",     "Húngaro"),
        new("ia",     "Interlingua"),
        new("id",     "Indonesio"),
        new("ie",     "Interlingue"),
        new("ga",     "Irlandés"),
        new("ig",     "Igbo"),
        new("ik",     "Inupiaq"),
        new("io",     "Ido"),
        new("is",     "Islandés"),
        new("iu",     "Inuktitut"),
        new("jv",     "Javanés"),
        new("kl",     "Groenlandés"),
        new("kn",     "Canarés"),
        new("kr",     "Kanuri"),
        new("ks",     "Cachemiro"),
        new("kk",     "Kazajo"),
        new("km",     "Jemer"),
        new("ki",     "Kikuyu"),
        new("rw",     "Kinyarwanda"),
        new("ky",     "Kirguís"),
        new("kv",     "Komi"),
        new("kg",     "Kongo"),
        new("kj",     "Kuanyama"),
        new("ku",     "Kurdo"),
        new("lo",     "Lao"),
        new("la",     "Latín"),
        new("lv",     "Letón"),
        new("li",     "Limburgués"),
        new("ln",     "Lingala"),
        new("lt",     "Lituano"),
        new("lu",     "Luba-katanga"),
        new("lb",     "Luxemburgués"),
        new("mk",     "Macedonio"),
        new("mg",     "Malgache"),
        new("ms",     "Malayo"),
        new("ml",     "Malayalam"),
        new("mt",     "Maltés"),
        new("gv",     "Manés"),
        new("mi",     "Maorí"),
        new("mr",     "Maratí"),
        new("mh",     "Marshalés"),
        new("mn",     "Mongol"),
        new("na",     "Nauruano"),
        new("nv",     "Navajo"),
        new("nd",     "Ndebele del norte"),
        new("nr",     "Ndebele del sur"),
        new("ng",     "Ndonga"),
        new("ne",     "Nepalí"),
        new("no",     "Noruego"),
        new("nb",     "Noruego bokmål"),
        new("nn",     "Noruego nynorsk"),
        new("ii",     "Yi de Sichuán"),
        new("oc",     "Occitano"),
        new("oj",     "Ojibwa"),
        new("or",     "Oriya"),
        new("om",     "Oromo"),
        new("os",     "Osetio"),
        new("pi",     "Pali"),
        new("pa",     "Panyabí"),
        new("fa",     "Persa"),
        new("pl",     "Polaco"),
        new("ps",     "Pastún"),
        new("qu",     "Quechua"),
        new("rm",     "Romanche"),
        new("ro",     "Rumano"),
        new("rn",     "Kirundi"),
        new("se",     "Sami septentrional"),
        new("sm",     "Samoano"),
        new("sg",     "Sango"),
        new("sa",     "Sánscrito"),
        new("sc",     "Sardo"),
        new("sr",     "Serbio"),
        new("sn",     "Shona"),
        new("sd",     "Sindi"),
        new("si",     "Cingalés"),
        new("sk",     "Eslovaco"),
        new("sl",     "Esloveno"),
        new("so",     "Somalí"),
        new("st",     "Sesotho"),
        new("su",     "Sundanés"),
        new("sw",     "Suajili"),
        new("ss",     "Suazi"),
        new("sv",     "Sueco"),
        new("ta",     "Tamil"),
        new("te",     "Telugu"),
        new("tg",     "Tayiko"),
        new("th",     "Tailandés"),
        new("ti",     "Tigriña"),
        new("bo",     "Tibetano"),
        new("tk",     "Turcomano"),
        new("tl",     "Tagalo"),
        new("tn",     "Setsuana"),
        new("to",     "Tongano"),
        new("tr",     "Turco"),
        new("ts",     "Tsonga"),
        new("tt",     "Tártaro"),
        new("tw",     "Twi"),
        new("ty",     "Tahitiano"),
        new("ug",     "Uigur"),
        new("uk",     "Ucraniano"),
        new("ur",     "Urdu"),
        new("uz",     "Uzbeko"),
        new("ve",     "Venda"),
        new("vi",     "Vietnamita"),
        new("vo",     "Volapük"),
        new("wa",     "Valón"),
        new("cy",     "Galés"),
        new("wo",     "Wólof"),
        new("fy",     "Frisón occidental"),
        new("xh",     "Xhosa"),
        new("yi",     "Yidis"),
        new("yo",     "Yoruba"),
        new("za",     "Zhuang"),
        new("zu",     "Zulú"),
    };

    private static readonly Dictionary<string, IsoLanguage> PorCodigo =
        Todos.ToDictionary(i => i.Codigo, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Deja un código en su forma ISO: minúsculas y sin los códigos viejos de la app. Uno
    /// que no conocemos se devuelve tal cual — inventarse una traducción sería peor que
    /// enseñar lo que el catálogo trae de verdad.
    /// </summary>
    public static string Normalizar(string? codigo)
    {
        var c = (codigo ?? "").Trim();
        if (c.Length == 0) return "";
        if (Alias.TryGetValue(c, out var ali)) return ali;
        return PorCodigo.TryGetValue(c, out var l) ? l.Codigo : c;
    }

    /// <summary>Cómo se llama ese idioma. De uno desconocido, su propio código.</summary>
    public static string Nombre(string? codigo)
    {
        var c = Normalizar(codigo);
        return PorCodigo.TryGetValue(c, out var l) ? l.Nombre : c;
    }

    /// <summary>
    /// Busca por código o por nombre, tolerando lo que se escribe de verdad: sin tildes, a
    /// medias y en cualquier caja. Se apoya en la misma normalización que compara títulos,
    /// así que «japones» encuentra «Japonés» igual que allí.
    ///
    /// El orden importa más que el filtro: primero el código exacto, luego lo que empieza
    /// por lo escrito y por último lo que lo contiene. Sin eso, escribir «de» sepultaría el
    /// alemán bajo cada nombre que lleve «de» dentro.
    /// </summary>
    public static IReadOnlyList<IsoLanguage> Buscar(string? consulta, int cuantos = 0)
    {
        var q = (consulta ?? "").Trim();

        if (q.Length == 0)
        {
            var frecuentes = Frecuentes.Where(PorCodigo.ContainsKey).Select(c => PorCodigo[c]).ToList();
            var resto = Todos.Except(frecuentes).OrderBy(i => i.Nombre, StringComparer.CurrentCulture);
            var todo = frecuentes.Concat(resto).ToList();
            return cuantos > 0 ? todo.Take(cuantos).ToList() : todo;
        }

        var qNorm = TitleMatch.Norm(q);
        var qCod = Normalizar(q).ToLowerInvariant();

        var puntuados = new List<(int Nivel, IsoLanguage Idioma)>();
        foreach (var i in Todos)
        {
            var cod = i.Codigo.ToLowerInvariant();
            var nom = TitleMatch.Norm(i.Nombre);

            int nivel =
                cod == qCod || cod == q.ToLowerInvariant() ? 0
                : cod.StartsWith(qCod, StringComparison.Ordinal) ? 1
                : nom.StartsWith(qNorm, StringComparison.Ordinal) ? 2
                : qNorm.Length > 0 && nom.Contains(qNorm, StringComparison.Ordinal) ? 3
                : -1;

            if (nivel >= 0) puntuados.Add((nivel, i));
        }

        var orden = puntuados
            .OrderBy(x => x.Nivel)
            .ThenBy(x => x.Idioma.Nombre, StringComparer.CurrentCulture)
            .Select(x => x.Idioma)
            .ToList();

        return cuantos > 0 ? orden.Take(cuantos).ToList() : orden;
    }
}
