using System.Text;

namespace Coraza.Core.Diapositivas;

/// <summary>Fragmento de texto con su formato.</summary>
public sealed record Fragmento(string Texto, bool Negrita, bool Cursiva, bool Subrayado, bool Resaltado, string? Color);

/// <summary>
/// Formato sencillo para textos y anuncios, fácil de escribir para cualquier voluntario:
/// **negrita**, *cursiva*, __subrayado__, ==resaltado==, {coral}color{/} o {#RRGGBB}color{/}, y listas con «- ».
/// </summary>
public static class FormatoTexto
{
    private static readonly Dictionary<string, string> ColoresConNombre = new(StringComparer.OrdinalIgnoreCase)
    {
        ["coral"] = "#FC8F8F",
        ["teja"] = "#B44446",
        ["vino"] = "#64242F",
        ["lino"] = "#DFD9D8",
        ["blanco"] = "#FFFFFF",
        ["negro"] = "#000000",
        ["dorado"] = "#D9A441",
        ["verde"] = "#4E8F6A",
        ["azul"] = "#3E6B8A",
        ["gris"] = "#A99597",
    };

    public static IReadOnlyCollection<string> NombresDeColores => ColoresConNombre.Keys;

    public static IReadOnlyList<Fragmento> Analizar(string linea)
    {
        var resultado = new List<Fragmento>();
        var actual = new StringBuilder();
        bool negrita = false, cursiva = false, subrayado = false, resaltado = false;
        var colores = new Stack<string>();

        void Cerrar()
        {
            if (actual.Length == 0) return;
            resultado.Add(new Fragmento(actual.ToString(), negrita, cursiva, subrayado, resaltado, colores.Count > 0 ? colores.Peek() : null));
            actual.Clear();
        }

        for (var i = 0; i < linea.Length; i++)
        {
            if (Empieza(linea, i, "**")) { Cerrar(); negrita = !negrita; i++; continue; }
            if (Empieza(linea, i, "__")) { Cerrar(); subrayado = !subrayado; i++; continue; }
            if (Empieza(linea, i, "==")) { Cerrar(); resaltado = !resaltado; i++; continue; }
            if (linea[i] == '*') { Cerrar(); cursiva = !cursiva; continue; }
            if (linea[i] == '{')
            {
                var fin = linea.IndexOf('}', i);
                if (fin > i)
                {
                    var clave = linea[(i + 1)..fin].Trim();
                    if (clave == "/")
                    {
                        Cerrar();
                        if (colores.Count > 0) colores.Pop();
                        i = fin;
                        continue;
                    }
                    var color = clave.StartsWith('#') && clave.Length is 7 or 9 ? clave : ColoresConNombre.GetValueOrDefault(clave);
                    if (color is not null)
                    {
                        Cerrar();
                        colores.Push(color);
                        i = fin;
                        continue;
                    }
                }
            }
            actual.Append(linea[i]);
        }
        Cerrar();
        return resultado;
    }

    /// <summary>El texto sin marcas de formato (para búsquedas y para el operador).</summary>
    public static string QuitarMarcas(string texto) =>
        string.Join("\n", texto.Replace("\r\n", "\n").Split('\n').Select(l => string.Concat(Analizar(l).Select(f => f.Texto))));

    /// <summary>Convierte las líneas que empiezan con «- » en viñetas.</summary>
    public static string Listas(string texto) =>
        string.Join("\n", texto.Split('\n').Select(l => l.TrimStart().StartsWith("- ") ? "•  " + l.TrimStart()[2..] : l));

    private static bool Empieza(string texto, int i, string marca) =>
        i + marca.Length <= texto.Length && string.CompareOrdinal(texto, i, marca, 0, marca.Length) == 0;
}
