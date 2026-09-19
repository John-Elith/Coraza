using System.IO;
using System.Text.Json;
using Coraza.Core.Diapositivas;
using Coraza.Core.Modelos;
using Coraza.Core.Temas;
using Serilog;

namespace Coraza.App.Servicios;

public sealed record ResultadoDiapositivas(IReadOnlyList<Diapositiva> Diapositivas, Tema Tema, string? Aviso = null);

/// <summary>Crea los elementos del servicio a partir de canciones, pasajes, medios y textos.</summary>
public static class FabricaElementos
{
    private sealed record DatosPasaje(int L, int C, int? Vi, int? Vf, int? Cf);

    public static ElementoServicio DeCancion(Cancion c) =>
        new() { Tipo = TipoElemento.Cancion, ReferenciaId = c.Id, Titulo = c.Titulo };

    public static ElementoServicio DePasaje(VersionBiblia version, ReferenciaBiblica r) => new()
    {
        Tipo = TipoElemento.Pasaje,
        ReferenciaId = version.Id,
        Titulo = $"{r} · {version.Abreviatura}",
        Datos = JsonSerializer.Serialize(new DatosPasaje(r.Libro, r.Capitulo, r.VersiculoInicio, r.VersiculoFin, r.CapituloFin)),
    };

    /// <summary>Las presentaciones de fotos avanzan solas cada 6 segundos y se repiten (se puede cambiar con clic derecho).</summary>
    public static ElementoServicio DeMedio(Medio m)
    {
        var presentacion = m.Etiquetas == Medio.EtiquetaPresentacion;
        return new()
        {
            Tipo = TipoElemento.Imagen,
            ReferenciaId = m.Id,
            Titulo = m.Nombre,
            AvanceSegundos = presentacion ? 6 : null,
            Bucle = presentacion,
        };
    }

    public static ElementoServicio DeTexto(TextoLibre t) =>
        new() { Tipo = TipoElemento.Texto, ReferenciaId = t.Id, Titulo = t.Titulo };

    public static ElementoServicio Encabezado(string titulo, string color = "#B44446") =>
        new() { Tipo = TipoElemento.Encabezado, Titulo = titulo, Color = color };

    public static ElementoServicio DeCuentaRegresiva(DatosCuentaRegresiva datos) => new()
    {
        Tipo = TipoElemento.CuentaRegresiva,
        Titulo = datos.Hora is { Length: > 0 } hora ? $"Cuenta regresiva hasta las {hora}" : $"Cuenta regresiva · {datos.Minutos} min",
        Datos = JsonSerializer.Serialize(datos),
    };

    public static DatosCuentaRegresiva LeerCuenta(string? datos)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(datos) && JsonSerializer.Deserialize<DatosCuentaRegresiva>(datos) is { } d) return d;
        }
        catch (JsonException)
        {
        }
        return new DatosCuentaRegresiva(5, null, "El servicio comienza en", "¡Bienvenidos!");
    }

    public static ReferenciaBiblica? LeerReferencia(string? datos)
    {
        if (string.IsNullOrWhiteSpace(datos)) return null;
        try
        {
            var d = JsonSerializer.Deserialize<DatosPasaje>(datos);
            return d is null ? null : new ReferenciaBiblica(d.L, d.C, d.Vi, d.Vf, d.Cf);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Genera las diapositivas de cualquier elemento y resuelve qué tema le corresponde.</summary>
public sealed class ProveedorDiapositivas
{
    private readonly Contexto _ctx;
    private List<Tema> _temas = new();

    public ProveedorDiapositivas(Contexto ctx)
    {
        _ctx = ctx;
        RecargarTemas();
    }

    public event EventHandler? TemasCambiaron;

    public IReadOnlyList<Tema> Temas => _temas;

    public void RecargarTemas()
    {
        _temas = _ctx.Temas.Listar();
        TemasCambiaron?.Invoke(this, EventArgs.Empty);
    }

    public static TipoContenido ContenidoDe(TipoElemento tipo) => tipo switch
    {
        TipoElemento.Cancion => TipoContenido.Canciones,
        TipoElemento.Pasaje => TipoContenido.Biblia,
        TipoElemento.Imagen => TipoContenido.Imagenes,
        _ => TipoContenido.Textos,
    };

    public Tema TemaPara(TipoContenido tipo)
    {
        if (_ctx.Preferencias.TemasPorContenido.TryGetValue(tipo, out var id) && _temas.FirstOrDefault(t => t.Id == id) is { } elegido)
            return elegido;
        var nombre = TemasPredefinidos.NombrePara(tipo);
        return _temas.FirstOrDefault(t => t.Nombre == nombre) ?? _temas.FirstOrDefault() ?? new Tema();
    }

    public Tema ObtenerTema(long? id, TipoContenido tipo) =>
        id is not null && _temas.FirstOrDefault(t => t.Id == id) is { } tema ? tema : TemaPara(tipo);

    public ResultadoDiapositivas Generar(ElementoServicio e)
    {
        var tipoContenido = ContenidoDe(e.Tipo);
        try
        {
            switch (e.Tipo)
            {
                case TipoElemento.Cancion:
                {
                    var cancion = _ctx.Canciones.Obtener(e.ReferenciaId ?? 0);
                    if (cancion is null) return Vacio(tipoContenido, e.TemaId, "La canción ya no existe en la biblioteca.");
                    var tema = ObtenerTema(e.TemaId ?? cancion.TemaId, tipoContenido);
                    return new(GeneradorDiapositivas.DeCancion(cancion, OpcionesDivision.DesdeTema(tema), _ctx.Preferencias.LicenciaCcli), tema);
                }
                case TipoElemento.Pasaje:
                {
                    var tema = ObtenerTema(e.TemaId, tipoContenido);
                    var referencia = FabricaElementos.LeerReferencia(e.Datos);
                    var versiones = _ctx.Biblias.Listar();
                    var version = versiones.FirstOrDefault(v => v.Id == e.ReferenciaId)
                                  ?? versiones.FirstOrDefault(v => v.Id == _ctx.Preferencias.BibliaPredeterminada)
                                  ?? versiones.FirstOrDefault();
                    if (referencia is null || version is null) return Vacio(tipoContenido, e.TemaId, "No hay una Biblia instalada para este pasaje.");
                    var versiculos = _ctx.Biblias.ObtenerPasaje(version.Id, referencia);
                    if (versiculos.Count == 0) return Vacio(tipoContenido, e.TemaId, $"No se encontró {referencia} en {version.Abreviatura}.");
                    return new(GeneradorDiapositivas.DePasaje(referencia, versiculos, version.Abreviatura, OpcionesDivision.DesdeTema(tema)), tema);
                }
                case TipoElemento.Imagen:
                {
                    var tema = ObtenerTema(e.TemaId, tipoContenido);
                    var medio = _ctx.Medios.Obtener(e.ReferenciaId ?? 0);
                    if (medio is null) return Vacio(tipoContenido, e.TemaId, "El archivo ya no existe en la biblioteca.");
                    var ruta = _ctx.Rutas.AAbsoluta(medio.Ruta);
                    switch (medio.Tipo)
                    {
                        case TipoMedio.Video:
                            return new(new[] { GeneradorDiapositivas.DeVideo(medio, ruta, e.Bucle) }, tema);
                        case TipoMedio.Documento:
                        {
                            var paginas = Medio.PaginasDe(ruta);
                            return paginas.Count == 0
                                ? Vacio(tipoContenido, e.TemaId, "No se encontraron las páginas de este documento.")
                                : new(GeneradorDiapositivas.DePaginas(medio, paginas), tema);
                        }
                        case TipoMedio.Audio:
                            return Vacio(tipoContenido, e.TemaId, "Los audios se reproducen como música de fondo: haz doble clic sobre ellos en la pestaña Medios.");
                        default:
                            return new(new[] { GeneradorDiapositivas.DeImagen(medio, ruta) }, tema);
                    }
                }
                case TipoElemento.Texto:
                {
                    var texto = _ctx.Textos.Obtener(e.ReferenciaId ?? 0);
                    if (texto is null) return Vacio(tipoContenido, e.TemaId, "El texto ya no existe.");
                    var tema = ObtenerTema(e.TemaId ?? texto.TemaId, tipoContenido);
                    return new(GeneradorDiapositivas.DeTexto(texto), tema);
                }
                case TipoElemento.CuentaRegresiva:
                {
                    var tema = ObtenerTema(e.TemaId, tipoContenido);
                    return new(new[] { GeneradorDiapositivas.DeCuentaRegresiva(FabricaElementos.LeerCuenta(e.Datos)) }, tema);
                }
                default:
                    return Vacio(tipoContenido, e.TemaId, null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error al generar las diapositivas de {Titulo}", e.Titulo);
            return Vacio(tipoContenido, e.TemaId, "No se pudieron preparar las diapositivas de este elemento.");
        }
    }

    private ResultadoDiapositivas Vacio(TipoContenido tipo, long? temaId, string? aviso) =>
        new(Array.Empty<Diapositiva>(), ObtenerTema(temaId, tipo), aviso);
}
