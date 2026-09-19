using Coraza.Remoto.Qr;
using Xunit.Abstractions;

namespace Coraza.Tests;

/// <summary>
/// Pruebas del generador de QR del control remoto.
///
/// Aviso honesto sobre su alcance: estas pruebas comprueban la ESTRUCTURA (tamaño,
/// patrones de búsqueda, temporización, módulo oscuro, elección de versión). Eso
/// descarta los errores gruesos, pero <b>no demuestra que el código sea legible</b>:
/// un fallo en el enmascarado, en los bits de formato o una matriz transpuesta
/// producen un QR perfectamente estructurado que ningún teléfono decodifica.
///
/// La verificación de verdad es <see cref="ImprimirParaEscanearConElTelefono"/>:
/// dibuja el código en la salida de la prueba para escanearlo con una cámara real.
/// </summary>
public sealed class GeneradorQrTests
{
    private readonly ITestOutputHelper _salida;

    public GeneradorQrTests(ITestOutputHelper salida) => _salida = salida;

    private const string UrlTipica = "http://192.168.101.78:8787/";

    [Theory]
    [InlineData(14, 1)]   // 16 codewords de datos menos 2 de cabecera
    [InlineData(15, 2)]
    [InlineData(26, 2)]   // 28 menos 2
    [InlineData(27, 3)]
    [InlineData(42, 3)]
    [InlineData(43, 4)]
    [InlineData(62, 4)]
    [InlineData(63, 5)]
    [InlineData(84, 5)]
    [InlineData(85, 6)]
    [InlineData(106, 6)]
    public void EligeLaVersionMasPequenaQueCabe(int bytes, int versionEsperada) =>
        Assert.Equal(versionEsperada, GeneradorQr.ElegirVersion(bytes));

    [Fact]
    public void RechazaUnaDireccionDemasiadoLarga() =>
        Assert.Throws<ArgumentException>(() => GeneradorQr.Crear(new string('a', 107)));

    [Theory]
    [InlineData(10, 21)]    // versión 1
    [InlineData(20, 25)]    // versión 2
    [InlineData(30, 29)]    // versión 3
    [InlineData(50, 33)]    // versión 4
    [InlineData(70, 37)]    // versión 5
    [InlineData(100, 41)]   // versión 6
    public void ElLadoCorrespondeALaVersion(int caracteres, int ladoEsperado)
    {
        var m = GeneradorQr.Crear(new string('a', caracteres));
        Assert.Equal(ladoEsperado, m.GetLength(0));
        Assert.Equal(ladoEsperado, m.GetLength(1));
    }

    [Fact]
    public void DibujaLosTresPatronesDeBusqueda()
    {
        var m = GeneradorQr.Crear(UrlTipica);
        var lado = m.GetLength(0);

        // Las tres esquinas que llevan buscador. La cuarta (abajo a la derecha) no.
        ComprobarBuscador(m, 0, 0);
        ComprobarBuscador(m, lado - 7, 0);
        ComprobarBuscador(m, 0, lado - 7);
    }

    [Fact]
    public void LaSeparacionAlrededorDelBuscadorEstaEnBlanco()
    {
        var m = GeneradorQr.Crear(UrlTipica);
        for (var i = 0; i < 8; i++)
        {
            Assert.False(m[i, 7]);
            Assert.False(m[7, i]);
        }
    }

    [Fact]
    public void LasLineasDeTemporizacionAlternan()
    {
        var m = GeneradorQr.Crear(UrlTipica);
        var lado = m.GetLength(0);
        for (var i = 8; i < lado - 8; i++)
        {
            Assert.Equal(i % 2 == 0, m[i, 6]);
            Assert.Equal(i % 2 == 0, m[6, i]);
        }
    }

    [Fact]
    public void ElModuloOscuroFijoEstaPuesto()
    {
        var m = GeneradorQr.Crear(UrlTipica);
        Assert.True(m[8, m.GetLength(0) - 8]);
    }

    [Fact]
    public void ElMismoTextoProduceSiempreLaMismaMatriz()
    {
        var a = GeneradorQr.Crear(UrlTipica);
        var b = GeneradorQr.Crear(UrlTipica);
        Assert.Equal(a.GetLength(0), b.GetLength(0));
        for (var x = 0; x < a.GetLength(0); x++)
            for (var y = 0; y < a.GetLength(1); y++)
                Assert.Equal(a[x, y], b[x, y]);
    }

    [Fact]
    public void MezclaClarosYOscurosDeFormaRazonable()
    {
        // Una matriz casi toda de un color delata que el enmascarado no se aplicó.
        var m = GeneradorQr.Crear(UrlTipica);
        var lado = m.GetLength(0);
        var oscuros = 0;
        foreach (var celda in m) if (celda) oscuros++;
        var porcentaje = oscuros * 100 / (lado * lado);
        Assert.InRange(porcentaje, 35, 65);
    }

    /// <summary>
    /// No afirma nada: dibuja el código para comprobarlo con una cámara real, que es
    /// lo único que demuestra que un QR sirve. Ejecutar con:
    ///   dotnet test tests\Coraza.Tests --filter ImprimirParaEscanearConElTelefono -l "console;verbosity=detailed"
    ///
    /// Dos detalles del dibujo, para que quepa y para que se pueda leer:
    ///
    /// - Se usan medios bloques (▀ ▄ █), que meten DOS filas de módulos en cada línea
    ///   de texto. Como la celda de una terminal es más alta que ancha, un módulo por
    ///   carácter de ancho y medio de alto sale prácticamente cuadrado.
    /// - La polaridad va invertida a propósito: en una terminal oscura un bloque lleno
    ///   se pinta con el color de texto, o sea CLARO. Por eso los módulos oscuros del
    ///   QR se dibujan como espacios (se ve el fondo) y los claros como bloques.
    ///   En una terminal de fondo blanco esto saldría al revés y no se leería.
    /// </summary>
    [Fact]
    public void ImprimirParaEscanearConElTelefono()
    {
        const int Silencio = 4;   // margen que exige la norma
        var m = GeneradorQr.Crear(UrlTipica);
        var lado = m.GetLength(0);
        var total = lado + Silencio * 2;

        // true = módulo CLARO. Fuera de la matriz está el margen, que también es claro.
        bool Claro(int x, int y)
        {
            var mx = x - Silencio;
            var my = y - Silencio;
            if (mx < 0 || my < 0 || mx >= lado || my >= lado) return true;
            return !m[mx, my];
        }

        _salida.WriteLine($"Contenido: {UrlTipica}");
        _salida.WriteLine($"Versión: {(lado - 17) / 4} · {lado} módulos · dibujo de {total}x{(total + 1) / 2}");
        _salida.WriteLine("");

        for (var y = 0; y < total; y += 2)
        {
            var linea = new System.Text.StringBuilder(total);
            for (var x = 0; x < total; x++)
            {
                var arriba = Claro(x, y);
                var abajo = y + 1 >= total || Claro(x, y + 1);
                linea.Append(arriba ? (abajo ? '█' : '▀') : (abajo ? '▄' : ' '));
            }
            _salida.WriteLine(linea.ToString());
        }
    }

    /// <summary>
    /// Guarda el código como imagen para escanearlo de verdad.
    ///
    /// El dibujo en la terminal sirve para mirarlo por encima, pero no para validarlo:
    /// el interlineado deja rayas horizontales entre líneas, los medios bloques no se
    /// tocan y la relación de aspecto depende de la fuente. Una imagen evita las tres
    /// cosas: módulos cuadrados, blanco y negro exactos y sin separaciones.
    ///
    /// Se escribe un BMP a mano porque es un formato trivial (cabecera de 54 bytes y
    /// píxeles en crudo) y así esta comprobación no añade ninguna dependencia.
    /// </summary>
    [Fact]
    public void GuardarImagenParaEscanear()
    {
        const int Escala = 10;    // píxeles por módulo
        const int Silencio = 4;   // margen que exige la norma

        var m = GeneradorQr.Crear(UrlTipica);
        var lado = m.GetLength(0);
        var modulos = lado + Silencio * 2;
        var pixeles = modulos * Escala;

        var ruta = Path.Combine(Path.GetTempPath(), "coraza-qr.bmp");
        EscribirBmp(ruta, pixeles, (px, py) =>
        {
            var mx = px / Escala - Silencio;
            var my = py / Escala - Silencio;
            if (mx < 0 || my < 0 || mx >= lado || my >= lado) return false;   // margen claro
            return m[mx, my];
        });

        _salida.WriteLine($"Contenido: {UrlTipica}");
        _salida.WriteLine($"Imagen de {pixeles}x{pixeles} px guardada en:");
        _salida.WriteLine(ruta);
        Assert.True(File.Exists(ruta));
    }

    /// <summary>
    /// La prueba que faltaba: escribe el BMP, lo vuelve a leer del disco y lo decodifica
    /// con el lector de IMÁGENES de ZXing.
    ///
    /// Decodificar la matriz en memoria demuestra que la codificación es correcta, pero
    /// no que el archivo sea legible: entre una y otra hay escala, polaridad, margen de
    /// silencio y el volteo vertical propio del formato BMP. Esto cubre ese tramo, que
    /// es justo el que ve la cámara.
    /// </summary>
    [Fact]
    public void ElArchivoDeImagenSeDecodificaComoLoHariaUnaCamara()
    {
        const int Escala = 10;
        const int Silencio = 4;

        var m = GeneradorQr.Crear(UrlTipica);
        var lado = m.GetLength(0);
        var pixeles = (lado + Silencio * 2) * Escala;

        var ruta = Path.Combine(Path.GetTempPath(), "coraza-qr-prueba.bmp");
        EscribirBmp(ruta, pixeles, (px, py) =>
        {
            var mx = px / Escala - Silencio;
            var my = py / Escala - Silencio;
            if (mx < 0 || my < 0 || mx >= lado || my >= lado) return false;
            return m[mx, my];
        });

        try
        {
            var rgb = LeerBmp(ruta, out var ancho, out var alto);
            var fuente = new ZXing.RGBLuminanceSource(rgb, ancho, alto,
                ZXing.RGBLuminanceSource.BitmapFormat.RGB24);
            var lector = new ZXing.BarcodeReaderGeneric
            {
                Options = new ZXing.Common.DecodingOptions
                {
                    PossibleFormats = new[] { ZXing.BarcodeFormat.QR_CODE },
                    TryHarder = true,
                },
            };

            var resultado = lector.Decode(fuente);

            _salida.WriteLine($"Archivo: {ruta} ({ancho}x{alto})");
            _salida.WriteLine($"Decodificado: {(resultado is null ? "NADA" : "«" + resultado.Text + "»")}");

            Assert.NotNull(resultado);
            Assert.Equal(UrlTipica, resultado!.Text);
        }
        finally
        {
            try { File.Delete(ruta); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Escribe un archivo con nombre NUEVO y módulos más grandes, para escanearlo con
    /// una cámara real sin riesgo de abrir por error una versión anterior en caché.
    /// </summary>
    [Fact]
    public void GuardarImagenGrandeParaEscanear()
    {
        const int Escala = 16;
        const int Silencio = 4;

        var m = GeneradorQr.Crear(UrlTipica);
        var lado = m.GetLength(0);
        var pixeles = (lado + Silencio * 2) * Escala;

        var ruta = Path.Combine(Path.GetTempPath(), "coraza-qr-nuevo.bmp");
        EscribirBmp(ruta, pixeles, (px, py) =>
        {
            var mx = px / Escala - Silencio;
            var my = py / Escala - Silencio;
            if (mx < 0 || my < 0 || mx >= lado || my >= lado) return false;
            return m[mx, my];
        });

        _salida.WriteLine($"Contenido: {UrlTipica}");
        _salida.WriteLine($"Imagen de {pixeles}x{pixeles} px ({Escala} px por módulo) en:");
        _salida.WriteLine(ruta);
        Assert.True(File.Exists(ruta));
    }

    /// <summary>Lee un BMP de 24 bits y devuelve los píxeles en RGB24 de arriba abajo.</summary>
    private static byte[] LeerBmp(string ruta, out int ancho, out int alto)
    {
        var bytes = File.ReadAllBytes(ruta);
        var desplazamiento = BitConverter.ToInt32(bytes, 10);
        ancho = BitConverter.ToInt32(bytes, 18);
        alto = BitConverter.ToInt32(bytes, 22);

        var bytesPorFila = (ancho * 3 + 3) / 4 * 4;
        var salida = new byte[ancho * alto * 3];

        for (var y = 0; y < alto; y++)
        {
            // El BMP guarda de abajo arriba; se le da la vuelta al leerlo.
            var origen = desplazamiento + (alto - 1 - y) * bytesPorFila;
            for (var x = 0; x < ancho; x++)
            {
                var b = bytes[origen + x * 3];
                var g = bytes[origen + x * 3 + 1];
                var r = bytes[origen + x * 3 + 2];
                var destino = (y * ancho + x) * 3;
                salida[destino] = r;
                salida[destino + 1] = g;
                salida[destino + 2] = b;
            }
        }
        return salida;
    }

    /// <summary>BMP de 24 bits sin comprimir. <paramref name="oscuro"/> decide cada píxel.</summary>
    private static void EscribirBmp(string ruta, int tamano, Func<int, int, bool> oscuro)
    {
        var bytesPorFila = (tamano * 3 + 3) / 4 * 4;   // cada fila se alinea a 4 bytes
        var tamanoImagen = bytesPorFila * tamano;

        using var archivo = File.Create(ruta);
        using var w = new BinaryWriter(archivo);

        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(54 + tamanoImagen);
        w.Write(0);              // reservado
        w.Write(54);             // desplazamiento hasta los píxeles

        w.Write(40);             // tamaño de la cabecera DIB
        w.Write(tamano);         // ancho
        w.Write(tamano);         // alto
        w.Write((short)1);       // planos
        w.Write((short)24);      // bits por píxel
        w.Write(0);              // sin compresión
        w.Write(tamanoImagen);
        w.Write(2835); w.Write(2835);   // ~72 ppp
        w.Write(0); w.Write(0);

        var relleno = new byte[bytesPorFila - tamano * 3];
        // El BMP guarda las filas de abajo hacia arriba.
        for (var y = tamano - 1; y >= 0; y--)
        {
            for (var x = 0; x < tamano; x++)
            {
                var v = oscuro(x, y) ? (byte)0 : (byte)255;
                w.Write(v); w.Write(v); w.Write(v);   // BGR
            }
            if (relleno.Length > 0) w.Write(relleno);
        }
    }

    private static void ComprobarBuscador(bool[,] m, int x0, int y0)
    {
        // Anillo exterior oscuro, aro interior claro, centro 3x3 oscuro.
        for (var dx = 0; dx < 7; dx++)
            for (var dy = 0; dy < 7; dy++)
            {
                var anillo = dx is 0 or 6 || dy is 0 or 6;
                var centro = dx is >= 2 and <= 4 && dy is >= 2 and <= 4;
                Assert.Equal(anillo || centro, m[x0 + dx, y0 + dy]);
            }
    }
}
