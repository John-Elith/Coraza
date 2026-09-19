using Coraza.Core.Canciones;
using Coraza.Core.Diapositivas;
using Coraza.Core.Modelos;

namespace Coraza.Tests;

public class GeneradorDiapositivasTests
{
    [Theory]
    [InlineData(6, 4, new[] { 3, 3 })]
    [InlineData(5, 4, new[] { 3, 2 })]
    [InlineData(4, 4, new[] { 4 })]
    [InlineData(9, 4, new[] { 3, 3, 3 })]
    [InlineData(1, 2, new[] { 1 })]
    public void DivideLineasEnGruposEquilibrados(int lineas, int maximo, int[] esperado)
    {
        var entrada = Enumerable.Range(1, lineas).Select(i => $"l{i}").ToList();
        var grupos = GeneradorDiapositivas.DividirLineas(entrada, maximo);
        Assert.Equal(esperado, grupos.Select(g => g.Split('\n').Length));
    }

    [Fact]
    public void GeneraDiapositivasSegunElOrden()
    {
        var r = AnalizadorLetra.Analizar("[Verso 1]\na\nb\n[Coro]\nc\nd\ne\nf\ng\nh");
        var cancion = new Cancion { Titulo = "Prueba", Secciones = r.Secciones, Orden = new() { "V1", "C", "V1" } };
        var d = GeneradorDiapositivas.DeCancion(cancion, new OpcionesDivision(LineasMaximas: 4));

        Assert.Equal(new[] { "Verso 1", "Coro (1/2)", "Coro (2/2)", "Verso 1" }, d.Select(x => x.Etiqueta));
        Assert.Equal(new[] { "V1", "C", "C", "V1" }, d.Select(x => x.CodigoSeccion));
    }

    [Fact]
    public void RespetaCorteManual()
    {
        var partes = GeneradorDiapositivas.DividirSeccion("a\nb\n---\nc\n\nd", 4);
        Assert.Equal(new[] { "a\nb", "c", "d" }, partes);
    }

    [Fact]
    public void AgregaCreditosEnLaUltimaDiapositiva()
    {
        var r = AnalizadorLetra.Analizar("[Verso 1]\na\n[Coro]\nb");
        var cancion = new Cancion { Titulo = "X", Autor = "Autor", Ccli = "123", Secciones = r.Secciones };
        var d = GeneradorDiapositivas.DeCancion(cancion, new OpcionesDivision(), "999");
        Assert.Null(d[0].Pie);
        Assert.Equal("Autor · CCLI 123 · Licencia CCLI 999", d[^1].Pie);
    }

    [Fact]
    public void DivideTextoLargoSinCortarPalabras()
    {
        var texto = string.Join(" ", Enumerable.Repeat("palabra larga, y otra más;", 30));
        var partes = GeneradorDiapositivas.DividirTextoLargo(texto, 200);
        Assert.True(partes.Count >= 4);
        Assert.All(partes, p => Assert.True(p.Length <= 200));
        Assert.Equal(Normalizar(texto), Normalizar(string.Join(" ", partes)));
        Assert.All(partes, p => Assert.DoesNotContain("  ", p));
    }

    [Fact]
    public void AgrupaVersiculosYEtiquetaElPasaje()
    {
        var referencia = new ReferenciaBiblica(43, 3, 16, 18);
        var versiculos = new List<Versiculo>
        {
            new() { Libro = 43, Capitulo = 3, Numero = 16, Texto = "Porque de tal manera amó Dios al mundo." },
            new() { Libro = 43, Capitulo = 3, Numero = 17, Texto = "Porque no envió Dios su Hijo al mundo." },
            new() { Libro = 43, Capitulo = 3, Numero = 18, Texto = "El que en él cree, no es condenado." },
        };
        var d = GeneradorDiapositivas.DePasaje(referencia, versiculos, "RV1909", new OpcionesDivision(VersiculosPorDiapositiva: 2));
        Assert.Equal(2, d.Count);
        Assert.Equal("3:16-17", d[0].Etiqueta);
        Assert.Equal("Juan 3:16-17 · RV1909", d[0].Pie);
        Assert.Equal(new[] { 16, 17 }, d[0].Versiculos!.Select(v => v.Numero));
        Assert.Equal("Juan 3:18 · RV1909", d[1].Pie);
    }

    [Fact]
    public void TextoLibreSeparaPorParrafos()
    {
        var d = GeneradorDiapositivas.DeTexto(new TextoLibre { Titulo = "Aviso", Contenido = "Bienvenidos\n\nOfrenda\nhoy" });
        Assert.Equal(new[] { "Bienvenidos", "Ofrenda\nhoy" }, d.Select(x => x.Texto));
    }

    [Fact]
    public void CreaCuentaRegresivaPorMinutosOHora()
    {
        var porMinutos = GeneradorDiapositivas.DeCuentaRegresiva(new DatosCuentaRegresiva(5, null, "Comenzamos en", "¡Bienvenidos!"));
        Assert.Equal(TipoDiapositiva.CuentaRegresiva, porMinutos.Tipo);
        Assert.Equal(TimeSpan.FromMinutes(5), porMinutos.DuracionCuenta);
        var ahora = new DateTime(2026, 9, 20, 9, 50, 0);
        Assert.Equal(ahora.AddMinutes(5), porMinutos.CalcularFinCuenta(ahora));
        var fijada = porMinutos.ConFinCuenta(ahora.AddMinutes(3));
        Assert.Equal(ahora.AddMinutes(3), fijada.CalcularFinCuenta(ahora.AddHours(1)));

        var porHora = GeneradorDiapositivas.DeCuentaRegresiva(new DatosCuentaRegresiva(null, "10:00", "Comenzamos en", "¡Bienvenidos!"));
        Assert.Null(porHora.DuracionCuenta);
        Assert.Equal(new DateTime(2026, 9, 20, 10, 0, 0), porHora.CalcularFinCuenta(ahora));
        Assert.Equal("Cuenta regresiva hasta las 10:00", porHora.Etiqueta);
    }

    private static string Normalizar(string s) => string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
