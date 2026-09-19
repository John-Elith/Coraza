using System.Text.RegularExpressions;
using Coraza.Core.Busqueda;
using Coraza.Core.Modelos;

namespace Coraza.Core.Canciones;

/// <summary>Traduce entre tipos de sección, códigos cortos (V1, C, P) y etiquetas escritas («Verso 1:», «[Coro]», «Chorus»).</summary>
public static partial class CodigosSeccion
{
    private static readonly Dictionary<string, TipoSeccion> Etiquetas = new()
    {
        ["verso"] = TipoSeccion.Verso, ["verse"] = TipoSeccion.Verso, ["estrofa"] = TipoSeccion.Verso,
        ["precoro"] = TipoSeccion.PreCoro, ["pre coro"] = TipoSeccion.PreCoro, ["prechorus"] = TipoSeccion.PreCoro,
        ["pre chorus"] = TipoSeccion.PreCoro,
        ["coro"] = TipoSeccion.Coro, ["chorus"] = TipoSeccion.Coro, ["estribillo"] = TipoSeccion.Coro,
        ["puente"] = TipoSeccion.Puente, ["bridge"] = TipoSeccion.Puente,
        ["final"] = TipoSeccion.Final, ["outro"] = TipoSeccion.Final, ["coda"] = TipoSeccion.Final,
        ["ending"] = TipoSeccion.Final,
        ["intro"] = TipoSeccion.Intro, ["introduccion"] = TipoSeccion.Intro,
        ["tag"] = TipoSeccion.Otro, ["interludio"] = TipoSeccion.Otro, ["instrumental"] = TipoSeccion.Otro,
        ["otro"] = TipoSeccion.Otro,
    };

    private static readonly Dictionary<string, TipoSeccion> CodigosCortos = new()
    {
        ["v"] = TipoSeccion.Verso, ["pc"] = TipoSeccion.PreCoro, ["c"] = TipoSeccion.Coro,
        ["p"] = TipoSeccion.Puente, ["f"] = TipoSeccion.Final, ["i"] = TipoSeccion.Intro, ["o"] = TipoSeccion.Otro,
    };

    public static string Codigo(TipoSeccion tipo, int numero)
    {
        var codigo = tipo switch
        {
            TipoSeccion.Verso => "V",
            TipoSeccion.PreCoro => "PC",
            TipoSeccion.Coro => "C",
            TipoSeccion.Puente => "P",
            TipoSeccion.Final => "F",
            TipoSeccion.Intro => "I",
            _ => "O",
        };
        return numero > 0 ? codigo + numero : codigo;
    }

    public static string Nombre(TipoSeccion tipo, int numero)
    {
        var nombre = tipo switch
        {
            TipoSeccion.Verso => "Verso",
            TipoSeccion.PreCoro => "Pre-coro",
            TipoSeccion.Coro => "Coro",
            TipoSeccion.Puente => "Puente",
            TipoSeccion.Final => "Final",
            TipoSeccion.Intro => "Intro",
            _ => "Otro",
        };
        return numero > 0 ? $"{nombre} {numero}" : nombre;
    }

    /// <summary>Interpreta un código corto como «V1», «c», «PC», «p2».</summary>
    public static bool IntentarInterpretarCodigo(string? codigo, out TipoSeccion tipo, out int numero)
    {
        tipo = TipoSeccion.Otro;
        numero = 0;
        if (string.IsNullOrWhiteSpace(codigo)) return false;
        var m = CodigoRegex().Match(codigo.Trim().ToLowerInvariant());
        if (!m.Success || !CodigosCortos.TryGetValue(m.Groups[1].Value, out tipo)) return false;
        numero = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        return true;
    }

    /// <summary>
    /// Reconoce una línea de etiqueta: «[Verso 1]», «Coro:», «CHORUS 2», «(Puente)», «[C]».
    /// Los códigos de una letra solo se aceptan entre corchetes para no confundirlos con la letra.
    /// </summary>
    public static bool IntentarInterpretarEtiqueta(string? linea, out TipoSeccion tipo, out int numero)
    {
        tipo = TipoSeccion.Otro;
        numero = 0;
        if (string.IsNullOrWhiteSpace(linea)) return false;
        var texto = linea.Trim();
        var entreCorchetes = false;
        var mc = CorchetesRegex().Match(texto);
        if (mc.Success)
        {
            texto = mc.Groups[1].Value.Trim();
            entreCorchetes = true;
        }
        texto = texto.TrimEnd(':', '.').Trim();
        var normal = Normalizador.Normalizar(texto.Replace('-', ' '));
        var m = EtiquetaRegex().Match(normal);
        if (!m.Success) return false;
        var nombre = m.Groups[1].Value.Trim();
        numero = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        if (Etiquetas.TryGetValue(nombre, out tipo)) return true;
        if (entreCorchetes && CodigosCortos.TryGetValue(nombre.Replace(" ", ""), out tipo)) return true;
        if (entreCorchetes && IntentarInterpretarCodigo(normal.Replace(" ", ""), out tipo, out numero)) return true;
        return false;
    }

    [GeneratedRegex(@"^(pc|v|c|p|f|i|o)(\d+)?$")]
    private static partial Regex CodigoRegex();

    [GeneratedRegex(@"^[\[\(\{]\s*(.+?)\s*[\]\)\}]$")]
    private static partial Regex CorchetesRegex();

    [GeneratedRegex(@"^([a-z]+(?: [a-z]+)?)\s*(\d+)?(?:\s*x\d+)?$")]
    private static partial Regex EtiquetaRegex();
}
