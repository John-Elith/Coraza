using Coraza.Remoto.Qr;
using Xunit.Abstractions;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace Coraza.Tests;

/// <summary>
/// La prueba que de verdad demuestra que el generador sirve: decodifica su propia
/// salida con ZXing, que es una implementación independiente de la norma.
///
/// Las pruebas estructurales de <see cref="GeneradorQrTests"/> comprueban que los
/// patrones están donde toca, pero no pueden detectar un error en el enmascarado,
/// en los bits de formato, en el orden de colocación de los datos o en la corrección
/// Reed-Solomon: todos ellos producen un código de aspecto impecable e ilegible.
///
/// ZXing entra <b>solo en el proyecto de pruebas</b>. Ni Coraza.Remoto ni la
/// aplicación la referencian, así que el instalador no crece.
/// </summary>
public sealed class GeneradorQrDecodificacionTests
{
    private readonly ITestOutputHelper _salida;

    public GeneradorQrDecodificacionTests(ITestOutputHelper salida) => _salida = salida;

    /// <summary>Decodifica la matriz tal cual, sin pasar por una imagen.</summary>
    private static string? Decodificar(bool[,] m, bool transponer)
    {
        var lado = m.GetLength(0);
        var matriz = new BitMatrix(lado);
        for (var x = 0; x < lado; x++)
            for (var y = 0; y < lado; y++)
                if (transponer ? m[y, x] : m[x, y])
                    matriz[x, y] = true;

        try
        {
            var resultado = new Decoder().decode(matriz, null);
            return resultado?.Text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [Theory]
    [InlineData("http://192.168.101.78:8787/")]
    [InlineData("http://10.0.0.5:8787/")]
    [InlineData("a")]
    [InlineData("Coraza · control remoto")]
    public void ElCodigoGeneradoSeDecodificaYDevuelveElMismoTexto(string texto)
    {
        var m = GeneradorQr.Crear(texto);
        Assert.Equal(texto, Decodificar(m, transponer: false));
    }

    [Fact]
    public void FuncionaEnTodasLasVersionesQueGeneramos()
    {
        // Una cadena por versión, de la 1 a la 6.
        foreach (var largo in new[] { 10, 20, 30, 50, 70, 100 })
        {
            var texto = new string('x', largo);
            var m = GeneradorQr.Crear(texto);
            Assert.Equal(texto, Decodificar(m, transponer: false));
        }
    }

    /// <summary>
    /// Diagnóstico: si la prueba de arriba falla, esta dice si el problema es que la
    /// matriz está transpuesta (el error clásico) o si es otra cosa.
    /// </summary>
    [Fact]
    public void Diagnostico()
    {
        const string texto = "http://192.168.101.78:8787/";
        var m = GeneradorQr.Crear(texto);

        var normal = Decodificar(m, transponer: false);
        var transpuesta = Decodificar(m, transponer: true);

        _salida.WriteLine($"Esperado:            «{texto}»");
        _salida.WriteLine($"Matriz tal cual:     {Describir(normal)}");
        _salida.WriteLine($"Matriz transpuesta:  {Describir(transpuesta)}");

        if (normal == texto) _salida.WriteLine("=> El generador es correcto.");
        else if (transpuesta == texto) _salida.WriteLine("=> ESTÁ TRANSPUESTA: hay que intercambiar los índices.");
        else _salida.WriteLine("=> No decodifica en ninguna orientación: el fallo está en los datos, el formato o la máscara.");

        static string Describir(string? v) => v is null ? "no decodifica" : $"«{v}»";
    }
}
