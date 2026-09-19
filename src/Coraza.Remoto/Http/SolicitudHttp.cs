using System.Text;

namespace Coraza.Remoto.Http;

/// <summary>
/// Una petición HTTP ya analizada.
///
/// Esto NO es un servidor HTTP de propósito general, y no debe convertirse en uno.
/// Es un analizador deliberadamente estrecho para las seis rutas de Coraza: si algo
/// no encaja en el mundo cerrado que describen los límites de <see cref="Limites"/>,
/// se responde con un código de error y se cierra. Esa estrechez es lo que lo hace
/// verificable con pruebas en vez de una fuente de sorpresas.
/// </summary>
public sealed record SolicitudHttp(
    string Metodo,
    string Ruta,
    string Consulta,
    IReadOnlyDictionary<string, string> Cabeceras,
    string Cuerpo)
{
    /// <summary>Valor de una cabecera, sin distinguir mayúsculas. <c>null</c> si no vino.</summary>
    public string? Cabecera(string nombre) =>
        Cabeceras.TryGetValue(nombre, out var valor) ? valor : null;

    /// <summary>Valor de un parámetro de la cadena de consulta. <c>null</c> si no vino.</summary>
    public string? Parametro(string nombre)
    {
        if (Consulta.Length == 0) return null;
        foreach (var par in Consulta.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var igual = par.IndexOf('=');
            var clave = igual < 0 ? par : par[..igual];
            if (!string.Equals(clave, nombre, StringComparison.Ordinal)) continue;
            return igual < 0 ? "" : Decodificar(par[(igual + 1)..]);
        }
        return null;
    }

    private static string Decodificar(string texto)
    {
        if (texto.IndexOf('%') < 0 && texto.IndexOf('+') < 0) return texto;
        try { return Uri.UnescapeDataString(texto.Replace('+', ' ')); }
        catch (UriFormatException) { return texto; }
    }

    /// <summary>Topes duros. Cualquier petición que los supere se rechaza sin leerla entera.</summary>
    public static class Limites
    {
        public const int LineaPeticion = 2 * 1024;
        public const int BloqueCabeceras = 8 * 1024;
        public const int NumeroCabeceras = 40;
        public const int Cuerpo = 64 * 1024;
    }

    /// <summary>
    /// Resultado del análisis: o una petición válida, o el código HTTP con que hay que
    /// responder. Nunca una excepción: un cliente mal educado no es un error del programa.
    /// </summary>
    public readonly record struct Analisis(SolicitudHttp? Solicitud, int CodigoError)
    {
        public bool Ok => Solicitud is not null;

        public static Analisis Correcto(SolicitudHttp s) => new(s, 0);

        public static Analisis Error(int codigo) => new(null, codigo);
    }

    /// <summary>
    /// Lee y analiza una petición del flujo. Devuelve <see cref="Analisis"/> con el código
    /// de error correspondiente en vez de lanzar. Un cero en <c>CodigoError</c> con
    /// <c>Solicitud</c> nula significa que el cliente cerró sin enviar nada.
    /// </summary>
    public static async Task<Analisis> LeerAsync(Stream flujo, CancellationToken ct = default)
    {
        var (cabeceraCruda, sobrante, corte) = await LeerBloqueCabecerasAsync(flujo, ct);
        if (corte != 0) return Analisis.Error(corte);
        if (cabeceraCruda is null) return new Analisis(null, 0);   // cierre limpio del cliente

        var lineas = cabeceraCruda.Split('\n');
        var peticion = lineas[0].TrimEnd('\r');
        if (peticion.Length > Limites.LineaPeticion) return Analisis.Error(414);

        var partes = peticion.Split(' ');
        if (partes.Length != 3) return Analisis.Error(400);
        var metodo = partes[0];
        if (metodo is not ("GET" or "POST" or "DELETE")) return Analisis.Error(405);
        if (!partes[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return Analisis.Error(505);

        var (ruta, consulta) = PartirObjetivo(partes[1]);
        if (ruta is null) return Analisis.Error(400);

        var cabeceras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lineas.Length; i++)
        {
            var linea = lineas[i].TrimEnd('\r');
            if (linea.Length == 0) continue;
            var dosPuntos = linea.IndexOf(':');
            if (dosPuntos <= 0) return Analisis.Error(400);
            if (cabeceras.Count >= Limites.NumeroCabeceras) return Analisis.Error(431);
            cabeceras[linea[..dosPuntos].Trim()] = linea[(dosPuntos + 1)..].Trim();
        }

        // Solo cuerpos con Content-Length. El troceado exigiría un analizador entero
        // más, y nuestro propio fetch() siempre manda la longitud.
        if (cabeceras.ContainsKey("Transfer-Encoding")) return Analisis.Error(400);

        var cuerpo = "";
        if (cabeceras.TryGetValue("Content-Length", out var textoLargo))
        {
            if (!int.TryParse(textoLargo, out var largo) || largo < 0) return Analisis.Error(400);
            if (largo > Limites.Cuerpo) return Analisis.Error(413);
            var bytes = await LeerCuerpoAsync(flujo, sobrante, largo, ct);
            if (bytes is null) return Analisis.Error(400);   // se cortó antes de completar
            // Se decodifica el arreglo completo de una vez: así un carácter multibyte
            // partido entre dos lecturas del socket no se corrompe.
            cuerpo = Encoding.UTF8.GetString(bytes);
        }

        return Analisis.Correcto(new SolicitudHttp(metodo, ruta, consulta, cabeceras, cuerpo));
    }

    /// <summary>Separa la ruta de la cadena de consulta y acepta también la forma absoluta.</summary>
    private static (string? Ruta, string Consulta) PartirObjetivo(string objetivo)
    {
        if (objetivo.Length == 0) return (null, "");

        // Forma absoluta: «GET http://equipo:8787/ruta HTTP/1.1» es legal y algunos
        // clientes la usan; nos quedamos con lo que va desde la primera barra del camino.
        if (objetivo.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            objetivo.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var trasEsquema = objetivo.IndexOf("//", StringComparison.Ordinal) + 2;
            var barra = objetivo.IndexOf('/', trasEsquema);
            objetivo = barra < 0 ? "/" : objetivo[barra..];
        }

        if (objetivo[0] != '/') return (null, "");

        var interrogante = objetivo.IndexOf('?');
        var ruta = interrogante < 0 ? objetivo : objetivo[..interrogante];
        var consulta = interrogante < 0 ? "" : objetivo[(interrogante + 1)..];

        try { ruta = Uri.UnescapeDataString(ruta); }
        catch (UriFormatException) { return (null, ""); }

        // Nada de subir de directorio: no servimos archivos del disco, pero más vale
        // que una ruta con «..» no llegue nunca al enrutador.
        if (ruta.Contains("..", StringComparison.Ordinal)) return (null, "");

        return (ruta, consulta);
    }

    /// <summary>
    /// Lee hasta el fin del bloque de cabeceras. Acepta tanto CRLF como LF suelto,
    /// porque un cliente de pruebas escrito a mano suele mandar solo LF.
    /// </summary>
    private static async Task<(string? Bloque, byte[] Sobrante, int Error)> LeerBloqueCabecerasAsync(
        Stream flujo, CancellationToken ct)
    {
        var acumulado = new MemoryStream();
        var buzon = new byte[1024];

        while (true)
        {
            var leidos = await flujo.ReadAsync(buzon, ct);
            if (leidos == 0)
                return acumulado.Length == 0
                    ? (null, Array.Empty<byte>(), 0)          // cierre limpio
                    : (null, Array.Empty<byte>(), 400);       // se cortó a media cabecera

            acumulado.Write(buzon, 0, leidos);
            if (acumulado.Length > Limites.BloqueCabeceras)
                return (null, Array.Empty<byte>(), 431);

            var datos = acumulado.GetBuffer();
            var largo = (int)acumulado.Length;
            var fin = BuscarFinCabeceras(datos, largo);
            if (fin < 0) continue;

            var bloque = Encoding.UTF8.GetString(datos, 0, fin);
            var sobrante = new byte[largo - fin];
            Array.Copy(datos, fin, sobrante, 0, sobrante.Length);
            return (bloque, sobrante, 0);
        }
    }

    /// <summary>Posición del primer byte del cuerpo, o -1 si el bloque aún no terminó.</summary>
    private static int BuscarFinCabeceras(byte[] datos, int largo)
    {
        for (var i = 0; i + 1 < largo; i++)
        {
            if (datos[i] != (byte)'\n') continue;
            if (datos[i + 1] == (byte)'\n') return i + 2;                       // LF LF
            if (i + 2 < largo && datos[i + 1] == (byte)'\r' && datos[i + 2] == (byte)'\n')
                return i + 3;                                                   // LF CRLF
        }
        return -1;
    }

    private static async Task<byte[]?> LeerCuerpoAsync(Stream flujo, byte[] sobrante, int largo, CancellationToken ct)
    {
        var cuerpo = new byte[largo];
        var copiado = Math.Min(sobrante.Length, largo);
        Array.Copy(sobrante, cuerpo, copiado);

        while (copiado < largo)
        {
            var leidos = await flujo.ReadAsync(cuerpo.AsMemory(copiado, largo - copiado), ct);
            if (leidos == 0) return null;   // el cliente prometió más de lo que envió
            copiado += leidos;
        }
        return cuerpo;
    }
}
