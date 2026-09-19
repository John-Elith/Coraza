using Dapper;
using Microsoft.Data.Sqlite;

namespace Coraza.Data;

/// <summary>Base de datos local SQLite: canciones, Biblias, medios, temas, servicios y configuración.</summary>
public sealed class BaseDatos
{
    public const int VersionEsquema = 2;

    private readonly string _cadena;

    static BaseDatos()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public BaseDatos(string rutaArchivo)
    {
        RutaArchivo = rutaArchivo;
        _cadena = new SqliteConnectionStringBuilder
        {
            DataSource = rutaArchivo,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public string RutaArchivo { get; }

    public SqliteConnection Abrir()
    {
        var conexion = new SqliteConnection(_cadena);
        conexion.Open();
        return conexion;
    }

    public void Inicializar()
    {
        var carpeta = Path.GetDirectoryName(RutaArchivo);
        if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);

        using var c = Abrir();
        c.Execute("PRAGMA journal_mode = WAL;");
        var version = c.ExecuteScalar<long>("PRAGMA user_version;");
        if (version < 1)
        {
            using var tx = c.BeginTransaction();
            c.Execute(Esquema.Version1, transaction: tx);
            c.Execute("PRAGMA user_version = 1;", transaction: tx);
            tx.Commit();
            version = 1;
        }
        if (version < 2)
        {
            // Fase 2: avance automático y bucle por elemento del servicio.
            using var tx = c.BeginTransaction();
            c.Execute(Esquema.Version2, transaction: tx);
            c.Execute("PRAGMA user_version = 2;", transaction: tx);
            tx.Commit();
        }
    }

    /// <summary>Copia consistente de la base de datos (usa la API de respaldo de SQLite).</summary>
    public void CopiarA(string destino)
    {
        var carpeta = Path.GetDirectoryName(destino);
        if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);
        using var origen = Abrir();
        using var copia = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destino, Pooling = false }.ToString());
        copia.Open();
        origen.BackupDatabase(copia);
    }
}

internal static class Esquema
{
    public const string Version2 = """
        ALTER TABLE elementos_servicio ADD COLUMN avance_seg INTEGER;
        ALTER TABLE elementos_servicio ADD COLUMN bucle INTEGER NOT NULL DEFAULT 0;
        """;

    public const string Version1 = """
        CREATE TABLE canciones (
            id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            titulo             TEXT NOT NULL,
            autor              TEXT,
            tonalidad          TEXT,
            tempo              INTEGER,
            ccli               TEXT,
            derechos           TEXT,
            idioma             TEXT NOT NULL DEFAULT 'es',
            categoria          TEXT,
            etiquetas          TEXT,
            tema_id            INTEGER,
            favorita           INTEGER NOT NULL DEFAULT 0,
            eliminada          INTEGER NOT NULL DEFAULT 0,
            fecha_creacion     TEXT NOT NULL,
            fecha_modificacion TEXT NOT NULL,
            veces_usada        INTEGER NOT NULL DEFAULT 0,
            ultima_vez         TEXT
        );

        CREATE TABLE secciones (
            id                   INTEGER PRIMARY KEY AUTOINCREMENT,
            cancion_id           INTEGER NOT NULL REFERENCES canciones(id) ON DELETE CASCADE,
            tipo                 TEXT NOT NULL,
            numero               INTEGER NOT NULL DEFAULT 0,
            texto                TEXT NOT NULL,
            acordes              TEXT,
            texto_segundo_idioma TEXT,
            posicion             INTEGER NOT NULL
        );
        CREATE INDEX ix_secciones_cancion ON secciones(cancion_id, posicion);

        CREATE TABLE orden_cancion (
            cancion_id INTEGER NOT NULL REFERENCES canciones(id) ON DELETE CASCADE,
            posicion   INTEGER NOT NULL,
            seccion_id INTEGER NOT NULL REFERENCES secciones(id) ON DELETE CASCADE,
            PRIMARY KEY (cancion_id, posicion)
        );

        CREATE TABLE canciones_historial (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            cancion_id INTEGER NOT NULL REFERENCES canciones(id) ON DELETE CASCADE,
            fecha      TEXT NOT NULL,
            titulo     TEXT NOT NULL,
            letra      TEXT NOT NULL,
            orden      TEXT
        );

        CREATE TABLE biblias (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            nombre      TEXT NOT NULL,
            abreviatura TEXT NOT NULL UNIQUE,
            idioma      TEXT NOT NULL DEFAULT 'es',
            licencia    TEXT
        );

        CREATE TABLE versiculos (
            id        INTEGER PRIMARY KEY,
            biblia_id INTEGER NOT NULL REFERENCES biblias(id) ON DELETE CASCADE,
            libro     INTEGER NOT NULL,
            capitulo  INTEGER NOT NULL,
            versiculo INTEGER NOT NULL,
            texto     TEXT NOT NULL
        );
        CREATE UNIQUE INDEX ix_versiculos_ref ON versiculos(biblia_id, libro, capitulo, versiculo);

        CREATE VIRTUAL TABLE versiculos_fts USING fts5(
            texto,
            content = 'versiculos',
            content_rowid = 'id',
            tokenize = 'unicode61 remove_diacritics 2'
        );

        CREATE TABLE medios (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            tipo           TEXT NOT NULL,
            ruta           TEXT NOT NULL,
            nombre         TEXT NOT NULL,
            duracion       REAL,
            ancho          INTEGER,
            alto           INTEGER,
            etiquetas      TEXT,
            ajuste         TEXT NOT NULL DEFAULT 'Ajustar',
            fecha_creacion TEXT NOT NULL
        );

        CREATE TABLE textos (
            id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            titulo             TEXT NOT NULL,
            contenido          TEXT NOT NULL,
            tema_id            INTEGER,
            fecha_modificacion TEXT NOT NULL
        );

        CREATE TABLE temas (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            nombre      TEXT NOT NULL,
            datos       TEXT NOT NULL,
            predefinido INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE servicios (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            nombre     TEXT NOT NULL,
            fecha      TEXT NOT NULL,
            plantilla  TEXT,
            notas      TEXT,
            modificado TEXT NOT NULL
        );

        CREATE TABLE elementos_servicio (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            servicio_id  INTEGER NOT NULL REFERENCES servicios(id) ON DELETE CASCADE,
            posicion     INTEGER NOT NULL,
            tipo         TEXT NOT NULL,
            referencia_id INTEGER,
            titulo       TEXT NOT NULL,
            datos        TEXT,
            tema_id      INTEGER,
            notas        TEXT,
            duracion_min INTEGER,
            color        TEXT
        );
        CREATE INDEX ix_elementos_servicio ON elementos_servicio(servicio_id, posicion);

        CREATE TABLE historial_uso (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            tipo          TEXT NOT NULL,
            referencia_id INTEGER,
            descripcion   TEXT,
            fecha_hora    TEXT NOT NULL
        );

        CREATE TABLE configuracion (
            clave TEXT PRIMARY KEY,
            valor TEXT
        );
        """;
}
