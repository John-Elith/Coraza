using Coraza.Core.Canciones;
using Coraza.Core.Modelos;

namespace Coraza.Tests;

public class AnalizadorLetraTests
{
    [Fact]
    public void InterpretaEtiquetasEntreCorchetes()
    {
        var r = AnalizadorLetra.Analizar("""
            [Verso 1]
            Linea uno
            Linea dos

            [Coro]
            Coro uno
            Coro dos

            [Verso 2]
            Otra linea

            [Coro]
            """);

        Assert.False(r.DeteccionAutomatica);
        Assert.Equal(new[] { "V1", "C", "V2" }, r.Secciones.Select(s => s.Codigo));
        Assert.Equal(new[] { "V1", "C", "V2", "C" }, r.Orden);
        Assert.Equal("Linea uno\nLinea dos", r.Secciones[0].Texto);
    }

    [Fact]
    public void InterpretaEtiquetasSinCorchetesEIngles()
    {
        var r = AnalizadorLetra.Analizar("""
            Verse 1:
            Amazing grace
            CHORUS
            My chains are gone
            Pre-Coro
            Antes del coro
            Puente:
            Puente texto
            """);

        Assert.Equal(new[] { TipoSeccion.Verso, TipoSeccion.Coro, TipoSeccion.PreCoro, TipoSeccion.Puente }, r.Secciones.Select(s => s.Tipo));
    }

    [Fact]
    public void VersosSinNumeroSeNumeranSolos()
    {
        var r = AnalizadorLetra.Analizar("[Verso]\nuno\n\n[Coro]\ncanto al Rey\n\n[Verso]\ndos");
        Assert.Equal(new[] { "V1", "C", "V2" }, r.Secciones.Select(s => s.Codigo));
    }

    [Fact]
    public void DetectaCorosRepetidosSinEtiquetas()
    {
        var r = AnalizadorLetra.Analizar("""
            Primera estrofa
            sigue aqui

            Cuan grande es El
            Cuan grande es El

            Segunda estrofa
            otra linea

            Cuán grande es Él
            Cuan grande es el
            """);

        Assert.True(r.DeteccionAutomatica);
        Assert.Equal(new[] { "V1", "C", "V2" }, r.Secciones.Select(s => s.Codigo));
        Assert.Equal(new[] { "V1", "C", "V2", "C" }, r.Orden);
    }

    [Fact]
    public void ProponeOrdenConCoroTrasCadaVerso()
    {
        var r = AnalizadorLetra.Analizar("[Verso 1]\na\n[Verso 2]\nb\n[Coro]\nc\n[Puente]\nd");
        Assert.Equal(new[] { "V1", "C", "V2", "C", "P", "C" }, AnalizadorLetra.ProponerOrden(r.Secciones));
    }

    [Fact]
    public void LimpiaTextoPegadoDeLaWeb()
    {
        var limpio = AnalizadorLetra.LimpiarTextoPegado("""
            G        D       Em   C
              Sublime  gracia   del Señor  (x2)
            Am7  D/F#


            que a mí pecador salvó
            (bis)
            """);
        Assert.Equal("Sublime gracia del Señor\n\nque a mí pecador salvó", limpio);
    }

    [Fact]
    public void InterpretaOrdenEscrito()
    {
        var r = AnalizadorLetra.Analizar("[Verso 1]\na\n[Coro]\nc");
        var (validos, desconocidos) = AnalizadorLetra.InterpretarOrden("v1 c v c x9", r.Secciones);
        Assert.Equal(new[] { "V1", "C", "V1", "C" }, validos);
        Assert.Equal(new[] { "x9" }, desconocidos);
    }

    [Fact]
    public void FormatearYAnalizarEsIdempotente()
    {
        var r = AnalizadorLetra.Analizar("[Verso 1]\nuno\n\n[Coro]\ndos\ntres");
        var texto = AnalizadorLetra.Formatear(r.Secciones);
        var r2 = AnalizadorLetra.Analizar(texto);
        Assert.Equal(r.Secciones.Select(s => (s.Codigo, s.Texto)), r2.Secciones.Select(s => (s.Codigo, s.Texto)));
    }

    [Fact]
    public void LineasDeLetraNoSeConfundenConEtiquetas()
    {
        Assert.False(CodigosSeccion.IntentarInterpretarEtiqueta("Coro de ángeles cantan", out _, out _));
        Assert.False(CodigosSeccion.IntentarInterpretarEtiqueta("C", out _, out _));
        Assert.True(CodigosSeccion.IntentarInterpretarEtiqueta("[C]", out var tipo, out _));
        Assert.Equal(TipoSeccion.Coro, tipo);
    }
}
