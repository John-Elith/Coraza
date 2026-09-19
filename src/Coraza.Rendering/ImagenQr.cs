using System.Windows.Media;
using System.Windows.Media.Imaging;
using Coraza.Remoto.Qr;

namespace Coraza.Rendering;

/// <summary>
/// Pinta el código QR del control remoto como imagen para la ventana de escritorio.
///
/// El generador vive en Coraza.Remoto (sin WPF, para poder probarlo); aquí solo se
/// convierte la matriz en píxeles. Se usa Gray8: un byte por píxel, que para blanco
/// y negro puro es todo lo que hace falta.
/// </summary>
public static class ImagenQr
{
    /// <param name="escala">Píxeles por módulo. Cuanto más grande, más fácil de escanear.</param>
    /// <param name="silencio">Margen blanco alrededor, en módulos. La norma pide 4 y sin él muchos lectores fallan.</param>
    public static BitmapSource Crear(string texto, int escala = 8, int silencio = 4)
    {
        var m = GeneradorQr.Crear(texto);
        var lado = m.GetLength(0);
        var modulos = lado + silencio * 2;
        var pixeles = modulos * escala;

        var datos = new byte[pixeles * pixeles];
        for (var py = 0; py < pixeles; py++)
        {
            var my = py / escala - silencio;
            for (var px = 0; px < pixeles; px++)
            {
                var mx = px / escala - silencio;
                var oscuro = mx >= 0 && my >= 0 && mx < lado && my < lado && m[mx, my];
                datos[py * pixeles + px] = oscuro ? (byte)0 : (byte)255;
            }
        }

        var imagen = BitmapSource.Create(pixeles, pixeles, 96, 96,
            PixelFormats.Gray8, null, datos, pixeles);
        imagen.Freeze();   // congelada: se puede pasar entre hilos sin copiarla
        return imagen;
    }
}
