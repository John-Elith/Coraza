using System.Text;

namespace Coraza.Remoto.Http;

/// <summary>
/// Construye y escribe respuestas HTTP.
///
/// Dos reglas que se respetan en todo el archivo:
/// 1. <b>Nunca se emite una cabecera <c>Access-Control-Allow-*</c>.</b> Sin CORS, una
///    página de otro origen no puede leer nuestras respuestas ni mandar la cabecera
///    de sesión, así que el CSRF y el «DNS rebinding» quedan cerrados por construcción.
/// 2. <b>Un error nunca lleva detalles.</b> Se responde el código y un cuerpo vacío:
///    ni trazas, ni mensajes internos, ni pistas sobre qué hay detrás.
/// </summary>
public sealed class RespuestaHttp
{
    private static readonly byte[] FinLinea = "\r\n"u8.ToArray();

    public static async Task EscribirAsync(Stream flujo, int codigo, string tipo, byte[] cuerpo,
        CancellationToken ct = default, IReadOnlyDictionary<string, string>? extra = null)
    {
        var cabecera = new StringBuilder();
        cabecera.Append("HTTP/1.1 ").Append(codigo).Append(' ').Append(Razon(codigo)).Append("\r\n");
        cabecera.Append("Content-Type: ").Append(tipo).Append("\r\n");
        cabecera.Append("Content-Length: ").Append(cuerpo.Length).Append("\r\n");
        // La página y el estado cambian en cada servicio: que nada quede cacheado.
        cabecera.Append("Cache-Control: no-store\r\n");
        cabecera.Append("X-Content-Type-Options: nosniff\r\n");
        cabecera.Append("Connection: close\r\n");
        if (extra is not null)
            foreach (var (clave, valor) in extra)
                cabecera.Append(clave).Append(": ").Append(valor).Append("\r\n");
        cabecera.Append("\r\n");

        await flujo.WriteAsync(Encoding.UTF8.GetBytes(cabecera.ToString()), ct);
        if (cuerpo.Length > 0) await flujo.WriteAsync(cuerpo, ct);
        await flujo.FlushAsync(ct);
    }

    public static Task TextoAsync(Stream flujo, int codigo, string texto, CancellationToken ct = default) =>
        EscribirAsync(flujo, codigo, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(texto), ct);

    public static Task JsonAsync(Stream flujo, int codigo, string json, CancellationToken ct = default) =>
        EscribirAsync(flujo, codigo, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), ct);

    public static Task HtmlAsync(Stream flujo, string html, CancellationToken ct = default) =>
        EscribirAsync(flujo, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), ct);

    /// <summary>Error seco: sin cuerpo, sin explicación, sin filtraciones.</summary>
    public static Task ErrorAsync(Stream flujo, int codigo, CancellationToken ct = default) =>
        EscribirAsync(flujo, codigo, "text/plain; charset=utf-8", Array.Empty<byte>(), ct);

    // ---------- Server-Sent Events ----------

    /// <summary>
    /// Abre un flujo de eventos. A partir de aquí la conexión queda viva y solo se
    /// escriben eventos hasta que el cliente se va.
    /// </summary>
    public static async Task AbrirEventosAsync(Stream flujo, CancellationToken ct = default)
    {
        var cabecera =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/event-stream; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Connection: keep-alive\r\n" +
            "\r\n" +
            // Si se corta la red, el navegador reintenta solo pasado este tiempo.
            // Es lo que hace que «se cayó el wifi» se arregle sin tocar el teléfono.
            "retry: 3000\r\n\r\n";
        await flujo.WriteAsync(Encoding.UTF8.GetBytes(cabecera), ct);
        await flujo.FlushAsync(ct);
    }

    /// <summary>
    /// Envía un evento. Cada salto de línea del contenido se convierte en su propia
    /// línea <c>data:</c>, que es como exige el formato; si no, el evento se trunca.
    /// </summary>
    public static async Task EnviarEventoAsync(Stream flujo, string contenido, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        foreach (var linea in contenido.Split('\n'))
            sb.Append("data: ").Append(linea.TrimEnd('\r')).Append('\n');
        sb.Append('\n');
        await flujo.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()), ct);
        await flujo.FlushAsync(ct);
    }

    /// <summary>
    /// Latido. Es una línea de comentario del formato, que el navegador ignora: sirve
    /// para que una conexión muerta aflore y para que la radio del teléfono no cierre
    /// el socket por inactividad.
    /// </summary>
    public static async Task LatirAsync(Stream flujo, CancellationToken ct = default)
    {
        await flujo.WriteAsync(": ping\n\n"u8.ToArray(), ct);
        await flujo.FlushAsync(ct);
    }

    private static string Razon(int codigo) => codigo switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        413 => "Payload Too Large",
        414 => "URI Too Long",
        429 => "Too Many Requests",
        431 => "Request Header Fields Too Large",
        503 => "Service Unavailable",
        505 => "HTTP Version Not Supported",
        _ => "Error",
    };
}
