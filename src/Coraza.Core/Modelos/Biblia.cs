using Coraza.Core.Biblia;

namespace Coraza.Core.Modelos;

public sealed class VersionBiblia
{
    public long Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Abreviatura { get; set; } = "";
    public string Idioma { get; set; } = "es";
    public string? Licencia { get; set; }

    public override string ToString() => $"{Abreviatura} · {Nombre}";
}

public sealed class Versiculo
{
    public long Id { get; set; }
    public long BibliaId { get; set; }
    public int Libro { get; set; }
    public int Capitulo { get; set; }
    public int Numero { get; set; }
    public string Texto { get; set; } = "";

    public string Referencia => $"{LibrosBiblia.PorNumero(Libro)?.Nombre} {Capitulo}:{Numero}";
}

/// <summary>Referencia a un pasaje: libro, capítulo y rango opcional de versículos (puede cruzar capítulos).</summary>
public sealed record ReferenciaBiblica(int Libro, int Capitulo, int? VersiculoInicio = null, int? VersiculoFin = null, int? CapituloFin = null)
{
    public LibroBiblico InfoLibro => LibrosBiblia.PorNumero(Libro)
        ?? throw new InvalidOperationException($"Libro {Libro} no válido");

    public bool CapituloCompleto => VersiculoInicio is null;

    public bool CruzaCapitulos => CapituloFin is not null && CapituloFin != Capitulo;

    public override string ToString()
    {
        var libro = InfoLibro.Nombre;
        if (VersiculoInicio is null) return $"{libro} {Capitulo}";
        if (CruzaCapitulos) return $"{libro} {Capitulo}:{VersiculoInicio}-{CapituloFin}:{VersiculoFin}";
        if (VersiculoFin is null || VersiculoFin == VersiculoInicio) return $"{libro} {Capitulo}:{VersiculoInicio}";
        return $"{libro} {Capitulo}:{VersiculoInicio}-{VersiculoFin}";
    }
}
