namespace Coraza.Core.Modelos;

public enum TipoMedio { Imagen, Video, Audio, Documento }

public enum ModoAjuste { Ajustar, Rellenar, Estirar, Centrar }

public sealed class Medio
{
    /// <summary>Etiqueta de los documentos que en realidad son presentaciones de fotos (avanzan solas y en bucle).</summary>
    public const string EtiquetaPresentacion = "presentacion";

    /// <summary>Páginas o fotos de un documento guardado como carpeta, en orden.</summary>
    public static IReadOnlyList<string> PaginasDe(string carpeta) =>
        Directory.Exists(carpeta)
            ? Directory.GetFiles(carpeta).Where(FormatosMedios.EsImagen).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<string>();

    public long Id { get; set; }
    public TipoMedio Tipo { get; set; } = TipoMedio.Imagen;
    /// <summary>Ruta relativa a la carpeta de datos de Coraza (o absoluta si está fuera de ella).</summary>
    public string Ruta { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int? Ancho { get; set; }
    public int? Alto { get; set; }
    public double? Duracion { get; set; }
    public string? Etiquetas { get; set; }
    public ModoAjuste Ajuste { get; set; } = ModoAjuste.Ajustar;
    public DateTime FechaCreacion { get; set; }
}

public static class FormatosMedios
{
    public static readonly string[] Imagenes =
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".ico"
    };

    /// <summary>Videos que reproduce el motor de Windows (MP4 H.264 es el más compatible).</summary>
    public static readonly string[] Videos = { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".mpg", ".mpeg", ".3gp" };

    public static readonly string[] Audios = { ".mp3", ".wav", ".wma", ".m4a", ".aac" };

    public static readonly string[] Documentos = { ".pdf", ".pptx", ".ppt", ".ppsx", ".pps", ".pptm" };

    public static bool EsImagen(string ruta) => Imagenes.Contains(Extension(ruta));

    public static bool EsVideo(string ruta) => Videos.Contains(Extension(ruta));

    public static bool EsAudio(string ruta) => Audios.Contains(Extension(ruta));

    public static bool EsDocumento(string ruta) => Documentos.Contains(Extension(ruta));

    public static bool EsCompatible(string ruta) => EsImagen(ruta) || EsVideo(ruta) || EsAudio(ruta) || EsDocumento(ruta);

    private static string Extension(string ruta) => Path.GetExtension(ruta).ToLowerInvariant();
}
