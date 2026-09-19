using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coraza.Remoto;
using Coraza.Remoto.Contratos;
using Coraza.Remoto.Sesiones;

namespace Coraza.Tests;

/// <summary>
/// Prueba de integración del control remoto: levanta el servidor de verdad y lo
/// conduce con un cliente HTTP real.
///
/// Va por loopback (127.0.0.1) a propósito: el tráfico local no atraviesa el Firewall
/// de Windows ni necesita reservas de URL, así que estas pruebas corren en cualquier
/// máquina sin permisos especiales. El puerto se pide como 0 para que lo asigne el
/// sistema y dos ejecuciones simultáneas no choquen.
/// </summary>
public sealed class RemotoServidorTests
{
    private sealed class ControlFalso : IControlRemoto
    {
        public List<ComandoRemoto> Recibidos { get; } = new();
        public List<string> Consultas { get; } = new();
        public EstadoRemoto Estado { get; set; } = EstadoRemoto.Vacio with { TextoVivo = "Santo, Santo, Santo" };

        public event EventHandler? EstadoCambio;

        public Task<EstadoRemoto> LeerEstadoAsync() => Task.FromResult(Estado);

        public Task<ResultadoComando> EjecutarAsync(ComandoRemoto comando)
        {
            Recibidos.Add(comando);
            return Task.FromResult(ResultadoComando.Hecho);
        }

        public Task<IReadOnlyList<ResultadoRemoto>> BuscarAsync(string consulta)
        {
            Consultas.Add(consulta);
            IReadOnlyList<ResultadoRemoto> r = new[] { new ResultadoRemoto(0, "Canción", "Cristo me ama", "V1 C V2 C") };
            return Task.FromResult(r);
        }

        public List<string> Categorias { get; } = new();

        public Task<IReadOnlyList<ResultadoRemoto>> ListarAsync(string categoria)
        {
            Categorias.Add(categoria);
            IReadOnlyList<ResultadoRemoto> r = new[] { new ResultadoRemoto(0, "Medio", "Fondo azul", "imagen") };
            return Task.FromResult(r);
        }

        public void Notificar() => EstadoCambio?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Montaje(ServidorRemoto Servidor, HttpClient Cliente, ControlFalso Control) : IAsyncDisposable
    {
        public string Pin => Servidor.Sesiones.Pin;

        public async ValueTask DisposeAsync()
        {
            Cliente.Dispose();
            await Servidor.DisposeAsync();
        }
    }

    private static Montaje Levantar()
    {
        var control = new ControlFalso();
        var servidor = new ServidorRemoto(control, new GestorSesiones());
        var puerto = servidor.Iniciar(0);   // 0 = que lo elija el sistema
        var cliente = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{puerto}"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        return new Montaje(servidor, cliente, control);
    }

    private static StringContent Json(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static async Task<string> Entrar(Montaje m)
    {
        var r = await m.Cliente.PostAsync("/api/sesion", Json(new { pin = m.Pin }));
        r.EnsureSuccessStatusCode();
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>();
        return cuerpo.GetProperty("Token").GetString()!;
    }

    [Fact]
    public async Task SirveLaPaginaSinPedirSesion()
    {
        await using var m = Levantar();

        var r = await m.Cliente.GetAsync("/");
        var html = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Coraza", html);
        Assert.Contains("text/html", r.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task UnaRutaDesconocidaDevuelve404()
    {
        await using var m = Levantar();
        var r = await m.Cliente.GetAsync("/no-existe");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task SinSesionNoSeAceptanComandos()
    {
        await using var m = Levantar();

        var r = await m.Cliente.PostAsync("/api/comando", Json(new { Accion = "Siguiente" }));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Empty(m.Control.Recibidos);
    }

    [Fact]
    public async Task UnPinEquivocadoNoConcedeSesion()
    {
        await using var m = Levantar();
        var malo = m.Pin == "000000" ? "111111" : "000000";

        var r = await m.Cliente.PostAsync("/api/sesion", Json(new { pin = malo }));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task ConElPinCorrectoSePuedeMandarUnComando()
    {
        await using var m = Levantar();
        var token = await Entrar(m);

        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/comando")
        {
            Content = Json(new { Accion = "Siguiente" }),
        };
        peticion.Headers.Add("X-Coraza-Token", token);
        var r = await m.Cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Single(m.Control.Recibidos);
        Assert.Equal(AccionRemota.Siguiente, m.Control.Recibidos[0].Accion);
    }

    [Fact]
    public async Task LaBusquedaLlegaAlEscritorioYDevuelveResultados()
    {
        await using var m = Levantar();
        var token = await Entrar(m);

        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/buscar")
        {
            Content = Json(new { consulta = "cristo" }),
        };
        peticion.Headers.Add("X-Coraza-Token", token);
        var r = await m.Cliente.SendAsync(peticion);
        var resultados = await r.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("cristo", Assert.Single(m.Control.Consultas));
        Assert.Equal("Cristo me ama", resultados[0].GetProperty("Titulo").GetString());
    }

    [Fact]
    public async Task UnComandoConAccionInventadaNoRompeElServidor()
    {
        await using var m = Levantar();
        var token = await Entrar(m);

        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/comando")
        {
            Content = Json(new { Accion = "VolarPorLosAires" }),
        };
        peticion.Headers.Add("X-Coraza-Token", token);
        var r = await m.Cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty(m.Control.Recibidos);
    }

    [Fact]
    public async Task UnJsonMalFormadoDevuelve400YNo500()
    {
        await using var m = Levantar();
        var token = await Entrar(m);

        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/comando")
        {
            Content = new StringContent("{esto no es json", Encoding.UTF8, "application/json"),
        };
        peticion.Headers.Add("X-Coraza-Token", token);
        var r = await m.Cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task UnaSesionNuevaDejaSinMandoALaAnterior()
    {
        await using var m = Levantar();
        var primero = await Entrar(m);

        // El PIN se regenera al conceder, así que el segundo teléfono usa el nuevo.
        await Task.Delay(1);
        var segundo = await Entrar(m);

        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/comando")
        {
            Content = Json(new { Accion = "Siguiente" }),
        };
        peticion.Headers.Add("X-Coraza-Token", primero);
        var r = await m.Cliente.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.NotEqual(primero, segundo);
    }

    [Fact]
    public async Task ElFlujoDeEventosMandaElEstadoAlConectar()
    {
        await using var m = Levantar();
        var token = await Entrar(m);

        using var flujo = await m.Cliente.GetStreamAsync($"/api/estado?t={Uri.EscapeDataString(token)}");
        using var lector = new StreamReader(flujo);

        // La primera trama llega sola: el teléfono se pone al día nada más conectar.
        var linea = await lector.ReadLineAsync();
        while (linea is not null && !linea.StartsWith("data:", StringComparison.Ordinal))
            linea = await lector.ReadLineAsync();

        Assert.NotNull(linea);
        Assert.Contains("Santo, Santo, Santo", linea);
    }
}
