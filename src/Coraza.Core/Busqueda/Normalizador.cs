using System.Globalization;
using System.Text;

namespace Coraza.Core.Busqueda;

/// <summary>Normaliza texto para búsquedas tolerantes a tildes, mayúsculas y signos.</summary>
public static class Normalizador
{
    /// <summary>Minúsculas, sin tildes ni diéresis (ñ → n), sin signos y con espacios simples.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        var espacioPendiente = false;
        foreach (var c in descompuesto)
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                if (espacioPendiente && sb.Length > 0) sb.Append(' ');
                espacioPendiente = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                espacioPendiente = true;
            }
        }
        return sb.ToString();
    }

    /// <summary>Quita tildes pero conserva dígitos y signos de puntuación (útil para citas bíblicas).</summary>
    public static string QuitarTildes(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string[] Palabras(string? texto) =>
        Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Distancia de edición con corte anticipado: devuelve max+1 si se supera el máximo.</summary>
    public static int Levenshtein(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        Span<int> previo = stackalloc int[b.Length + 1];
        Span<int> actual = stackalloc int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previo[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            actual[0] = i;
            var minimoFila = actual[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var costo = a[i - 1] == b[j - 1] ? 0 : 1;
                actual[j] = Math.Min(Math.Min(actual[j - 1] + 1, previo[j] + 1), previo[j - 1] + costo);
                minimoFila = Math.Min(minimoFila, actual[j]);
            }
            if (minimoFila > max) return max + 1;
            actual.CopyTo(previo);
        }
        return previo[b.Length];
    }

    /// <summary>Errores de escritura tolerados según la longitud de la palabra.</summary>
    public static int Tolerancia(int longitud) => longitud switch
    {
        <= 3 => 0,
        <= 6 => 1,
        _ => 2,
    };
}
