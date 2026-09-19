using System.Security.Cryptography;
using System.Text;

namespace Coraza.Remoto.Sesiones;

/// <summary>Quién está controlando la proyección ahora mismo.</summary>
public sealed record SesionActiva(string Ip, string Agente, DateTime Desde, DateTime UltimaActividad);

/// <summary>Lo que devuelve un intento de entrar con el PIN.</summary>
public sealed record IntentoSesion(bool Ok, string? Token, int EsperaSegundos, string? Mensaje)
{
    public static IntentoSesion Concedida(string token) => new(true, token, 0, null);

    public static IntentoSesion Rechazada(string mensaje) => new(false, null, 0, mensaje);

    public static IntentoSesion Bloqueada(int segundos) =>
        new(false, null, segundos, $"Demasiados intentos. Espera {segundos} segundos.");
}

/// <summary>
/// Autorización del control remoto: un PIN efímero y <b>una sola sesión a la vez</b>.
///
/// Decisiones deliberadas, todas por la misma razón (que el control lo tenga quien
/// está delante del computador, y que una intrusión se note):
///
/// - El PIN y el token <b>viven solo en memoria</b>. Nunca en preferencias, ni en la
///   base de datos, ni en el registro. Reiniciar Coraza revoca todo, y eso es una
///   característica: un teléfono autorizado hace tres domingos no sigue mandando.
/// - Conceder una sesión <b>expulsa a la anterior</b>. Por eso una intrusión es
///   ruidosa: al operador se le cae el teléfono de las manos, no pasa inadvertida.
/// - El PIN se regenera al arrancar, al conceder y al revocar.
/// - <b>No se ata a la IP.</b> Los teléfonos saltan de punto de acceso y renuevan DHCP;
///   exigirla produciría cierres de sesión misteriosos a media predicación y compraría
///   poco, porque quien está en esa red también puede tomar la IP ajena. Se registra
///   y se muestra para que el operador vea quién está, y un cambio se avisa.
///
/// El reloj se inyecta para que las pruebas de caducidad no necesiten esperas reales.
/// </summary>
public sealed class GestorSesiones
{
    private const int FallosParaBloquear = 5;
    private static readonly TimeSpan Bloqueo = TimeSpan.FromSeconds(60);

    /// <summary>Tras una expulsión no se concede nada durante unos segundos, para que
    /// dos teléfonos peleándose por el control se auto-limiten en vez de alternarse.</summary>
    private static readonly TimeSpan TreguaTrasExpulsion = TimeSpan.FromSeconds(10);

    private readonly Func<DateTime> _ahora;
    private readonly object _candado = new();

    private string _pin = "";
    private string? _token;
    private SesionActiva? _sesion;
    private int _fallos;
    private DateTime _bloqueadoHasta = DateTime.MinValue;
    private DateTime _treguaHasta = DateTime.MinValue;

    public GestorSesiones(Func<DateTime>? ahora = null)
    {
        _ahora = ahora ?? (() => DateTime.UtcNow);
        RegenerarPin();
    }

    /// <summary>Cuánto puede estar quieta una sesión antes de caducar. Cubre montaje y culto completo.</summary>
    public TimeSpan Inactividad { get; init; } = TimeSpan.FromHours(4);

    /// <summary>Se dispara cuando hay que cerrar el flujo de eventos de la sesión anterior.</summary>
    public event EventHandler? SesionExpulsada;

    /// <summary>Se dispara cuando cambia quién está conectado, para refrescar el escritorio.</summary>
    public event EventHandler? SesionCambio;

    public string Pin { get { lock (_candado) return _pin; } }

    public SesionActiva? Actual
    {
        get { lock (_candado) { CaducarSiProcede(); return _sesion; } }
    }

    public bool HaySesion => Actual is not null;

    /// <summary>Canjea el PIN por un token de sesión.</summary>
    public IntentoSesion Conceder(string? pin, string ip, string agente)
    {
        bool expulsar;
        IntentoSesion resultado;

        lock (_candado)
        {
            var ahora = _ahora();
            CaducarSiProcede();

            if (ahora < _bloqueadoHasta)
                return IntentoSesion.Bloqueada((int)Math.Ceiling((_bloqueadoHasta - ahora).TotalSeconds));

            if (!PinCorrecto(pin))
            {
                _fallos++;
                if (_fallos >= FallosParaBloquear)
                {
                    _fallos = 0;
                    _bloqueadoHasta = ahora + Bloqueo;
                    return IntentoSesion.Bloqueada((int)Bloqueo.TotalSeconds);
                }
                return IntentoSesion.Rechazada("PIN incorrecto.");
            }

            if (ahora < _treguaHasta)
                return IntentoSesion.Rechazada("Otro dispositivo acaba de tomar el control. Intenta en unos segundos.");

            expulsar = _sesion is not null;
            if (expulsar) _treguaHasta = ahora + TreguaTrasExpulsion;

            _fallos = 0;
            _token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            _sesion = new SesionActiva(ip, Recortar(agente), ahora, ahora);
            RegenerarPin();
            resultado = IntentoSesion.Concedida(_token);
        }

        if (expulsar) SesionExpulsada?.Invoke(this, EventArgs.Empty);
        SesionCambio?.Invoke(this, EventArgs.Empty);
        return resultado;
    }

    /// <summary>
    /// ¿Este token manda? Cuenta como actividad, así que un teléfono en uso nunca caduca.
    /// </summary>
    public bool Validar(string? token, string? ip = null)
    {
        string? avisoIp = null;
        bool ok;

        lock (_candado)
        {
            CaducarSiProcede();
            if (_token is null || _sesion is null || string.IsNullOrEmpty(token)) return false;

            ok = IgualEnTiempoConstante(token, _token);
            if (ok)
            {
                if (ip is not null && ip != _sesion.Ip) avisoIp = ip;
                _sesion = _sesion with { UltimaActividad = _ahora(), Ip = ip ?? _sesion.Ip };
            }
        }

        if (avisoIp is not null) SesionCambio?.Invoke(this, EventArgs.Empty);
        return ok;
    }

    /// <summary>Cierra la sesión y cambia el PIN. Lo usa el botón «Desconectar» del escritorio.</summary>
    public void Revocar()
    {
        bool habia;
        lock (_candado)
        {
            habia = _sesion is not null;
            _sesion = null;
            _token = null;
            RegenerarPin();
        }

        if (habia)
        {
            SesionExpulsada?.Invoke(this, EventArgs.Empty);
            SesionCambio?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CaducarSiProcede()
    {
        if (_sesion is null) return;
        if (_ahora() - _sesion.UltimaActividad < Inactividad) return;
        _sesion = null;
        _token = null;
        RegenerarPin();
    }

    private void RegenerarPin() => _pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>
    /// Comparación de duración constante. Si la longitud no coincide se compara igual
    /// contra el PIN real antes de fallar, para no revelar por el tiempo de respuesta
    /// cuántos dígitos se acertaron.
    /// </summary>
    private bool PinCorrecto(string? pin)
    {
        var enviado = Encoding.UTF8.GetBytes(pin ?? "");
        var real = Encoding.UTF8.GetBytes(_pin);
        if (enviado.Length != real.Length)
        {
            CryptographicOperations.FixedTimeEquals(real, real);
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(enviado, real);
    }

    private static bool IgualEnTiempoConstante(string a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a);
        var y = Encoding.UTF8.GetBytes(b);
        if (x.Length != y.Length)
        {
            CryptographicOperations.FixedTimeEquals(y, y);
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(x, y);
    }

    private static string Recortar(string agente) =>
        string.IsNullOrWhiteSpace(agente) ? "Teléfono"
        : agente.Length <= 80 ? agente
        : agente[..80];
}
