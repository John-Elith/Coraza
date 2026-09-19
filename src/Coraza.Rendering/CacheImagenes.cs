using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Coraza.Rendering;

/// <summary>
/// Carga imágenes sin bloquear el archivo, corrige la orientación EXIF (fotos de celular),
/// limita el tamaño decodificado (fotos de más de 50 MP) y conserva las recientes en memoria.
/// </summary>
public static class CacheImagenes
{
    private const int Capacidad = 80;
    private const int AnchoMaximo = 3840;

    private static readonly object Candado = new();
    private static readonly Dictionary<string, LinkedListNode<(string Clave, BitmapSource Imagen)>> Mapa = new();
    private static readonly LinkedList<(string Clave, BitmapSource Imagen)> Recientes = new();

    /// <summary>Devuelve la imagen o null si no existe o está dañada (nunca lanza excepción).</summary>
    public static BitmapSource? Obtener(string? ruta, int anchoDecodificado = 0)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return null;
        try
        {
            var info = new FileInfo(ruta);
            if (!info.Exists) return null;
            var clave = $"{info.FullName}|{anchoDecodificado}|{info.LastWriteTimeUtc.Ticks}";
            lock (Candado)
            {
                if (Mapa.TryGetValue(clave, out var nodo))
                {
                    Recientes.Remove(nodo);
                    Recientes.AddFirst(nodo);
                    return nodo.Value.Imagen;
                }
            }

            var imagen = Cargar(info.FullName, anchoDecodificado);
            if (imagen is null) return null;

            lock (Candado)
            {
                if (!Mapa.ContainsKey(clave))
                {
                    Mapa[clave] = Recientes.AddFirst((clave, imagen));
                    while (Recientes.Count > Capacidad)
                    {
                        var ultimo = Recientes.Last!;
                        Recientes.RemoveLast();
                        Mapa.Remove(ultimo.Value.Clave);
                    }
                }
            }
            return imagen;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Decodifica en segundo plano la imagen de la siguiente diapositiva para que el cambio sea instantáneo.</summary>
    public static void Precargar(string? ruta, int anchoDecodificado = 0)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return;
        Task.Run(() => Obtener(ruta, anchoDecodificado));
    }

    public static (int Ancho, int Alto)? Dimensiones(string ruta)
    {
        try
        {
            using var flujo = File.OpenRead(ruta);
            var cuadro = BitmapFrame.Create(flujo, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            return (cuadro.PixelWidth, cuadro.PixelHeight);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Vaciar()
    {
        lock (Candado)
        {
            Mapa.Clear();
            Recientes.Clear();
        }
    }

    private static BitmapSource? Cargar(string ruta, int anchoDecodificado)
    {
        var rotacion = Rotation.Rotate0;
        var anchoOriginal = 0;
        try
        {
            using var flujo = File.OpenRead(ruta);
            var cuadro = BitmapFrame.Create(flujo, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            anchoOriginal = cuadro.PixelWidth;
            if (cuadro.Metadata is BitmapMetadata meta && meta.ContainsQuery("/app1/ifd/{ushort=274}"))
            {
                rotacion = meta.GetQuery("/app1/ifd/{ushort=274}") switch
                {
                    (ushort)3 => Rotation.Rotate180,
                    (ushort)6 => Rotation.Rotate90,
                    (ushort)8 => Rotation.Rotate270,
                    _ => Rotation.Rotate0,
                };
            }
        }
        catch (Exception)
        {
            // Sin metadatos legibles: se intenta decodificar igualmente.
        }

        try
        {
            var limite = anchoDecodificado > 0 ? anchoDecodificado : AnchoMaximo;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(ruta);
            bmp.Rotation = rotacion;
            if (anchoOriginal == 0 || anchoOriginal > limite) bmp.DecodePixelWidth = limite;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
