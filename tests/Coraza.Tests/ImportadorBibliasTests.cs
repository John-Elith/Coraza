using System.IO.Compression;
using System.Text;
using Coraza.Core.Biblia;
using Coraza.Data.Importacion;
using Microsoft.Data.Sqlite;

namespace Coraza.Tests;

public sealed class ImportadorBibliasTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "coraza-biblias-" + Guid.NewGuid().ToString("N"));

    public ImportadorBibliasTests() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    private static BibliaImportada LeerTexto(string contenido) =>
        ImportadorBiblias.Leer(new MemoryStream(Encoding.UTF8.GetBytes(contenido)));

    [Fact]
    public void LeeOsisConVersiculosContenedores()
    {
        var b = LeerTexto("""
            <?xml version="1.0" encoding="UTF-8"?>
            <osis><osisText osisIDWork="PRB" xml:lang="es">
              <header><work osisWork="PRB"><title>Biblia de Prueba</title></work></header>
              <div type="book" osisID="John"><chapter osisID="John.3">
                <title>Nicodemo</title>
                <verse osisID="John.3.16">Porque de tal manera<note>nota</note> amó Dios al mundo</verse>
                <verse osisID="John.3.17">Porque no envió Dios</verse>
              </chapter></div>
            </osisText></osis>
            """);
        Assert.Equal("Biblia de Prueba", b.Nombre);
        Assert.Equal("es", b.Idioma);
        Assert.Equal(2, b.Versiculos.Count);
        Assert.Equal((43, 3, 16, "Porque de tal manera amó Dios al mundo"),
            (b.Versiculos[0].Libro, b.Versiculos[0].Capitulo, b.Versiculos[0].Versiculo, b.Versiculos[0].Texto));
    }

    [Fact]
    public void LeeOsisConHitos()
    {
        var b = LeerTexto("""
            <osis><osisText>
              <div type="book" osisID="Ps"><chapter sID="Ps.23" osisID="Ps.23"/>
                <verse sID="Ps.23.1" osisID="Ps.23.1"/>Jehová es mi pastor; <verse eID="Ps.23.1"/>
                <verse sID="Ps.23.2" osisID="Ps.23.2"/>En lugares de delicados pastos<verse eID="Ps.23.2"/>
              </div>
            </osisText></osis>
            """);
        Assert.Equal(new[] { "Jehová es mi pastor;", "En lugares de delicados pastos" }, b.Versiculos.Select(v => v.Texto));
        Assert.All(b.Versiculos, v => Assert.Equal(19, v.Libro));
    }

    [Fact]
    public void LeeUsfmQuitandoNotasYMarcas()
    {
        var b = LeerTexto("""
            \id JHN Juan
            \h Juan
            \mt1 San Juan
            \c 3
            \s1 Nicodemo
            \p
            \v 16 Porque de tal manera \w amó|strong="G25"\w* Dios al mundo\f + \fr 3.16 \ft Nota.\f*,
            \q1 que ha dado a su Hijo.
            \v 17 Porque no envió Dios \v 18 El que en él cree
            """);
        Assert.Equal(new[] { 16, 17, 18 }, b.Versiculos.Select(v => v.Versiculo));
        Assert.Equal("Porque de tal manera amó Dios al mundo, que ha dado a su Hijo.", b.Versiculos[0].Texto);
        Assert.Equal("El que en él cree", b.Versiculos[2].Texto);
    }

    [Fact]
    public void LeeUsfmDentroDeUnZip()
    {
        var ruta = Path.Combine(_carpeta, "biblia.zip");
        using (var zip = ZipFile.Open(ruta, ZipArchiveMode.Create))
        {
            foreach (var (archivo, contenido) in new[] { ("01GEN.usfm", "\\id GEN\n\\c 1\n\\p\n\\v 1 En el principio"), ("44JHN.usfm", "\\id JHN\n\\c 1\n\\p\n\\v 1 En el principio era el Verbo") })
            {
                using var escritor = new StreamWriter(zip.CreateEntry(archivo).Open());
                escritor.Write(contenido);
            }
        }
        var b = ImportadorBiblias.Leer(ruta);
        Assert.Equal(new[] { 1, 43 }, b.Versiculos.Select(v => v.Libro));
    }

    [Fact]
    public void LeeModuloMySword()
    {
        var ruta = CrearSqlite("prueba.bbl.mybible",
            "CREATE TABLE Bible (Book INT, Chapter INT, Verse INT, Scripture TEXT); CREATE TABLE Details (Title TEXT, Abbreviation TEXT);",
            "INSERT INTO Details VALUES ('Reina-Valera 1960', 'RVR1960');" +
            "INSERT INTO Bible VALUES (43, 3, 16, 'Porque de tal manera amó<WG25> Dios<RF>nota<Rf> al <FI>mundo<Fi>');");
        var b = ImportadorBiblias.Leer(ruta);
        Assert.Equal("Reina-Valera 1960", b.Nombre);
        Assert.Equal("RVR1960", b.Abreviatura);
        Assert.Equal("Porque de tal manera amó Dios al mundo", b.Versiculos.Single().Texto);
    }

    [Fact]
    public void LeeModuloMyBible()
    {
        var ruta = CrearSqlite("prueba.SQLite3",
            "CREATE TABLE verses (book_number INT, chapter INT, verse INT, text TEXT); CREATE TABLE info (name TEXT, value TEXT);",
            "INSERT INTO info VALUES ('description', 'Biblia MyBible'); INSERT INTO info VALUES ('language', 'es');" +
            "INSERT INTO verses VALUES (500, 3, 16, 'Porque de tal manera amó<S>25</S> Dios<f>[1]</f> al mundo');" +
            "INSERT INTO verses VALUES (10, 1, 1, 'En el principio');");
        var b = ImportadorBiblias.Leer(ruta);
        Assert.Equal("Biblia MyBible", b.Nombre);
        Assert.Equal(new[] { 43, 1 }, b.Versiculos.Select(v => v.Libro).OrderByDescending(x => x));
        Assert.Equal("Porque de tal manera amó Dios al mundo", b.Versiculos.First(v => v.Libro == 43).Texto);
    }

    [Fact]
    public void LimpiaTextoRtfDeESword()
    {
        Assert.Equal("Porque de tal manera amó", ImportadorBiblias.LimpiarTexto(ImportadorBiblias.LimpiarMarcado(@"{\cf6 Porque}\cf0  de tal manera am\'f3")));
    }

    [Fact]
    public void ReconoceCodigosOsisYMyBible()
    {
        Assert.Equal(43, LibrosBiblia.PorCodigo("John")?.Numero);
        Assert.Equal(19, LibrosBiblia.PorCodigo("Ps")?.Numero);
        Assert.Equal(62, LibrosBiblia.PorCodigo("1John")?.Numero);
        Assert.Equal(17, LibrosBiblia.PorNumeroMyBible(190)?.Numero);
        Assert.Equal(66, LibrosBiblia.PorNumeroMyBible(730)?.Numero);
        Assert.Null(LibrosBiblia.PorNumeroMyBible(170));
    }

    private string CrearSqlite(string nombre, string esquema, string datos)
    {
        var ruta = Path.Combine(_carpeta, nombre);
        using var c = new SqliteConnection($"Data Source={ruta};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = esquema + datos;
        cmd.ExecuteNonQuery();
        return ruta;
    }
}
