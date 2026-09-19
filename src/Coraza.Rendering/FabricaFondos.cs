using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Coraza.Core.Modelos;

namespace Coraza.Rendering;

/// <summary>Construye la capa de fondo de un tema: color, degradado lineal o radial, o imagen con oscurecido y desenfoque.</summary>
public static class FabricaFondos
{
    public static FrameworkElement Crear(FondoTema fondo, bool miniatura)
    {
        var contenedor = new Grid { ClipToBounds = true, IsHitTestVisible = false };
        var c1 = Colores.DesdeHex(fondo.Color1, Color.FromRgb(0x64, 0x24, 0x2F));
        var c2 = Colores.DesdeHex(fondo.Color2, Color.FromRgb(0x1E, 0x0B, 0x0F));

        switch (fondo.Tipo)
        {
            case TipoFondo.Color:
                contenedor.Background = Congelar(new SolidColorBrush(c1));
                break;

            case TipoFondo.Degradado:
            {
                var rad = fondo.Angulo * Math.PI / 180;
                var dx = Math.Cos(rad) / 2;
                var dy = Math.Sin(rad) / 2;
                contenedor.Background = Congelar(new LinearGradientBrush(c1, c2, new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy)));
                break;
            }

            case TipoFondo.Radial:
                contenedor.Background = Congelar(new RadialGradientBrush(c1, c2)
                {
                    Center = new Point(0.5, 0.42),
                    GradientOrigin = new Point(0.5, 0.42),
                    RadiusX = 0.8,
                    RadiusY = 0.95,
                });
                break;

            case TipoFondo.Video:
            {
                // Video en bucle y sin sonido; en miniaturas, un fotograma.
                contenedor.Background = Congelar(new SolidColorBrush(c2));
                var rutaVideo = fondo.RutaImagen;
                if (string.IsNullOrWhiteSpace(rutaVideo) || !System.IO.File.Exists(rutaVideo)) break;
                if (miniatura)
                {
                    var fotograma = new Image { Stretch = Stretch.UniformToFill };
                    contenedor.Children.Add(fotograma);
                    CargarFotograma(fotograma, rutaVideo);
                    break;
                }
                var video = new MediaElement
                {
                    Source = new Uri(rutaVideo),
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Close,
                    IsMuted = true,
                    Stretch = Stretch.UniformToFill,
                };
                video.MediaEnded += (_, _) =>
                {
                    video.Position = TimeSpan.Zero;
                    video.Play();
                };
                video.MediaFailed += (_, _) => contenedor.Children.Remove(video);
                video.Loaded += (_, _) => video.Play();
                contenedor.Children.Add(video);
                break;
            }

            case TipoFondo.Imagen:
            {
                contenedor.Background = Congelar(new SolidColorBrush(c2));
                var fuente = CacheImagenes.Obtener(fondo.RutaImagen, miniatura ? 480 : 0);
                if (fuente is not null)
                {
                    var imagen = new Image { Source = fuente, Stretch = Stretch.UniformToFill };
                    RenderOptions.SetBitmapScalingMode(imagen, BitmapScalingMode.HighQuality);
                    if (fondo.Desenfoque > 0)
                    {
                        var desenfoque = new BlurEffect { KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
                        imagen.Effect = desenfoque;
                        contenedor.SizeChanged += (_, e) =>
                        {
                            var radio = e.NewSize.Height * fondo.Desenfoque / 100;
                            desenfoque.Radius = radio;
                            imagen.Margin = new Thickness(-radio * 1.5); // evita bordes transparentes
                        };
                    }
                    contenedor.Children.Add(imagen);
                }
                break;
            }
        }

        if (fondo.Oscurecer > 0.001)
        {
            contenedor.Children.Add(new Rectangle { Fill = Brushes.Black, Opacity = Math.Clamp(fondo.Oscurecer, 0, 1) });
        }
        // Las luces del fondo animado van encima del oscurecido para que sigan brillando.
        if (fondo.Animacion != AnimacionFondo.Ninguna) contenedor.Children.Add(new FondoAnimado(fondo, miniatura));
        return contenedor;
    }

    private static async void CargarFotograma(Image imagen, string ruta) =>
        imagen.Source = await MiniaturasShell.ObtenerAsync(ruta, 480);

    private static Brush Congelar(Brush b)
    {
        b.Freeze();
        return b;
    }
}
