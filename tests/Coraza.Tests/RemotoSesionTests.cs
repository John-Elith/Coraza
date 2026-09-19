using Coraza.Remoto.Sesiones;

namespace Coraza.Tests;

/// <summary>
/// Pruebas de la autorización del control remoto: el PIN, el token y la regla de que
/// solo puede haber una persona controlando la proyección.
///
/// El reloj se inyecta en todas: si no, comprobar la caducidad de cuatro horas o el
/// bloqueo de sesenta segundos exigiría esperas reales y las pruebas serían inútiles.
/// </summary>
public sealed class RemotoSesionTests
{
    private DateTime _reloj = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

    private GestorSesiones Crear(TimeSpan? inactividad = null) =>
        new(() => _reloj) { Inactividad = inactividad ?? TimeSpan.FromHours(4) };

    [Fact]
    public void ElPinTieneSeisDigitos()
    {
        var g = Crear();
        Assert.Equal(6, g.Pin.Length);
        Assert.True(g.Pin.All(char.IsDigit));
    }

    [Fact]
    public void ConcedeUnTokenConElPinCorrecto()
    {
        var g = Crear();
        var intento = g.Conceder(g.Pin, "192.168.1.40", "iPhone");

        Assert.True(intento.Ok);
        Assert.NotNull(intento.Token);
        // 32 bytes en Base64 sin relleno: material suficiente y seguro para una URL.
        Assert.Equal(43, intento.Token!.Length);
        Assert.DoesNotContain('+', intento.Token);
        Assert.DoesNotContain('/', intento.Token);
        Assert.DoesNotContain('=', intento.Token);
        Assert.True(g.Validar(intento.Token));
    }

    [Fact]
    public void RechazaUnPinEquivocado()
    {
        var g = Crear();
        var malo = g.Pin == "000000" ? "111111" : "000000";

        var intento = g.Conceder(malo, "192.168.1.40", "iPhone");

        Assert.False(intento.Ok);
        Assert.Null(intento.Token);
        Assert.False(g.HaySesion);
    }

    [Fact]
    public void RechazaUnPinDeLongitudDistintaSinRomperse()
    {
        var g = Crear();
        Assert.False(g.Conceder("123", "ip", "a").Ok);
        Assert.False(g.Conceder("", "ip", "a").Ok);
        Assert.False(g.Conceder(null, "ip", "a").Ok);
    }

    [Fact]
    public void CincoFallosBloqueanYElSextoIntentoNoPasaAunqueAcierte()
    {
        var g = Crear();
        var malo = g.Pin == "000000" ? "111111" : "000000";

        for (var i = 0; i < 4; i++) Assert.False(g.Conceder(malo, "ip", "a").Ok);

        var quinto = g.Conceder(malo, "ip", "a");
        Assert.False(quinto.Ok);
        Assert.Equal(60, quinto.EsperaSegundos);

        // Ahora ni con el PIN bueno: ese es justo el punto del bloqueo.
        Assert.False(g.Conceder(g.Pin, "ip", "a").Ok);
    }

    [Fact]
    public void ElBloqueoExpiraSolo()
    {
        var g = Crear();
        var malo = g.Pin == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++) g.Conceder(malo, "ip", "a");

        _reloj = _reloj.AddSeconds(61);

        Assert.True(g.Conceder(g.Pin, "ip", "a").Ok);
    }

    [Fact]
    public void UnaSesionNuevaExpulsaALaAnterior()
    {
        var g = Crear();
        var expulsiones = 0;
        g.SesionExpulsada += (_, _) => expulsiones++;

        var primera = g.Conceder(g.Pin, "192.168.1.40", "iPhone");
        Assert.True(primera.Ok);

        // La tregua evita que dos teléfonos se alternen a golpes; se deja pasar.
        _reloj = _reloj.AddSeconds(11);
        var segunda = g.Conceder(g.Pin, "192.168.1.55", "Android");

        Assert.True(segunda.Ok);
        Assert.Equal(1, expulsiones);
        Assert.False(g.Validar(primera.Token));   // el primero pierde el mando
        Assert.True(g.Validar(segunda.Token));
    }

    [Fact]
    public void TrasUnaExpulsionHayUnaTreguaBreve()
    {
        var g = Crear();
        g.Conceder(g.Pin, "ip1", "a");
        _reloj = _reloj.AddSeconds(11);
        g.Conceder(g.Pin, "ip2", "b");           // esta expulsa y abre la tregua

        var tercera = g.Conceder(g.Pin, "ip3", "c");

        Assert.False(tercera.Ok);
        Assert.Contains("control", tercera.Mensaje!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RevocarCierraLaSesionYCambiaElPin()
    {
        var g = Crear();
        var pinAntes = g.Pin;
        var intento = g.Conceder(pinAntes, "ip", "a");
        var pinTrasConceder = g.Pin;

        g.Revocar();

        Assert.False(g.Validar(intento.Token));
        Assert.Null(g.Actual);
        Assert.NotEqual(pinAntes, pinTrasConceder);   // se regenera al conceder
        Assert.NotEqual(pinTrasConceder, g.Pin);      // y también al revocar
    }

    [Fact]
    public void LaSesionCaducaPorInactividad()
    {
        var g = Crear(TimeSpan.FromMinutes(30));
        var intento = g.Conceder(g.Pin, "ip", "a");

        _reloj = _reloj.AddMinutes(31);

        Assert.False(g.Validar(intento.Token));
        Assert.Null(g.Actual);
    }

    [Fact]
    public void UsarElMandoCuentaComoActividadYEvitaLaCaducidad()
    {
        var g = Crear(TimeSpan.FromMinutes(30));
        var intento = g.Conceder(g.Pin, "ip", "a");

        // Un teléfono en uso no debe caducar nunca a media predicación.
        for (var i = 0; i < 5; i++)
        {
            _reloj = _reloj.AddMinutes(20);
            Assert.True(g.Validar(intento.Token));
        }
    }

    [Fact]
    public void UnTokenInventadoNoVale()
    {
        var g = Crear();
        g.Conceder(g.Pin, "ip", "a");

        Assert.False(g.Validar("no-es-un-token"));
        Assert.False(g.Validar(""));
        Assert.False(g.Validar(null));
    }

    [Fact]
    public void SinSesionNadaValida()
    {
        var g = Crear();
        Assert.False(g.HaySesion);
        Assert.False(g.Validar("cualquier-cosa"));
    }

    [Fact]
    public void RecuerdaQuienEstaConectado()
    {
        var g = Crear();
        g.Conceder(g.Pin, "192.168.1.40", "iPhone 13");

        var sesion = g.Actual;

        Assert.NotNull(sesion);
        Assert.Equal("192.168.1.40", sesion!.Ip);
        Assert.Equal("iPhone 13", sesion.Agente);
    }

    [Fact]
    public void SigueLaIpCuandoElTelefonoCambiaDeRed()
    {
        // No se ata la sesión a la IP: los teléfonos saltan de punto de acceso y
        // renuevan DHCP. Se actualiza para mostrarla, no para exigirla.
        var g = Crear();
        var intento = g.Conceder(g.Pin, "192.168.1.40", "iPhone");

        Assert.True(g.Validar(intento.Token, "192.168.1.55"));
        Assert.Equal("192.168.1.55", g.Actual!.Ip);
    }
}
