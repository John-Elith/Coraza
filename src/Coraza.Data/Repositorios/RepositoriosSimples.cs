using System.Text.Json;
using Coraza.Core.Modelos;
using Coraza.Core.Temas;
using Dapper;

namespace Coraza.Data.Repositorios;

public sealed class RepositorioMedios
{
    private readonly BaseDatos _db;

    public RepositorioMedios(BaseDatos db) => _db = db;

    public List<Medio> Listar()
    {
        using var c = _db.Abrir();
        return c.Query<Medio>("SELECT id, tipo, ruta, nombre, duracion, ancho, alto, etiquetas, ajuste, fecha_creacion FROM medios ORDER BY id DESC").ToList();
    }

    public Medio? Obtener(long id)
    {
        using var c = _db.Abrir();
        return c.QuerySingleOrDefault<Medio>(
            "SELECT id, tipo, ruta, nombre, duracion, ancho, alto, etiquetas, ajuste, fecha_creacion FROM medios WHERE id = @id", new { id });
    }

    public long Agregar(Medio m)
    {
        m.FechaCreacion = DateTime.Now;
        using var c = _db.Abrir();
        m.Id = c.ExecuteScalar<long>("""
            INSERT INTO medios (tipo, ruta, nombre, duracion, ancho, alto, etiquetas, ajuste, fecha_creacion)
            VALUES (@Tipo, @Ruta, @Nombre, @Duracion, @Ancho, @Alto, @Etiquetas, @Ajuste, @FechaCreacion);
            SELECT last_insert_rowid();
            """, new { Tipo = m.Tipo.ToString(), m.Ruta, m.Nombre, m.Duracion, m.Ancho, m.Alto, m.Etiquetas, Ajuste = m.Ajuste.ToString(), m.FechaCreacion });
        return m.Id;
    }

    public void Actualizar(Medio m)
    {
        using var c = _db.Abrir();
        c.Execute("UPDATE medios SET nombre = @Nombre, etiquetas = @Etiquetas, ajuste = @Ajuste WHERE id = @Id",
            new { m.Id, m.Nombre, m.Etiquetas, Ajuste = m.Ajuste.ToString() });
    }

    public void Eliminar(long id)
    {
        using var c = _db.Abrir();
        c.Execute("DELETE FROM medios WHERE id = @id", new { id });
    }
}

public sealed class RepositorioTextos
{
    private readonly BaseDatos _db;

    public RepositorioTextos(BaseDatos db) => _db = db;

    public List<TextoLibre> Listar()
    {
        using var c = _db.Abrir();
        return c.Query<TextoLibre>("SELECT id, titulo, contenido, tema_id, fecha_modificacion FROM textos ORDER BY titulo COLLATE NOCASE").ToList();
    }

    public TextoLibre? Obtener(long id)
    {
        using var c = _db.Abrir();
        return c.QuerySingleOrDefault<TextoLibre>("SELECT id, titulo, contenido, tema_id, fecha_modificacion FROM textos WHERE id = @id", new { id });
    }

    public long Guardar(TextoLibre t)
    {
        t.FechaModificacion = DateTime.Now;
        using var c = _db.Abrir();
        if (t.Id == 0)
        {
            t.Id = c.ExecuteScalar<long>("""
                INSERT INTO textos (titulo, contenido, tema_id, fecha_modificacion) VALUES (@Titulo, @Contenido, @TemaId, @FechaModificacion);
                SELECT last_insert_rowid();
                """, t);
        }
        else
        {
            c.Execute("UPDATE textos SET titulo = @Titulo, contenido = @Contenido, tema_id = @TemaId, fecha_modificacion = @FechaModificacion WHERE id = @Id", t);
        }
        return t.Id;
    }

    public void Eliminar(long id)
    {
        using var c = _db.Abrir();
        c.Execute("DELETE FROM textos WHERE id = @id", new { id });
    }
}

public sealed class RepositorioTemas
{
    private readonly BaseDatos _db;

    public RepositorioTemas(BaseDatos db) => _db = db;

    private sealed record FilaTema(long Id, string Nombre, string Datos, long Predefinido);

    public List<Tema> Listar()
    {
        using var c = _db.Abrir();
        return c.Query<FilaTema>("SELECT id, nombre, datos, predefinido FROM temas ORDER BY predefinido DESC, nombre")
            .Select(ATema).ToList();
    }

    public Tema? Obtener(long id)
    {
        using var c = _db.Abrir();
        var fila = c.QuerySingleOrDefault<FilaTema>("SELECT id, nombre, datos, predefinido FROM temas WHERE id = @id", new { id });
        return fila is null ? null : ATema(fila);
    }

    public long Guardar(Tema t)
    {
        using var c = _db.Abrir();
        var datos = t.AJson();
        if (t.Id == 0)
        {
            t.Id = c.ExecuteScalar<long>("INSERT INTO temas (nombre, datos, predefinido) VALUES (@nombre, @datos, @p); SELECT last_insert_rowid();",
                new { nombre = t.Nombre, datos, p = t.Predefinido ? 1 : 0 });
        }
        else
        {
            c.Execute("UPDATE temas SET nombre = @nombre, datos = @datos WHERE id = @id", new { id = t.Id, nombre = t.Nombre, datos });
        }
        return t.Id;
    }

    public void Eliminar(long id)
    {
        using var c = _db.Abrir();
        c.Execute("DELETE FROM temas WHERE id = @id AND predefinido = 0", new { id });
    }

    /// <summary>Crea los temas incluidos que falten (así las nuevas versiones agregan sus temas a bibliotecas existentes).</summary>
    public void AsegurarPredefinidos()
    {
        HashSet<string> existentes;
        using (var c = _db.Abrir())
        {
            existentes = c.Query<string>("SELECT nombre FROM temas WHERE predefinido = 1").ToHashSet();
        }
        foreach (var tema in TemasPredefinidos.Crear().Where(t => !existentes.Contains(t.Nombre))) Guardar(tema);
    }

    private static Tema ATema(FilaTema fila)
    {
        Tema tema;
        try { tema = Tema.DesdeJson(fila.Datos); }
        catch (JsonException) { tema = new Tema(); }
        tema.Id = fila.Id;
        tema.Nombre = fila.Nombre;
        tema.Predefinido = fila.Predefinido != 0;
        return tema;
    }
}

public sealed class RepositorioServicios
{
    private readonly BaseDatos _db;

    public RepositorioServicios(BaseDatos db) => _db = db;

    public List<Servicio> Listar()
    {
        using var c = _db.Abrir();
        return c.Query<Servicio>("SELECT id, nombre, fecha, plantilla, notas, modificado FROM servicios ORDER BY fecha DESC, modificado DESC").ToList();
    }

    public Servicio? Obtener(long id)
    {
        using var c = _db.Abrir();
        var s = c.QuerySingleOrDefault<Servicio>("SELECT id, nombre, fecha, plantilla, notas, modificado FROM servicios WHERE id = @id", new { id });
        if (s is null) return null;
        s.Elementos = c.Query<ElementoServicio>("""
            SELECT id, servicio_id, posicion, tipo, referencia_id, titulo, datos, tema_id, notas, duracion_min, color,
                   avance_seg AS avance_segundos, bucle
            FROM elementos_servicio WHERE servicio_id = @id ORDER BY posicion
            """, new { id }).ToList();
        return s;
    }

    public long Crear(Servicio s)
    {
        s.Modificado = DateTime.Now;
        using var c = _db.Abrir();
        s.Id = c.ExecuteScalar<long>("""
            INSERT INTO servicios (nombre, fecha, plantilla, notas, modificado) VALUES (@Nombre, @Fecha, @Plantilla, @Notas, @Modificado);
            SELECT last_insert_rowid();
            """, s);
        if (s.Elementos.Count > 0) GuardarElementos(s.Id, s.Elementos);
        return s.Id;
    }

    public void ActualizarCabecera(Servicio s)
    {
        s.Modificado = DateTime.Now;
        using var c = _db.Abrir();
        c.Execute("UPDATE servicios SET nombre = @Nombre, fecha = @Fecha, plantilla = @Plantilla, notas = @Notas, modificado = @Modificado WHERE id = @Id", s);
    }

    /// <summary>Reemplaza todos los elementos del servicio (autoguardado tras cada cambio).</summary>
    public void GuardarElementos(long servicioId, IReadOnlyList<ElementoServicio> elementos)
    {
        using var c = _db.Abrir();
        using var tx = c.BeginTransaction();
        c.Execute("DELETE FROM elementos_servicio WHERE servicio_id = @servicioId", new { servicioId }, tx);
        for (var i = 0; i < elementos.Count; i++)
        {
            var e = elementos[i];
            e.ServicioId = servicioId;
            e.Posicion = i;
            e.Id = c.ExecuteScalar<long>("""
                INSERT INTO elementos_servicio (servicio_id, posicion, tipo, referencia_id, titulo, datos, tema_id, notas, duracion_min, color, avance_seg, bucle)
                VALUES (@ServicioId, @Posicion, @Tipo, @ReferenciaId, @Titulo, @Datos, @TemaId, @Notas, @DuracionMin, @Color, @AvanceSegundos, @Bucle);
                SELECT last_insert_rowid();
                """, new
                {
                    e.ServicioId, e.Posicion, Tipo = e.Tipo.ToString(), e.ReferenciaId, e.Titulo, e.Datos, e.TemaId, e.Notas, e.DuracionMin, e.Color,
                    e.AvanceSegundos, Bucle = e.Bucle ? 1 : 0,
                }, tx);
        }
        c.Execute("UPDATE servicios SET modificado = @ahora WHERE id = @servicioId", new { servicioId, ahora = DateTime.Now }, tx);
        tx.Commit();
    }

    public long Duplicar(long id, string nuevoNombre)
    {
        var original = Obtener(id) ?? throw new InvalidOperationException("El servicio no existe.");
        var copia = new Servicio
        {
            Nombre = nuevoNombre,
            Fecha = DateTime.Today,
            Plantilla = original.Plantilla,
            Notas = original.Notas,
            Elementos = original.Elementos.Select(e => { var x = e.Clonar(); x.Id = 0; return x; }).ToList(),
        };
        return Crear(copia);
    }

    public void Eliminar(long id)
    {
        using var c = _db.Abrir();
        c.Execute("DELETE FROM servicios WHERE id = @id", new { id });
    }
}

public sealed class RepositorioConfiguracion
{
    private static readonly JsonSerializerOptions OpcionesJson = new() { WriteIndented = false };
    private readonly BaseDatos _db;

    public RepositorioConfiguracion(BaseDatos db) => _db = db;

    public string? Obtener(string clave)
    {
        using var c = _db.Abrir();
        return c.ExecuteScalar<string?>("SELECT valor FROM configuracion WHERE clave = @clave", new { clave });
    }

    public void Guardar(string clave, string? valor)
    {
        using var c = _db.Abrir();
        c.Execute("INSERT INTO configuracion (clave, valor) VALUES (@clave, @valor) ON CONFLICT(clave) DO UPDATE SET valor = excluded.valor",
            new { clave, valor });
    }

    public T? ObtenerJson<T>(string clave)
    {
        var json = Obtener(clave);
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, OpcionesJson); }
        catch (JsonException) { return default; }
    }

    public void GuardarJson<T>(string clave, T valor) => Guardar(clave, JsonSerializer.Serialize(valor, OpcionesJson));
}

public sealed class RepositorioHistorial
{
    private readonly BaseDatos _db;

    public RepositorioHistorial(BaseDatos db) => _db = db;

    public void Registrar(string tipo, long? referenciaId, string descripcion)
    {
        using var c = _db.Abrir();
        c.Execute("INSERT INTO historial_uso (tipo, referencia_id, descripcion, fecha_hora) VALUES (@tipo, @referenciaId, @descripcion, @ahora)",
            new { tipo, referenciaId, descripcion, ahora = DateTime.Now });
    }
}
