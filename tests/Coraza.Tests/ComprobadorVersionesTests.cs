using Coraza.Core.Actualizacion;

namespace Coraza.Tests;

/// <summary>
/// Pruebas del comprobador de versiones. Equivocarse aquí tiene dos formas, y las dos
/// son malas: no avisar nunca de una versión nueva, o avisar siempre aunque el
/// programa esté al día.
/// </summary>
public sealed class ComprobadorVersionesTests
{
    [Theory]
    [InlineData("v1.0", "1.0.0.0")]
    [InlineData("1.0", "1.0.0.0")]
    [InlineData("V2.3.4", "2.3.4.0")]
    [InlineData("v1.2.3.4", "1.2.3.4")]
    [InlineData("v1.1-beta", "1.1.0.0")]
    public void InterpretaLasEtiquetasHabituales(string etiqueta, string esperada) =>
        Assert.Equal(Version.Parse(esperada), ComprobadorVersiones.Interpretar(etiqueta));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("estable")]
    [InlineData("v")]
    public void IgnoraLasEtiquetasQueNoSonVersiones(string? etiqueta) =>
        Assert.Null(ComprobadorVersiones.Interpretar(etiqueta));

    [Fact]
    public void AvisaSoloSiLaPublicadaEsMayor()
    {
        var instalada = new Version(1, 0, 0);
        Assert.True(ComprobadorVersiones.EsMasNueva("v1.1", instalada));
        Assert.True(ComprobadorVersiones.EsMasNueva("v2.0", instalada));
        Assert.True(ComprobadorVersiones.EsMasNueva("v1.0.1", instalada));
        Assert.False(ComprobadorVersiones.EsMasNueva("v1.0", instalada));
        Assert.False(ComprobadorVersiones.EsMasNueva("v0.9", instalada));
    }

    [Fact]
    public void LaMismaVersionEscritaDistintoNoCuentaComoNueva()
    {
        // Este era el fallo de raíz: el programa decía 0.1.0 y la Release v1.0.
        Assert.False(ComprobadorVersiones.EsMasNueva("v1.0", new Version(1, 0, 0, 0)));
        Assert.False(ComprobadorVersiones.EsMasNueva("1.0.0.0", new Version(1, 0)));
    }

    [Fact]
    public void AnteLaDudaNoAvisa()
    {
        Assert.False(ComprobadorVersiones.EsMasNueva("estable", new Version(1, 0)));
        Assert.False(ComprobadorVersiones.EsMasNueva("v1.1", null));
    }

    private const string Json = """
        {
          "tag_name": "v1.1",
          "name": "Coraza 1.1",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "Coraza-win-x64.zip", "size": 100, "browser_download_url": "https://x/zip" },
            { "name": "CorazaInstalador.exe", "size": 63750295, "browser_download_url": "https://x/instalador" }
          ]
        }
        """;

    [Fact]
    public void EncuentraElInstaladorEnLaRelease()
    {
        var info = ComprobadorVersiones.LeerRelease(Json, "CorazaInstalador.exe");

        Assert.NotNull(info);
        Assert.Equal("v1.1", info!.Etiqueta);
        Assert.Equal("Coraza 1.1", info.Nombre);
        Assert.Equal("https://x/instalador", info.Url);
        Assert.Equal(63750295, info.Tamano);
    }

    [Fact]
    public void SinInstaladorNoHayNadaQueOfrecer() =>
        Assert.Null(ComprobadorVersiones.LeerRelease(Json, "OtroArchivo.exe"));

    [Fact]
    public void NoOfreceBorradoresNiPreestrenos()
    {
        Assert.Null(ComprobadorVersiones.LeerRelease(Json.Replace("\"draft\": false", "\"draft\": true"), "CorazaInstalador.exe"));
        Assert.Null(ComprobadorVersiones.LeerRelease(Json.Replace("\"prerelease\": false", "\"prerelease\": true"), "CorazaInstalador.exe"));
    }

    [Fact]
    public void UnaRespuestaQueNoEsJsonNoRompeNada()
    {
        // El portal cautivo del wifi de un hotel devuelve HTML, no JSON.
        Assert.Null(ComprobadorVersiones.LeerRelease("<html>inicia sesión</html>", "CorazaInstalador.exe"));
        Assert.Null(ComprobadorVersiones.LeerRelease("", "CorazaInstalador.exe"));
    }
}
