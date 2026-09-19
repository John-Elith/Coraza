using Coraza.Core.Canciones;

namespace Coraza.Core.Modelos;

public enum TipoSeccion { Verso, PreCoro, Coro, Puente, Final, Intro, Otro }

/// <summary>Parte etiquetada de una canción (Verso 1, Coro, Puente…). Se escribe una vez y se reutiliza en el orden.</summary>
public sealed class Seccion
{
    public long Id { get; set; }
    public long CancionId { get; set; }
    public TipoSeccion Tipo { get; set; }
    /// <summary>0 cuando la sección no lleva número (p. ej. «Coro»).</summary>
    public int Numero { get; set; }
    public string Texto { get; set; } = "";
    public string? Acordes { get; set; }
    public string? TextoSegundoIdioma { get; set; }
    public int Posicion { get; set; }

    /// <summary>Código corto usado en el orden y en las teclas rápidas: V1, C, PC, P…</summary>
    public string Codigo => CodigosSeccion.Codigo(Tipo, Numero);

    /// <summary>Nombre legible: «Verso 1», «Coro», «Pre-coro».</summary>
    public string Nombre => CodigosSeccion.Nombre(Tipo, Numero);
}

public sealed class Cancion
{
    public long Id { get; set; }
    public string Titulo { get; set; } = "";
    public string? Autor { get; set; }
    public string? Tonalidad { get; set; }
    public int? Tempo { get; set; }
    public string? Ccli { get; set; }
    public string? Derechos { get; set; }
    public string Idioma { get; set; } = "es";
    public string? Categoria { get; set; }
    public string? Etiquetas { get; set; }
    public long? TemaId { get; set; }
    public bool Favorita { get; set; }
    public bool Eliminada { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaModificacion { get; set; }
    public int VecesUsada { get; set; }
    public DateTime? UltimaVez { get; set; }

    /// <summary>Primera línea de la letra (se llena en los listados).</summary>
    public string? PrimeraLinea { get; set; }

    public List<Seccion> Secciones { get; set; } = new();

    /// <summary>Orden de interpretación como códigos de sección (V1, C, V2, C…).</summary>
    public List<string> Orden { get; set; } = new();

    /// <summary>Texto completo de la letra, cada sección una vez.</summary>
    public string LetraCompleta => string.Join("\n", Secciones.OrderBy(s => s.Posicion).Select(s => s.Texto));
}

public static class CategoriasCancion
{
    public static readonly string[] Todas =
    {
        "Alabanza", "Adoración", "Himno", "Infantil", "Santa Cena", "Navidad", "Ofrenda", "Especial"
    };
}
