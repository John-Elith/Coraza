using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coraza.App.Infraestructura;
using Coraza.App.Servicios;
using Coraza.App.Vistas;
using Coraza.Core.Modelos;
using Coraza.Data.Importacion;
using Coraza.Rendering;
using Serilog;

namespace Coraza.App.ViewModels;

/// <summary>
/// Coordina la ventana del operador: orden del servicio, diapositivas del elemento elegido,
/// vista previa, lo que está en vivo, pantallas y atajos.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _temporizadorReloj;
    private readonly DispatcherTimer _temporizadorGuardado;
    private readonly DispatcherTimer _temporizadorMensaje;
    private readonly DispatcherTimer _temporizadorAvance = new();
    private readonly DispatcherTimer _temporizadorVideo = new() { Interval = TimeSpan.FromMilliseconds(500) };
    [ObservableProperty] private string _estadoVideo = "";
    private readonly HashSet<object> _usoRegistrado = new(ReferenceEqualityComparer.Instance);

    private ElementoServicio? _elementoVivo;
    private List<DiapositivaViewModel> _diapositivasVivo = new();
    private int _indiceVivo = -1;
    private bool _cargandoTema;
    private bool _cargandoPantallas;
    private bool _cargandoVersion;

    // Servicio
    [ObservableProperty] private Servicio _servicio = new();
    [ObservableProperty] private ElementoViewModel? _elementoSeleccionado;
    [ObservableProperty] private string _duracionTotal = "";
    [ObservableProperty] private string _estadoGuardado = "";

    // Elemento actual
    [ObservableProperty] private ElementoServicio? _elementoActual;
    [ObservableProperty] private string _tituloActual = "Elige una canción, un pasaje o una imagen";
    [ObservableProperty] private string _subtituloActual = "Sus diapositivas aparecerán aquí.";
    [ObservableProperty] private string? _avisoActual;
    [ObservableProperty] private DiapositivaViewModel? _diapositivaPrevia;
    [ObservableProperty] private Tema? _temaActual;
    [ObservableProperty] private IReadOnlyList<OpcionTema> _opcionesTema = Array.Empty<OpcionTema>();
    [ObservableProperty] private OpcionTema? _temaElegido;
    [ObservableProperty] private VersionBiblia? _versionElegida;

    // En vivo
    [ObservableProperty] private string _tituloVivo = "Nada en vivo";
    [ObservableProperty] private string _siguienteTexto = "";

    // Pantallas
    [ObservableProperty] private OpcionPantalla? _pantallaSeleccionada;
    [ObservableProperty] private bool _avisoDuplicar;
    [ObservableProperty] private bool _unaSolaPantalla;

    // Barra inferior y estado
    [ObservableProperty] private OpcionTransicion _transicionSeleccionada;
    [ObservableProperty] private int _duracionTransicion;
    [ObservableProperty] private string _reloj = "";
    [ObservableProperty] private string? _mensaje;
    [ObservableProperty] private bool _mensajeEsError;
    [ObservableProperty] private bool _modoConcentracion;
    [ObservableProperty] private bool _preparando;
    [ObservableProperty] private string _textoPreparando = "";

    public MainViewModel(Contexto ctx)
    {
        Ctx = ctx;
        Proveedor = new ProveedorDiapositivas(ctx);
        Proyeccion = new ControladorProyeccion(ctx);
        Atajos = new GestorAtajos(ctx.Preferencias.Atajos);
        Canciones = new PanelCancionesViewModel(this);
        Biblia = new PanelBibliaViewModel(this);
        Medios = new PanelMediosViewModel(this);
        Textos = new PanelTextosViewModel(this);
        Temas = new PanelTemasViewModel(this);
        Busqueda = new BusquedaUniversalViewModel(this);

        Transiciones = OpcionTransicion.Todas;
        _transicionSeleccionada = Transiciones.FirstOrDefault(t => t.Valor == ctx.Preferencias.TransicionForzada) ?? Transiciones[0];
        _duracionTransicion = ctx.Preferencias.DuracionForzadaMs;

        ReconstruirOpcionesTema();
        Proveedor.TemasCambiaron += (_, _) =>
        {
            ReconstruirOpcionesTema();
            RefrescarElementoActual();
        };
        Proyeccion.CierreSolicitado += (_, _) => AlternarProyeccion();

        _temporizadorReloj = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _temporizadorReloj.Tick += (_, _) => Reloj = DateTime.Now.ToString("HH:mm");
        _temporizadorReloj.Start();
        _reloj = DateTime.Now.ToString("HH:mm");

        _temporizadorGuardado = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _temporizadorGuardado.Tick += (_, _) => GuardarServicioAhora();

        _temporizadorMensaje = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _temporizadorMensaje.Tick += (_, _) =>
        {
            _temporizadorMensaje.Stop();
            Mensaje = null;
        };

        _temporizadorAvance.Tick += (_, _) => AvanceAutomatico();
        _temporizadorVideo.Tick += (_, _) => ActualizarEstadoVideo();
        _temporizadorVideo.Start();
        Proyeccion.ErrorMedio += (_, detalle) =>
            MostrarMensaje($"No se pudo reproducir el archivo ({detalle}). Usa videos MP4 (H.264) y audios MP3.", esError: true);

        Elementos.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ServicioVacio));
            ActualizarDuracion();
        };

        CargarServicioInicial();
        ActualizarPantallas();
    }

    public Contexto Ctx { get; }
    public ProveedorDiapositivas Proveedor { get; }
    public ControladorProyeccion Proyeccion { get; }
    public GestorAtajos Atajos { get; private set; }

    public PanelCancionesViewModel Canciones { get; }
    public PanelBibliaViewModel Biblia { get; }
    public PanelMediosViewModel Medios { get; }
    public PanelTextosViewModel Textos { get; }
    public PanelTemasViewModel Temas { get; }
    public BusquedaUniversalViewModel Busqueda { get; }

    public ObservableCollection<ElementoViewModel> Elementos { get; } = new();
    public ObservableCollection<DiapositivaViewModel> Diapositivas { get; } = new();
    public ObservableCollection<string> SeccionesVivo { get; } = new();
    public ObservableCollection<OpcionPantalla> Pantallas { get; } = new();
    public ObservableCollection<VersionBiblia> VersionesBiblia { get; } = new();
    public IReadOnlyList<OpcionTransicion> Transiciones { get; }

    public bool ServicioVacio => Elementos.Count == 0;

    public bool HayElementoActual => ElementoActual is not null;

    public bool ActualEnServicio => ElementoActual is not null && Elementos.Any(e => ReferenceEquals(e.Modelo, ElementoActual));

    public bool ActualEditable => ElementoActual?.Tipo is TipoElemento.Cancion or TipoElemento.Texto;

    public bool EsPasaje => ElementoActual?.Tipo == TipoElemento.Pasaje;

    public bool TransicionManual => TransicionSeleccionada.Valor is not null;

    public string NombreServicio
    {
        get => Servicio.Nombre;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == Servicio.Nombre) return;
            Servicio.Nombre = value.Trim();
            OnPropertyChanged();
            Ctx.Servicios.ActualizarCabecera(Servicio);
        }
    }

    // ================= Mensajes =================

    public void MostrarMensaje(string texto, bool esError = false)
    {
        Mensaje = texto;
        MensajeEsError = esError;
        _temporizadorMensaje.Stop();
        _temporizadorMensaje.Start();
        if (esError) Log.Warning("Aviso al operador: {Mensaje}", texto);
    }

    // ================= Inicio =================

    /// <summary>Tareas pesadas del primer arranque: índice de búsqueda e instalación de la Biblia incluida.</summary>
    public async Task PrepararAsync()
    {
        Preparando = true;
        TextoPreparando = "Preparando la biblioteca…";
        var nuevasBiblias = new List<string>();
        try
        {
            await Task.Run(Ctx.ReconstruirIndice);
            nuevasBiblias = await Task.Run(InstalarBibliasIncluidas);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error al preparar la biblioteca");
            MostrarMensaje("No se pudo preparar toda la biblioteca. Revise el registro de errores.", esError: true);
        }
        finally
        {
            Preparando = false;
        }

        Canciones.Recargar();
        Biblia.Recargar();
        Medios.Recargar();
        Textos.Recargar();
        Temas.Recargar();
        MostrarMensaje(nuevasBiblias.Count > 0
            ? $"Se agregó a la biblioteca: {string.Join(" y ", nuevasBiblias)}. Cambia de versión en la pestaña Biblia."
            : UnaSolaPantalla
                ? "Listo. Solo hay una pantalla: al proyectar se abrirá una ventana de ensayo. Ctrl+K para buscar."
                : "Listo. Pulsa F5 para proyectar o Ctrl+K para buscar.");
    }

    /// <summary>
    /// Biblias que vienen con Coraza. La primera es la predeterminada en una instalación nueva:
    /// la Versión Biblia Libre usa español actual, mientras que la Reina-Valera 1909 conserva la
    /// ortografía de su época («crió», «fué», la preposición «á») por ser el texto original de 1909.
    /// </summary>
    private static readonly (string Archivo, string Nombre, string Abreviatura, string Licencia)[] BibliasIncluidas =
    {
        ("VBL.vpl.gz", "Versión Biblia Libre", "VBL",
            "CC BY-SA 4.0 · © 2018-2020 Jonathan Gallagher y Shelly Barrios de Avila (freebibleversion.org)"),
        ("RV1909.vpl.gz", "Reina-Valera 1909", "RV1909", "Dominio público"),
    };

    /// <summary>Instala las Biblias incluidas que falten (también al actualizar). Devuelve las recién agregadas.</summary>
    private List<string> InstalarBibliasIncluidas()
    {
        var nuevas = new List<string>();
        var existentes = Ctx.Biblias.Listar().Select(b => b.Abreviatura).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (archivo, nombre, abreviatura, licencia) in BibliasIncluidas)
        {
            if (existentes.Contains(abreviatura)) continue;
            var ruta = Path.Combine(AppContext.BaseDirectory, "Recursos", "Biblias", archivo);
            if (!File.Exists(ruta))
            {
                Log.Warning("No se encontró la Biblia incluida en {Ruta}", ruta);
                continue;
            }
            try
            {
                TextoPreparando = $"Instalando la Biblia {nombre}…";
                var biblia = ImportadorBiblias.Leer(ruta);
                var id = Ctx.Biblias.Importar(new VersionBiblia
                {
                    Nombre = nombre,
                    Abreviatura = abreviatura,
                    Idioma = "es",
                    Licencia = licencia,
                }, biblia.Versiculos);
                // Solo se elige predeterminada si el usuario no tenía ninguna: nunca se cambia la suya.
                Ctx.Preferencias.BibliaPredeterminada ??= id;
                Ctx.GuardarPreferencias();
                nuevas.Add(nombre);
                Log.Information("Biblia {Abreviatura} instalada con {Cantidad} versículos", abreviatura, biblia.Versiculos.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo instalar la Biblia incluida {Archivo}", archivo);
            }
        }
        return nuevas;
    }

    // ================= Orden del servicio =================

    private void CargarServicioInicial()
    {
        Servicio? servicio = null;
        if (Ctx.Preferencias.ServicioActual is long id) servicio = Ctx.Servicios.Obtener(id);
        if (servicio is null)
        {
            var reciente = Ctx.Servicios.Listar().FirstOrDefault();
            if (reciente is not null) servicio = Ctx.Servicios.Obtener(reciente.Id);
        }
        if (servicio is null)
        {
            servicio = new Servicio { Nombre = NombrePredeterminado(), Fecha = DateTime.Today };
            Ctx.Servicios.Crear(servicio);
        }
        AbrirServicio(servicio);
    }

    public void AbrirServicio(Servicio servicio)
    {
        GuardarServicioAhora();
        Servicio = servicio;
        Elementos.Clear();
        foreach (var e in servicio.Elementos) Elementos.Add(CrearVm(e));
        Ctx.Preferencias.ServicioActual = servicio.Id;
        Ctx.GuardarPreferencias();
        OnPropertyChanged(nameof(NombreServicio));
        ElementoSeleccionado = null;
        MarcarElementoVivo();
        EstadoGuardado = "";
    }

    private ElementoViewModel CrearVm(ElementoServicio e) => new(e, ProgramarGuardado);

    [RelayCommand]
    private void NuevoServicio()
    {
        var nombre = Dialogo.PedirTexto("Nuevo servicio", "Nombre del servicio:", NombrePredeterminado());
        if (string.IsNullOrWhiteSpace(nombre)) return;
        var servicio = new Servicio { Nombre = nombre, Fecha = DateTime.Today };
        Ctx.Servicios.Crear(servicio);
        AbrirServicio(servicio);
        MostrarMensaje($"Servicio «{nombre}» creado.");
    }

    [RelayCommand]
    private void AbrirOtroServicio()
    {
        GuardarServicioAhora();
        var id = Ventanas.ElegirServicio(this);
        if (id is null) return;
        var servicio = Ctx.Servicios.Obtener(id.Value);
        if (servicio is not null) AbrirServicio(servicio);
    }

    [RelayCommand]
    private void DuplicarServicio()
    {
        GuardarServicioAhora();
        var id = Ctx.Servicios.Duplicar(Servicio.Id, $"{Servicio.Nombre} (copia)");
        var copia = Ctx.Servicios.Obtener(id);
        if (copia is not null) AbrirServicio(copia);
        MostrarMensaje("Servicio duplicado.");
    }

    /// <summary>Si el elemento que se ve en la cuadrícula es el mismo que se quiere agregar, se reutiliza.</summary>
    public ElementoServicio ReutilizarActual(ElementoServicio nuevo)
    {
        var actual = ElementoActual;
        if (actual is not null && !ActualEnServicio && actual.Tipo == nuevo.Tipo && actual.ReferenciaId == nuevo.ReferenciaId && actual.Datos == nuevo.Datos)
            return actual;
        return nuevo;
    }

    public void AgregarAlServicio(ElementoServicio elemento, int? posicion = null)
    {
        if (Elementos.Any(e => ReferenceEquals(e.Modelo, elemento))) elemento = elemento.Clonar();
        elemento.Id = 0;
        var vm = CrearVm(elemento);
        if (posicion is int p && p >= 0 && p <= Elementos.Count) Elementos.Insert(p, vm);
        else Elementos.Add(vm);
        GuardarServicioAhora();
        OnPropertyChanged(nameof(ActualEnServicio));
        MostrarMensaje($"«{elemento.Titulo}» se agregó al servicio.");
    }

    [RelayCommand]
    private void AgregarActualAlServicio()
    {
        if (ElementoActual is null) return;
        AgregarAlServicio(ElementoActual);
    }

    [RelayCommand]
    private void QuitarElemento(ElementoViewModel? vm)
    {
        vm ??= ElementoSeleccionado;
        if (vm is null) return;
        Elementos.Remove(vm);
        GuardarServicioAhora();
        OnPropertyChanged(nameof(ActualEnServicio));
    }

    public void MoverElemento(ElementoViewModel vm, int nuevoIndice)
    {
        var actual = Elementos.IndexOf(vm);
        if (actual < 0) return;
        nuevoIndice = Math.Clamp(nuevoIndice, 0, Elementos.Count - 1);
        if (actual == nuevoIndice) return;
        Elementos.Move(actual, nuevoIndice);
        GuardarServicioAhora();
    }

    [RelayCommand]
    private void SubirElemento(ElementoViewModel? vm)
    {
        vm ??= ElementoSeleccionado;
        if (vm is not null) MoverElemento(vm, Elementos.IndexOf(vm) - 1);
    }

    [RelayCommand]
    private void BajarElemento(ElementoViewModel? vm)
    {
        vm ??= ElementoSeleccionado;
        if (vm is not null) MoverElemento(vm, Elementos.IndexOf(vm) + 1);
    }

    [RelayCommand]
    private void AgregarEncabezado()
    {
        var nombre = Dialogo.PedirTexto("Encabezado de sección", "Nombre de la sección (Bienvenida, Alabanza, Palabra, Ofrenda, Despedida…):", "Alabanza");
        if (string.IsNullOrWhiteSpace(nombre)) return;
        var normal = Coraza.Core.Busqueda.Normalizador.Normalizar(nombre);
        var color = normal switch
        {
            var n when n.Contains("bienvenida") => "#FC8F8F",
            var n when n.Contains("palabra") || n.Contains("predica") || n.Contains("sermon") => "#D9A441",
            var n when n.Contains("ofrenda") || n.Contains("diezmo") => "#4E8F6A",
            var n when n.Contains("despedida") || n.Contains("anuncio") => "#A99597",
            _ => "#B44446",
        };
        var indice = ElementoSeleccionado is null ? (int?)null : Elementos.IndexOf(ElementoSeleccionado) + 1;
        AgregarAlServicio(FabricaElementos.Encabezado(nombre, color), indice);
    }

    [RelayCommand]
    private void EditarDuracion(ElementoViewModel? vm)
    {
        vm ??= ElementoSeleccionado;
        if (vm is null) return;
        var texto = Dialogo.PedirTexto("Tiempo estimado", $"Minutos para «{vm.Titulo}»:", vm.DuracionMin?.ToString() ?? "");
        if (texto is null) return;
        vm.DuracionMin = int.TryParse(texto, out var minutos) && minutos > 0 ? minutos : null;
        ActualizarDuracion();
    }

    [RelayCommand]
    private void RenombrarElemento(ElementoViewModel? vm)
    {
        vm ??= ElementoSeleccionado;
        if (vm is null) return;
        var nombre = Dialogo.PedirTexto("Renombrar", "Nombre visible en el orden del servicio:", vm.Titulo);
        if (!string.IsNullOrWhiteSpace(nombre)) vm.Titulo = nombre;
    }

    private void ProgramarGuardado()
    {
        _temporizadorGuardado.Stop();
        _temporizadorGuardado.Start();
        ActualizarDuracion();
    }

    /// <summary>Autoguardado: el servicio se guarda tras cada cambio para recuperarlo si el equipo se apaga.</summary>
    public void GuardarServicioAhora()
    {
        _temporizadorGuardado.Stop();
        if (Servicio.Id == 0) return;
        try
        {
            Ctx.Servicios.GuardarElementos(Servicio.Id, Elementos.Select(e => e.Modelo).ToList());
            EstadoGuardado = $"Guardado {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo guardar el servicio");
            MostrarMensaje("No se pudo guardar el servicio. Se reintentará con el próximo cambio.", esError: true);
        }
    }

    private void ActualizarDuracion()
    {
        var total = Elementos.Sum(e => e.DuracionMin ?? 0);
        DuracionTotal = total <= 0 ? "" : total >= 60 ? $"Total: {total / 60} h {total % 60} min" : $"Total: {total} min";
    }

    partial void OnElementoSeleccionadoChanged(ElementoViewModel? value)
    {
        if (value is { EsEncabezado: false }) MostrarElemento(value.Modelo);
    }

    // ================= Elemento actual =================

    public void MostrarElemento(ElementoServicio elemento, int indicePrevia = 0)
    {
        ElementoActual = elemento;
        var resultado = Proveedor.Generar(elemento);
        TemaActual = resultado.Tema;
        AvisoActual = resultado.Aviso;

        Diapositivas.Clear();
        for (var i = 0; i < resultado.Diapositivas.Count; i++)
            Diapositivas.Add(new DiapositivaViewModel(resultado.Diapositivas[i], resultado.Tema, i));

        var tipo = elemento.Tipo switch
        {
            TipoElemento.Cancion => "Canción",
            TipoElemento.Pasaje => "Biblia",
            TipoElemento.Imagen => "Medio",
            TipoElemento.Texto => "Texto",
            TipoElemento.CuentaRegresiva => "Cuenta regresiva",
            _ => "Sección",
        };
        TituloActual = elemento.Titulo;
        SubtituloActual = $"{tipo} · {Diapositivas.Count} diapositiva{(Diapositivas.Count == 1 ? "" : "s")} · clic: vista previa, doble clic: al vivo";

        _cargandoTema = true;
        TemaElegido = OpcionesTema.FirstOrDefault(o => o.Id == elemento.TemaId) ?? OpcionesTema.FirstOrDefault();
        _cargandoTema = false;

        _cargandoVersion = true;
        VersionElegida = elemento.Tipo == TipoElemento.Pasaje ? VersionesBiblia.FirstOrDefault(v => v.Id == elemento.ReferenciaId) : null;
        _cargandoVersion = false;

        if (ReferenceEquals(elemento, _elementoVivo))
        {
            _diapositivasVivo = Diapositivas.ToList();
            indicePrevia = Math.Min(_indiceVivo + 1, Diapositivas.Count - 1);
        }
        MarcarDiapositivaViva();
        DiapositivaPrevia = Diapositivas.ElementAtOrDefault(Math.Clamp(indicePrevia, 0, Math.Max(0, Diapositivas.Count - 1)));
        OnPropertyChanged(nameof(HayElementoActual));
        OnPropertyChanged(nameof(ActualEnServicio));
        OnPropertyChanged(nameof(ActualEditable));
        OnPropertyChanged(nameof(EsPasaje));
        if (Diapositivas.FirstOrDefault()?.Diapositiva.RutaImagen is { } ruta) CacheImagenes.Precargar(ruta);
    }

    /// <summary>Vuelve a generar las diapositivas (tras editar la canción o cambiar el tema) sin perder la posición.</summary>
    public void RefrescarElementoActual()
    {
        if (ElementoActual is not null)
        {
            var indice = DiapositivaPrevia?.Indice ?? 0;
            var eraVivo = ReferenceEquals(ElementoActual, _elementoVivo);
            MostrarElemento(ElementoActual, indice);
            if (eraVivo && _indiceVivo >= 0 && _indiceVivo < _diapositivasVivo.Count)
            {
                var d = _diapositivasVivo[_indiceVivo];
                Proyeccion.Mostrar(d.Diapositiva, d.Tema);
                ActualizarInfoVivo();
            }
        }
        else if (_elementoVivo is not null)
        {
            var r = Proveedor.Generar(_elementoVivo);
            _diapositivasVivo = r.Diapositivas.Select((d, i) => new DiapositivaViewModel(d, r.Tema, i)).ToList();
            _indiceVivo = Math.Min(_indiceVivo, _diapositivasVivo.Count - 1);
            ActualizarInfoVivo();
        }
    }

    /// <summary>Tras editar una canción: actualiza los títulos en el servicio y las diapositivas visibles.</summary>
    public void AlModificarCancion(long id)
    {
        var cancion = Ctx.Canciones.Obtener(id);
        if (cancion is not null)
        {
            foreach (var vm in Elementos.Where(e => e.Tipo == TipoElemento.Cancion && e.Modelo.ReferenciaId == id))
            {
                vm.Modelo.Titulo = cancion.Titulo;
                vm.NotificarTitulo();
            }
            ProgramarGuardado();
        }
        if (ElementoActual is { Tipo: TipoElemento.Cancion } actual && actual.ReferenciaId == id)
        {
            if (cancion is not null) actual.Titulo = cancion.Titulo;
            RefrescarElementoActual();
        }
    }

    [RelayCommand]
    private void EditarActual()
    {
        if (ElementoActual is null) return;
        switch (ElementoActual.Tipo)
        {
            case TipoElemento.Cancion when ElementoActual.ReferenciaId is long id:
                Canciones.EditarPorId(id);
                break;
            case TipoElemento.Texto when ElementoActual.ReferenciaId is long id:
                Textos.EditarPorId(id);
                break;
        }
    }

    private void ReconstruirOpcionesTema()
    {
        _cargandoTema = true;
        var idActual = TemaElegido?.Id;
        OpcionesTema = new[] { new OpcionTema(null, "Tema predeterminado") }
            .Concat(Proveedor.Temas.Select(t => new OpcionTema(t.Id, t.Nombre)))
            .ToList();
        TemaElegido = OpcionesTema.FirstOrDefault(o => o.Id == idActual) ?? OpcionesTema[0];
        _cargandoTema = false;
    }

    partial void OnTemaElegidoChanged(OpcionTema? value)
    {
        if (_cargandoTema || ElementoActual is null || value is null) return;
        if (ElementoActual.TemaId == value.Id) return;
        ElementoActual.TemaId = value.Id;
        if (ActualEnServicio) GuardarServicioAhora();
        RefrescarElementoActual();
    }

    /// <summary>Lista de versiones instaladas para el selector de versión del pasaje.</summary>
    public void RecargarVersiones()
    {
        _cargandoVersion = true;
        VersionesBiblia.Clear();
        foreach (var v in Ctx.Biblias.Listar()) VersionesBiblia.Add(v);
        VersionElegida = ElementoActual is { Tipo: TipoElemento.Pasaje } e ? VersionesBiblia.FirstOrDefault(v => v.Id == e.ReferenciaId) : null;
        _cargandoVersion = false;
    }

    /// <summary>Cambia la versión del pasaje elegido (RV1909 ↔ RVR1960…), aunque ya esté en el servicio o en vivo.</summary>
    partial void OnVersionElegidaChanged(VersionBiblia? value)
    {
        if (_cargandoVersion || value is null || ElementoActual is not { Tipo: TipoElemento.Pasaje } elemento || elemento.ReferenciaId == value.Id) return;
        elemento.ReferenciaId = value.Id;
        if (FabricaElementos.LeerReferencia(elemento.Datos) is { } referencia) elemento.Titulo = $"{referencia} · {value.Abreviatura}";
        foreach (var vm in Elementos.Where(x => ReferenceEquals(x.Modelo, elemento))) vm.NotificarTitulo();
        if (ActualEnServicio) GuardarServicioAhora();
        RefrescarElementoActual();
        MostrarMensaje($"Pasaje en {value.Nombre}.");
    }

    // ================= Vista previa y en vivo =================

    [RelayCommand]
    private void EnviarAlVivo()
    {
        if (ElementoActual is null || DiapositivaPrevia is null)
        {
            MostrarMensaje("Primero elige una diapositiva para la vista previa.");
            return;
        }
        AsegurarProyeccion();
        PonerEnVivo(ElementoActual, Diapositivas.ToList(), DiapositivaPrevia.Indice);
    }

    /// <summary>Doble clic en una miniatura: directo al vivo.</summary>
    public void EnviarDiapositivaAlVivo(DiapositivaViewModel diapositiva)
    {
        if (ElementoActual is null) return;
        DiapositivaPrevia = diapositiva;
        AsegurarProyeccion();
        PonerEnVivo(ElementoActual, Diapositivas.ToList(), diapositiva.Indice);
    }

    private void AsegurarProyeccion()
    {
        if (Proyeccion.Proyectando) return;
        Proyeccion.Iniciar();
        if (UnaSolaPantalla)
            MostrarMensaje("Solo hay una pantalla conectada: la proyección se abrió en una ventana de ensayo.");
    }

    private void PonerEnVivo(ElementoServicio elemento, List<DiapositivaViewModel> lista, int indice)
    {
        if (indice < 0 || indice >= lista.Count) return;
        var cambioDeElemento = !ReferenceEquals(elemento, _elementoVivo);
        _elementoVivo = elemento;
        _diapositivasVivo = lista;
        _indiceVivo = indice;

        var d = lista[indice];
        var diapositiva = d.Diapositiva;
        // La cuenta regresiva fija su final al salir al vivo, así la salida y el monitor coinciden al segundo.
        if (diapositiva.Tipo == TipoDiapositiva.CuentaRegresiva && diapositiva.FinCuenta is null)
            diapositiva = diapositiva.ConFinCuenta(diapositiva.CalcularFinCuenta(DateTime.Now));
        Proyeccion.Mostrar(diapositiva, d.Tema);
        ProgramarAvance(elemento);

        if (cambioDeElemento)
        {
            RegistrarUso(elemento);
            ActualizarSeccionesVivo();
            MarcarElementoVivo();
        }
        MarcarDiapositivaViva();
        ActualizarInfoVivo();

        // La vista previa avanza a la siguiente diapositiva para que el operador vea lo que viene.
        if (ReferenceEquals(ElementoActual, elemento) && indice + 1 < Diapositivas.Count)
            DiapositivaPrevia = Diapositivas[indice + 1];

        if (indice + 1 < lista.Count && lista[indice + 1].Diapositiva.RutaImagen is { } siguiente)
            CacheImagenes.Precargar(siguiente);
    }

    [RelayCommand]
    private void Siguiente()
    {
        if (_elementoVivo is null)
        {
            MoverPrevia(1);
            return;
        }
        if (_indiceVivo + 1 < _diapositivasVivo.Count)
        {
            PonerEnVivo(_elementoVivo, _diapositivasVivo, _indiceVivo + 1);
            return;
        }
        // Fin del elemento: se prepara el siguiente del servicio en la vista previa, sin enviarlo.
        var actual = Elementos.FirstOrDefault(e => ReferenceEquals(e.Modelo, _elementoVivo));
        if (actual is not null && SiguienteNoEncabezado(Elementos.IndexOf(actual), 1) is { } proximo)
        {
            ElementoSeleccionado = proximo;
            MostrarMensaje($"Fin de «{actual.Titulo}». Enter para enviar «{proximo.Titulo}».");
        }
    }

    [RelayCommand]
    private void Anterior()
    {
        if (_elementoVivo is null)
        {
            MoverPrevia(-1);
            return;
        }
        if (_indiceVivo > 0) PonerEnVivo(_elementoVivo, _diapositivasVivo, _indiceVivo - 1);
    }

    private void MoverPrevia(int delta)
    {
        if (Diapositivas.Count == 0) return;
        var indice = Math.Clamp((DiapositivaPrevia?.Indice ?? -1) + delta, 0, Diapositivas.Count - 1);
        DiapositivaPrevia = Diapositivas[indice];
    }

    [RelayCommand]
    private void SiguienteElemento() => SeleccionarVecino(1);

    [RelayCommand]
    private void ElementoAnterior() => SeleccionarVecino(-1);

    private void SeleccionarVecino(int direccion)
    {
        if (Elementos.Count == 0) return;
        var desde = ElementoSeleccionado is null ? (direccion > 0 ? -1 : Elementos.Count) : Elementos.IndexOf(ElementoSeleccionado);
        if (SiguienteNoEncabezado(desde, direccion) is { } vecino) ElementoSeleccionado = vecino;
    }

    private ElementoViewModel? SiguienteNoEncabezado(int desde, int direccion)
    {
        for (var i = desde + direccion; i >= 0 && i < Elementos.Count; i += direccion)
        {
            if (!Elementos[i].EsEncabezado) return Elementos[i];
        }
        return null;
    }

    /// <summary>Salta a una sección (C, V1, P…) de la canción en vivo; si no hay nada en vivo, mueve la vista previa.</summary>
    [RelayCommand]
    public void IrASeccion(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return;
        var enVivo = _elementoVivo is not null;
        var lista = enVivo ? _diapositivasVivo : Diapositivas.ToList();
        if (lista.Count == 0) return;
        var desde = enVivo ? _indiceVivo : DiapositivaPrevia?.Indice ?? -1;
        var indice = BuscarSeccion(lista, codigo, desde);
        if (indice < 0)
        {
            MostrarMensaje($"Esta canción no tiene la sección {codigo}.");
            return;
        }
        if (enVivo) PonerEnVivo(_elementoVivo!, lista, indice);
        else DiapositivaPrevia = Diapositivas[indice];
    }

    private static int BuscarSeccion(List<DiapositivaViewModel> lista, string codigo, int desde)
    {
        bool Coincide(DiapositivaViewModel d, int i) =>
            string.Equals(d.Diapositiva.CodigoSeccion, codigo, StringComparison.OrdinalIgnoreCase)
            && (i == 0 || !string.Equals(lista[i - 1].Diapositiva.CodigoSeccion, codigo, StringComparison.OrdinalIgnoreCase));
        for (var i = desde + 1; i < lista.Count; i++) if (Coincide(lista[i], i)) return i;
        for (var i = 0; i <= Math.Min(desde, lista.Count - 1); i++) if (Coincide(lista[i], i)) return i;
        return -1;
    }

    /// <summary>Avance automático (fotos, anuncios en bucle, sala de espera).</summary>
    private void ProgramarAvance(ElementoServicio elemento)
    {
        _temporizadorAvance.Stop();
        if (elemento.AvanceSegundos is not > 0) return;
        _temporizadorAvance.Interval = TimeSpan.FromSeconds(elemento.AvanceSegundos.Value);
        _temporizadorAvance.Start();
    }

    private void AvanceAutomatico()
    {
        if (_elementoVivo is null || _diapositivasVivo.Count == 0)
        {
            _temporizadorAvance.Stop();
            return;
        }
        if (_indiceVivo + 1 < _diapositivasVivo.Count) PonerEnVivo(_elementoVivo, _diapositivasVivo, _indiceVivo + 1);
        else if (_elementoVivo.Bucle) PonerEnVivo(_elementoVivo, _diapositivasVivo, 0);
        else _temporizadorAvance.Stop();
    }

    [RelayCommand]
    private void EditarAvance(ElementoViewModel? vm)
    {
        vm ??= ElementoSeleccionado;
        if (vm is null || vm.EsEncabezado) return;
        var texto = Dialogo.PedirTexto("Avance automático", $"Segundos por diapositiva para «{vm.Titulo}» (0 = avanzar a mano). Útil para fotos y anuncios:",
            vm.AvanceSegundos?.ToString() ?? "0");
        if (texto is null) return;
        vm.AvanceSegundos = int.TryParse(texto, out var segundos) && segundos > 0 ? Math.Min(segundos, 3600) : null;
        if (ReferenceEquals(vm.Modelo, _elementoVivo)) ProgramarAvance(vm.Modelo);
    }

    [RelayCommand]
    private void AgregarCuentaRegresiva()
    {
        var entrada = Dialogo.PedirTexto("Cuenta regresiva", "Minutos (por ejemplo 5) o la hora de inicio (por ejemplo 10:00):", "5");
        if (string.IsNullOrWhiteSpace(entrada)) return;
        entrada = entrada.Trim();
        int? minutos = null;
        string? hora = null;
        if (entrada.Contains(':') && TimeSpan.TryParse(entrada, System.Globalization.CultureInfo.InvariantCulture, out _)) hora = entrada;
        else if (int.TryParse(entrada, out var m) && m > 0) minutos = m;
        else
        {
            MostrarMensaje("Escribe minutos (5) o una hora (10:00).", esError: true);
            return;
        }
        var texto = Dialogo.PedirTexto("Cuenta regresiva", "Texto sobre el reloj:", "El servicio comienza en");
        if (texto is null) return;
        var elemento = FabricaElementos.DeCuentaRegresiva(new DatosCuentaRegresiva(minutos, hora, texto, "¡Bienvenidos!"));
        var indice = ElementoSeleccionado is null ? 0 : Elementos.IndexOf(ElementoSeleccionado) + 1;
        AgregarAlServicio(elemento, indice);
        ElementoSeleccionado = Elementos.FirstOrDefault(e => ReferenceEquals(e.Modelo, elemento));
    }

    [RelayCommand]
    private void EditarMarquesina()
    {
        var texto = Dialogo.PedirTexto("Marquesina", "Texto que se desplaza en la parte inferior de la proyección (déjalo vacío para quitarla):",
            Proyeccion.Marquesina ?? "");
        if (texto is null) return;
        Proyeccion.Marquesina = string.IsNullOrWhiteSpace(texto) ? null : texto;
        MostrarMensaje(Proyeccion.HayMarquesina ? "Marquesina en pantalla." : "Marquesina retirada.");
    }

    [RelayCommand]
    private void EnviarMensajeUrgente()
    {
        var texto = Dialogo.PedirTexto("Mensaje urgente",
            "Aparece sobre cualquier contenido y desaparece solo en 20 segundos. Por ejemplo: «Padres del niño con código 24, acercarse a la guardería».");
        if (string.IsNullOrWhiteSpace(texto)) return;
        AsegurarProyeccion();
        Proyeccion.MensajeUrgente(texto, 20);
        MostrarMensaje("Mensaje urgente en pantalla durante 20 segundos.");
    }

    [RelayCommand]
    private void AlternarReloj() => Proyeccion.RelojVisible = !Proyeccion.RelojVisible;

    [RelayCommand]
    private void VideoReproducirPausar() =>
        Proyeccion.ControlarVideo(Proyeccion.VideoPausado ? AccionVideo.Reproducir : AccionVideo.Pausar);

    [RelayCommand]
    private void VideoReiniciar() => Proyeccion.ControlarVideo(AccionVideo.Reiniciar);

    [RelayCommand]
    private void MusicaAlternar() => Proyeccion.AlternarMusica();

    [RelayCommand]
    private void MusicaDetener() => Proyeccion.DetenerMusica();

    private void ActualizarEstadoVideo()
    {
        var estado = Proyeccion.HayVideo ? Proyeccion.EstadoVideo() : null;
        var texto = estado is { } e
            ? $"{Formatear(e.Posicion)} / {(e.Duracion is { } d ? Formatear(d) : "--:--")}"
            : "";
        if (texto != EstadoVideo) EstadoVideo = texto;

        static string Formatear(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    [RelayCommand]
    private void Limpiar()
    {
        _temporizadorAvance.Stop();
        Proyeccion.Mostrar(null, Proyeccion.TemaVivo ?? TemaActual ?? new Tema());
        Proyeccion.TextoOculto = false;
        _elementoVivo = null;
        _diapositivasVivo = new List<DiapositivaViewModel>();
        _indiceVivo = -1;
        SeccionesVivo.Clear();
        MarcarElementoVivo();
        MarcarDiapositivaViva();
        ActualizarInfoVivo();
    }

    [RelayCommand]
    private void AlternarNegro() => Proyeccion.Negro = !Proyeccion.Negro;

    [RelayCommand]
    private void AlternarLogo() => Proyeccion.Logo = !Proyeccion.Logo;

    [RelayCommand]
    private void AlternarTexto() => Proyeccion.TextoOculto = !Proyeccion.TextoOculto;

    [RelayCommand]
    private void AlternarPatronPrueba()
    {
        if (!Proyeccion.PatronPrueba) AsegurarProyeccion();
        Proyeccion.PatronPrueba = !Proyeccion.PatronPrueba;
    }

    [RelayCommand]
    public void AlternarProyeccion()
    {
        if (!Proyeccion.Proyectando)
        {
            AsegurarProyeccion();
            return;
        }
        if (Dialogo.Confirmar("Detener la proyección", "El público dejará de ver la pantalla. ¿Deseas detener la proyección?", "Detener", peligro: true))
            Proyeccion.Detener();
    }

    /// <summary>Esc en pantalla completa: sale de ella y devuelve la pantalla al operador.</summary>
    public void SalirPantallaCompleta()
    {
        Proyeccion.Detener();
        MostrarMensaje("Saliste de la pantalla completa. Pulsa F5 para volver a proyectar.");
    }

    [RelayCommand]
    private void AlternarApariencia()
    {
        var nueva = Apariencia.Siguiente(Ctx.Preferencias.Paleta);
        Ctx.Preferencias.Paleta = nueva;
        Ctx.GuardarPreferencias();
        Apariencia.Aplicar(nueva);
        MostrarMensaje($"Apariencia: {nueva}.");
    }

    [RelayCommand]
    private void AlternarConcentracion() => ModoConcentracion = !ModoConcentracion;

    [RelayCommand]
    private void AbrirBusqueda() => Busqueda.Abrir();

    private void MarcarElementoVivo()
    {
        foreach (var e in Elementos) e.EnVivo = _elementoVivo is not null && ReferenceEquals(e.Modelo, _elementoVivo);
    }

    private void MarcarDiapositivaViva()
    {
        var mismo = ElementoActual is not null && ReferenceEquals(ElementoActual, _elementoVivo);
        foreach (var d in Diapositivas) d.EsVivo = mismo && d.Indice == _indiceVivo;
    }

    private void ActualizarInfoVivo()
    {
        if (_elementoVivo is null || _indiceVivo < 0 || _indiceVivo >= _diapositivasVivo.Count)
        {
            TituloVivo = "Nada en vivo";
            SiguienteTexto = "";
            return;
        }
        var d = _diapositivasVivo[_indiceVivo];
        TituloVivo = $"{_elementoVivo.Titulo} · {d.Etiqueta} ({_indiceVivo + 1}/{_diapositivasVivo.Count})";
        SiguienteTexto = _indiceVivo + 1 < _diapositivasVivo.Count
            ? _diapositivasVivo[_indiceVivo + 1].TextoPlano.Replace('\n', ' ')
            : "— Fin del elemento —";
    }

    private void ActualizarSeccionesVivo()
    {
        SeccionesVivo.Clear();
        foreach (var codigo in _diapositivasVivo.Select(d => d.Diapositiva.CodigoSeccion).Where(c => !string.IsNullOrEmpty(c)).Distinct())
            SeccionesVivo.Add(codigo!);
    }

    /// <summary>Estadística de uso (reportes CCLI): una vez por elemento y sesión.</summary>
    private void RegistrarUso(ElementoServicio elemento)
    {
        if (!_usoRegistrado.Add(elemento)) return;
        try
        {
            if (elemento is { Tipo: TipoElemento.Cancion, ReferenciaId: long id }) Ctx.Canciones.RegistrarUso(id);
            Ctx.Historial.Registrar(elemento.Tipo.ToString(), elemento.ReferenciaId, elemento.Titulo);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se registró el uso de {Titulo}", elemento.Titulo);
        }
    }

    // ================= Pantallas =================

    public void ActualizarPantallas()
    {
        var lista = GestorPantallas.Enumerar();
        _cargandoPantallas = true;
        Pantallas.Clear();
        Pantallas.Add(new OpcionPantalla(null, "Automática (segunda pantalla)"));
        foreach (var p in lista) Pantallas.Add(new OpcionPantalla(p.Dispositivo, p.Descripcion));
        Pantallas.Add(new OpcionPantalla(Preferencias.PantallaFlotante, "Ventana de ensayo (flotante)"));
        PantallaSeleccionada = Pantallas.FirstOrDefault(o => o.Clave == Ctx.Preferencias.Pantalla) ?? Pantallas[0];
        _cargandoPantallas = false;
        AvisoDuplicar = GestorPantallas.ModoDuplicar();
        UnaSolaPantalla = lista.Count < 2;
    }

    /// <summary>Se llama cuando Windows avisa que se conectó o desconectó una pantalla.</summary>
    public void AlCambiarPantallas()
    {
        var antes = Pantallas.Count;
        ActualizarPantallas();
        Proyeccion.Recolocar();
        MostrarMensaje(Pantallas.Count > antes ? "Se detectó una nueva pantalla." : "Cambió la configuración de pantallas.");
    }

    partial void OnPantallaSeleccionadaChanged(OpcionPantalla? value)
    {
        if (_cargandoPantallas || value is null) return;
        Ctx.Preferencias.Pantalla = value.Clave;
        Ctx.GuardarPreferencias();
        Proyeccion.Recolocar();
    }

    [RelayCommand]
    private void CambiarAExtender()
    {
        if (GestorPantallas.CambiarAExtender()) MostrarMensaje("Cambiando Windows a modo «Extender»…");
        else MostrarMensaje("No se pudo cambiar automáticamente. Pulsa Win + P y elige «Extender».", esError: true);
    }

    partial void OnTransicionSeleccionadaChanged(OpcionTransicion value)
    {
        Ctx.Preferencias.TransicionForzada = value.Valor;
        Ctx.GuardarPreferencias();
        OnPropertyChanged(nameof(TransicionManual));
        // Con «Reducir movimiento» la salida corta en seco: se avisa para que el efecto elegido no parezca roto.
        if (value.Valor is not null && Ctx.Preferencias.ReducirMovimiento)
            MostrarMensaje("«Reducir movimiento» está activado: la proyección cambia sin animación. "
                           + "Desmárcalo en Configuración → Proyección para ver las transiciones.", esError: true);
    }

    partial void OnDuracionTransicionChanged(int value)
    {
        Ctx.Preferencias.DuracionForzadaMs = value;
        Ctx.GuardarPreferencias();
    }

    // ================= Configuración y atajos =================

    [RelayCommand]
    private void AbrirConfiguracion()
    {
        if (!Ventanas.Configuracion(this)) return;
        AplicarConfiguracion();
    }

    public void AplicarConfiguracion()
    {
        Atajos = new GestorAtajos(Ctx.Preferencias.Atajos);
        Proyeccion.AplicarPreferencias();
        ActualizarPantallas();
        Proveedor.RecargarTemas();
        Biblia.Recargar();
        Temas.Recargar();
        MostrarMensaje("Configuración guardada.");
    }

    [RelayCommand]
    private void NuevaCancion() => Canciones.NuevaCommand.Execute(null);

    /// <summary>Ejecuta la acción de un atajo de teclado. Devuelve true si se manejó.</summary>
    public bool EjecutarAtajo(AccionAtajo accion)
    {
        switch (accion)
        {
            case AccionAtajo.Siguiente: Siguiente(); break;
            case AccionAtajo.Anterior: Anterior(); break;
            case AccionAtajo.SiguienteElemento: SiguienteElemento(); break;
            case AccionAtajo.ElementoAnterior: ElementoAnterior(); break;
            case AccionAtajo.EnviarAlVivo: EnviarAlVivo(); break;
            case AccionAtajo.PantallaNegra: AlternarNegro(); break;
            case AccionAtajo.PantallaLogo: AlternarLogo(); break;
            case AccionAtajo.OcultarTexto: AlternarTexto(); break;
            case AccionAtajo.Limpiar: Limpiar(); break;
            case AccionAtajo.Buscar: AbrirBusqueda(); break;
            case AccionAtajo.NuevaCancion: NuevaCancion(); break;
            case AccionAtajo.AlternarProyeccion: AlternarProyeccion(); break;
            case AccionAtajo.ModoConcentracion: AlternarConcentracion(); break;
            case AccionAtajo.IrCoro: IrASeccion("C"); break;
            case AccionAtajo.IrPuente: IrASeccion("P"); break;
            case AccionAtajo.IrPreCoro: IrASeccion("PC"); break;
            case AccionAtajo.IrFinal: IrASeccion("F"); break;
            case AccionAtajo.PatronPrueba: AlternarPatronPrueba(); break;
            default: return false;
        }
        return true;
    }

    private static string NombrePredeterminado() =>
        $"Servicio {DateTime.Today.ToString("dddd d 'de' MMMM", new System.Globalization.CultureInfo("es-ES"))}";
}
