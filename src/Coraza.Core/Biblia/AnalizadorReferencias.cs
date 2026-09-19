using System.Text.RegularExpressions;
using Coraza.Core.Busqueda;
using Coraza.Core.Modelos;

namespace Coraza.Core.Biblia;

/// <summary>
/// Interpreta citas bíblicas escritas de forma libre:
/// «jn 3 16», «Juan 3:16-18», «1 cor 13», «sal 23 1-6», «Ef 6.14», «San Juan 3:16», «primera de corintios 13».
/// </summary>
public static partial class AnalizadorReferencias
{
    public static bool IntentarAnalizar(string? texto, out ReferenciaBiblica referencia)
    {
        referencia = null!;
        var r = Analizar(texto);
        if (r is null) return false;
        referencia = r;
        return true;
    }

    public static ReferenciaBiblica? Analizar(string? texto)
    {
        var partes = Separar(texto);
        if (partes is null || partes.Value.Resto.Length == 0) return null;
        var libro = LibrosBiblia.Buscar(partes.Value.Libro);
        if (libro is null) return null;

        var m = RestoRegex().Match(partes.Value.Resto);
        if (!m.Success) return null;

        var capitulo = int.Parse(m.Groups["cap"].Value);
        int? inicio = m.Groups["ini"].Success ? int.Parse(m.Groups["ini"].Value) : null;
        int? fin = m.Groups["fin"].Success ? int.Parse(m.Groups["fin"].Value) : null;
        int? capituloFin = m.Groups["capfin"].Success ? int.Parse(m.Groups["capfin"].Value) : null;

        // En libros de un solo capítulo «Judas 3» significa el versículo 3.
        if (libro.Capitulos == 1 && inicio is null && capitulo > 1)
        {
            inicio = capitulo;
            capitulo = 1;
        }

        if (capitulo < 1 || capitulo > libro.Capitulos) return null;
        if (inicio is < 1) return null;
        if (capituloFin is not null)
        {
            if (capituloFin < capitulo || capituloFin > libro.Capitulos || fin is null) return null;
            if (capituloFin == capitulo) capituloFin = null;
        }
        if (capituloFin is null && fin is not null && inicio is not null && fin < inicio) return null;

        return new ReferenciaBiblica(libro.Numero, capitulo, inicio, fin, capituloFin);
    }

    /// <summary>Si el texto solo nombra un libro («efesios», «1 cor»), devuelve ese libro.</summary>
    public static LibroBiblico? AnalizarSoloLibro(string? texto)
    {
        var partes = Separar(texto);
        if (partes is null || partes.Value.Resto.Length > 0) return null;
        return LibrosBiblia.Buscar(partes.Value.Libro);
    }

    /// <summary>¿Parece una cita (libro reconocible seguido de números)?</summary>
    public static bool PareceReferencia(string? texto) => Analizar(texto) is not null;

    private static (string Libro, string Resto)? Separar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var s = Normalizador.QuitarTildes(texto).ToLowerInvariant().Trim()
            .Replace('–', '-').Replace('—', '-');
        s = OrdinalRegex().Replace(s, m => m.Groups["n"].Success ? m.Groups["n"].Value + " " : Ordinal(m.Value) + " ");
        s = SanRegex().Replace(s, "san$1");
        s = s.TrimEnd('-', ':', '.', ',', ' ');

        var m = LibroRegex().Match(s);
        if (!m.Success) return null;
        var libro = (m.Groups["pre"].Value + m.Groups["nombre"].Value).Trim();
        var resto = m.Groups["resto"].Success ? m.Groups["resto"].Value.Trim() : "";
        return (libro, resto);
    }

    private static string Ordinal(string palabra)
    {
        var p = palabra.Trim().ToLowerInvariant();
        if (p.StartsWith("prim") || p.StartsWith("1") || p == "i" || p.StartsWith("i ")) return "1";
        if (p.StartsWith("seg") || p.StartsWith("2") || p.StartsWith("ii ") || p == "ii") return "2";
        return "3";
    }

    // «primera de», «1ra.», «segunda», «iii» → número del libro
    [GeneratedRegex(@"^(?:(?:primera|primero|1ra|1era|1ro|1a|segunda|segundo|2da|2do|2a|tercera|tercero|3ra|3era|3ro|3a|iii|ii|i)\.?\s+(?:de\s+)?|(?<n>[1-3])\s*(?:ra|era|ro|da|do|a)?\.?\s+(?:de\s+)?)(?=[a-z])")]
    private static partial Regex OrdinalRegex();

    // «san juan» → «sanjuan» (no afecta a «santiago»)
    [GeneratedRegex(@"\bsan\s+(mateo|marcos|lucas|juan)\b")]
    private static partial Regex SanRegex();

    [GeneratedRegex(@"^(?<pre>[1-3])?\s*(?<nombre>[a-z][a-z\s\.]*?)\.?\s*(?<resto>\d.*)?$")]
    private static partial Regex LibroRegex();

    // 3 | 3:16 | 3 16 | 3.16 | 3,16 | 3:16-18 | 3:16-4:2
    [GeneratedRegex(@"^(?<cap>\d{1,3})(?:\s*[:\.,\s]\s*(?<ini>\d{1,3})(?:\s*-\s*(?:(?<capfin>\d{1,3})\s*[:\.]\s*)?(?<fin>\d{1,3}))?)?$")]
    private static partial Regex RestoRegex();
}
