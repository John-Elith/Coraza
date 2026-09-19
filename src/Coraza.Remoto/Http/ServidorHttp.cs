using System.Net;
using System.Net.Sockets;

namespace Coraza.Remoto.Http;

/// <summary>Lo que se le entrega a un manejador de ruta.</summary>
public sealed record ContextoPeticion(
    SolicitudHttp Solicitud,
    Stream Flujo,
    string Ip,
    CancellationToken Cancelacion);

/// <summary>
/// El bucle de aceptación: escucha, analiza y entrega. Nada más.
///
/// Se usa <see cref="TcpListener"/> y no <c>HttpListener</c> por una razón dura, no
/// por gusto: <c>HttpListener</c> se apoya en HTTP.sys, que exige una reserva de URL
/// con permisos de administrador para cualquier prefijo que no sea localhost. Coraza
/// corre como usuario normal (<c>asInvoker</c>) y su instalador tampoco eleva, así que
/// no hay ningún momento de su vida en que pudiera crear esa reserva. El teléfono no
/// es localhost, de modo que esa vía está cerrada del todo.
///
/// Este servidor no toca nunca un objeto de la interfaz: corre en hilos del grupo, y
/// el único puente hacia WPF es el adaptador, que salta al Dispatcher.
/// </summary>
public sealed class ServidorHttp : IAsyncDisposable
{
    /// <summary>Más allá de esto no hay nadie legítimo: solo un cliente atascado o malicioso.</summary>
    private const int ConexionesSimultaneas = 12;

    private static readonly TimeSpan EsperaCabeceras = TimeSpan.FromSeconds(10);

    private readonly Func<ContextoPeticion, Task> _manejador;
    private readonly Action<string, Exception?>? _registrar;
    private readonly SemaphoreSlim _plazas = new(ConexionesSimultaneas, ConexionesSimultaneas);

    private TcpListener? _escucha;
    private CancellationTokenSource? _cancelacion;
    private Task? _bucle;

    public ServidorHttp(Func<ContextoPeticion, Task> manejador, Action<string, Exception?>? registrar = null)
    {
        _manejador = manejador;
        _registrar = registrar;
    }

    /// <summary>Puerto en el que quedó escuchando. Cero si no está arrancado.</summary>
    public int Puerto { get; private set; }

    public bool Activo => _escucha is not null;

    /// <summary>
    /// Arranca en el primer puerto libre a partir del preferido. Si el puerto está
    /// ocupado por otro programa no se falla: se prueba el siguiente y se informa
    /// cuál quedó, porque es el que hay que poner en el código QR.
    /// </summary>
    public int Iniciar(int puertoPreferido, int intentos = 5)
    {
        if (_escucha is not null) return Puerto;

        for (var i = 0; i < intentos; i++)
        {
            var puerto = puertoPreferido + i;
            var escucha = new TcpListener(IPAddress.Any, puerto);
            try
            {
                escucha.Start();
                _escucha = escucha;
                // El puerto REAL, no el pedido: si se pasa 0 lo asigna el sistema, que
                // es lo que usan las pruebas para no chocar entre ejecuciones. Además
                // es el que hay que poner en el código QR.
                Puerto = ((IPEndPoint)escucha.LocalEndpoint).Port;
                _cancelacion = new CancellationTokenSource();
                _bucle = Task.Run(() => AceptarAsync(_cancelacion.Token));
                return Puerto;
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.AddressAlreadyInUse
                                                or SocketError.AccessDenied)
            {
                escucha.Stop();
            }
        }

        throw new InvalidOperationException(
            $"No hay ningún puerto libre entre {puertoPreferido} y {puertoPreferido + intentos - 1}.");
    }

    public async Task DetenerAsync()
    {
        var cancelacion = _cancelacion;
        var escucha = _escucha;
        var bucle = _bucle;

        _cancelacion = null;
        _escucha = null;
        _bucle = null;
        Puerto = 0;

        if (cancelacion is not null)
        {
            try { cancelacion.Cancel(); } catch (ObjectDisposedException) { }
        }
        try { escucha?.Stop(); } catch (Exception) { }

        if (bucle is not null)
        {
            try { await bucle.ConfigureAwait(false); } catch (Exception) { }
        }

        cancelacion?.Dispose();
    }

    private async Task AceptarAsync(CancellationToken ct)
    {
        var escucha = _escucha;
        if (escucha is null) return;

        while (!ct.IsCancellationRequested)
        {
            TcpClient cliente;
            try
            {
                cliente = await escucha.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }

            // Sin esperar: una conexión lenta no puede retrasar a la siguiente.
            _ = Task.Run(() => AtenderAsync(cliente, ct), CancellationToken.None);
        }
    }

    private async Task AtenderAsync(TcpClient cliente, CancellationToken ct)
    {
        // Si no hay plaza libre se responde 503 y se cierra, en vez de dejar al
        // teléfono esperando para siempre sin saber qué pasó.
        if (!await _plazas.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            try
            {
                await using var saturado = cliente.GetStream();
                await RespuestaHttp.ErrorAsync(saturado, 503, ct).ConfigureAwait(false);
            }
            catch (Exception) { }
            finally { cliente.Dispose(); }
            return;
        }

        try
        {
            cliente.NoDelay = true;   // los botones ◀ ▶ deben sentirse instantáneos
            var ip = (cliente.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";

            await using var flujo = cliente.GetStream();

            // Espera solo para leer la petición: un cliente que abre y no envía nada
            // no puede quedarse con una plaza. El flujo de eventos, que vive abierto
            // a propósito, queda fuera de este límite porque se atiende después.
            using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
            espera.CancelAfter(EsperaCabeceras);

            var analisis = await SolicitudHttp.LeerAsync(flujo, espera.Token).ConfigureAwait(false);

            if (!analisis.Ok)
            {
                if (analisis.CodigoError != 0)
                    await RespuestaHttp.ErrorAsync(flujo, analisis.CodigoError, ct).ConfigureAwait(false);
                return;   // código 0 = el cliente cerró sin pedir nada
            }

            await _manejador(new ContextoPeticion(analisis.Solicitud!, flujo, ip, ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }          // el teléfono se fue a media respuesta
        catch (SocketException) { }
        catch (Exception e)
        {
            // Nunca se propaga: un fallo atendiendo a un teléfono no puede afectar
            // a la proyección ni mostrar una pantalla de error durante el culto.
            _registrar?.Invoke("Fallo atendiendo una petición del control remoto", e);
        }
        finally
        {
            _plazas.Release();
            cliente.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DetenerAsync().ConfigureAwait(false);
        _plazas.Dispose();
    }
}
