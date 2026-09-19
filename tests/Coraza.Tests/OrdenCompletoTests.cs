using Coraza.Core.Canciones;
using Coraza.Core.Diapositivas;
using Coraza.Core.Modelos;

namespace Coraza.Tests;

/// <summary>Una sección escrita en la letra nunca debe perderse por no estar en el orden.</summary>
public class OrdenCompletoTests
{
    [Fact]
    public void ProyectaElFinalAunqueNoEsteEnElOrden()
    {
        var r = AnalizadorLetra.Analizar("[Verso 1]\na\n[Coro]\nb\n[Final]\nAl gran Yo Soy");
        var cancion = new Cancion { Titulo = "X", Secciones = r.Secciones, Orden = new() { "V1", "C" } };

        var d = GeneradorDiapositivas.DeCancion(cancion, new OpcionesDivision());

        Assert.Contains("Al gran Yo Soy", d.Select(x => x.Texto));
        Assert.Equal("Final", d[^1].Etiqueta);
        Assert.Equal("F", d[^1].CodigoSeccion);
    }

    [Fact]
    public void NoDuplicaLoQueYaEstaEnElOrden()
    {
        var r = AnalizadorLetra.Analizar("[Verso 1]\na\n[Coro]\nb");

        var orden = AnalizadorLetra.CompletarOrden(new[] { "V1", "C", "V1" }, r.Secciones, out var agregadas);

        Assert.Equal(new[] { "V1", "C", "V1" }, orden);
        Assert.Empty(agregadas);
    }

    [Fact]
    public void InformaLasSeccionesAgregadasEnElOrdenEscrito()
    {
        var r = AnalizadorLetra.Analizar("[Verso 1]\na\n[Puente]\nb\n[Final]\nc");

        var orden = AnalizadorLetra.CompletarOrden(new[] { "V1" }, r.Secciones, out var agregadas);

        Assert.Equal(new[] { "P", "F" }, agregadas);
        Assert.Equal(new[] { "V1", "P", "F" }, orden);
    }
}
