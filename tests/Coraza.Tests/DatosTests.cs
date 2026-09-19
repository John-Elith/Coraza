using Coraza.Core.Biblia;
using Coraza.Core.Canciones;
using Coraza.Core.Modelos;
using Coraza.Data;
using Coraza.Data.Importacion;
using Coraza.Data.Repositorios;

namespace Coraza.Tests;

public sealed class DatosTests : IDisposable
{
    private readonly string _carpeta;
    private readonly BaseDatos _db;

    public DatosTests()
    {
        _carpeta = Path.Combine(Path.GetTempPath(), "coraza-pruebas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_carpeta);
        _db = new BaseDatos(Path.Combine(_carpeta, "prueba.db"));
        _db.Inicializar();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    [Fact]
    public void GuardaYRecuperaCancionConSeccionesYOrden()
    {
        var repo = new RepositorioCanciones(_db);
        var letra = AnalizadorLetra.Analizar("[Verso 1]\nuno\n\n[Coro]\ncanto al Rey\n\n[Verso 2]\ndos");
        var cancion = new Cancion
        {
            Titulo = "Prueba", Autor = "Anónimo", Tempo = 72, Favorita = true,
            Secciones = letra.Secciones, Orden = new() { "V1", "C", "V2", "C" },
        };
        var id = repo.Guardar(cancion);

        var leida = repo.Obtener(id)!;
        Assert.Equal("Prueba", leida.Titulo);
        Assert.Equal(72, leida.Tempo);
        Assert.True(leida.Favorita);
        Assert.Equal(new[] { TipoSeccion.Verso, TipoSeccion.Coro, TipoSeccion.Verso }, leida.Secciones.Select(s => s.Tipo));
        Assert.Equal(new[] { "V1", "C", "V2", "C" }, leida.Orden);
        Assert.Equal("uno", leida.PrimeraLinea);

        leida.Titulo = "Prueba editada";
        repo.Guardar(leida);
        Assert.Single(repo.VersionesAnteriores(id));
        Assert.Equal("Prueba editada", repo.Listar().Single().Titulo);

        var indice = repo.TextosParaIndice();
        Assert.Contains("canto al Rey", indice.Single().Letra);

        repo.MarcarEliminada(id, true);
        Assert.Empty(repo.Listar());
        Assert.Single(repo.Listar(eliminadas: true));
    }

    [Fact]
    public void ImportaBibliaVplYBuscaTextoCompleto()
    {
        const string vpl = """
            GEN 1:1 EN el principio crió Dios los cielos y la tierra.
            GEN 1:2 Y la tierra estaba desordenada y vacía.
            JOH 3:16 Porque de tal manera amó Dios al mundo, que ha dado á su Hijo unigénito.
            JOH 3:17 Porque no envió Dios á su Hijo al mundo, para que condene al mundo.
            JOH 4:1 DE manera que como Jesús entendió.
            """;
        var importada = ImportadorBiblias.LeerVpl(new StringReader(vpl));
        Assert.Equal("En el principio crió Dios los cielos y la tierra.", importada.Versiculos[0].Texto);

        var repo = new RepositorioBiblias(_db);
        var id = repo.Importar(new VersionBiblia { Nombre = "Prueba", Abreviatura = "PRB" }, importada.Versiculos);

        var pasaje = repo.ObtenerPasaje(id, AnalizadorReferencias.Analizar("jn 3 16-17")!);
        Assert.Equal(new[] { 16, 17 }, pasaje.Select(v => v.Numero));

        var cruzado = repo.ObtenerPasaje(id, AnalizadorReferencias.Analizar("juan 3:17-4:1")!);
        Assert.Equal(new[] { 17, 1 }, cruzado.Select(v => v.Numero));

        Assert.Single(repo.Buscar(id, "principio"));
        Assert.Single(repo.Buscar(id, "amo mundo"));          // sin tildes
        Assert.Equal(2, repo.Buscar(id, "hijo").Count);
        Assert.Single(repo.Buscar(id, "hijo", 43, 43, 1));
        Assert.Empty(repo.Buscar(id, "hijo", 1, 39));        // solo Antiguo Testamento
        Assert.Equal(4, repo.CantidadCapitulos(id, 43));
        Assert.Equal(17, repo.CantidadVersiculos(id, 43, 3));

        repo.Eliminar(id);
        Assert.Empty(repo.Listar());
    }

    [Fact]
    public void ImportaZefania()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <XMLBIBLE biblename="Prueba Zefania">
              <INFORMATION><identifier>PZF</identifier><language>SPA</language></INFORMATION>
              <BIBLEBOOK bnumber="43" bname="Juan">
                <CHAPTER cnumber="1">
                  <VERS vnumber="1">En el principio era el Verbo<NOTE>nota</NOTE>.</VERS>
                </CHAPTER>
              </BIBLEBOOK>
            </XMLBIBLE>
            """;
        using var flujo = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var b = ImportadorBiblias.Leer(flujo);
        Assert.Equal("Prueba Zefania", b.Nombre);
        Assert.Equal("PZF", b.Abreviatura);
        Assert.Equal("En el principio era el Verbo.", b.Versiculos.Single().Texto);
    }

    [Fact]
    public void GuardaServicioYTemas()
    {
        var temas = new RepositorioTemas(_db);
        temas.AsegurarPredefinidos();
        temas.AsegurarPredefinidos();
        var lista = temas.Listar();
        Assert.Equal(Coraza.Core.Temas.TemasPredefinidos.Crear().Count, lista.Count);
        Assert.Contains(lista, t => t.Fondo.Animacion == AnimacionFondo.Bokeh);
        var tema = lista.First(t => t.Nombre == "Coral Aurora");
        Assert.True(tema.Texto.Mayusculas);

        var servicios = new RepositorioServicios(_db);
        var id = servicios.Crear(new Servicio { Nombre = "Domingo" });
        servicios.GuardarElementos(id, new List<ElementoServicio>
        {
            new() { Tipo = TipoElemento.Encabezado, Titulo = "Alabanza", Color = "#B44446" },
            new() { Tipo = TipoElemento.Pasaje, Titulo = "Juan 3:16", ReferenciaId = 1, Datos = "{}", Notas = "Leer despacio" },
            new() { Tipo = TipoElemento.Imagen, Titulo = "Fotos del campamento", ReferenciaId = 2, AvanceSegundos = 8, Bucle = true },
        });
        var s = servicios.Obtener(id)!;
        Assert.Equal(8, s.Elementos[2].AvanceSegundos);
        Assert.True(s.Elementos[2].Bucle);
        Assert.Null(s.Elementos[1].AvanceSegundos);
        s.Elementos.RemoveAt(2);
        Assert.Equal(new[] { TipoElemento.Encabezado, TipoElemento.Pasaje }, s.Elementos.Select(e => e.Tipo));
        Assert.Equal("Leer despacio", s.Elementos[1].Notas);

        var copia = servicios.Duplicar(id, "Domingo (copia)");
        Assert.Equal(3, servicios.Obtener(copia)!.Elementos.Count);

        var config = new RepositorioConfiguracion(_db);
        config.Guardar("clave", "uno");
        config.Guardar("clave", "dos");
        Assert.Equal("dos", config.Obtener("clave"));
    }

    [Fact]
    public void CreaRespaldoDiario()
    {
        var carpeta = Path.Combine(_carpeta, "Respaldos");
        Assert.NotNull(GestorRespaldos.RespaldoDiario(_db, carpeta));
        Assert.Null(GestorRespaldos.RespaldoDiario(_db, carpeta));
        Assert.Single(Directory.GetFiles(carpeta));
    }
}
