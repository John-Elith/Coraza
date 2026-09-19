using Coraza.Core.Busqueda;
using Coraza.Core.Modelos;
using Coraza.Data.Importacion;
using Dapper;

namespace Coraza.Data.Repositorios;

public sealed class RepositorioBiblias
{
    private const string ColumnasVersiculo = "v.id, v.biblia_id, v.libro, v.capitulo, v.versiculo AS numero, v.texto";

    private readonly BaseDatos _db;

    public RepositorioBiblias(BaseDatos db) => _db = db;

    public List<VersionBiblia> Listar()
    {
        using var c = _db.Abrir();
        return c.Query<VersionBiblia>("SELECT id, nombre, abreviatura, idioma, licencia FROM biblias ORDER BY nombre").ToList();
    }

    public bool ExisteAbreviatura(string abreviatura)
    {
        using var c = _db.Abrir();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM biblias WHERE abreviatura = @abreviatura COLLATE NOCASE", new { abreviatura }) > 0;
    }

    /// <summary>Crea la versión e inserta todos sus versículos en una sola transacción.</summary>
    public long Importar(VersionBiblia version, IEnumerable<VersiculoImportado> versiculos, IProgress<int>? progreso = null)
    {
        using var c = _db.Abrir();
        using var tx = c.BeginTransaction();
        version.Id = c.ExecuteScalar<long>("""
            INSERT INTO biblias (nombre, abreviatura, idioma, licencia) VALUES (@Nombre, @Abreviatura, @Idioma, @Licencia);
            SELECT last_insert_rowid();
            """, version, tx);

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO versiculos (biblia_id, libro, capitulo, versiculo, texto) VALUES ($b, $l, $c, $v, $t)";
        var pB = cmd.CreateParameter(); pB.ParameterName = "$b"; cmd.Parameters.Add(pB);
        var pL = cmd.CreateParameter(); pL.ParameterName = "$l"; cmd.Parameters.Add(pL);
        var pC = cmd.CreateParameter(); pC.ParameterName = "$c"; cmd.Parameters.Add(pC);
        var pV = cmd.CreateParameter(); pV.ParameterName = "$v"; cmd.Parameters.Add(pV);
        var pT = cmd.CreateParameter(); pT.ParameterName = "$t"; cmd.Parameters.Add(pT);
        cmd.Prepare();

        var cantidad = 0;
        foreach (var v in versiculos)
        {
            pB.Value = version.Id;
            pL.Value = v.Libro;
            pC.Value = v.Capitulo;
            pV.Value = v.Versiculo;
            pT.Value = v.Texto;
            cmd.ExecuteNonQuery();
            if (++cantidad % 2000 == 0) progreso?.Report(cantidad);
        }

        c.Execute("INSERT INTO versiculos_fts (rowid, texto) SELECT id, texto FROM versiculos WHERE biblia_id = @Id", new { version.Id }, tx);
        tx.Commit();
        progreso?.Report(cantidad);
        return version.Id;
    }

    public void Eliminar(long id)
    {
        using var c = _db.Abrir();
        using var tx = c.BeginTransaction();
        c.Execute("DELETE FROM versiculos WHERE biblia_id = @id; DELETE FROM biblias WHERE id = @id;", new { id }, tx);
        c.Execute("INSERT INTO versiculos_fts (versiculos_fts) VALUES ('rebuild');", transaction: tx);
        tx.Commit();
    }

    public List<Versiculo> ObtenerPasaje(long bibliaId, ReferenciaBiblica r)
    {
        using var c = _db.Abrir();
        if (r.VersiculoInicio is null)
        {
            return c.Query<Versiculo>(
                $"SELECT {ColumnasVersiculo} FROM versiculos v WHERE v.biblia_id = @bibliaId AND v.libro = @Libro AND v.capitulo = @Capitulo ORDER BY v.versiculo",
                new { bibliaId, r.Libro, r.Capitulo }).ToList();
        }
        if (r.CruzaCapitulos)
        {
            return c.Query<Versiculo>($"""
                SELECT {ColumnasVersiculo} FROM versiculos v
                WHERE v.biblia_id = @bibliaId AND v.libro = @Libro AND (
                    (v.capitulo = @Capitulo AND v.versiculo >= @VersiculoInicio) OR
                    (v.capitulo > @Capitulo AND v.capitulo < @CapituloFin) OR
                    (v.capitulo = @CapituloFin AND v.versiculo <= @VersiculoFin))
                ORDER BY v.capitulo, v.versiculo
                """, new { bibliaId, r.Libro, r.Capitulo, r.VersiculoInicio, r.CapituloFin, r.VersiculoFin }).ToList();
        }
        return c.Query<Versiculo>($"""
            SELECT {ColumnasVersiculo} FROM versiculos v
            WHERE v.biblia_id = @bibliaId AND v.libro = @Libro AND v.capitulo = @Capitulo AND v.versiculo BETWEEN @inicio AND @fin
            ORDER BY v.versiculo
            """, new { bibliaId, r.Libro, r.Capitulo, inicio = r.VersiculoInicio, fin = r.VersiculoFin ?? r.VersiculoInicio }).ToList();
    }

    public int CantidadCapitulos(long bibliaId, int libro)
    {
        using var c = _db.Abrir();
        return c.ExecuteScalar<int>("SELECT COALESCE(MAX(capitulo), 0) FROM versiculos WHERE biblia_id = @bibliaId AND libro = @libro", new { bibliaId, libro });
    }

    public int CantidadVersiculos(long bibliaId, int libro, int capitulo)
    {
        using var c = _db.Abrir();
        return c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(versiculo), 0) FROM versiculos WHERE biblia_id = @bibliaId AND libro = @libro AND capitulo = @capitulo",
            new { bibliaId, libro, capitulo });
    }

    public HashSet<int> LibrosDisponibles(long bibliaId)
    {
        using var c = _db.Abrir();
        return c.Query<int>("SELECT DISTINCT libro FROM versiculos WHERE biblia_id = @bibliaId", new { bibliaId }).ToHashSet();
    }

    /// <summary>Búsqueda por palabras en toda la Biblia (texto completo, sin importar tildes). Opcionalmente filtra por rango de libros.</summary>
    public List<Versiculo> Buscar(long bibliaId, string consulta, int? libroDesde = null, int? libroHasta = null, int limite = 300)
    {
        var palabras = Normalizador.Palabras(consulta);
        if (palabras.Length == 0) return new List<Versiculo>();
        var fts = string.Join(" AND ", palabras.Select(p => $"\"{p}\"*"));
        using var c = _db.Abrir();
        return c.Query<Versiculo>($"""
            SELECT {ColumnasVersiculo}
            FROM versiculos_fts f JOIN versiculos v ON v.id = f.rowid
            WHERE versiculos_fts MATCH @fts AND v.biblia_id = @bibliaId
              AND v.libro BETWEEN @desde AND @hasta
            ORDER BY v.libro, v.capitulo, v.versiculo
            LIMIT @limite
            """, new { fts, bibliaId, desde = libroDesde ?? 1, hasta = libroHasta ?? 66, limite }).ToList();
    }
}
