using Coraza.Core.Busqueda;

namespace Coraza.Tests;

public class BusquedaTests
{
    private static IndiceCanciones CrearIndice()
    {
        var indice = new IndiceCanciones();
        indice.Actualizar(1, "Cuán Grande Es Él", "Señor mi Dios, al contemplar los cielos\nel firmamento y las estrellas mil");
        indice.Actualizar(2, "Sublime Gracia", "Sublime gracia del Señor\nque a un infeliz salvó");
        indice.Actualizar(3, "Aleluya", "Aleluya, aleluya\nal Rey de reyes");
        indice.Actualizar(4, "Grande y fuerte", "Grande y fuerte es nuestro Dios");
        return indice;
    }

    [Fact]
    public void IgnoraTildesYMayusculas()
    {
        var r = CrearIndice().Buscar("cuan grande");
        Assert.Equal(1, r[0]);
    }

    [Fact]
    public void BuscaPorPalabrasDeLaLetra()
    {
        Assert.Equal(new long[] { 1 }, CrearIndice().Buscar("firmamento estrellas"));
    }

    [Fact]
    public void ToleraErroresDeEscritura()
    {
        Assert.Contains(2L, CrearIndice().Buscar("sublme"));
        Assert.Contains(3L, CrearIndice().Buscar("aleluia"));
    }

    [Fact]
    public void ElTituloPesaMasQueLaLetra()
    {
        var r = CrearIndice().Buscar("grande");
        Assert.Equal(4, r[0]);
        Assert.Contains(1L, r);
    }

    [Fact]
    public void NormalizaTexto()
    {
        Assert.Equal("cuan grande es el", Normalizador.Normalizar("¡Cuán  Grande, es Él!"));
        Assert.Equal("senor", Normalizador.Normalizar("Señor"));
    }

    [Fact]
    public void LevenshteinConCorte()
    {
        Assert.Equal(1, Normalizador.Levenshtein("gracia", "gracias", 2));
        Assert.Equal(3, Normalizador.Levenshtein("abc", "xyzabc", 2));
    }
}
