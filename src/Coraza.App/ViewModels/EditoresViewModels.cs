using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coraza.App.Servicios;
using Coraza.App.Vistas;
using Coraza.Core.Canciones;
using Coraza.Core.Diapositivas;
using Coraza.Core.Modelos;
using Coraza.Data.Repositorios;
using Coraza.Rendering;
using Microsoft.Win32;

namespace Coraza.App.ViewModels;

// ===================== Editor de canciones =====================

public sealed partial class EditorCancionViewModel : ObservableObject
{
    private readonly Contexto _ctx;
    private readonly ProveedorDiapositivas _proveedor;
    private readonly Cancion _cancion;
    private readonly DispatcherTimer _demora;
    /// <summary>Orden realmente proyectado: el escrito más las secciones que faltaban en él.</summary>
    private List<string> _ordenEfectivo = new();

    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private string _autor = "";
    [ObservableProperty] private string _tonalidad = "";
    [ObservableProperty] private string _tempo = "";
    [ObservableProperty] private string _ccli = "";
    [ObservableProperty] private string _derechos = "";
    [ObservableProperty] private string _categoria = "";
    [ObservableProperty] private string _etiquetas = "";
    [ObservableProperty] private string _letra = "";
    [ObservableProperty] private string _orden = "";
    [ObservableProperty] private string? _avisoOrden;
    [ObservableProperty] private string _resumen = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private OpcionTema? _temaElegido;
    [ObservableProperty] private VersionAnterior? _versionElegida;

    public EditorCancionViewModel(Contexto ctx, ProveedorDiapositivas proveedor, long? id, string? letraInicial)
    {
        _ctx = ctx;
        _proveedor = proveedor;
        _cancion = (id is long i ? ctx.Canciones.Obtener(i) : null) ?? new Cancion();

        _titulo = _cancion.Titulo;
        _autor = _cancion.Autor ?? "";
        _tonalidad = _cancion.Tonalidad ?? "";
        _tempo = _cancion.Tempo?.ToString() ?? "";
        _ccli = _cancion.Ccli ?? "";
        _derechos = _cancion.Derechos ?? "";
        _categoria = _cancion.Categoria ?? "";
        _etiquetas = _cancion.Etiquetas ?? "";
        _letra = _cancion.Id != 0 ? AnalizadorLetra.Formatear(_cancion.Secciones) : letraInicial ?? "";
        _orden = _cancion.Id != 0 ? string.Join(" ", _cancion.Orden) : "";

        OpcionesTema = new[] { new OpcionTema(null, "Tema predeterminado de canciones") }
            .Concat(proveedor.Temas.Select(t => new OpcionTema(t.Id, t.Nombre))).ToList();
        _temaElegido = OpcionesTema.FirstOrDefault(o => o.Id == _cancion.TemaId) ?? OpcionesTema[0];
        Versiones = _cancion.Id != 0 ? ctx.Canciones.VersionesAnteriores(_cancion.Id) : new List<VersionAnterior>();

        _demora = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _demora.Tick += (_, _) => Analizar();
        Analizar();
    }

    public string TituloVentana => _cancion.Id == 0 ? "Nueva canción" : $"Editar canción · {_cancion.Titulo}";
    public IReadOnlyList<OpcionTema> OpcionesTema { get; }
    public IReadOnlyList<string> Categorias => CategoriasCancion.Todas;
    public IReadOnlyList<VersionAnterior> Versiones { get; }
    public bool TieneVersiones => Versiones.Count > 0;
    public ObservableCollection<string> Codigos { get; } = new();
    public ObservableCollection<DiapositivaViewModel> Vista { get; } = new();
    public long? IdGuardado { get; private set; }

    public event Action<bool>? CerrarSolicitado;

    partial void OnLetraChanged(string value) => Reprogramar();
    partial void OnOrdenChanged(string value) => Reprogramar();
    partial void OnTituloChanged(string value) => Reprogramar();
    partial void OnTemaElegidoChanged(OpcionTema? value) => Reprogramar();

    private void Reprogramar()
    {
        _demora.Stop();
        _demora.Start();
    }

    private void Analizar()
    {
        _demora.Stop();
        var resultado = AnalizadorLetra.Analizar(Letra);
        Codigos.Clear();
        foreach (var s in resultado.Secciones) Codigos.Add(s.Codigo);

        var orden = resultado.Orden;
        AvisoOrden = null;
        if (!string.IsNullOrWhiteSpace(Orden))
        {
            var (validos, desconocidos) = AnalizadorLetra.InterpretarOrden(Orden, resultado.Secciones);
            if (desconocidos.Count > 0) AvisoOrden = $"No existen estas secciones: {string.Join(", ", desconocidos)}";
            if (validos.Count > 0) orden = validos;
        }

        // Lo que se escribe se proyecta: una sección que quedó fuera del orden se agrega al final y se avisa.
        orden = AnalizadorLetra.CompletarOrden(orden, resultado.Secciones, out var agregadas);
        _ordenEfectivo = orden;
        if (agregadas.Count > 0)
        {
            var nombres = agregadas.Select(c => resultado.Secciones.First(s => s.Codigo.Equals(c, StringComparison.OrdinalIgnoreCase)).Nombre);
            var aviso = $"Al final se proyectará también (no estaba en el orden): {string.Join(", ", nombres)}";
            AvisoOrden = AvisoOrden is null ? aviso : $"{AvisoOrden} · {aviso}";
        }

        var tema = _proveedor.ObtenerTema(TemaElegido?.Id, TipoContenido.Canciones);
        var cancion = new Cancion { Titulo = Titulo, Autor = Autor, Ccli = Ccli, Derechos = Derechos, Secciones = resultado.Secciones, Orden = orden };
        var diapositivas = resultado.Secciones.Count == 0
            ? Array.Empty<Diapositiva>()
            : GeneradorDiapositivas.DeCancion(cancion, OpcionesDivision.DesdeTema(tema), _ctx.Preferencias.LicenciaCcli);
        Vista.Clear();
        for (var i = 0; i < diapositivas.Count; i++) Vista.Add(new DiapositivaViewModel(diapositivas[i], tema, i));

        Resumen = resultado.Secciones.Count == 0
            ? "Escribe o pega la letra. Usa etiquetas como [Verso 1], [Coro] o [Puente]."
            : $"{resultado.Secciones.Count} secciones · {diapositivas.Count} diapositivas"
              + (resultado.DeteccionAutomatica ? " · coros detectados automáticamente (pulsa «Etiquetar» para revisarlos)" : "");
    }

    [RelayCommand]
    private void ProponerOrden()
    {
        var r = AnalizadorLetra.Analizar(Letra);
        Orden = string.Join(" ", AnalizadorLetra.ProponerOrden(r.Secciones));
    }

    [RelayCommand]
    private void AgregarAlOrden(string? codigo)
    {
        if (!string.IsNullOrWhiteSpace(codigo)) Orden = (Orden + " " + codigo).Trim();
    }

    [RelayCommand]
    private void LimpiarLetra() => Letra = AnalizadorLetra.LimpiarTextoPegado(Letra);

    /// <summary>Asistente de letras: convierte estrofas detectadas en secciones etiquetadas con su orden.</summary>
    [RelayCommand]
    private void Etiquetar()
    {
        var r = AnalizadorLetra.Analizar(AnalizadorLetra.LimpiarTextoPegado(Letra));
        if (r.Secciones.Count == 0) return;
        Letra = AnalizadorLetra.Formatear(r.Secciones);
        Orden = string.Join(" ", r.Orden);
    }

    [RelayCommand]
    private void PegarDelPortapapeles()
    {
        if (!Clipboard.ContainsText()) return;
        var limpio = AnalizadorLetra.LimpiarTextoPegado(Clipboard.GetText());
        Letra = string.IsNullOrWhiteSpace(Letra) ? limpio : Letra.TrimEnd() + "\n\n" + limpio;
    }

    [RelayCommand]
    private void RestaurarVersion()
    {
        if (VersionElegida is null) return;
        if (!Dialogo.Confirmar("Restaurar versión", $"¿Reemplazar la letra actual por la versión del {VersionElegida.Fecha:g}? Se aplicará al guardar.", "Restaurar"))
            return;
        Titulo = VersionElegida.Titulo;
        Letra = VersionElegida.Letra;
        Orden = VersionElegida.Orden ?? "";
    }

    [RelayCommand]
    private void Guardar()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Titulo))
        {
            Error = "Escribe el título de la canción.";
            return;
        }
        var resultado = AnalizadorLetra.Analizar(Letra);
        if (resultado.Secciones.Count == 0)
        {
            Error = "La letra está vacía.";
            return;
        }
        var orden = resultado.Orden;
        if (!string.IsNullOrWhiteSpace(Orden))
        {
            var validos = AnalizadorLetra.InterpretarOrden(Orden, resultado.Secciones).Validos;
            if (validos.Count > 0) orden = validos;
        }
        // Se guarda el orden completo (con las secciones que faltaban), igual que muestra la vista previa.
        orden = AnalizadorLetra.CompletarOrden(orden, resultado.Secciones, out _);

        _cancion.Titulo = Titulo.Trim();
        _cancion.Autor = Nulo(Autor);
        _cancion.Tonalidad = Nulo(Tonalidad);
        _cancion.Tempo = int.TryParse(Tempo, out var t) && t > 0 ? t : null;
        _cancion.Ccli = Nulo(Ccli);
        _cancion.Derechos = Nulo(Derechos);
        _cancion.Categoria = Nulo(Categoria);
        _cancion.Etiquetas = Nulo(Etiquetas);
        _cancion.TemaId = TemaElegido?.Id;
        _cancion.Secciones = resultado.Secciones;
        _cancion.Orden = orden;
        IdGuardado = _ctx.Canciones.Guardar(_cancion);
        CerrarSolicitado?.Invoke(true);
    }

    [RelayCommand]
    private void Cancelar() => CerrarSolicitado?.Invoke(false);

    private static string? Nulo(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}

// ===================== Editor de textos y anuncios =====================

public sealed record PlantillaTexto(string Nombre, string Titulo, string Contenido);

public sealed partial class EditorTextoViewModel : ObservableObject
{
    private readonly Contexto _ctx;
    private readonly ProveedorDiapositivas _proveedor;
    private readonly TextoLibre _texto;

    [ObservableProperty] private string _titulo;
    [ObservableProperty] private string _contenido;
    [ObservableProperty] private OpcionTema? _temaElegido;
    [ObservableProperty] private PlantillaTexto? _plantilla;
    [ObservableProperty] private string? _error;

    public EditorTextoViewModel(Contexto ctx, ProveedorDiapositivas proveedor, TextoLibre texto)
    {
        _ctx = ctx;
        _proveedor = proveedor;
        _texto = texto;
        _titulo = texto.Titulo;
        _contenido = texto.Contenido;
        OpcionesTema = new[] { new OpcionTema(null, "Tema predeterminado de textos") }
            .Concat(proveedor.Temas.Select(t => new OpcionTema(t.Id, t.Nombre))).ToList();
        _temaElegido = OpcionesTema.FirstOrDefault(o => o.Id == texto.TemaId) ?? OpcionesTema[0];
        ActualizarVista();
    }

    /// <summary>Vista previa de las diapositivas mientras se escribe.</summary>
    public ObservableCollection<DiapositivaViewModel> Vista { get; } = new();

    partial void OnContenidoChanged(string value) => ActualizarVista();

    partial void OnTemaElegidoChanged(OpcionTema? value) => ActualizarVista();

    private void ActualizarVista()
    {
        var tema = _proveedor.ObtenerTema(TemaElegido?.Id, TipoContenido.Textos);
        var diapositivas = GeneradorDiapositivas.DeTexto(new TextoLibre { Titulo = Titulo, Contenido = Contenido ?? "" });
        Vista.Clear();
        for (var i = 0; i < diapositivas.Count; i++) Vista.Add(new DiapositivaViewModel(diapositivas[i], tema, i));
    }

    public string TituloVentana => _texto.Id == 0 ? "Nuevo texto o anuncio" : $"Editar · {_texto.Titulo}";
    public IReadOnlyList<OpcionTema> OpcionesTema { get; }

    public IReadOnlyList<PlantillaTexto> Plantillas { get; } = new[]
    {
        new PlantillaTexto("Bienvenida", "Bienvenida", "¡Bienvenidos!\nNos alegra que estés aquí\n\nApaga o silencia tu celular\npara disfrutar del servicio"),
        new PlantillaTexto("Ofrenda", "Ofrenda", "Tiempo de ofrenda\n\n«Cada uno dé como propuso en su corazón»\n2 Corintios 9:7"),
        new PlantillaTexto("Evento", "Próximo evento", "Nombre del evento\nFecha · Hora\nLugar\n\n¡Te esperamos!"),
        new PlantillaTexto("Cumpleaños", "Cumpleaños del mes", "¡Feliz cumpleaños!\n\nNombre\nNombre\nNombre"),
        new PlantillaTexto("Bautizos", "Bautizos", "Celebramos los bautizos\n\n«El que creyere y fuere bautizado, será salvo»\nMarcos 16:16"),
    };

    public event Action<bool>? CerrarSolicitado;

    partial void OnPlantillaChanged(PlantillaTexto? value)
    {
        if (value is null) return;
        if (!string.IsNullOrWhiteSpace(Contenido) && !Dialogo.Confirmar("Usar plantilla", "¿Reemplazar el contenido actual por la plantilla?", "Reemplazar"))
            return;
        Titulo = value.Titulo;
        Contenido = value.Contenido;
    }

    [RelayCommand]
    private void Guardar()
    {
        if (string.IsNullOrWhiteSpace(Titulo))
        {
            Error = "Escribe un título.";
            return;
        }
        _texto.Titulo = Titulo.Trim();
        _texto.Contenido = Contenido.Trim();
        _texto.TemaId = TemaElegido?.Id;
        _ctx.Textos.Guardar(_texto);
        CerrarSolicitado?.Invoke(true);
    }

    [RelayCommand]
    private void Cancelar() => CerrarSolicitado?.Invoke(false);
}

// ===================== Editor de temas =====================

public sealed partial class EditorTemaViewModel : ObservableObject
{
    private readonly Contexto _ctx;
    private readonly Tema _tema;
    private bool _cargando = true;

    [ObservableProperty] private string _nombre = "";
    [ObservableProperty] private TipoFondo _tipoFondo;
    [ObservableProperty] private string _color1 = "";
    [ObservableProperty] private string _color2 = "";
    [ObservableProperty] private double _angulo;
    [ObservableProperty] private string? _rutaImagen;
    [ObservableProperty] private double _oscurecer;
    [ObservableProperty] private double _desenfoque;
    [ObservableProperty] private AnimacionFondo _animacion;
    [ObservableProperty] private double _velocidad;
    [ObservableProperty] private double _intensidad;
    [ObservableProperty] private string _fuente = "";
    [ObservableProperty] private double _tamanoPct;
    [ObservableProperty] private int _peso;
    [ObservableProperty] private bool _cursiva;
    [ObservableProperty] private bool _mayusculas;
    [ObservableProperty] private string _colorTexto = "";
    [ObservableProperty] private double _interlineado;
    [ObservableProperty] private AlineacionHorizontal _alineacionH;
    [ObservableProperty] private AlineacionVertical _alineacionV;
    [ObservableProperty] private bool _ajusteAutomatico;
    [ObservableProperty] private bool _sombraActiva;
    [ObservableProperty] private double _sombraOpacidad;
    [ObservableProperty] private double _sombraDesenfoque;
    [ObservableProperty] private bool _cajaActiva;
    [ObservableProperty] private string _colorCaja = "";
    [ObservableProperty] private double _opacidadCaja;
    [ObservableProperty] private bool _mostrarPie;
    [ObservableProperty] private string _colorPie = "";
    [ObservableProperty] private double _tamanoPiePct;
    [ObservableProperty] private PosicionPie _posicionPie;
    [ObservableProperty] private double _margenPct;
    [ObservableProperty] private int _lineasMaximas;
    [ObservableProperty] private int _caracteresMaximos;
    [ObservableProperty] private int _versiculosPorDiapositiva;
    [ObservableProperty] private bool _numerosVersiculo;
    [ObservableProperty] private TipoTransicion _transicion;
    [ObservableProperty] private int _duracionMs;
    [ObservableProperty] private bool _muestraBiblia;
    [ObservableProperty] private Tema _temaVista;

    public EditorTemaViewModel(Contexto ctx, Tema tema)
    {
        _ctx = ctx;
        _tema = tema.Clonar();
        _temaVista = _tema.Clonar();
        Cargar();
        _cargando = false;
    }

    public string TituloVentana => _tema.Id == 0 ? "Nuevo tema" : $"Editar tema · {_tema.Nombre}";
    public IReadOnlyList<Opcion<TipoFondo>> TiposFondo => Listas.TiposFondo;
    public IReadOnlyList<Opcion<AnimacionFondo>> Animaciones => Listas.AnimacionesFondo;
    public bool TieneAnimacion => Animacion != AnimacionFondo.Ninguna;
    public IReadOnlyList<Opcion<int>> Pesos => Listas.Pesos;
    public IReadOnlyList<Opcion<AlineacionHorizontal>> AlineacionesH => Listas.AlineacionesH;
    public IReadOnlyList<Opcion<AlineacionVertical>> AlineacionesV => Listas.AlineacionesV;
    public IReadOnlyList<Opcion<PosicionPie>> PosicionesPie => Listas.PosicionesPie;
    public IReadOnlyList<Opcion<TipoTransicion>> Transiciones => Listas.Transiciones;
    public IReadOnlyList<string> Fuentes { get; } = FuentesCoraza.Todas();
    public Diapositiva Muestra => MuestraBiblia ? PanelTemasViewModel.MuestraVersiculo : PanelTemasViewModel.MuestraCancion;
    /// <summary>El fondo usa un archivo (imagen o video).</summary>
    public bool EsImagen => TipoFondo is TipoFondo.Imagen or TipoFondo.Video;
    public bool EsDegradado => TipoFondo is TipoFondo.Degradado or TipoFondo.Radial;

    /// <summary>Regla práctica: la letra debería medir al menos 1/20 de la altura de la pantalla.</summary>
    public string? AvisoLegibilidad => TamanoPct < 5
        ? "La letra podría ser pequeña para leerse desde el fondo del salón (se recomienda al menos 5 % de la altura de la pantalla)."
        : null;

    public event Action<bool>? CerrarSolicitado;

    private void Cargar()
    {
        Nombre = _tema.Nombre;
        TipoFondo = _tema.Fondo.Tipo;
        Color1 = _tema.Fondo.Color1;
        Color2 = _tema.Fondo.Color2;
        Angulo = _tema.Fondo.Angulo;
        RutaImagen = _tema.Fondo.RutaImagen;
        Oscurecer = _tema.Fondo.Oscurecer;
        Desenfoque = _tema.Fondo.Desenfoque;
        Animacion = _tema.Fondo.Animacion;
        Velocidad = _tema.Fondo.Velocidad;
        Intensidad = _tema.Fondo.Intensidad;
        Fuente = _tema.Texto.Fuente;
        TamanoPct = _tema.Texto.TamanoPct;
        Peso = _tema.Texto.Peso;
        Cursiva = _tema.Texto.Cursiva;
        Mayusculas = _tema.Texto.Mayusculas;
        ColorTexto = _tema.Texto.Color;
        Interlineado = _tema.Texto.Interlineado;
        AlineacionH = _tema.Texto.AlineacionH;
        AlineacionV = _tema.Texto.AlineacionV;
        AjusteAutomatico = _tema.Texto.AjusteAutomatico;
        SombraActiva = _tema.Sombra.Activa;
        SombraOpacidad = _tema.Sombra.Opacidad;
        SombraDesenfoque = _tema.Sombra.Desenfoque;
        CajaActiva = _tema.Caja.Activa;
        ColorCaja = _tema.Caja.Color;
        OpacidadCaja = _tema.Caja.Opacidad;
        MostrarPie = _tema.Pie.Mostrar;
        ColorPie = _tema.Pie.Color;
        TamanoPiePct = _tema.Pie.TamanoPct;
        PosicionPie = _tema.Pie.Posicion;
        MargenPct = _tema.MargenPct;
        LineasMaximas = _tema.LineasMaximas;
        CaracteresMaximos = _tema.CaracteresMaximos;
        VersiculosPorDiapositiva = _tema.VersiculosPorDiapositiva;
        NumerosVersiculo = _tema.NumerosVersiculo;
        Transicion = _tema.Transicion;
        DuracionMs = _tema.DuracionTransicionMs;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_cargando) return;
        switch (e.PropertyName)
        {
            case nameof(TemaVista):
            case nameof(Muestra):
            case nameof(AvisoLegibilidad):
            case nameof(EsImagen):
            case nameof(EsDegradado):
            case nameof(TieneAnimacion):
                return;
            case nameof(Animacion):
                OnPropertyChanged(nameof(TieneAnimacion));
                break;
            case nameof(MuestraBiblia):
                OnPropertyChanged(nameof(Muestra));
                return;
            case nameof(TipoFondo):
                OnPropertyChanged(nameof(EsImagen));
                OnPropertyChanged(nameof(EsDegradado));
                break;
            case nameof(TamanoPct):
                OnPropertyChanged(nameof(AvisoLegibilidad));
                break;
        }
        Aplicar();
    }

    private void Aplicar()
    {
        _tema.Nombre = Nombre;
        _tema.Fondo.Tipo = TipoFondo;
        _tema.Fondo.Color1 = Color1;
        _tema.Fondo.Color2 = Color2;
        _tema.Fondo.Angulo = Angulo;
        _tema.Fondo.RutaImagen = RutaImagen;
        _tema.Fondo.Oscurecer = Oscurecer;
        _tema.Fondo.Desenfoque = Desenfoque;
        _tema.Fondo.Animacion = Animacion;
        _tema.Fondo.Velocidad = Velocidad;
        _tema.Fondo.Intensidad = Intensidad;
        _tema.Texto.Fuente = Fuente;
        _tema.Texto.TamanoPct = TamanoPct;
        _tema.Texto.Peso = Peso;
        _tema.Texto.Cursiva = Cursiva;
        _tema.Texto.Mayusculas = Mayusculas;
        _tema.Texto.Color = ColorTexto;
        _tema.Texto.Interlineado = Interlineado;
        _tema.Texto.AlineacionH = AlineacionH;
        _tema.Texto.AlineacionV = AlineacionV;
        _tema.Texto.AjusteAutomatico = AjusteAutomatico;
        _tema.Sombra.Activa = SombraActiva;
        _tema.Sombra.Opacidad = SombraOpacidad;
        _tema.Sombra.Desenfoque = SombraDesenfoque;
        _tema.Caja.Activa = CajaActiva;
        _tema.Caja.Color = ColorCaja;
        _tema.Caja.Opacidad = OpacidadCaja;
        _tema.Pie.Mostrar = MostrarPie;
        _tema.Pie.Color = ColorPie;
        _tema.Pie.TamanoPct = TamanoPiePct;
        _tema.Pie.Posicion = PosicionPie;
        _tema.MargenPct = MargenPct;
        _tema.LineasMaximas = LineasMaximas;
        _tema.CaracteresMaximos = CaracteresMaximos;
        _tema.VersiculosPorDiapositiva = VersiculosPorDiapositiva;
        _tema.NumerosVersiculo = NumerosVersiculo;
        _tema.Transicion = Transicion;
        _tema.DuracionTransicionMs = DuracionMs;
        TemaVista = _tema.Clonar();
    }

    [RelayCommand]
    private void ElegirImagen()
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Imagen o video de fondo",
            Filter = "Imágenes y videos|" + string.Join(";", FormatosMedios.Imagenes.Concat(FormatosMedios.Videos).Select(e => "*" + e))
                     + "|Imágenes|" + string.Join(";", FormatosMedios.Imagenes.Select(e => "*" + e))
                     + "|Videos (MP4 recomendado)|" + string.Join(";", FormatosMedios.Videos.Select(e => "*" + e)),
        };
        if (dialogo.ShowDialog() != true) return;
        var destino = Path.Combine(_ctx.Rutas.Fondos, Path.GetFileName(dialogo.FileName));
        if (!string.Equals(Path.GetFullPath(dialogo.FileName), Path.GetFullPath(destino), StringComparison.OrdinalIgnoreCase))
            File.Copy(dialogo.FileName, destino, overwrite: true);
        RutaImagen = destino;
        TipoFondo = FormatosMedios.EsVideo(destino) ? TipoFondo.Video : TipoFondo.Imagen;
        if (Oscurecer < 0.2) Oscurecer = 0.35;
    }

    [RelayCommand]
    private void Guardar()
    {
        if (string.IsNullOrWhiteSpace(Nombre))
        {
            Dialogo.Informar("Falta el nombre", "Escribe un nombre para el tema.");
            return;
        }
        Aplicar();
        _ctx.Temas.Guardar(_tema);
        Resultado = _tema;
        CerrarSolicitado?.Invoke(true);
    }

    [RelayCommand]
    private void Cancelar() => CerrarSolicitado?.Invoke(false);

    public Tema? Resultado { get; private set; }
}
