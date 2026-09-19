using Coraza.Remoto.Http;

namespace Coraza.Remoto.Difusion;

/// <summary>
/// Un teléfono escuchando el flujo de eventos. Cada uno tiene su propio candado de
/// escritura: dos envíos simultáneos sobre el mismo socket entrelazarían las tramas
/// y el navegador vería JSON partido por la mitad.
/// </summary>
public sealed class Suscriptor
{
    private readonly SemaphoreSlim _escritura = new(1, 1);

    public Suscriptor(Stream flujo) => Flujo = flujo;

    public Stream Flujo { get; }

    /// <summary>Se cancela cuando hay que echar a este suscriptor (expulsión, parada, error).</summary>
    public CancellationTokenSource Cancelacion { get; } = new();

    internal async Task<bool> IntentarAsync(Func<Stream, CancellationToken, Task> envio)
    {
        if (Cancelacion.IsCancellationRequested) return false;
        await _escritura.WaitAsync(Cancelacion.Token).ConfigureAwait(false);
        try
        {
            await envio(Flujo, Cancelacion.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // Un teléfono que se fue, se bloqueó o perdió el wifi no es un error del
            // programa: se descarta en silencio y los demás siguen recibiendo.
            return false;
        }
        finally
        {
            _escritura.Release();
        }
    }

    internal void Cerrar()
    {
        try { Cancelacion.Cancel(); } catch (ObjectDisposedException) { }
        try { Flujo.Dispose(); } catch (Exception) { }
    }
}

/// <summary>
/// Reparte el estado a los teléfonos conectados por Server-Sent Events.
///
/// Se envía el estado completo en cada cambio (menos de 2 KB) en vez de diferencias:
/// no hay que reconciliar nada al reconectar, y un teléfono que estuvo bloqueado se
/// pone al día con la primera trama que recibe.
/// </summary>
public sealed class CanalEstado
{
    private readonly object _candado = new();
    private readonly List<Suscriptor> _suscriptores = new();

    public int Conectados { get { lock (_candado) return _suscriptores.Count; } }

    public Suscriptor Suscribir(Stream flujo)
    {
        var suscriptor = new Suscriptor(flujo);
        lock (_candado) _suscriptores.Add(suscriptor);
        return suscriptor;
    }

    public void Quitar(Suscriptor suscriptor)
    {
        lock (_candado) _suscriptores.Remove(suscriptor);
        suscriptor.Cerrar();
    }

    public Task DifundirAsync(string contenido) =>
        RecorrerAsync((flujo, ct) => RespuestaHttp.EnviarEventoAsync(flujo, contenido, ct));

    public Task LatirAsync() =>
        RecorrerAsync(RespuestaHttp.LatirAsync);

    /// <summary>Echa a todos. Lo usa la expulsión de sesión y la parada del servidor.</summary>
    public void CerrarTodo()
    {
        Suscriptor[] copia;
        lock (_candado)
        {
            copia = _suscriptores.ToArray();
            _suscriptores.Clear();
        }
        foreach (var suscriptor in copia) suscriptor.Cerrar();
    }

    /// <summary>
    /// Se itera sobre una copia y los caídos se retiran después: así un suscriptor que
    /// falla no interrumpe el reparto ni muta la lista mientras se recorre.
    /// </summary>
    private async Task RecorrerAsync(Func<Stream, CancellationToken, Task> envio)
    {
        Suscriptor[] copia;
        lock (_candado) copia = _suscriptores.ToArray();
        if (copia.Length == 0) return;

        List<Suscriptor>? caidos = null;
        foreach (var suscriptor in copia)
        {
            if (await suscriptor.IntentarAsync(envio).ConfigureAwait(false)) continue;
            (caidos ??= new List<Suscriptor>()).Add(suscriptor);
        }

        if (caidos is null) return;
        lock (_candado)
            foreach (var caido in caidos) _suscriptores.Remove(caido);
        foreach (var caido in caidos) caido.Cerrar();
    }
}
