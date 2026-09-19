using System.Text.Json;
using System.Text.Json.Serialization;
using Coraza.Remoto.Contratos;
using Coraza.Remoto.Difusion;
using Coraza.Remoto.Http;
using Coraza.Remoto.Sesiones;

namespace Coraza.Remoto;

/// <summary>
/// El control remoto completo: rutas, sesión y difusión del estado.
///
/// Es puramente aditivo. Si algo aquí falla —el wifi, el puerto, un teléfono que se
/// va a media respuesta— la proyección desde el teclado sigue funcionando igual. Por
/// eso ningún fallo se propaga hacia fuera: se registra y se sigue.
/// </summary>
public sealed class ServidorRemoto : IAsyncDisposable
{
    private const string RecursoPagina = "Coraza.Remoto.Recursos.control.html";
    private static readonly TimeSpan Latido = TimeSpan.FromSeconds(15);

    // Los enum viajan como texto ("Siguiente", no 3). El JavaScript del teléfono manda
    // nombres, y además así el protocolo se puede leer y depurar a ojo. Sin este
    // convertidor, System.Text.Json solo acepta números y todo comando daría 400.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IControlRemoto _control;
    private readonly Action<string, Exception?>? _registrar;
    private readonly CanalEstado _canal = new();
    private readonly ServidorHttp _http;

    private CancellationTokenSource? _latido;
    private long _version;
    private string? _pagina;

    public ServidorRemoto(IControlRemoto control, GestorSesiones? sesiones = null,
        Action<string, Exception?>? registrar = null)
    {
        _control = control;
        _registrar = registrar;
        Sesiones = sesiones ?? new GestorSesiones();

        var enrutador = new Enrutador((token, ip) => Sesiones.Validar(token, ip))
            .Mapear("GET", "/", ServirPagina, exigeToken: false)
            .Mapear("POST", "/api/sesion", AbrirSesion, exigeToken: false)
            .Mapear("DELETE", "/api/sesion", CerrarSesion)
            .Mapear("GET", "/api/estado", TransmitirEstado)
            .Mapear("POST", "/api/comando", Ejecutar)
            .Mapear("POST", "/api/buscar", Buscar)
            .Mapear("POST", "/api/listar", Listar);

        _http = new ServidorHttp(enrutador.AtenderAsync, registrar);

        // Al expulsar a alguien hay que cortarle el flujo: si no, su teléfono seguiría
        // viendo la letra aunque ya no pueda mandar nada.
        Sesiones.SesionExpulsada += (_, _) => _canal.CerrarTodo();
        _control.EstadoCambio += AlCambiarElEstado;
    }

    public GestorSesiones Sesiones { get; }

    public int Puerto => _http.Puerto;

    public bool Activo => _http.Activo;

    /// <summary>Arranca y devuelve el puerto real, que puede no ser el preferido si estaba ocupado.</summary>
    public int Iniciar(int puertoPreferido = 8787)
    {
        var puerto = _http.Iniciar(puertoPreferido);
        _latido = new CancellationTokenSource();
        _ = Task.Run(() => LatirAsync(_latido.Token));
        _registrar?.Invoke($"Control remoto escuchando en el puerto {puerto}", null);
        return puerto;
    }

    public async Task DetenerAsync()
    {
        if (_latido is not null)
        {
            try { _latido.Cancel(); } catch (ObjectDisposedException) { }
            _latido.Dispose();
            _latido = null;
        }

        _canal.CerrarTodo();
        Sesiones.Revocar();
        await _http.DetenerAsync().ConfigureAwait(false);
        _registrar?.Invoke("Control remoto detenido", null);
    }

    // ---------- rutas ----------

    private async Task ServirPagina(ContextoPeticion c)
    {
        _pagina ??= LeerPaginaIncrustada();
        await RespuestaHttp.HtmlAsync(c.Flujo, _pagina, c.Cancelacion).ConfigureAwait(false);
    }

    private async Task AbrirSesion(ContextoPeticion c)
    {
        string? pin = null;
        try
        {
            using var doc = JsonDocument.Parse(c.Solicitud.Cuerpo);
            if (doc.RootElement.TryGetProperty("pin", out var v)) pin = v.GetString();
        }
        catch (JsonException)
        {
            await RespuestaHttp.ErrorAsync(c.Flujo, 400, c.Cancelacion).ConfigureAwait(false);
            return;
        }

        var agente = c.Solicitud.Cabecera("User-Agent") ?? "";
        var intento = Sesiones.Conceder(pin, c.Ip, agente);

        if (!intento.Ok)
        {
            // 429 cuando está bloqueado por intentos; 401 cuando el PIN no es el bueno.
            var codigo = intento.EsperaSegundos > 0 ? 429 : 401;
            await RespuestaHttp.JsonAsync(c.Flujo, codigo,
                JsonSerializer.Serialize(new { intento.Mensaje, intento.EsperaSegundos }, Json),
                c.Cancelacion).ConfigureAwait(false);
            return;
        }

        _registrar?.Invoke($"Control remoto: sesión concedida a {c.Ip}", null);
        await RespuestaHttp.JsonAsync(c.Flujo, 200,
            JsonSerializer.Serialize(new { intento.Token }, Json), c.Cancelacion).ConfigureAwait(false);
    }

    private async Task CerrarSesion(ContextoPeticion c)
    {
        Sesiones.Revocar();
        await RespuestaHttp.ErrorAsync(c.Flujo, 204, c.Cancelacion).ConfigureAwait(false);
    }

    /// <summary>
    /// Flujo de eventos. Se manda el estado completo al conectar para que el teléfono
    /// se ponga al día de golpe, y luego uno por cada cambio.
    /// </summary>
    private async Task TransmitirEstado(ContextoPeticion c)
    {
        await RespuestaHttp.AbrirEventosAsync(c.Flujo, c.Cancelacion).ConfigureAwait(false);
        var suscriptor = _canal.Suscribir(c.Flujo);
        try
        {
            await EnviarEstadoAsync(suscriptor).ConfigureAwait(false);

            // Se queda viva hasta que el teléfono se va o lo echamos.
            using var unido = CancellationTokenSource.CreateLinkedTokenSource(
                suscriptor.Cancelacion.Token, c.Cancelacion);
            await Task.Delay(Timeout.Infinite, unido.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _canal.Quitar(suscriptor);
        }
    }

    private async Task Ejecutar(ContextoPeticion c)
    {
        ComandoRemoto? comando;
        try
        {
            comando = JsonSerializer.Deserialize<ComandoRemoto>(c.Solicitud.Cuerpo, Json);
        }
        catch (JsonException)
        {
            await RespuestaHttp.ErrorAsync(c.Flujo, 400, c.Cancelacion).ConfigureAwait(false);
            return;
        }

        if (comando is null || !Enum.IsDefined(comando.Accion))
        {
            await RespuestaHttp.ErrorAsync(c.Flujo, 400, c.Cancelacion).ConfigureAwait(false);
            return;
        }

        var resultado = await _control.EjecutarAsync(comando).ConfigureAwait(false);
        await RespuestaHttp.JsonAsync(c.Flujo, 200,
            JsonSerializer.Serialize(resultado, Json), c.Cancelacion).ConfigureAwait(false);
    }

    private async Task Buscar(ContextoPeticion c)
    {
        string consulta;
        try
        {
            using var doc = JsonDocument.Parse(c.Solicitud.Cuerpo);
            consulta = doc.RootElement.TryGetProperty("consulta", out var v) ? v.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            await RespuestaHttp.ErrorAsync(c.Flujo, 400, c.Cancelacion).ConfigureAwait(false);
            return;
        }

        var resultados = await _control.BuscarAsync(consulta).ConfigureAwait(false);
        await RespuestaHttp.JsonAsync(c.Flujo, 200,
            JsonSerializer.Serialize(resultados, Json), c.Cancelacion).ConfigureAwait(false);
    }

    /// <summary>Hojear la biblioteca por categoría, sin escribir nada.</summary>
    private async Task Listar(ContextoPeticion c)
    {
        string categoria;
        try
        {
            using var doc = JsonDocument.Parse(c.Solicitud.Cuerpo);
            categoria = doc.RootElement.TryGetProperty("categoria", out var v) ? v.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            await RespuestaHttp.ErrorAsync(c.Flujo, 400, c.Cancelacion).ConfigureAwait(false);
            return;
        }

        var resultados = await _control.ListarAsync(categoria).ConfigureAwait(false);
        await RespuestaHttp.JsonAsync(c.Flujo, 200,
            JsonSerializer.Serialize(resultados, Json), c.Cancelacion).ConfigureAwait(false);
    }

    // ---------- difusión ----------

    private void AlCambiarElEstado(object? remitente, EventArgs e) =>
        _ = Task.Run(async () =>
        {
            try { await DifundirAsync().ConfigureAwait(false); }
            catch (Exception ex) { _registrar?.Invoke("Fallo difundiendo el estado remoto", ex); }
        });

    private async Task DifundirAsync()
    {
        if (_canal.Conectados == 0) return;
        var estado = await _control.LeerEstadoAsync().ConfigureAwait(false);
        await _canal.DifundirAsync(Serializar(estado)).ConfigureAwait(false);
    }

    private async Task EnviarEstadoAsync(Suscriptor suscriptor)
    {
        var estado = await _control.LeerEstadoAsync().ConfigureAwait(false);
        await RespuestaHttp.EnviarEventoAsync(suscriptor.Flujo, Serializar(estado),
            suscriptor.Cancelacion.Token).ConfigureAwait(false);
    }

    private string Serializar(EstadoRemoto estado) =>
        JsonSerializer.Serialize(estado with { Version = Interlocked.Increment(ref _version) }, Json);

    private async Task LatirAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Latido, ct).ConfigureAwait(false);
                await _canal.LatirAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { _registrar?.Invoke("Fallo en el latido del control remoto", e); }
        }
    }

    private static string LeerPaginaIncrustada()
    {
        using var flujo = typeof(ServidorRemoto).Assembly.GetManifestResourceStream(RecursoPagina);
        if (flujo is null) return "<!doctype html><title>Coraza</title><p>Falta la página del control remoto.";
        using var lector = new StreamReader(flujo);
        return lector.ReadToEnd();
    }

    public async ValueTask DisposeAsync()
    {
        _control.EstadoCambio -= AlCambiarElEstado;
        await DetenerAsync().ConfigureAwait(false);
        await _http.DisposeAsync().ConfigureAwait(false);
    }
}
