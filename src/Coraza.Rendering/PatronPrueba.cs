using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Coraza.Rendering;

/// <summary>
/// Patrón de prueba: cuadrícula, márgenes de seguridad, círculo de proporción y tamaños de letra,
/// para verificar en segundos que el proyector muestra toda el área y que el texto se lee desde el fondo.
/// </summary>
public sealed class PatronPrueba : FrameworkElement
{
    private static readonly Typeface Tipografia = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public PatronPrueba()
    {
        IsHitTestVisible = false;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x0A, 0x0C)), null, new Rect(0, 0, w, h));

        var cuadricula = new Pen(new SolidColorBrush(Color.FromArgb(70, 0xDF, 0xD9, 0xD8)), Math.Max(1, h / 1080));
        for (var i = 1; i < 10; i++)
        {
            dc.DrawLine(cuadricula, new Point(w * i / 10, 0), new Point(w * i / 10, h));
            dc.DrawLine(cuadricula, new Point(0, h * i / 10), new Point(w, h * i / 10));
        }

        var grosor = Math.Max(2, h / 360);
        dc.DrawRectangle(null, new Pen(Brushes.White, grosor), new Rect(grosor / 2, grosor / 2, w - grosor, h - grosor));

        var seguro = new Pen(new SolidColorBrush(Color.FromRgb(0xB4, 0x44, 0x46)), grosor) { DashStyle = DashStyles.Dash };
        dc.DrawRectangle(null, seguro, new Rect(w * 0.05, h * 0.05, w * 0.9, h * 0.9));

        var coral = new SolidColorBrush(Color.FromRgb(0xFC, 0x8F, 0x8F));
        var cruz = new Pen(coral, grosor);
        dc.DrawLine(cruz, new Point(w / 2 - h * 0.04, h / 2), new Point(w / 2 + h * 0.04, h / 2));
        dc.DrawLine(cruz, new Point(w / 2, h / 2 - h * 0.04), new Point(w / 2, h / 2 + h * 0.04));
        dc.DrawEllipse(null, new Pen(coral, grosor), new Point(w / 2, h / 2), h * 0.25, h * 0.25);

        var pxAncho = (int)Math.Round(w * dpi.DpiScaleX);
        var pxAlto = (int)Math.Round(h * dpi.DpiScaleY);
        Texto(dc, "CORAZA · PATRÓN DE PRUEBA", h * 0.045, new Point(w * 0.07, h * 0.07), Brushes.White, dpi);
        Texto(dc, $"{pxAncho} × {pxAlto} px · {Proporcion(pxAncho, pxAlto)}", h * 0.03, new Point(w * 0.07, h * 0.13), coral, dpi);
        Texto(dc, "El borde blanco debe verse completo. La línea discontinua marca el margen de seguridad (5 %). El círculo debe verse redondo.",
            h * 0.02, new Point(w * 0.07, h * 0.18), Brushes.LightGray, dpi, w * 0.86);

        var y = h * 0.58;
        foreach (var (fraccion, etiqueta) in new[] { (10.0, "1/10 de la altura"), (15.0, "1/15"), (20.0, "1/20 · mínimo recomendado"), (30.0, "1/30 · demasiado pequeño a distancia") })
        {
            var tam = h / fraccion;
            Texto(dc, $"Aa — {etiqueta}", tam * 0.72, new Point(w * 0.07, y), fraccion <= 20 ? Brushes.White : Brushes.Gray, dpi);
            y += tam * 0.95;
        }
    }

    private static void Texto(DrawingContext dc, string texto, double tamano, Point origen, Brush color, DpiScale dpi, double anchoMaximo = 0)
    {
        var ft = new FormattedText(texto, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Tipografia, Math.Max(1, tamano), color, dpi.PixelsPerDip);
        if (anchoMaximo > 0) ft.MaxTextWidth = anchoMaximo;
        dc.DrawText(ft, origen);
    }

    public static string Proporcion(int ancho, int alto)
    {
        if (ancho <= 0 || alto <= 0) return "";
        var r = (double)ancho / alto;
        return r switch
        {
            < 1.40 => "4:3",
            < 1.70 => "16:10",
            < 1.85 => "16:9",
            _ => "21:9",
        };
    }
}
