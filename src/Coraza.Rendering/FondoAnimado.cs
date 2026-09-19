using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Coraza.Core.Modelos;

namespace Coraza.Rendering;

/// <summary>
/// Fondos en movimiento generados en tiempo real (no ocupan espacio en disco): aurora de colores,
/// luces bokeh que flotan y ondas suaves. Solo se animan transformaciones y opacidades, que la GPU
/// compone sin costo de CPU. En miniaturas se dibujan quietos. Usan una semilla fija, así la salida
/// y el monitor del operador se ven iguales.
/// </summary>
public sealed class FondoAnimado : Canvas
{
    private static readonly Color Coral = Color.FromRgb(0xFC, 0x8F, 0x8F);
    private static readonly Color Lino = Color.FromRgb(0xDF, 0xD9, 0xD8);

    private readonly FondoTema _fondo;
    private readonly bool _estatico;
    private Size _tamano;

    public FondoAnimado(FondoTema fondo, bool estatico)
    {
        _fondo = fondo;
        _estatico = estatico;
        ClipToBounds = true;
        IsHitTestVisible = false;
        SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width < 1 || e.NewSize.Height < 1) return;
            if (Math.Abs(e.NewSize.Width - _tamano.Width) < 2 && Math.Abs(e.NewSize.Height - _tamano.Height) < 2) return;
            _tamano = e.NewSize;
            Construir();
        };
    }

    private double Velocidad => Math.Clamp(_fondo.Velocidad, 0.1, 4);

    private double Intensidad => Math.Clamp(_fondo.Intensidad, 0, 1);

    private void Construir()
    {
        Children.Clear();
        var azar = new Random(7);
        switch (_fondo.Animacion)
        {
            case AnimacionFondo.Aurora: Aurora(azar); break;
            case AnimacionFondo.Bokeh: Bokeh(azar); break;
            case AnimacionFondo.Ondas: Ondas(); break;
        }
    }

    /// <summary>Manchas de luz grandes y difusas que se desplazan y respiran lentamente.</summary>
    private void Aurora(Random azar)
    {
        var w = _tamano.Width;
        var h = _tamano.Height;
        var baseColor = Aclarar(Colores.DesdeHex(_fondo.Color1, Color.FromRgb(0x64, 0x24, 0x2F)), 0.35);
        var colores = new[] { baseColor, Coral, Color.FromRgb(0xB4, 0x44, 0x46), baseColor };
        for (var i = 0; i < colores.Length; i++)
        {
            var diametro = h * (0.9 + i * 0.22);
            var alfa = (byte)(35 + 85 * Intensidad);
            var mancha = new Ellipse
            {
                Width = diametro * 1.45,
                Height = diametro,
                Fill = new RadialGradientBrush(Color.FromArgb(alfa, colores[i].R, colores[i].G, colores[i].B), Color.FromArgb(0, colores[i].R, colores[i].G, colores[i].B)),
            };
            SetLeft(mancha, w * (0.05 + 0.28 * i) - diametro * 0.7);
            SetTop(mancha, h * (0.15 + 0.3 * (i % 2)) - diametro / 2);
            var mover = new TranslateTransform();
            mancha.RenderTransform = mover;
            Children.Add(mancha);
            if (_estatico) continue;
            var suave = new SineEase { EasingMode = EasingMode.EaseInOut };
            mover.BeginAnimation(TranslateTransform.XProperty, Vaiven(-w * 0.16, w * 0.16, (24 + i * 9 + azar.NextDouble() * 6) / Velocidad, suave));
            mover.BeginAnimation(TranslateTransform.YProperty, Vaiven(-h * 0.1, h * 0.1, (17 + i * 7) / Velocidad, suave));
            mancha.BeginAnimation(OpacityProperty, Vaiven(0.55, 1, (10 + i * 4) / Velocidad, suave));
        }
    }

    /// <summary>Luces suaves (bokeh) que suben flotando con un leve vaivén.</summary>
    private void Bokeh(Random azar)
    {
        var w = _tamano.Width;
        var h = _tamano.Height;
        var cantidad = (int)(8 + 26 * Intensidad);
        for (var i = 0; i < cantidad; i++)
        {
            var tam = h * (0.02 + azar.NextDouble() * 0.07);
            var color = (i % 3) switch { 0 => Coral, 1 => Colors.White, _ => Lino };
            var alfa = (byte)(50 + 100 * Intensidad);
            var luz = new Ellipse
            {
                Width = tam,
                Height = tam,
                Fill = new RadialGradientBrush(Color.FromArgb(alfa, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B)),
                Opacity = 0.45 + azar.NextDouble() * 0.55,
            };
            SetLeft(luz, azar.NextDouble() * w);
            SetTop(luz, 0);
            var mover = new TranslateTransform(0, azar.NextDouble() * h);
            luz.RenderTransform = mover;
            Children.Add(luz);
            if (_estatico) continue;

            var duracion = TimeSpan.FromSeconds((14 + azar.NextDouble() * 18) / Velocidad);
            var subir = new DoubleAnimation(h + tam, -tam, duracion) { RepeatBehavior = RepeatBehavior.Forever };
            var reloj = subir.CreateClock();
            mover.ApplyAnimationClock(TranslateTransform.YProperty, reloj);
            reloj.Controller?.Seek(TimeSpan.FromSeconds(azar.NextDouble() * duracion.TotalSeconds), TimeSeekOrigin.BeginTime);
            mover.BeginAnimation(TranslateTransform.XProperty, Vaiven(-tam, tam, (5 + azar.NextDouble() * 6) / Velocidad, new SineEase { EasingMode = EasingMode.EaseInOut }));
        }
    }

    /// <summary>Bandas amplias y translúcidas que ondulan lentamente, como humo o agua.</summary>
    private void Ondas()
    {
        var w = _tamano.Width;
        var h = _tamano.Height;
        var claro = Aclarar(Colores.DesdeHex(_fondo.Color1, Color.FromRgb(0x64, 0x24, 0x2F)), 0.4);
        for (var i = 0; i < 4; i++)
        {
            var ancho = w * 2.2;
            var alto = h * (0.5 + 0.12 * i);
            var color = i % 2 == 0 ? claro : Coral;
            var alfa = (byte)(30 + 55 * Intensidad);
            var banda = new Ellipse
            {
                Width = ancho,
                Height = alto,
                Fill = new LinearGradientBrush(Color.FromArgb(0, color.R, color.G, color.B), Color.FromArgb(alfa, color.R, color.G, color.B), 90),
            };
            SetLeft(banda, -w * 0.6);
            SetTop(banda, h * (0.35 + 0.1 * i));
            var girar = new RotateTransform(0, ancho / 2, alto / 2 + h * 0.6);
            banda.RenderTransform = girar;
            Children.Add(banda);
            if (_estatico) continue;
            girar.BeginAnimation(RotateTransform.AngleProperty,
                Vaiven(-6 - i * 2, 6 + i * 2, (16 + i * 5) / Velocidad, new SineEase { EasingMode = EasingMode.EaseInOut }));
        }
    }

    private static DoubleAnimation Vaiven(double desde, double hasta, double segundos, IEasingFunction suave) =>
        new(desde, hasta, TimeSpan.FromSeconds(segundos))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = suave,
        };

    private static Color Aclarar(Color c, double factor) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * factor),
        (byte)(c.G + (255 - c.G) * factor),
        (byte)(c.B + (255 - c.B) * factor));
}
