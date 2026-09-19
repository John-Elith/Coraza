using System.Windows.Media;

namespace Coraza.Rendering;

public static class Colores
{
    public static Color DesdeHex(string? hex, Color? predeterminado = null)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex.Trim()) is Color c) return c;
            }
            catch (FormatException)
            {
            }
        }
        return predeterminado ?? Colors.White;
    }

    public static SolidColorBrush Pincel(string? hex, double opacidad = 1)
    {
        var pincel = new SolidColorBrush(DesdeHex(hex)) { Opacity = opacidad };
        pincel.Freeze();
        return pincel;
    }

    public static string AHex(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
