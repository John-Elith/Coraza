namespace Coraza.Core.Modelos;

/// <summary>Texto, anuncio o aviso. Cada párrafo (separado por una línea en blanco) es una diapositiva.</summary>
public sealed class TextoLibre
{
    public long Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Contenido { get; set; } = "";
    public long? TemaId { get; set; }
    public DateTime FechaModificacion { get; set; }
}
