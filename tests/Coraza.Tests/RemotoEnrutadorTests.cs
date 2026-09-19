using System.Text;
using Coraza.Remoto.Http;

namespace Coraza.Tests;

/// <summary>
/// Pruebas del enrutador: qué rutas existen y, sobre todo, que ninguna que exija
/// sesión se pueda tocar sin un token válido.
/// </summary>
public sealed class RemotoEnrutadorTests
{
    /// <summary>Analiza una petición cruda y devuelve el contexto con un flujo de respuesta aparte.</summary>
    private static async Task<(ContextoPeticion Contexto, MemoryStream Respuesta)> Preparar(
        string crudo, string ip = "192.168.1.40")
    {
        var analisis = await SolicitudHttp.LeerAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(crudo)));
        Assert.True(analisis.Ok);

        var respuesta = new MemoryStream();
        return (new ContextoPeticion(analisis.Solicitud!, respuesta, ip, CancellationToken.None), respuesta);
    }

    private static int CodigoDe(MemoryStream respuesta)
    {
        var texto = Encoding.UTF8.GetString(respuesta.ToArray());
        var partes = texto.Split(' ');
        return int.Parse(partes[1]);
    }

    [Fact]
    public async Task UnaRutaDesconocidaDevuelve404()
    {
        var enrutador = new Enrutador((_, _) => true);
        var (contexto, respuesta) = await Preparar("GET /no-existe HTTP/1.1\r\n\r\n");

        await enrutador.AtenderAsync(contexto);

        Assert.Equal(404, CodigoDe(respuesta));
    }

    [Fact]
    public async Task ElMetodoFormaParteDeLaRuta()
    {
        var enrutador = new Enrutador((_, _) => true);
        enrutador.Mapear("GET", "/api/estado", _ => Task.CompletedTask, exigeToken: false);

        var (contexto, respuesta) = await Preparar("POST /api/estado HTTP/1.1\r\n\r\n");
        await enrutador.AtenderAsync(contexto);

        Assert.Equal(404, CodigoDe(respuesta));
    }

    [Fact]
    public async Task SinTokenNoSeEntraAUnaRutaProtegida()
    {
        var llamado = false;
        var enrutador = new Enrutador((token, _) => token == "bueno");
        enrutador.Mapear("POST", "/api/comando", _ => { llamado = true; return Task.CompletedTask; });

        var (contexto, respuesta) = await Preparar("POST /api/comando HTTP/1.1\r\nContent-Length: 0\r\n\r\n");
        await enrutador.AtenderAsync(contexto);

        Assert.Equal(401, CodigoDe(respuesta));
        Assert.False(llamado);
    }

    [Fact]
    public async Task UnTokenEquivocadoTampocoEntra()
    {
        var enrutador = new Enrutador((token, _) => token == "bueno");
        enrutador.Mapear("POST", "/api/comando", _ => Task.CompletedTask);

        var (contexto, respuesta) = await Preparar(
            "POST /api/comando HTTP/1.1\r\nX-Coraza-Token: malo\r\nContent-Length: 0\r\n\r\n");
        await enrutador.AtenderAsync(contexto);

        Assert.Equal(401, CodigoDe(respuesta));
    }

    [Fact]
    public async Task ConElTokenEnLaCabeceraSeEjecutaElManejador()
    {
        var llamado = false;
        var enrutador = new Enrutador((token, _) => token == "bueno");
        enrutador.Mapear("POST", "/api/comando", _ => { llamado = true; return Task.CompletedTask; });

        var (contexto, _) = await Preparar(
            "POST /api/comando HTTP/1.1\r\nX-Coraza-Token: bueno\r\nContent-Length: 0\r\n\r\n");
        await enrutador.AtenderAsync(contexto);

        Assert.True(llamado);
    }

    [Fact]
    public async Task ElFlujoDeEventosPuedeLlevarElTokenEnLaConsulta()
    {
        // EventSource no permite cabeceras, así que esta vía tiene que funcionar.
        var llamado = false;
        var enrutador = new Enrutador((token, _) => token == "bueno");
        enrutador.Mapear("GET", "/api/estado", _ => { llamado = true; return Task.CompletedTask; });

        var (contexto, _) = await Preparar("GET /api/estado?t=bueno HTTP/1.1\r\n\r\n");
        await enrutador.AtenderAsync(contexto);

        Assert.True(llamado);
    }

    [Fact]
    public async Task LaPaginaYElIngresoNoExigenSesion()
    {
        var abiertas = 0;
        var enrutador = new Enrutador((_, _) => false);   // nadie tiene sesión
        enrutador.Mapear("GET", "/", _ => { abiertas++; return Task.CompletedTask; }, exigeToken: false);
        enrutador.Mapear("POST", "/api/sesion", _ => { abiertas++; return Task.CompletedTask; }, exigeToken: false);

        var (raiz, _) = await Preparar("GET / HTTP/1.1\r\n\r\n");
        await enrutador.AtenderAsync(raiz);
        var (sesion, _) = await Preparar("POST /api/sesion HTTP/1.1\r\nContent-Length: 0\r\n\r\n");
        await enrutador.AtenderAsync(sesion);

        Assert.Equal(2, abiertas);
    }

    [Fact]
    public async Task LaCabeceraGanaALaConsulta()
    {
        string? visto = null;
        var enrutador = new Enrutador((token, _) => { visto = token; return true; });
        enrutador.Mapear("GET", "/api/estado", _ => Task.CompletedTask);

        var (contexto, _) = await Preparar(
            "GET /api/estado?t=deConsulta HTTP/1.1\r\nX-Coraza-Token: deCabecera\r\n\r\n");
        await enrutador.AtenderAsync(contexto);

        Assert.Equal("deCabecera", visto);
    }

    [Fact]
    public async Task LaIpLlegaAlValidador()
    {
        string? vista = null;
        var enrutador = new Enrutador((_, ip) => { vista = ip; return true; });
        enrutador.Mapear("GET", "/api/estado", _ => Task.CompletedTask);

        var (contexto, _) = await Preparar("GET /api/estado?t=x HTTP/1.1\r\n\r\n", ip: "192.168.1.77");
        await enrutador.AtenderAsync(contexto);

        Assert.Equal("192.168.1.77", vista);
    }
}
