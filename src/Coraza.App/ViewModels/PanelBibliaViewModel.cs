using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coraza.App.Servicios;
using Coraza.App.Vistas;
using Coraza.Core.Biblia;
using Coraza.Core.Modelos;
using Coraza.Data.Importacion;
using Microsoft.Win32;
using Serilog;

namespace Coraza.App.ViewModels;

public enum ModoBiblia { Libros, Capitulos, Pasaje, Resultados }

public sealed record LibroOpcion(LibroBiblico Libro, bool Disponible)
{
    public string Corto => Libro.Abreviatura;
    public string Nombre => Libro.Nombre;
}

public sealed partial class PanelBibliaViewModel : ObservableObject
{
    private const string Ayuda = "Escribe una cita (jn 3 16, Sal 23, Ef 6.14) o palabras para buscar.";
    private const int LimiteResultados = 300;

    private readonly MainViewModel _main;
    private readonly DispatcherTimer _demora;
    private bool _ignorarConsulta;
    private IList<Versiculo>? _seleccion;

    [ObservableProperty] private VersionBiblia? _version;
    [ObservableProperty] private string _consulta = "";
    [ObservableProperty] private ModoBiblia _modo = ModoBiblia.Libros;
    [ObservableProperty] private LibroBiblico? _libroActual;
    [ObservableProperty] private ReferenciaBiblica? _referencia;
    [ObservableProperty] private int _filtroTestamento;
    [ObservableProperty] private string _estado = Ayuda;
    [ObservableProperty] private bool _sinBiblias;

    public PanelBibliaViewModel(MainViewModel main)
    {
        _main = main;
        _demora = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _demora.Tick += (_, _) => Interpretar();
    }

    public ObservableCollection<VersionBiblia> Versiones { get; } = new();
    public ObservableCollection<LibroOpcion> LibrosAT { get; } = new();
    public ObservableCollection<LibroOpcion> LibrosNT { get; } = new();
    public ObservableCollection<int> Capitulos { get; } = new();
    public ObservableCollection<Versiculo> Versiculos { get; } = new();
    public ObservableCollection<Versiculo> Resultados { get; } = new();
    public IReadOnlyList<string> FiltrosTestamento { get; } = new[] { "Toda la Biblia", "Antiguo Testamento", "Nuevo Testamento" };

    public bool PuedeVolver => Modo != ModoBiblia.Libros;

    public void Recargar()
    {
        var idActual = Version?.Id ?? _main.Ctx.Preferencias.BibliaPredeterminada;
        Versiones.Clear();
        foreach (var v in _main.Ctx.Biblias.Listar()) Versiones.Add(v);
        SinBiblias = Versiones.Count == 0;
        Version = Versiones.FirstOrDefault(v => v.Id == idActual) ?? Versiones.FirstOrDefault();
        CargarLibros();
        _main.RecargarVersiones();
    }

    partial void OnVersionChanged(VersionBiblia? value)
    {
        if (value is null) return;
        if (_main.Ctx.Preferencias.BibliaPredeterminada != value.Id)
        {
            _main.Ctx.Preferencias.BibliaPredeterminada = value.Id;
            _main.Ctx.GuardarPreferencias();
        }
        CargarLibros();
        if (Modo == ModoBiblia.Pasaje)
        {
            CargarPasaje();
            // Si el pasaje de la cuadrícula viene del panel (no del servicio), se muestra en la nueva versión.
            if (_main.ElementoActual is { Tipo: TipoElemento.Pasaje } && !_main.ActualEnServicio) Mostrar();
        }
        else if (Modo == ModoBiblia.Resultados)
        {
            Interpretar();
        }
    }

    partial void OnModoChanged(ModoBiblia value) => OnPropertyChanged(nameof(PuedeVolver));

    partial void OnConsultaChanged(string value)
    {
        if (_ignorarConsulta) return;
        _demora.Stop();
        _demora.Start();
    }

    partial void OnFiltroTestamentoChanged(int value)
    {
        if (Modo == ModoBiblia.Resultados) Interpretar();
    }

    private void CargarLibros()
    {
        var disponibles = Version is null ? new HashSet<int>() : _main.Ctx.Biblias.LibrosDisponibles(Version.Id);
        LibrosAT.Clear();
        LibrosNT.Clear();
        foreach (var libro in LibrosBiblia.Todos)
            (libro.AntiguoTestamento ? LibrosAT : LibrosNT).Add(new LibroOpcion(libro, disponibles.Contains(libro.Numero)));
    }

    /// <summary>Decide si lo escrito es una cita, un libro o palabras para buscar.</summary>
    public void Interpretar()
    {
        _demora.Stop();
        var texto = Consulta.Trim();
        if (texto.Length == 0)
        {
            Modo = ModoBiblia.Libros;
            Estado = Ayuda;
            return;
        }
        if (AnalizadorReferencias.Analizar(texto) is { } referencia)
        {
            Referencia = referencia;
            CargarPasaje();
            return;
        }
        if (AnalizadorReferencias.AnalizarSoloLibro(texto) is { } libro)
        {
            MostrarCapitulos(libro);
            return;
        }
        if (texto.Length >= 3) BuscarPalabras(texto);
    }

    private void MostrarCapitulos(LibroBiblico libro)
    {
        LibroActual = libro;
        Capitulos.Clear();
        var cantidad = Version is null ? libro.Capitulos : _main.Ctx.Biblias.CantidadCapitulos(Version.Id, libro.Numero);
        for (var i = 1; i <= cantidad; i++) Capitulos.Add(i);
        Modo = ModoBiblia.Capitulos;
        Estado = $"{libro.Nombre}: elige un capítulo.";
    }

    private void CargarPasaje()
    {
        Versiculos.Clear();
        _seleccion = null;
        if (Version is null || Referencia is null) return;
        foreach (var v in _main.Ctx.Biblias.ObtenerPasaje(Version.Id, Referencia)) Versiculos.Add(v);
        LibroActual = Referencia.InfoLibro;
        Modo = ModoBiblia.Pasaje;
        Estado = Versiculos.Count == 0
            ? $"No se encontró {Referencia} en {Version.Abreviatura}."
            : $"{Referencia} · {Versiculos.Count} versículo{(Versiculos.Count == 1 ? "" : "s")} · Enter para mostrar";
    }

    private void BuscarPalabras(string texto)
    {
        Resultados.Clear();
        if (Version is null) return;
        var (desde, hasta) = FiltroTestamento switch
        {
            1 => (1, 39),
            2 => (40, 66),
            _ => (1, 66),
        };
        foreach (var v in _main.Ctx.Biblias.Buscar(Version.Id, texto, desde, hasta, LimiteResultados)) Resultados.Add(v);
        Modo = ModoBiblia.Resultados;
        Estado = Resultados.Count == 0
            ? $"Sin resultados para «{texto}»."
            : $"{Resultados.Count}{(Resultados.Count >= LimiteResultados ? "+" : "")} resultado(s) para «{texto}».";
    }

    /// <summary>Versículos elegidos en la lista (para proyectar solo una parte del capítulo).</summary>
    public void EstablecerSeleccion(IList<Versiculo> seleccion) => _seleccion = seleccion;

    [RelayCommand]
    private void ElegirLibro(LibroOpcion? opcion)
    {
        if (opcion is null || !opcion.Disponible) return;
        EscribirSinBuscar(opcion.Libro.Nombre + " ");
        MostrarCapitulos(opcion.Libro);
    }

    [RelayCommand]
    private void ElegirCapitulo(int capitulo)
    {
        if (LibroActual is null) return;
        Referencia = new ReferenciaBiblica(LibroActual.Numero, capitulo);
        EscribirSinBuscar(Referencia.ToString());
        CargarPasaje();
    }

    [RelayCommand]
    private void Volver()
    {
        if (Modo == ModoBiblia.Pasaje && LibroActual is not null)
        {
            EscribirSinBuscar(LibroActual.Nombre + " ");
            MostrarCapitulos(LibroActual);
            return;
        }
        EscribirSinBuscar("");
        Modo = ModoBiblia.Libros;
        Estado = Ayuda;
    }

    [RelayCommand]
    private void ElegirResultado(Versiculo? v)
    {
        if (v is null) return;
        Referencia = new ReferenciaBiblica(v.Libro, v.Capitulo, v.Numero);
        EscribirSinBuscar(Referencia.ToString());
        CargarPasaje();
        Mostrar();
    }

    /// <summary>Muestra el pasaje (o los versículos elegidos) en la cuadrícula y en la vista previa.</summary>
    [RelayCommand]
    public void Mostrar()
    {
        var elemento = CrearElemento();
        if (elemento is not null) _main.MostrarElemento(elemento);
    }

    [RelayCommand]
    private void AgregarAlServicio()
    {
        var elemento = CrearElemento();
        if (elemento is not null) _main.AgregarAlServicio(_main.ReutilizarActual(elemento));
    }

    private ElementoServicio? CrearElemento()
    {
        if (Modo != ModoBiblia.Pasaje) Interpretar();
        if (Version is null)
        {
            _main.MostrarMensaje("No hay ninguna Biblia instalada. Importa una desde el botón de importar.", esError: true);
            return null;
        }
        if (Referencia is null || Modo != ModoBiblia.Pasaje || Versiculos.Count == 0)
        {
            _main.MostrarMensaje("Escribe una cita como «Juan 3:16» o elige libro y capítulo.");
            return null;
        }
        var referencia = Referencia;
        if (_seleccion is { Count: > 0 } seleccion && seleccion.Count < Versiculos.Count)
        {
            var orden = seleccion.OrderBy(v => v.Capitulo).ThenBy(v => v.Numero).ToList();
            var a = orden[0];
            var b = orden[^1];
            referencia = a.Capitulo == b.Capitulo
                ? new ReferenciaBiblica(a.Libro, a.Capitulo, a.Numero, b.Numero == a.Numero ? null : b.Numero)
                : new ReferenciaBiblica(a.Libro, a.Capitulo, a.Numero, b.Numero, b.Capitulo);
        }
        return FabricaElementos.DePasaje(Version, referencia);
    }

    [RelayCommand]
    private async Task ImportarAsync()
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Importar una Biblia",
            Filter = "Biblias (e-Sword, MySword, MyBible, Zefania, OSIS, USFM, VPL)|*.bblx;*.mybible;*.sqlite3;*.xml;*.osis;*.usfm;*.sfm;*.zip;*.txt;*.vpl;*.gz|Todos los archivos|*.*",
        };
        if (dialogo.ShowDialog() != true) return;

        BibliaImportada biblia;
        try
        {
            _main.Preparando = true;
            _main.TextoPreparando = "Leyendo la Biblia…";
            biblia = await Task.Run(() => ImportadorBiblias.Leer(dialogo.FileName));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo leer la Biblia {Ruta}", dialogo.FileName);
            _main.Preparando = false;
            Dialogo.Informar("No se pudo importar",
                $"El archivo no tiene un formato reconocido (e-Sword, MySword, MyBible, Zefania, OSIS, USFM o VPL).\n\n{ex.Message}");
            return;
        }
        _main.Preparando = false;

        var nombre = Dialogo.PedirTexto("Importar Biblia", $"Se encontraron {biblia.Versiculos.Count:N0} versículos. Nombre de la versión:",
            biblia.Nombre ?? Path.GetFileNameWithoutExtension(dialogo.FileName));
        if (string.IsNullOrWhiteSpace(nombre)) return;
        var abreviatura = Dialogo.PedirTexto("Importar Biblia", "Abreviatura que se mostrará en la referencia (por ejemplo RVR, NVI):",
            biblia.Abreviatura ?? SugerirAbreviatura(nombre));
        if (string.IsNullOrWhiteSpace(abreviatura)) return;
        if (_main.Ctx.Biblias.ExisteAbreviatura(abreviatura))
        {
            Dialogo.Informar("Abreviatura en uso", $"Ya existe una Biblia con la abreviatura «{abreviatura}».");
            return;
        }
        if (!Dialogo.Confirmar("Derechos de la traducción",
                "Muchas traducciones modernas (RVR1960, NVI, NTV, DHH…) tienen derechos de autor. Confirma que tienes permiso para usar esta versión.",
                "Tengo permiso"))
            return;

        try
        {
            _main.Preparando = true;
            _main.TextoPreparando = $"Instalando {nombre}…";
            var version = new VersionBiblia { Nombre = nombre, Abreviatura = abreviatura, Idioma = biblia.Idioma ?? "es", Licencia = "Importada por el usuario" };
            await Task.Run(() => _main.Ctx.Biblias.Importar(version, biblia.Versiculos));
            Recargar();
            Version = Versiones.FirstOrDefault(v => v.Id == version.Id) ?? Version;
            _main.MostrarMensaje($"Biblia «{nombre}» instalada.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error al instalar la Biblia");
            Dialogo.Informar("No se pudo importar", ex.Message);
        }
        finally
        {
            _main.Preparando = false;
        }
    }

    [RelayCommand]
    private void EliminarVersion()
    {
        if (Version is null) return;
        if (!Dialogo.Confirmar("Eliminar Biblia", $"¿Eliminar «{Version.Nombre}» de Coraza? Los pasajes del servicio que la usen tomarán otra versión.", "Eliminar", peligro: true))
            return;
        _main.Ctx.Biblias.Eliminar(Version.Id);
        Version = null;
        Recargar();
    }

    /// <summary>Abreviatura sugerida: «Reina-Valera 1960» → RVR1960, «Reina Valera 1909» → RV1909.</summary>
    private static string SugerirAbreviatura(string nombre)
    {
        var normal = Coraza.Core.Busqueda.Normalizador.Normalizar(nombre);
        if (normal.Contains("reina") || normal.Contains("valera") || normal.StartsWith("rv"))
        {
            foreach (var anio in new[] { "1960", "1995", "1977", "2015", "2020", "1909", "1865" })
                if (normal.Contains(anio)) return (anio is "1909" or "1865" ? "RV" : "RVR") + anio;
        }
        var letras = new string(nombre.Where(char.IsLetterOrDigit).Take(6).ToArray()).ToUpperInvariant();
        return letras.Length > 0 ? letras : "BIBLIA";
    }

    private void EscribirSinBuscar(string texto)
    {
        _ignorarConsulta = true;
        Consulta = texto;
        _ignorarConsulta = false;
    }
}
