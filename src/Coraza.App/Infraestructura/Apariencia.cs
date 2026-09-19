using System.Windows;
using System.Windows.Media;

namespace Coraza.App.Infraestructura;

/// <summary>
/// Paletas de la interfaz del operador. Se aplican al instante reemplazando los pinceles
/// que las vistas usan con DynamicResource. B.Coral es el color de acento: coral sobre fondos
/// oscuros y teja profundo sobre el modo claro para mantener el contraste de lectura.
/// </summary>
public static class Apariencia
{
    public const string Vino = "Vino Coraza";
    public const string Oscuro = "Oscuro grafito";
    public const string Claro = "Claro lino";

    public static IReadOnlyList<string> Paletas { get; } = new[] { Vino, Oscuro, Claro };

    private static readonly Dictionary<string, Dictionary<string, string>> Definiciones = new()
    {
        // Identidad Coraza equilibrada: base carbón casi negra, texto lino claro y el vino reservado
        // para la barra superior y los acentos (así el color de marca resalta en vez de saturar la vista).
        [Vino] = new()
        {
            ["B.Fondo"] = "#141011",
            ["B.Panel"] = "#1C1718",
            ["B.PanelAlto"] = "#262021",
            ["B.Borde"] = "#382E30",
            ["B.BordeHover"] = "#584648",
            ["B.Entrada"] = "#100D0E",
            ["B.Texto"] = "#ECE6E5",
            ["B.TextoSuave"] = "#A79B9C",
            ["B.Barra"] = "#5E222C",
            ["B.LibroNT"] = "#3A2429",
            ["B.Hover"] = "#1FFC8F8F",
            ["B.Seleccion"] = "#59B44446",
            ["B.Velo"] = "#B3000000",
            ["B.Coral"] = "#FC8F8F",
            ["B.SobreAcento"] = "#2A1216",
            ["B.Advertencia"] = "#D9A441",
            ["B.Estado"] = "#8FD1A9",
        },
        // Modo oscuro neutro y descansado: grises grafito con los acentos coral y teja.
        [Oscuro] = new()
        {
            ["B.Fondo"] = "#131416",
            ["B.Panel"] = "#1B1C1F",
            ["B.PanelAlto"] = "#26272B",
            ["B.Borde"] = "#34363B",
            ["B.BordeHover"] = "#4A4C53",
            ["B.Entrada"] = "#0F1012",
            ["B.Texto"] = "#E6E6E8",
            ["B.TextoSuave"] = "#9C9EA5",
            ["B.Barra"] = "#1F2024",
            ["B.LibroNT"] = "#2E2528",
            ["B.Hover"] = "#14FFFFFF",
            ["B.Seleccion"] = "#59B44446",
            ["B.Velo"] = "#B3000000",
            ["B.Coral"] = "#FC8F8F",
            ["B.SobreAcento"] = "#64242F",
            ["B.Advertencia"] = "#D9A441",
            ["B.Estado"] = "#8FD1A9",
        },
        // Modo claro para salones iluminados: fondo lino, texto vino y la barra superior de la marca.
        [Claro] = new()
        {
            ["B.Fondo"] = "#EFEBEA",
            ["B.Panel"] = "#F8F6F5",
            ["B.PanelAlto"] = "#FFFFFF",
            ["B.Borde"] = "#D6CDCC",
            ["B.BordeHover"] = "#B9A9AA",
            ["B.Entrada"] = "#FFFFFF",
            ["B.Texto"] = "#2A1216",
            ["B.TextoSuave"] = "#6E5A5D",
            ["B.Barra"] = "#64242F",
            ["B.LibroNT"] = "#F3E1E2",
            ["B.Hover"] = "#14B44446",
            ["B.Seleccion"] = "#40B44446",
            ["B.Velo"] = "#D9F8F6F5",
            ["B.Coral"] = "#A8373B",
            ["B.SobreAcento"] = "#FFFFFF",
            ["B.Advertencia"] = "#9A6508",
            ["B.Estado"] = "#2E7050",
        },
    };

    public static string Actual { get; private set; } = Vino;

    public static bool EsOscura => Actual != Claro;

    public static void Aplicar(string? nombre)
    {
        if (nombre is null || !Definiciones.TryGetValue(nombre, out var paleta))
        {
            nombre = Vino;
            paleta = Definiciones[Vino];
        }
        var recursos = Application.Current.Resources;
        foreach (var (clave, hex) in paleta)
        {
            var pincel = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            pincel.Freeze();
            recursos[clave] = pincel;
        }
        Actual = nombre;

        // La barra de título de los diálogos abiertos sigue a la paleta (la proyección siempre es oscura).
        foreach (Window ventana in Application.Current.Windows)
            if (ventana is not Servicios.VentanaProyeccion) Nativo.AplicarBarraTitulo(ventana, EsOscura);
    }

    public static string Siguiente(string? actual)
    {
        var indice = Paletas.ToList().IndexOf(actual ?? Vino);
        return Paletas[(indice + 1) % Paletas.Count];
    }
}
