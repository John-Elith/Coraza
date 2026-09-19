using Coraza.Core.Biblia;

namespace Coraza.Tests;

public class AnalizadorReferenciasTests
{
    [Theory]
    [InlineData("jn 3 16", 43, 3, 16, null, null)]
    [InlineData("Juan 3:16-18", 43, 3, 16, 18, null)]
    [InlineData("1 cor 13", 46, 13, null, null, null)]
    [InlineData("sal 23 1-6", 19, 23, 1, 6, null)]
    [InlineData("Ef 6.14", 49, 6, 14, null, null)]
    [InlineData("efesios 6:14", 49, 6, 14, null, null)]
    [InlineData("San Juan 3:16", 43, 3, 16, null, null)]
    [InlineData("1juan 1:9", 62, 1, 9, null, null)]
    [InlineData("1 Jn 1 9", 62, 1, 9, null, null)]
    [InlineData("Génesis 1", 1, 1, null, null, null)]
    [InlineData("genesis 1:1", 1, 1, 1, null, null)]
    [InlineData("Apocalipsis 22:20-21", 66, 22, 20, 21, null)]
    [InlineData("Judas 3", 65, 1, 3, null, null)]
    [InlineData("primera de corintios 13:4", 46, 13, 4, null, null)]
    [InlineData("2 Reyes 5:14", 12, 5, 14, null, null)]
    [InlineData("Is 53 5", 23, 53, 5, null, null)]
    [InlineData("ro 8 28", 45, 8, 28, null, null)]
    [InlineData("flp 4:13", 50, 4, 13, null, null)]
    [InlineData("stg 1 5", 59, 1, 5, null, null)]
    [InlineData("Hebreos 11:1", 58, 11, 1, null, null)]
    [InlineData("mt 5", 40, 5, null, null, null)]
    [InlineData("Hch 2:1-4", 44, 2, 1, 4, null)]
    [InlineData("Cantares 2:4", 22, 2, 4, null, null)]
    [InlineData("cantar de los cantares 2 4", 22, 2, 4, null, null)]
    [InlineData("Juan 3:16-4:2", 43, 3, 16, 2, 4)]
    [InlineData("  JUAN   3 : 16  ", 43, 3, 16, null, null)]
    [InlineData("Juan 3:16-", 43, 3, 16, null, null)]
    [InlineData("éxodo 20", 2, 20, null, null, null)]
    [InlineData("Filemón 4", 57, 1, 4, null, null)]
    [InlineData("II Samuel 7", 10, 7, null, null, null)]
    public void InterpretaCitas(string texto, int libro, int capitulo, int? inicio, int? fin, int? capituloFin)
    {
        var r = AnalizadorReferencias.Analizar(texto);
        Assert.NotNull(r);
        Assert.Equal(libro, r!.Libro);
        Assert.Equal(capitulo, r.Capitulo);
        Assert.Equal(inicio, r.VersiculoInicio);
        Assert.Equal(fin, r.VersiculoFin);
        Assert.Equal(capituloFin, r.CapituloFin);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hola")]
    [InlineData("cuan grande es el")]
    [InlineData("123")]
    [InlineData("juan 30")]
    [InlineData("juan 3:18-16")]
    [InlineData("negro")]
    public void RechazaTextoQueNoEsCita(string texto)
    {
        Assert.Null(AnalizadorReferencias.Analizar(texto));
    }

    [Fact]
    public void ReconoceSoloLibro()
    {
        Assert.Equal(49, AnalizadorReferencias.AnalizarSoloLibro("efesios")?.Numero);
        Assert.Equal(46, AnalizadorReferencias.AnalizarSoloLibro("1 cor")?.Numero);
        Assert.Null(AnalizadorReferencias.AnalizarSoloLibro("juan 3"));
    }

    [Fact]
    public void FormateaReferencia()
    {
        Assert.Equal("Juan 3:16-18", AnalizadorReferencias.Analizar("jn 3 16-18")!.ToString());
        Assert.Equal("Efesios 6:14", AnalizadorReferencias.Analizar("ef 6.14")!.ToString());
        Assert.Equal("1 Corintios 13", AnalizadorReferencias.Analizar("1cor 13")!.ToString());
    }

    [Fact]
    public void TodosLosLibrosSeEncuentranPorSuNombre()
    {
        foreach (var libro in LibrosBiblia.Todos)
        {
            Assert.Equal(libro.Numero, LibrosBiblia.Buscar(libro.Nombre)?.Numero);
            Assert.Equal(libro.Numero, LibrosBiblia.Buscar(libro.Abreviatura)?.Numero);
        }
    }
}
