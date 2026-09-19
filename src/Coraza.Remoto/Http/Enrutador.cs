namespace Coraza.Remoto.Http;

public delegate Task ManejadorRuta(ContextoPeticion contexto);

/// <summary>
/// Tabla de rutas con el control de sesión incorporado.
///
/// Solo existen las rutas que Coraza necesita; cualquier otra cosa recibe un 404 con
/// el cuerpo vacío. No se sirven archivos del disco, así que no hay forma de pedir
/// algo que no esté explícitamente aquí.
///
/// El token se acepta por la cabecera <c>X-Coraza-Token</c> y, solo para el flujo de
/// eventos, por la cadena de consulta: <c>EventSource</c> del navegador no permite
/// enviar cabeceras. Por eso hay una regla que se respeta en todo el proyecto:
/// <b>nunca registrar la cadena de consulta, solo la ruta.</b>
/// </summary>
public sealed class Enrutador
{
    public const string CabeceraToken = "X-Coraza-Token";
    public const string ParametroToken = "t";

    private readonly Dictionary<(string Metodo, string Ruta), (ManejadorRuta Manejador, bool ExigeToken)> _rutas = new();

    private readonly Func<string?, string, bool> _validarToken;

    /// <param name="validarToken">Recibe el token y la IP; decide si esa sesión manda.</param>
    public Enrutador(Func<string?, string, bool> validarToken) => _validarToken = validarToken;

    public Enrutador Mapear(string metodo, string ruta, ManejadorRuta manejador, bool exigeToken = true)
    {
        _rutas[(metodo, ruta)] = (manejador, exigeToken);
        return this;
    }

    public async Task AtenderAsync(ContextoPeticion contexto)
    {
        var clave = (contexto.Solicitud.Metodo, contexto.Solicitud.Ruta);

        if (!_rutas.TryGetValue(clave, out var entrada))
        {
            await RespuestaHttp.ErrorAsync(contexto.Flujo, 404, contexto.Cancelacion).ConfigureAwait(false);
            return;
        }

        if (entrada.ExigeToken && !_validarToken(LeerToken(contexto.Solicitud), contexto.Ip))
        {
            await RespuestaHttp.ErrorAsync(contexto.Flujo, 401, contexto.Cancelacion).ConfigureAwait(false);
            return;
        }

        await entrada.Manejador(contexto).ConfigureAwait(false);
    }

    internal static string? LeerToken(SolicitudHttp solicitud) =>
        solicitud.Cabecera(CabeceraToken) ?? solicitud.Parametro(ParametroToken);
}
