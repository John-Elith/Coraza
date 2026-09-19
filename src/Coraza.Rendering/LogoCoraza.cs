using System.Windows;
using System.Windows.Media;

namespace Coraza.Rendering;

/// <summary>
/// Escudo (peto) estilizado de Coraza con líneas geométricas y un destello coral en la zona del corazón.
/// Se dibuja con vectores, así se ve nítido en cualquier tamaño.
/// </summary>
public static class LogoCoraza
{
    /// <summary>Silueta del escudo en una caja de 100×120 (se usa también como máscara del «Barrido Coraza»).</summary>
    public static readonly Geometry Escudo = Geometry.Parse(
        "M50,4 C62,10 76,13 90,14 C92,52 82,88 50,116 C18,88 8,52 10,14 C24,13 38,10 50,4 Z");

    private static readonly Geometry Lineas = Geometry.Parse(
        "M50,17 L50,104 M22,32 L50,45 L78,32 M24,56 L50,69 L76,56 M31,80 L50,90 L69,80");

    private static readonly Geometry Destello = Geometry.Parse(
        "M50,48 C51.4,54 52.6,55.6 59,57 C52.6,58.4 51.4,60 50,66 C48.6,60 47.4,58.4 41,57 C47.4,55.6 48.6,54 50,48 Z");

    public static DrawingImage Crear(bool paraFondoOscuro = true)
    {
        var vino = Color.FromRgb(0x64, 0x24, 0x2F);
        var teja = Color.FromRgb(0xB4, 0x44, 0x46);
        var coral = Color.FromRgb(0xFC, 0x8F, 0x8F);
        var lino = Color.FromRgb(0xDF, 0xD9, 0xD8);

        var relleno = new LinearGradientBrush(teja, vino, new Point(0.5, 0), new Point(0.5, 1));
        var borde = new Pen(new SolidColorBrush(paraFondoOscuro ? coral : vino), 2.2) { LineJoin = PenLineJoin.Round };
        var lineas = new Pen(new SolidColorBrush(Color.FromArgb(paraFondoOscuro ? (byte)150 : (byte)190, lino.R, lino.G, lino.B)), 2.4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        var brillo = new RadialGradientBrush(Color.FromArgb(200, coral.R, coral.G, coral.B), Color.FromArgb(0, coral.R, coral.G, coral.B));

        var grupo = new DrawingGroup();
        grupo.Children.Add(new GeometryDrawing(relleno, borde, Escudo));
        grupo.Children.Add(new GeometryDrawing(null, lineas, Lineas));
        grupo.Children.Add(new GeometryDrawing(brillo, null, new EllipseGeometry(new Point(50, 57), 17, 17)));
        grupo.Children.Add(new GeometryDrawing(new SolidColorBrush(coral), null, Destello));
        grupo.Children.Add(new GeometryDrawing(Brushes.White, null, new EllipseGeometry(new Point(50, 57), 1.6, 1.6)));
        var imagen = new DrawingImage(grupo);
        imagen.Freeze();
        return imagen;
    }
}
