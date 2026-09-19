using System.Text;
using System.Text.RegularExpressions;
using Coraza.Core.Busqueda;
using Coraza.Core.Modelos;

namespace Coraza.Core.Canciones;

public sealed class ResultadoLetra
{
    public List<Seccion> Secciones { get; } = new();
    public List<string> Orden { get; } = new();
    /// <summary>True si no había etiquetas y los coros se detectaron por repetición.</summary>
    public bool DeteccionAutomatica { get; set; }
}

/// <summary>
/// Convierte texto de letra en secciones. Acepta etiquetas como «[Verso 1]», «Coro:», «CHORUS».
/// Sin etiquetas separa estrofas por líneas en blanco y marca como coro las que se repiten.
/// </summary>
public static partial class AnalizadorLetra
{
    /// <summary>Línea que fuerza un cambio de diapositiva dentro de una sección.</summary>
    public const string SeparadorDiapositiva = "---";

    public static ResultadoLetra Analizar(string? texto)
    {
        var lineas = (texto ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var hayEtiquetas = lineas.Any(l => CodigosSeccion.IntentarInterpretarEtiqueta(l, out _, out _));
        return hayEtiquetas ? AnalizarConEtiquetas(lineas) : DetectarAutomaticamente(lineas);
    }

    private sealed class Bloque
    {
        public TipoSeccion? Tipo;
        public int Numero;
        public bool NumeroExplicito;
        public readonly List<string> Lineas = new();
    }

    private static ResultadoLetra AnalizarConEtiquetas(string[] lineas)
    {
        var bloques = new List<Bloque>();
        Bloque? actual = null;
        foreach (var linea in lineas)
        {
            if (CodigosSeccion.IntentarInterpretarEtiqueta(linea, out var tipo, out var numero))
            {
                actual = new Bloque { Tipo = tipo, Numero = numero, NumeroExplicito = numero > 0 };
                bloques.Add(actual);
                continue;
            }
            if (actual is null)
            {
                if (string.IsNullOrWhiteSpace(linea)) continue;
                actual = new Bloque();
                bloques.Add(actual);
            }
            actual.Lineas.Add(linea.Trim());
        }

        var resultado = new ResultadoLetra();
        var porCodigo = new Dictionary<string, Seccion>();

        foreach (var bloque in bloques)
        {
            var cuerpo = LimpiarCuerpo(bloque.Lineas);
            var tipo = bloque.Tipo ?? TipoSeccion.Verso;
            var numero = bloque.Numero;

            if (tipo == TipoSeccion.Verso && numero == 0)
            {
                // «[Verso]» sin número: si ya existe uno idéntico se reutiliza; si no, toma el siguiente número.
                var igual = resultado.Secciones.FirstOrDefault(s => s.Tipo == TipoSeccion.Verso && MismoTexto(s.Texto, cuerpo));
                if (igual is not null || (cuerpo.Length == 0 && bloque.Tipo is not null))
                {
                    var referencia = igual ?? resultado.Secciones.LastOrDefault(s => s.Tipo == TipoSeccion.Verso);
                    if (referencia is not null) resultado.Orden.Add(referencia.Codigo);
                    continue;
                }
                numero = SiguienteNumero(resultado.Secciones, TipoSeccion.Verso);
            }

            var codigo = CodigosSeccion.Codigo(tipo, numero);
            if (porCodigo.TryGetValue(codigo, out var existente))
            {
                if (cuerpo.Length == 0 || MismoTexto(existente.Texto, cuerpo))
                {
                    resultado.Orden.Add(codigo);
                    continue;
                }
                // Misma etiqueta con distinto texto: se numera como una sección nueva.
                numero = Math.Max(SiguienteNumero(resultado.Secciones, tipo), 2);
                codigo = CodigosSeccion.Codigo(tipo, numero);
            }
            if (cuerpo.Length == 0) continue;

            var seccion = new Seccion { Tipo = tipo, Numero = numero, Texto = cuerpo, Posicion = resultado.Secciones.Count };
            resultado.Secciones.Add(seccion);
            porCodigo[codigo] = seccion;
            resultado.Orden.Add(codigo);
        }
        return resultado;
    }

    private static ResultadoLetra DetectarAutomaticamente(string[] lineas)
    {
        var estrofas = new List<string>();
        var actual = new List<string>();
        foreach (var linea in lineas)
        {
            if (string.IsNullOrWhiteSpace(linea))
            {
                if (actual.Count > 0) estrofas.Add(string.Join("\n", actual));
                actual.Clear();
            }
            else
            {
                actual.Add(linea.Trim());
            }
        }
        if (actual.Count > 0) estrofas.Add(string.Join("\n", actual));

        var resultado = new ResultadoLetra { DeteccionAutomatica = true };
        var repeticiones = estrofas.GroupBy(Normalizador.Normalizar).ToDictionary(g => g.Key, g => g.Count());
        var asignadas = new Dictionary<string, Seccion>();
        var coros = 0;
        var versos = 0;

        foreach (var estrofa in estrofas)
        {
            var clave = Normalizador.Normalizar(estrofa);
            if (!asignadas.TryGetValue(clave, out var seccion))
            {
                var esCoro = repeticiones[clave] > 1;
                seccion = esCoro
                    ? new Seccion { Tipo = TipoSeccion.Coro, Numero = coros++ == 0 ? 0 : coros }
                    : new Seccion { Tipo = TipoSeccion.Verso, Numero = ++versos };
                seccion.Texto = estrofa;
                seccion.Posicion = resultado.Secciones.Count;
                resultado.Secciones.Add(seccion);
                asignadas[clave] = seccion;
            }
            resultado.Orden.Add(seccion.Codigo);
        }
        return resultado;
    }

    /// <summary>Propone un orden típico: cada verso seguido de (pre-coro y) coro, y el coro después del puente.</summary>
    public static List<string> ProponerOrden(IReadOnlyList<Seccion> secciones)
    {
        var ordenadas = secciones.OrderBy(s => s.Posicion).ToList();
        var coro = ordenadas.FirstOrDefault(s => s.Tipo == TipoSeccion.Coro);
        var precoro = ordenadas.FirstOrDefault(s => s.Tipo == TipoSeccion.PreCoro);
        var orden = new List<string>();

        foreach (var intro in ordenadas.Where(s => s.Tipo == TipoSeccion.Intro)) orden.Add(intro.Codigo);

        foreach (var s in ordenadas)
        {
            switch (s.Tipo)
            {
                case TipoSeccion.Verso:
                    orden.Add(s.Codigo);
                    if (precoro is not null) orden.Add(precoro.Codigo);
                    if (coro is not null) orden.Add(coro.Codigo);
                    break;
                case TipoSeccion.Puente:
                    orden.Add(s.Codigo);
                    if (coro is not null) orden.Add(coro.Codigo);
                    break;
                case TipoSeccion.Otro:
                    orden.Add(s.Codigo);
                    break;
                case TipoSeccion.Coro when !ordenadas.Any(x => x.Tipo == TipoSeccion.Verso):
                    orden.Add(s.Codigo);
                    break;
                case TipoSeccion.Coro when s != coro:
                    orden.Add(s.Codigo);
                    break;
            }
        }

        foreach (var final in ordenadas.Where(s => s.Tipo == TipoSeccion.Final)) orden.Add(final.Codigo);
        if (orden.Count == 0) orden.AddRange(ordenadas.Select(s => s.Codigo));
        return orden;
    }

    /// <summary>Genera el texto editable con etiquetas, cada sección una vez.</summary>
    public static string Formatear(IEnumerable<Seccion> secciones)
    {
        var sb = new StringBuilder();
        foreach (var s in secciones.OrderBy(s => s.Posicion))
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append('[').Append(s.Nombre).Append("]\n").Append(s.Texto.Trim());
        }
        return sb.ToString();
    }

    /// <summary>Interpreta un orden escrito como «V1 C V2 C P C». Devuelve los códigos válidos y los desconocidos.</summary>
    public static (List<string> Validos, List<string> Desconocidos) InterpretarOrden(string? texto, IReadOnlyList<Seccion> secciones)
    {
        var validos = new List<string>();
        var desconocidos = new List<string>();
        var codigos = secciones.Select(s => s.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var token in (texto ?? "").Split(new[] { ' ', ',', ';', '\t', '\n', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (CodigosSeccion.IntentarInterpretarCodigo(token, out var tipo, out var numero))
            {
                var codigo = CodigosSeccion.Codigo(tipo, numero);
                if (codigos.Contains(codigo)) { validos.Add(codigo); continue; }
                // «V» sin número cuando solo existe «V1»
                if (numero == 0 && codigos.Contains(codigo + "1")) { validos.Add(codigo + "1"); continue; }
            }
            desconocidos.Add(token);
        }
        return (validos, desconocidos);
    }

    /// <summary>
    /// Completa un orden con las secciones escritas que no aparecen en él (por ejemplo, un «[Final]»
    /// agregado después de fijar el orden), en el mismo lugar en que están escritas. Así todo lo que
    /// se escribe en la letra se proyecta: una sección nunca se pierde en silencio.
    /// </summary>
    public static List<string> CompletarOrden(IEnumerable<string> orden, IReadOnlyList<Seccion> secciones, out List<string> agregadas)
    {
        var resultado = orden.ToList();
        var usados = resultado.ToHashSet(StringComparer.OrdinalIgnoreCase);
        agregadas = secciones
            .OrderBy(s => s.Posicion)
            .Where(s => !usados.Contains(s.Codigo))
            .Select(s => s.Codigo)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        resultado.AddRange(agregadas);
        return resultado;
    }

    /// <summary>Limpia letra pegada desde una página web: acordes, marcas de repetición, espacios y líneas vacías sobrantes.</summary>
    public static string LimpiarTextoPegado(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var lineas = texto.Replace("\r\n", "\n").Replace('\r', '\n').Replace(' ', ' ').Replace('\t', ' ').Split('\n');
        var salida = new List<string>();
        foreach (var original in lineas)
        {
            var linea = EspaciosRegex().Replace(original, " ").Trim();
            if (EsLineaDeAcordes(linea)) continue;
            if (RepeticionSolaRegex().IsMatch(linea)) continue;
            linea = RepeticionFinalRegex().Replace(linea, "").Trim();
            if (linea.Length == 0)
            {
                if (salida.Count > 0 && salida[^1].Length > 0) salida.Add("");
                continue;
            }
            salida.Add(linea);
        }
        while (salida.Count > 0 && salida[^1].Length == 0) salida.RemoveAt(salida.Count - 1);
        return string.Join("\n", salida);
    }

    public static bool EsLineaDeAcordes(string linea)
    {
        var tokens = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(t => AcordeRegex().IsMatch(t) || t == "|" || t == "/");
    }

    private static string LimpiarCuerpo(List<string> lineas)
    {
        var inicio = 0;
        var fin = lineas.Count - 1;
        while (inicio <= fin && string.IsNullOrWhiteSpace(lineas[inicio])) inicio++;
        while (fin >= inicio && string.IsNullOrWhiteSpace(lineas[fin])) fin--;
        if (inicio > fin) return "";
        var sb = new StringBuilder();
        var blancoPrevio = false;
        for (var i = inicio; i <= fin; i++)
        {
            var linea = lineas[i].Trim();
            if (linea.Length == 0)
            {
                blancoPrevio = true;
                continue;
            }
            if (sb.Length > 0) sb.Append(blancoPrevio ? "\n\n" : "\n");
            sb.Append(linea);
            blancoPrevio = false;
        }
        return sb.ToString();
    }

    private static bool MismoTexto(string a, string b) => Normalizador.Normalizar(a) == Normalizador.Normalizar(b);

    private static int SiguienteNumero(IEnumerable<Seccion> secciones, TipoSeccion tipo)
    {
        var numeros = secciones.Where(s => s.Tipo == tipo).Select(s => Math.Max(s.Numero, 1)).ToList();
        return numeros.Count == 0 ? 1 : numeros.Max() + 1;
    }

    [GeneratedRegex(@"^[A-G](?:#|b)?(?:m|maj|min|sus|dim|aug|add|M)?\d*(?:sus\d*|add\d*|maj\d*|b\d+|#\d+)*(?:/[A-G](?:#|b)?)?$")]
    private static partial Regex AcordeRegex();

    [GeneratedRegex(@"^\(?\s*(?:x\s*\d+|\d+\s*x|bis|2\s*veces|repetir)\s*\)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RepeticionSolaRegex();

    [GeneratedRegex(@"\s*\(\s*(?:x\s*\d+|\d+\s*x|bis)\s*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RepeticionFinalRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspaciosRegex();
}
