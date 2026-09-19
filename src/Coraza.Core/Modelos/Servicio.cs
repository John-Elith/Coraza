namespace Coraza.Core.Modelos;

public enum TipoElemento { Cancion, Pasaje, Imagen, Texto, Encabezado, CuentaRegresiva }

/// <summary>Orden del servicio: la lista de todo lo que se proyectará en una reunión.</summary>
public sealed class Servicio
{
    public long Id { get; set; }
    public string Nombre { get; set; } = "";
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string? Plantilla { get; set; }
    public string? Notas { get; set; }
    public DateTime Modificado { get; set; }
    public List<ElementoServicio> Elementos { get; set; } = new();
}

public sealed class ElementoServicio
{
    public long Id { get; set; }
    public long ServicioId { get; set; }
    public int Posicion { get; set; }
    public TipoElemento Tipo { get; set; }
    /// <summary>Id de la canción, medio o texto. Para pasajes es el id de la versión bíblica.</summary>
    public long? ReferenciaId { get; set; }
    public string Titulo { get; set; } = "";
    /// <summary>Datos adicionales (p. ej. la referencia bíblica en JSON).</summary>
    public string? Datos { get; set; }
    public long? TemaId { get; set; }
    /// <summary>Notas visibles solo para el operador.</summary>
    public string? Notas { get; set; }
    public int? DuracionMin { get; set; }
    /// <summary>Color de los encabezados de sección.</summary>
    public string? Color { get; set; }
    /// <summary>Avance automático: segundos por diapositiva (null = a mano).</summary>
    public int? AvanceSegundos { get; set; }
    /// <summary>Al llegar a la última diapositiva vuelve a la primera (fotos, anuncios, sala de espera).</summary>
    public bool Bucle { get; set; }

    public ElementoServicio Clonar() => (ElementoServicio)MemberwiseClone();
}
