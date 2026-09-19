using Coraza.Core.Busqueda;

namespace Coraza.Core.Biblia;

public sealed record LibroBiblico(int Numero, string Nombre, string Abreviatura, int Capitulos, string[] Alias)
{
    public bool AntiguoTestamento => Numero <= 39;
    public override string ToString() => Nombre;
}

/// <summary>Los 66 libros del canon protestante con nombres y abreviaturas comunes en español.</summary>
public static class LibrosBiblia
{
    public static IReadOnlyList<LibroBiblico> Todos { get; } = new LibroBiblico[]
    {
        new(1, "Génesis", "Gn", 50, new[] { "gn", "gen", "ge", "gens" }),
        new(2, "Éxodo", "Éx", 40, new[] { "ex", "exo", "exod" }),
        new(3, "Levítico", "Lv", 27, new[] { "lv", "lev", "le" }),
        new(4, "Números", "Nm", 36, new[] { "nm", "num", "nu", "nms" }),
        new(5, "Deuteronomio", "Dt", 34, new[] { "dt", "deu", "deut" }),
        new(6, "Josué", "Jos", 24, new[] { "jos", "jsu" }),
        new(7, "Jueces", "Jue", 21, new[] { "jue", "jc", "jz", "juec" }),
        new(8, "Rut", "Rt", 4, new[] { "rt", "rut" }),
        new(9, "1 Samuel", "1 S", 31, new[] { "1s", "1sa", "1sam", "1sm" }),
        new(10, "2 Samuel", "2 S", 24, new[] { "2s", "2sa", "2sam", "2sm" }),
        new(11, "1 Reyes", "1 R", 22, new[] { "1r", "1re", "1rey", "1ry" }),
        new(12, "2 Reyes", "2 R", 25, new[] { "2r", "2re", "2rey", "2ry" }),
        new(13, "1 Crónicas", "1 Cr", 29, new[] { "1cr", "1cro", "1cron" }),
        new(14, "2 Crónicas", "2 Cr", 36, new[] { "2cr", "2cro", "2cron" }),
        new(15, "Esdras", "Esd", 10, new[] { "esd", "esdr" }),
        new(16, "Nehemías", "Neh", 13, new[] { "neh", "ne" }),
        new(17, "Ester", "Est", 10, new[] { "est", "es" }),
        new(18, "Job", "Job", 42, new[] { "job", "jb" }),
        new(19, "Salmos", "Sal", 150, new[] { "sal", "sl", "salmo", "salm", "ps", "slm" }),
        new(20, "Proverbios", "Pr", 31, new[] { "pr", "pro", "prov", "prv" }),
        new(21, "Eclesiastés", "Ec", 12, new[] { "ec", "ecl", "ecles", "qo" }),
        new(22, "Cantares", "Cnt", 8, new[] { "cnt", "cant", "cantar", "cantardeloscantares", "canticodeloscanticos", "ct" }),
        new(23, "Isaías", "Is", 66, new[] { "is", "isa" }),
        new(24, "Jeremías", "Jer", 52, new[] { "jer", "jr" }),
        new(25, "Lamentaciones", "Lm", 5, new[] { "lm", "lam" }),
        new(26, "Ezequiel", "Ez", 48, new[] { "ez", "eze", "ezeq" }),
        new(27, "Daniel", "Dn", 12, new[] { "dn", "dan", "da" }),
        new(28, "Oseas", "Os", 14, new[] { "os", "ose" }),
        new(29, "Joel", "Jl", 3, new[] { "jl", "joel" }),
        new(30, "Amós", "Am", 9, new[] { "am", "amo" }),
        new(31, "Abdías", "Abd", 1, new[] { "abd", "ab" }),
        new(32, "Jonás", "Jon", 4, new[] { "jon" }),
        new(33, "Miqueas", "Mi", 7, new[] { "mi", "miq" }),
        new(34, "Nahúm", "Nah", 3, new[] { "nah", "na" }),
        new(35, "Habacuc", "Hab", 3, new[] { "hab", "ha" }),
        new(36, "Sofonías", "Sof", 3, new[] { "sof", "so" }),
        new(37, "Hageo", "Hag", 2, new[] { "hag", "hg" }),
        new(38, "Zacarías", "Zac", 14, new[] { "zac", "za" }),
        new(39, "Malaquías", "Mal", 4, new[] { "mal", "ml" }),
        new(40, "Mateo", "Mt", 28, new[] { "mt", "mat", "sanmateo" }),
        new(41, "Marcos", "Mr", 16, new[] { "mr", "mc", "mar", "marc", "sanmarcos" }),
        new(42, "Lucas", "Lc", 24, new[] { "lc", "luc", "lu", "sanlucas" }),
        new(43, "Juan", "Jn", 21, new[] { "jn", "jua", "san juan", "sanjuan" }),
        new(44, "Hechos", "Hch", 28, new[] { "hch", "hech", "hec", "hechosdelosapostoles" }),
        new(45, "Romanos", "Ro", 16, new[] { "ro", "rom", "rm" }),
        new(46, "1 Corintios", "1 Co", 16, new[] { "1co", "1cor", "1corin" }),
        new(47, "2 Corintios", "2 Co", 13, new[] { "2co", "2cor", "2corin" }),
        new(48, "Gálatas", "Gá", 6, new[] { "ga", "gal" }),
        new(49, "Efesios", "Ef", 6, new[] { "ef", "efe", "efes" }),
        new(50, "Filipenses", "Fil", 4, new[] { "fil", "flp", "filip" }),
        new(51, "Colosenses", "Col", 4, new[] { "col", "co" }),
        new(52, "1 Tesalonicenses", "1 Ts", 5, new[] { "1ts", "1tes", "1tesa" }),
        new(53, "2 Tesalonicenses", "2 Ts", 3, new[] { "2ts", "2tes", "2tesa" }),
        new(54, "1 Timoteo", "1 Ti", 6, new[] { "1ti", "1tim", "1tm" }),
        new(55, "2 Timoteo", "2 Ti", 4, new[] { "2ti", "2tim", "2tm" }),
        new(56, "Tito", "Tit", 3, new[] { "tit", "tt" }),
        new(57, "Filemón", "Flm", 1, new[] { "flm", "filem", "fm" }),
        new(58, "Hebreos", "He", 13, new[] { "he", "heb" }),
        new(59, "Santiago", "Stg", 5, new[] { "stg", "sant", "stgo", "sg" }),
        new(60, "1 Pedro", "1 P", 5, new[] { "1p", "1pe", "1ped" }),
        new(61, "2 Pedro", "2 P", 3, new[] { "2p", "2pe", "2ped" }),
        new(62, "1 Juan", "1 Jn", 5, new[] { "1jn", "1jua" }),
        new(63, "2 Juan", "2 Jn", 1, new[] { "2jn", "2jua" }),
        new(64, "3 Juan", "3 Jn", 1, new[] { "3jn", "3jua" }),
        new(65, "Judas", "Jud", 1, new[] { "jud", "jds" }),
        new(66, "Apocalipsis", "Ap", 22, new[] { "ap", "apo", "apoc", "rev", "revelacion" }),
    };

    /// <summary>Códigos de libro usados por los archivos VPL/BibleWorks y por USFM, en orden canónico.</summary>
    private static readonly string[] CodigosVpl =
    {
        "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR", "NEH",
        "EST", "JOB", "PSA", "PRO", "ECC", "SOL", "ISA", "JER", "LAM", "EZE", "DAN", "HOS", "JOE", "AMO", "OBA", "JON",
        "MIC", "NAH", "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MAR", "LUK", "JOH", "ACT", "ROM", "1CO", "2CO", "GAL",
        "EPH", "PHI", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAM", "1PE", "2PE", "1JO", "2JO", "3JO",
        "JUD", "REV",
    };

    private static readonly string[] CodigosUsfm =
    {
        "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR", "NEH",
        "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO", "OBA", "JON",
        "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL",
        "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN",
        "JUD", "REV",
    };

    /// <summary>Códigos OSIS (Gen, Exod, 1Sam, Matt…), en orden canónico.</summary>
    private static readonly string[] CodigosOsis =
    {
        "Gen", "Exod", "Lev", "Num", "Deut", "Josh", "Judg", "Ruth", "1Sam", "2Sam", "1Kgs", "2Kgs", "1Chr", "2Chr", "Ezra", "Neh",
        "Esth", "Job", "Ps", "Prov", "Eccl", "Song", "Isa", "Jer", "Lam", "Ezek", "Dan", "Hos", "Joel", "Amos", "Obad", "Jonah",
        "Mic", "Nah", "Hab", "Zeph", "Hag", "Zech", "Mal", "Matt", "Mark", "Luke", "John", "Acts", "Rom", "1Cor", "2Cor", "Gal",
        "Eph", "Phil", "Col", "1Thess", "2Thess", "1Tim", "2Tim", "Titus", "Phlm", "Heb", "Jas", "1Pet", "2Pet", "1John", "2John", "3John",
        "Jude", "Rev",
    };

    /// <summary>Numeración de libros de los módulos MyBible (Génesis = 10, Éxodo = 20…; sin deuterocanónicos).</summary>
    private static readonly int[] NumerosMyBible =
    {
        10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 190, 220, 230, 240, 250, 260, 290, 300, 310, 330,
        340, 350, 360, 370, 380, 390, 400, 410, 420, 430, 440, 450, 460, 470, 480, 490, 500, 510, 520, 530, 540, 550, 560, 570,
        580, 590, 600, 610, 620, 630, 640, 650, 660, 670, 680, 690, 700, 710, 720, 730,
    };

    private static readonly Dictionary<string, int> PorCodigoMapa = CrearMapaCodigos();
    private static readonly Dictionary<string, LibroBiblico> PorAlias = CrearMapaAlias();
    private static readonly (string Clave, LibroBiblico Libro)[] NombresNormalizados =
        Todos.Select(l => (Clave(l.Nombre), l)).ToArray();

    public static LibroBiblico? PorNumero(int numero) =>
        numero >= 1 && numero <= Todos.Count ? Todos[numero - 1] : null;

    /// <summary>Convierte el número de libro de un módulo MyBible (10, 20… 730) al libro canónico.</summary>
    public static LibroBiblico? PorNumeroMyBible(int numero)
    {
        var indice = Array.IndexOf(NumerosMyBible, numero);
        return indice >= 0 ? Todos[indice] : null;
    }

    /// <summary>Busca un libro por código VPL, USFM u OSIS (GEN, JOH, JHN, John…).</summary>
    public static LibroBiblico? PorCodigo(string codigo) =>
        PorCodigoMapa.TryGetValue(codigo.Trim().ToUpperInvariant(), out var n) ? PorNumero(n) : null;

    /// <summary>Encuentra un libro a partir de un nombre, abreviatura o comienzo del nombre.</summary>
    public static LibroBiblico? Buscar(string? texto)
    {
        var clave = Clave(texto);
        if (clave.Length == 0) return null;
        if (PorAlias.TryGetValue(clave, out var libro)) return libro;
        if (clave.Length < 2) return null;
        foreach (var (nombre, l) in NombresNormalizados)
        {
            if (nombre.StartsWith(clave, StringComparison.Ordinal)) return l;
        }
        return null;
    }

    /// <summary>Libros cuyo nombre comienza por el texto (para sugerencias).</summary>
    public static IEnumerable<LibroBiblico> Sugerir(string? texto)
    {
        var clave = Clave(texto);
        if (clave.Length == 0) return Enumerable.Empty<LibroBiblico>();
        return NombresNormalizados
            .Where(n => n.Clave.StartsWith(clave, StringComparison.Ordinal)
                        || n.Libro.Alias.Any(a => Clave(a) == clave))
            .Select(n => n.Libro);
    }

    /// <summary>Clave de comparación: sin tildes, minúsculas y sin espacios ni puntos («1 Cor.» → «1cor»).</summary>
    internal static string Clave(string? texto) => Normalizador.Normalizar(texto).Replace(" ", "");

    private static Dictionary<string, int> CrearMapaCodigos()
    {
        var mapa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < CodigosVpl.Length; i++)
        {
            mapa[CodigosVpl[i]] = i + 1;
            mapa[CodigosUsfm[i]] = i + 1;
            mapa.TryAdd(CodigosOsis[i], i + 1);
        }
        return mapa;
    }

    private static Dictionary<string, LibroBiblico> CrearMapaAlias()
    {
        var mapa = new Dictionary<string, LibroBiblico>();
        foreach (var libro in Todos)
        {
            mapa.TryAdd(Clave(libro.Nombre), libro);
            mapa.TryAdd(Clave(libro.Abreviatura), libro);
            foreach (var alias in libro.Alias) mapa.TryAdd(Clave(alias), libro);
        }
        return mapa;
    }
}
