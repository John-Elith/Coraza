using System.IO;
using System.Windows.Markup;
using System.Windows.Media;

namespace Coraza.Rendering;

/// <summary>
/// Fuentes disponibles: las instaladas en Windows y las copiadas a la carpeta «Fuentes» de Coraza
/// (se usan sin instalarlas, así que no hacen falta permisos de administrador).
/// </summary>
public static class FuentesCoraza
{
    private static readonly Dictionary<string, FontFamily> Personales = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, FontFamily> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Candado = new();

    public static string? CarpetaPersonal { get; private set; }

    public static void CargarCarpeta(string carpeta)
    {
        CarpetaPersonal = carpeta;
        lock (Candado)
        {
            Personales.Clear();
            Cache.Clear();
            if (!Directory.Exists(carpeta)) return;
            try
            {
                var uri = new Uri(Path.GetFullPath(carpeta).TrimEnd('\\') + "\\");
                foreach (var familia in Fonts.GetFontFamilies(uri))
                {
                    var nombre = NombreDe(familia);
                    if (!string.IsNullOrEmpty(nombre)) Personales[nombre] = familia;
                }
            }
            catch (Exception)
            {
                // Una fuente dañada no debe impedir el inicio.
            }
        }
    }

    public static FontFamily Obtener(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) nombre = "Segoe UI";
        lock (Candado)
        {
            if (Personales.TryGetValue(nombre, out var personal)) return personal;
            if (!Cache.TryGetValue(nombre, out var familia))
            {
                familia = new FontFamily(nombre + ", Segoe UI");
                Cache[nombre] = familia;
            }
            return familia;
        }
    }

    public static IReadOnlyList<string> Todas()
    {
        var nombres = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var f in Fonts.SystemFontFamilies)
        {
            var n = NombreDe(f);
            if (!string.IsNullOrEmpty(n)) nombres.Add(n);
        }
        lock (Candado)
        {
            foreach (var n in Personales.Keys) nombres.Add(n);
        }
        return nombres.ToList();
    }

    private static string NombreDe(FontFamily familia)
    {
        if (familia.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("es-es"), out var es)) return es;
        if (familia.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-us"), out var en)) return en;
        var fuente = familia.Source;
        var hash = fuente.LastIndexOf('#');
        return hash >= 0 ? fuente[(hash + 1)..] : fuente;
    }
}
