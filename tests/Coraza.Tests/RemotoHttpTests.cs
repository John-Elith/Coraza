using System.Text;
using Coraza.Remoto.Http;

namespace Coraza.Tests;

/// <summary>
/// Pruebas del analizador de HTTP del control remoto.
///
/// Todas corren sobre un <see cref="MemoryStream"/>, sin abrir un socket: lo que se
/// prueba es el analizador, no la red. El objetivo es que ninguna petición rara
/// lance una excepción — un cliente mal educado debe recibir un código de error,
/// nunca tumbar el hilo que lo atiende durante un culto.
/// </summary>
public sealed class RemotoHttpTests
{
    private static Stream Flujo(string texto) => new MemoryStream(Encoding.UTF8.GetBytes(texto));

    private static async Task<SolicitudHttp.Analisis> Leer(string texto) =>
        await SolicitudHttp.LeerAsync(Flujo(texto));

    [Fact]
    public async Task InterpretaUnGetSencillo()
    {
        var a = await Leer("GET /api/estado HTTP/1.1\r\nHost: equipo\r\n\r\n");

        Assert.True(a.Ok);
        Assert.Equal("GET", a.Solicitud!.Metodo);
        Assert.Equal("/api/estado", a.Solicitud.Ruta);
        Assert.Equal("", a.Solicitud.Cuerpo);
    }

    [Fact]
    public async Task InterpretaUnPostConCuerpo()
    {
        var cuerpo = "{\"pin\":\"123456\"}";
        var a = await Leer($"POST /api/sesion HTTP/1.1\r\nContent-Length: {cuerpo.Length}\r\n\r\n{cuerpo}");

        Assert.True(a.Ok);
        Assert.Equal("POST", a.Solicitud!.Metodo);
        Assert.Equal(cuerpo, a.Solicitud.Cuerpo);
    }

    [Fact]
    public async Task LasCabecerasNoDistinguenMayusculas()
    {
        var a = await Leer("GET / HTTP/1.1\r\nX-Coraza-Token: abc\r\n\r\n");

        Assert.Equal("abc", a.Solicitud!.Cabecera("x-coraza-token"));
        Assert.Equal("abc", a.Solicitud.Cabecera("X-CORAZA-TOKEN"));
        Assert.Null(a.Solicitud.Cabecera("no-vino"));
    }

    [Fact]
    public async Task SeparaYDecodificaLaCadenaDeConsulta()
    {
        var a = await Leer("GET /api/estado?t=ab%2Fc&x=uno+dos HTTP/1.1\r\n\r\n");

        Assert.Equal("/api/estado", a.Solicitud!.Ruta);
        Assert.Equal("ab/c", a.Solicitud.Parametro("t"));
        Assert.Equal("uno dos", a.Solicitud.Parametro("x"));
        Assert.Null(a.Solicitud.Parametro("z"));
    }

    [Fact]
    public async Task DecodificaLaRuta()
    {
        var a = await Leer("GET /api/%65stado HTTP/1.1\r\n\r\n");
        Assert.Equal("/api/estado", a.Solicitud!.Ruta);
    }

    [Fact]
    public async Task AceptaLaFormaAbsoluta()
    {
        // «GET http://equipo:8787/ruta HTTP/1.1» es legal y algunos clientes la usan.
        var a = await Leer("GET http://192.168.1.5:8787/api/estado?t=1 HTTP/1.1\r\n\r\n");

        Assert.True(a.Ok);
        Assert.Equal("/api/estado", a.Solicitud!.Ruta);
        Assert.Equal("1", a.Solicitud.Parametro("t"));
    }

    [Fact]
    public async Task ToleraSaltosDeLineaSueltos()
    {
        // Un cliente de pruebas escrito a mano suele mandar solo LF.
        var a = await Leer("GET /api/estado HTTP/1.1\nHost: equipo\n\n");

        Assert.True(a.Ok);
        Assert.Equal("/api/estado", a.Solicitud!.Ruta);
    }

    [Fact]
    public async Task RechazaUnMetodoQueNoUsamos()
    {
        Assert.Equal(405, (await Leer("PUT / HTTP/1.1\r\n\r\n")).CodigoError);
        Assert.Equal(405, (await Leer("PATCH / HTTP/1.1\r\n\r\n")).CodigoError);
        Assert.Equal(405, (await Leer("TRACE / HTTP/1.1\r\n\r\n")).CodigoError);
    }

    [Fact]
    public async Task RechazaOtraVersionDelProtocolo() =>
        Assert.Equal(505, (await Leer("GET / HTTP/2.0\r\n\r\n")).CodigoError);

    [Fact]
    public async Task RechazaUnaLineaDePeticionDemasiadoLarga()
    {
        var ruta = "/" + new string('a', SolicitudHttp.Limites.LineaPeticion);
        Assert.Equal(414, (await Leer($"GET {ruta} HTTP/1.1\r\n\r\n")).CodigoError);
    }

    [Fact]
    public async Task RechazaDemasiadasCabeceras()
    {
        var sb = new StringBuilder("GET / HTTP/1.1\r\n");
        for (var i = 0; i <= SolicitudHttp.Limites.NumeroCabeceras; i++)
            sb.Append($"X-Relleno-{i}: v\r\n");
        sb.Append("\r\n");

        Assert.Equal(431, (await Leer(sb.ToString())).CodigoError);
    }

    [Fact]
    public async Task RechazaUnBloqueDeCabecerasEnorme()
    {
        var sb = new StringBuilder("GET / HTTP/1.1\r\n");
        sb.Append("X-Grande: ").Append(new string('a', SolicitudHttp.Limites.BloqueCabeceras)).Append("\r\n\r\n");

        Assert.Equal(431, (await Leer(sb.ToString())).CodigoError);
    }

    [Fact]
    public async Task RechazaUnCuerpoDemasiadoGrande()
    {
        var largo = SolicitudHttp.Limites.Cuerpo + 1;
        Assert.Equal(413, (await Leer($"POST / HTTP/1.1\r\nContent-Length: {largo}\r\n\r\n")).CodigoError);
    }

    [Fact]
    public async Task RechazaElTroceado()
    {
        // No implementamos Transfer-Encoding: nuestro propio fetch() siempre manda longitud.
        Assert.Equal(400, (await Leer("POST / HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n")).CodigoError);
    }

    [Fact]
    public async Task RechazaUnaCabeceraSinDosPuntos() =>
        Assert.Equal(400, (await Leer("GET / HTTP/1.1\r\nEstoNoEsUnaCabecera\r\n\r\n")).CodigoError);

    [Fact]
    public async Task RechazaUnaLongitudQueNoEsUnNumero() =>
        Assert.Equal(400, (await Leer("POST / HTTP/1.1\r\nContent-Length: muchos\r\n\r\n")).CodigoError);

    [Fact]
    public async Task RechazaUnCuerpoMasCortoQueLoPrometido() =>
        Assert.Equal(400, (await Leer("POST / HTTP/1.1\r\nContent-Length: 50\r\n\r\ncorto")).CodigoError);

    [Fact]
    public async Task RechazaUnaRutaQueIntentaSubirDeDirectorio() =>
        Assert.Equal(400, (await Leer("GET /../secreto HTTP/1.1\r\n\r\n")).CodigoError);

    [Fact]
    public async Task RechazaUnaPeticionSinLasTresPartes() =>
        Assert.Equal(400, (await Leer("GET /solo-dos\r\n\r\n")).CodigoError);

    [Fact]
    public async Task UnCierreLimpioNoEsUnError()
    {
        // El cliente abrió y se fue sin pedir nada: ni petición, ni código de error.
        var a = await Leer("");

        Assert.False(a.Ok);
        Assert.Equal(0, a.CodigoError);
    }

    [Fact]
    public async Task ReconstruyeUtf8AunqueLlegueAGoteo()
    {
        // Este es el error clásico: decodificar cada lectura por separado parte un
        // carácter de varios bytes por la mitad. Se decodifica el cuerpo completo.
        var cuerpo = "{\"consulta\":\"cuán grande es él · añoranza € ñ\"}";
        var bytes = Encoding.UTF8.GetByteCount(cuerpo);
        var crudo = $"POST /api/buscar HTTP/1.1\r\nContent-Length: {bytes}\r\n\r\n{cuerpo}";

        var a = await SolicitudHttp.LeerAsync(new FlujoTacano(Encoding.UTF8.GetBytes(crudo)));

        Assert.True(a.Ok);
        Assert.Equal(cuerpo, a.Solicitud!.Cuerpo);
    }

    /// <summary>Devuelve un byte por lectura, para imitar un socket lento y partido.</summary>
    private sealed class FlujoTacano : Stream
    {
        private readonly byte[] _datos;
        private int _posicion;

        public FlujoTacano(byte[] datos) => _datos = datos;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_posicion >= _datos.Length) return 0;
            buffer[offset] = _datos[_posicion++];
            return 1;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_posicion >= _datos.Length) return ValueTask.FromResult(0);
            buffer.Span[0] = _datos[_posicion++];
            return ValueTask.FromResult(1);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _datos.Length;
        public override long Position { get => _posicion; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
