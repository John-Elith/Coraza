using Coraza.Core.Canciones;
using Coraza.Core.Modelos;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Coraza.Data.Repositorios;

public sealed class VersionAnterior
{
    public DateTime Fecha { get; set; }
    public string Titulo { get; set; } = "";
    public string Letra { get; set; } = "";
    public string? Orden { get; set; }

    public string Descripcion => $"{Fecha:dd/MM/yyyy HH:mm} · {Titulo}";
}

public sealed class RepositorioCanciones
{
    private const int VersionesConservadas = 20;

    private const string Columnas = """
        c.id, c.titulo, c.autor, c.tonalidad, c.tempo, c.ccli, c.derechos, c.idioma, c.categoria, c.etiquetas,
        c.tema_id, c.favorita, c.eliminada, c.fecha_creacion, c.fecha_modificacion, c.veces_usada, c.ultima_vez,
        (SELECT s.texto FROM secciones s WHERE s.cancion_id = c.id ORDER BY s.posicion LIMIT 1) AS primera_linea
        """;

    private readonly BaseDatos _db;

    public RepositorioCanciones(BaseDatos db) => _db = db;

    public List<Cancion> Listar(bool eliminadas = false)
    {
        using var c = _db.Abrir();
        var lista = c.Query<Cancion>(
            $"SELECT {Columnas} FROM canciones c WHERE c.eliminada = @e ORDER BY c.titulo COLLATE NOCASE",
            new { e = eliminadas ? 1 : 0 }).ToList();
        foreach (var cancion in lista) cancion.PrimeraLinea = PrimeraLinea(cancion.PrimeraLinea);
        return lista;
    }

    /// <summary>Título y letra completa de cada canción activa, para el índice de búsqueda.</summary>
    public List<(long Id, string Titulo, string Letra)> TextosParaIndice()
    {
        using var c = _db.Abrir();
        return c.Query<(long, string, string)>("""
            SELECT c.id, c.titulo, COALESCE(group_concat(s.texto, char(10)), '')
            FROM canciones c LEFT JOIN secciones s ON s.cancion_id = c.id
            WHERE c.eliminada = 0
            GROUP BY c.id
            """).ToList();
    }

    public Cancion? Obtener(long id)
    {
        using var c = _db.Abrir();
        return Obtener(c, null, id);
    }

    private static Cancion? Obtener(SqliteConnection c, SqliteTransaction? tx, long id)
    {
        var cancion = c.QuerySingleOrDefault<Cancion>($"SELECT {Columnas} FROM canciones c WHERE c.id = @id", new { id }, tx);
        if (cancion is null) return null;
        cancion.PrimeraLinea = PrimeraLinea(cancion.PrimeraLinea);
        cancion.Secciones = c.Query<Seccion>(
            "SELECT id, cancion_id, tipo, numero, texto, acordes, texto_segundo_idioma, posicion FROM secciones WHERE cancion_id = @id ORDER BY posicion",
            new { id }, tx).ToList();
        var porId = cancion.Secciones.ToDictionary(s => s.Id);
        cancion.Orden = c.Query<long>("SELECT seccion_id FROM orden_cancion WHERE cancion_id = @id ORDER BY posicion", new { id }, tx)
            .Where(porId.ContainsKey)
            .Select(sid => porId[sid].Codigo)
            .ToList();
        return cancion;
    }

    public long Guardar(Cancion cancion)
    {
        using var c = _db.Abrir();
        using var tx = c.BeginTransaction();
        var ahora = DateTime.Now;
        cancion.FechaModificacion = ahora;

        var parametros = new
        {
            cancion.Id, cancion.Titulo, cancion.Autor, cancion.Tonalidad, cancion.Tempo, cancion.Ccli, cancion.Derechos,
            cancion.Idioma, cancion.Categoria, cancion.Etiquetas, cancion.TemaId,
            Favorita = cancion.Favorita ? 1 : 0, FechaCreacion = cancion.Id == 0 ? ahora : cancion.FechaCreacion,
            FechaModificacion = ahora,
        };

        if (cancion.Id == 0)
        {
            cancion.FechaCreacion = ahora;
            cancion.Id = c.ExecuteScalar<long>("""
                INSERT INTO canciones (titulo, autor, tonalidad, tempo, ccli, derechos, idioma, categoria, etiquetas, tema_id,
                                       favorita, fecha_creacion, fecha_modificacion)
                VALUES (@Titulo, @Autor, @Tonalidad, @Tempo, @Ccli, @Derechos, @Idioma, @Categoria, @Etiquetas, @TemaId,
                        @Favorita, @FechaCreacion, @FechaModificacion);
                SELECT last_insert_rowid();
                """, parametros, tx);
        }
        else
        {
            GuardarVersionAnterior(c, tx, cancion.Id);
            c.Execute("""
                UPDATE canciones SET titulo = @Titulo, autor = @Autor, tonalidad = @Tonalidad, tempo = @Tempo, ccli = @Ccli,
                    derechos = @Derechos, idioma = @Idioma, categoria = @Categoria, etiquetas = @Etiquetas, tema_id = @TemaId,
                    favorita = @Favorita, fecha_modificacion = @FechaModificacion
                WHERE id = @Id
                """, parametros, tx);
            c.Execute("DELETE FROM orden_cancion WHERE cancion_id = @Id; DELETE FROM secciones WHERE cancion_id = @Id;", new { cancion.Id }, tx);
        }

        var porCodigo = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cancion.Secciones.Count; i++)
        {
            var s = cancion.Secciones[i];
            s.CancionId = cancion.Id;
            s.Posicion = i;
            s.Id = c.ExecuteScalar<long>("""
                INSERT INTO secciones (cancion_id, tipo, numero, texto, acordes, texto_segundo_idioma, posicion)
                VALUES (@CancionId, @Tipo, @Numero, @Texto, @Acordes, @TextoSegundoIdioma, @Posicion);
                SELECT last_insert_rowid();
                """, new { s.CancionId, Tipo = s.Tipo.ToString(), s.Numero, s.Texto, s.Acordes, s.TextoSegundoIdioma, s.Posicion }, tx);
            porCodigo.TryAdd(s.Codigo, s.Id);
        }

        var posicion = 0;
        foreach (var codigo in cancion.Orden)
        {
            if (!porCodigo.TryGetValue(codigo, out var seccionId)) continue;
            c.Execute("INSERT INTO orden_cancion (cancion_id, posicion, seccion_id) VALUES (@id, @posicion, @seccionId)",
                new { id = cancion.Id, posicion = posicion++, seccionId }, tx);
        }

        tx.Commit();
        return cancion.Id;
    }

    private static void GuardarVersionAnterior(SqliteConnection c, SqliteTransaction tx, long id)
    {
        var anterior = Obtener(c, tx, id);
        if (anterior is null) return;
        c.Execute("INSERT INTO canciones_historial (cancion_id, fecha, titulo, letra, orden) VALUES (@id, @fecha, @titulo, @letra, @orden)",
            new
            {
                id,
                fecha = DateTime.Now,
                titulo = anterior.Titulo,
                letra = AnalizadorLetra.Formatear(anterior.Secciones),
                orden = string.Join(" ", anterior.Orden),
            }, tx);
        c.Execute("""
            DELETE FROM canciones_historial WHERE cancion_id = @id AND id NOT IN
                (SELECT id FROM canciones_historial WHERE cancion_id = @id ORDER BY id DESC LIMIT @n)
            """, new { id, n = VersionesConservadas }, tx);
    }

    public List<VersionAnterior> VersionesAnteriores(long id)
    {
        using var c = _db.Abrir();
        return c.Query<VersionAnterior>(
            "SELECT fecha, titulo, letra, orden FROM canciones_historial WHERE cancion_id = @id ORDER BY id DESC",
            new { id }).ToList();
    }

    public void MarcarEliminada(long id, bool eliminada)
    {
        using var c = _db.Abrir();
        c.Execute("UPDATE canciones SET eliminada = @e WHERE id = @id", new { id, e = eliminada ? 1 : 0 });
    }

    public void EliminarDefinitivamente(long id)
    {
        using var c = _db.Abrir();
        c.Execute("DELETE FROM canciones WHERE id = @id", new { id });
    }

    public void EstablecerFavorita(long id, bool favorita)
    {
        using var c = _db.Abrir();
        c.Execute("UPDATE canciones SET favorita = @f WHERE id = @id", new { id, f = favorita ? 1 : 0 });
    }

    public void RegistrarUso(long id)
    {
        using var c = _db.Abrir();
        c.Execute("UPDATE canciones SET veces_usada = veces_usada + 1, ultima_vez = @ahora WHERE id = @id", new { id, ahora = DateTime.Now });
    }

    public long Duplicar(long id)
    {
        var original = Obtener(id) ?? throw new InvalidOperationException("La canción no existe.");
        original.Id = 0;
        original.Titulo += " (copia)";
        original.VecesUsada = 0;
        foreach (var s in original.Secciones) s.Id = 0;
        return Guardar(original);
    }

    public int Contar()
    {
        using var c = _db.Abrir();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM canciones WHERE eliminada = 0");
    }

    private static string? PrimeraLinea(string? texto) =>
        texto?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
}
