using Coraza.Core.Diapositivas;
using Coraza.Core.Modelos;

namespace Coraza.Tests;

public class FormatoTextoTests
{
    [Fact]
    public void InterpretaNegritaCursivaSubrayadoYResaltado()
    {
        var f = FormatoTexto.Analizar("Hoy **gran** *culto* __de__ ==jóvenes==");
        Assert.Equal(new[] { "Hoy ", "gran", " ", "culto", " ", "de", " ", "jóvenes" }, f.Select(x => x.Texto));
        Assert.True(f[1].Negrita);
        Assert.True(f[3].Cursiva);
        Assert.True(f[5].Subrayado);
        Assert.True(f[7].Resaltado);
        Assert.False(f[0].Negrita);
    }

    [Fact]
    public void InterpretaColoresConNombreYHexadecimal()
    {
        var f = FormatoTexto.Analizar("{coral}Ofrenda{/} y {#4E8F6A}verde{/} normal");
        Assert.Equal("#FC8F8F", f[0].Color);
        Assert.Null(f[1].Color);
        Assert.Equal("#4E8F6A", f[2].Color);
        Assert.Null(f[3].Color);
    }

    [Fact]
    public void DejaTextoSinMarcasTalCual()
    {
        Assert.Equal("Precio {sin cerrar", FormatoTexto.QuitarMarcas("Precio {sin cerrar"));
        Assert.Equal("Bienvenidos\nhoy", FormatoTexto.QuitarMarcas("**Bienvenidos**\n*hoy*"));
    }

    [Fact]
    public void ConvierteListas()
    {
        Assert.Equal("•  Uno\n•  Dos\nNormal", FormatoTexto.Listas("- Uno\n- Dos\nNormal"));
    }

    [Fact]
    public void LosTextosLibresUsanFormato()
    {
        var d = GeneradorDiapositivas.DeTexto(new TextoLibre { Titulo = "Aviso", Contenido = "**Hoy**\n- Café" });
        Assert.True(d[0].ConFormato);
        Assert.Equal("**Hoy**\n•  Café", d[0].Texto);
    }
}
