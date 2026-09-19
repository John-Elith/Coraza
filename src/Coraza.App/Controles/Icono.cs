using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Coraza.App.Controles;

/// <summary>Icono lineal dibujado a partir de una geometría en una caja de 24×24. Toma el color del texto.</summary>
public sealed class Icono : FrameworkElement
{
    public static readonly DependencyProperty DatosProperty = DependencyProperty.Register(
        nameof(Datos), typeof(Geometry), typeof(Icono), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icono), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RellenoProperty = DependencyProperty.Register(
        nameof(Relleno), typeof(bool), typeof(Icono), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GrosorProperty = DependencyProperty.Register(
        nameof(Grosor), typeof(double), typeof(Icono), new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Datos
    {
        get => (Geometry?)GetValue(DatosProperty);
        set => SetValue(DatosProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public bool Relleno
    {
        get => (bool)GetValue(RellenoProperty);
        set => SetValue(RellenoProperty, value);
    }

    public double Grosor
    {
        get => (double)GetValue(GrosorProperty);
        set => SetValue(GrosorProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(16, 16);

    protected override void OnRender(DrawingContext dc)
    {
        var datos = Datos;
        var lado = Math.Min(ActualWidth, ActualHeight);
        if (datos is null || lado <= 0) return;
        var escala = lado / 24.0;
        dc.PushTransform(new TranslateTransform((ActualWidth - lado) / 2, (ActualHeight - lado) / 2));
        dc.PushTransform(new ScaleTransform(escala, escala));
        var lapiz = new Pen(Foreground, Grosor)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        dc.DrawGeometry(Relleno ? Foreground : null, lapiz, datos);
        dc.Pop();
        dc.Pop();
    }
}
