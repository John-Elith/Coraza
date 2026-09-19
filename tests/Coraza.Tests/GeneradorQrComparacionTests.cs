using System.Text;
using Coraza.Remoto.Qr;
using Xunit.Abstractions;
using ZXing;
using ZXing.QrCode.Internal;

namespace Coraza.Tests;

/// <summary>
/// Diagnóstico: compara la matriz de nuestro generador con la de ZXing, módulo a
/// módulo, forzando la misma máscara en ambos.
///
/// No pretende quedarse para siempre; existe para localizar en qué etapa está el
/// fallo cuando el código no decodifica. El patrón de las diferencias lo delata:
/// si solo difieren las franjas de formato, el error está en los bits de formato;
/// si difiere todo el área de datos, está en la colocación, el relleno o
/// Reed-Solomon; si difiere en una retícula regular, está en la máscara.
/// </summary>
public sealed class GeneradorQrComparacionTests
{
    private readonly ITestOutputHelper _salida;

    public GeneradorQrComparacionTests(ITestOutputHelper salida) => _salida = salida;

    [Fact]
    public void CompararConLaReferencia()
    {
        const string texto = "http://192.168.101.78:8787/";

        // Sin la pista del juego de caracteres: con ella ZXing antepone una cabecera
        // ECI (modo 0111 + designador 26) y su flujo deja de ser comparable con el
        // nuestro, que codifica en modo byte directo como manda la norma.
        var pistas = new Dictionary<EncodeHintType, object>();
        var referencia = ZXing.QrCode.Internal.Encoder.encode(texto, ErrorCorrectionLevel.M, pistas);
        var bm = referencia.Matrix;
        var mascara = referencia.MaskPattern;
        var version = referencia.Version.VersionNumber;
        var lado = bm.Width;

        _salida.WriteLine($"ZXing  -> versión {version}, máscara {mascara}, lado {lado}");

        var mia = GeneradorQr.CrearConMascara(texto, mascara);
        _salida.WriteLine($"Nuestro-> lado {mia.GetLength(0)} (misma máscara forzada)");
        _salida.WriteLine("");

        if (mia.GetLength(0) != lado)
        {
            _salida.WriteLine("Los lados no coinciden: la elección de versión es distinta. No sigo comparando.");
            return;
        }

        var zonas = new Dictionary<string, int>
        {
            ["buscadores"] = 0, ["temporización"] = 0, ["formato"] = 0,
            ["alineación"] = 0, ["datos"] = 0,
        };

        var mapa = new StringBuilder();
        for (var y = 0; y < lado; y++)
        {
            for (var x = 0; x < lado; x++)
            {
                var suyo = bm[x, y] == 1;
                var nuestro = mia[x, y];
                if (suyo == nuestro) { mapa.Append('·'); continue; }
                mapa.Append('X');
                zonas[Zona(x, y, lado)]++;
            }
            mapa.Append('\n');
        }

        // Si al transponer bajan mucho, el problema es de orientación en el área de datos.
        var transpuestas = 0;
        for (var y = 0; y < lado; y++)
            for (var x = 0; x < lado; x++)
                if ((bm[x, y] == 1) != mia[y, x]) transpuestas++;

        var total = zonas.Values.Sum();
        _salida.WriteLine($"Diferencias: {total} de {lado * lado} módulos");
        _salida.WriteLine($"Diferencias si transponemos la nuestra: {transpuestas}");
        foreach (var (zona, cuantas) in zonas.Where(z => z.Value > 0))
            _salida.WriteLine($"  {zona}: {cuantas}");
        _salida.WriteLine("");
        _salida.WriteLine("Mapa (· igual, X distinto):");
        _salida.WriteLine(mapa.ToString());

        Assert.Equal(0, total);
    }

    /// <summary>
    /// Compara los codewords: los nuestros contra los que se leen de la matriz de
    /// ZXing con nuestro mismo recorrido. Dónde divergen señala la etapa culpable.
    /// </summary>
    [Fact]
    public void CompararLosCodewords()
    {
        const string texto = "http://192.168.101.78:8787/";

        // Sin la pista del juego de caracteres: con ella ZXing antepone una cabecera
        // ECI (modo 0111 + designador 26) y su flujo deja de ser comparable con el
        // nuestro, que codifica en modo byte directo como manda la norma.
        var pistas = new Dictionary<EncodeHintType, object>();
        var referencia = ZXing.QrCode.Internal.Encoder.encode(texto, ErrorCorrectionLevel.M, pistas);
        var bm = referencia.Matrix;
        var mascara = referencia.MaskPattern;
        var lado = bm.Width;

        var (mio, reservado, _, version) = GeneradorQr.Interior(texto);

        // Se le quita la máscara a la matriz de ZXing para dejar los datos crudos.
        var sinMascara = new bool[lado, lado];
        for (var x = 0; x < lado; x++)
            for (var y = 0; y < lado; y++)
            {
                var v = bm[x, y] == 1;
                if (!reservado[x, y] && GeneradorQr.MascaraInvierte(mascara, x, y)) v = !v;
                sinMascara[x, y] = v;
            }

        var suyo = GeneradorQr.LeerDatos(sinMascara, reservado, mio.Length);

        _salida.WriteLine($"versión {version}, máscara {mascara}, {mio.Length} codewords");
        _salida.WriteLine("");
        _salida.WriteLine("i    nuestro  zxing");
        var primera = -1;
        for (var i = 0; i < mio.Length; i++)
        {
            var igual = mio[i] == suyo[i];
            if (!igual && primera < 0) primera = i;
            _salida.WriteLine($"{i,-4} {mio[i],5:X2}    {suyo[i],5:X2}  {(igual ? "" : "<-- distinto")}");
        }

        _salida.WriteLine("");
        _salida.WriteLine(primera < 0
            ? "Los codewords coinciden: el fallo estaría en la colocación."
            : $"Primer byte distinto: {primera} de {mio.Length}.");

        Assert.Equal(suyo, mio);
    }

    private static string Zona(int x, int y, int lado)
    {
        var enFormato = (x == 8 && (y < 9 || y >= lado - 8)) || (y == 8 && (x < 9 || x >= lado - 8));
        if (enFormato) return "formato";

        var buscador = (x < 8 && y < 8) || (x >= lado - 8 && y < 8) || (x < 8 && y >= lado - 8);
        if (buscador) return "buscadores";

        if (x == 6 || y == 6) return "temporización";

        // Para las versiones 2-6 solo hay un patrón de alineación, abajo a la derecha.
        var centro = lado - 7;
        if (Math.Abs(x - centro) <= 2 && Math.Abs(y - centro) <= 2) return "alineación";

        return "datos";
    }
}
