using System.Text.Json;

namespace Coraza.Core.Actualizacion;

/// <summary>Una versión publicada que se puede instalar.</summary>
/// <param name="Etiqueta">La etiqueta de la Release tal cual («v1.1»).</param>
/// <param name="Nombre">Nombre legible de la versión, para mostrárselo al operador.</param>
/// <param name="Url">Descarga directa del instalador.</param>
/// <param name="Tamano">Tamaño en bytes que declara el servidor; sirve para comprobar la descarga.</param>
public sealed record InfoActualizacion(string Etiqueta, string Nombre, string Url, long Tamano);

/// <summary>
/// Decide si lo publicado es más nuevo que lo instalado, y saca del JSON de la Release
/// la dirección del instalador.
///
/// Está aquí, fuera de la capa de interfaz, para poder probarlo: comparar versiones
/// tiene más esquinas de las que parece (etiquetas con «v», con distinto número de
/// partes, o iguales), y equivocarse significa o no avisar nunca, o avisar siempre.
/// </summary>
public static class ComprobadorVersiones
{
    /// <summary>
    /// Convierte «v1.2.3», «1.2» o «V1.2.3.4» en un número de versión. Devuelve
    /// <c>null</c> si no se parece a una versión, que es lo que se hace con las
    /// etiquetas raras: ignorarlas en vez de inventar una comparación.
    /// </summary>
    public static Version? Interpretar(string? etiqueta)
    {
        if (string.IsNullOrWhiteSpace(etiqueta)) return null;
        var limpia = etiqueta.Trim().TrimStart('v', 'V');

        // Se corta en el primer sufijo (1.2.3-beta): comparar preestrenos es otra
        // historia y aquí no hace falta.
        var guion = limpia.IndexOfAny(new[] { '-', '+' });
        if (guion > 0) limpia = limpia[..guion];

        return Version.TryParse(limpia, out var v) ? Normalizar(v) : null;
    }

    /// <summary>
    /// ¿La etiqueta publicada es más nueva que la instalada? Ante cualquier duda
    /// devuelve <c>false</c>: es preferible no avisar que dar la lata con un aviso
    /// falso que el operador no puede quitarse de encima.
    /// </summary>
    public static bool EsMasNueva(string? etiquetaPublicada, Version? instalada)
    {
        if (instalada is null) return false;
        var publicada = Interpretar(etiquetaPublicada);
        return publicada is not null && publicada > Normalizar(instalada);
    }

    /// <summary>
    /// Saca de la respuesta de GitHub la etiqueta y el activo que se llama como el
    /// instalador. Si la Release no trae ese archivo se devuelve <c>null</c>: puede
    /// haber versiones publicadas solo como código fuente, y ahí no hay nada que ofrecer.
    /// </summary>
    public static InfoActualizacion? LeerRelease(string json, string nombreInstalador)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;

            if (raiz.ValueKind != JsonValueKind.Object) return null;
            if (!raiz.TryGetProperty("tag_name", out var etiqueta)) return null;
            if (raiz.TryGetProperty("draft", out var borrador) && borrador.ValueKind == JsonValueKind.True) return null;
            if (raiz.TryGetProperty("prerelease", out var previa) && previa.ValueKind == JsonValueKind.True) return null;
            if (!raiz.TryGetProperty("assets", out var activos) || activos.ValueKind != JsonValueKind.Array) return null;

            foreach (var activo in activos.EnumerateArray())
            {
                if (!activo.TryGetProperty("name", out var nombre)) continue;
                if (!string.Equals(nombre.GetString(), nombreInstalador, StringComparison.OrdinalIgnoreCase)) continue;
                if (!activo.TryGetProperty("browser_download_url", out var url)) continue;

                var tamano = activo.TryGetProperty("size", out var t) && t.TryGetInt64(out var bytes) ? bytes : 0;
                var titulo = raiz.TryGetProperty("name", out var n) ? n.GetString() : null;

                return new InfoActualizacion(
                    Etiqueta: etiqueta.GetString() ?? "",
                    Nombre: string.IsNullOrWhiteSpace(titulo) ? etiqueta.GetString() ?? "" : titulo!,
                    Url: url.GetString() ?? "",
                    Tamano: tamano);
            }
            return null;
        }
        catch (JsonException)
        {
            // Una respuesta que no es JSON (un portal cautivo del wifi, por ejemplo)
            // no es un error del programa: simplemente no hay actualización que ofrecer.
            return null;
        }
    }

    /// <summary>Las partes sin definir valen 0, para que 1.1 y 1.1.0.0 sean iguales.</summary>
    private static Version Normalizar(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
