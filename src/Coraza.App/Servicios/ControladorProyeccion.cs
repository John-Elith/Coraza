using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Coraza.App.Infraestructura;
using Coraza.Core.Modelos;
using Coraza.Rendering;
using Serilog;

namespace Coraza.App.Servicios;

/// <summary>
/// Estado de lo que ve el público y gestión de la ventana de salida. Todas las salidas registradas
/// (Video Beam y el monitor «En vivo» del operador) reciben exactamente las mismas órdenes.
/// </summary>
public sealed partial class ControladorProyeccion : ObservableObject
{
    public const double AnchoLienzo = 1920;

    private static readonly Tema TemaNeutro = new() { Fondo = new FondoTema { Tipo = TipoFondo.Color, Color1 = "#000000" } };

    private readonly Contexto _ctx;
    private readonly List<SalidaProyeccion> _salidas = new();
    private VentanaProyeccion? _ventana;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayVideo))]
    private Diapositiva? _diapositivaVivo;
    [ObservableProperty] private Tema? _temaVivo;
    [ObservableProperty] private bool _negro;
    [ObservableProperty] private bool _logo;
    [ObservableProperty] private bool _textoOculto;
    [ObservableProperty] private bool _patronPrueba;
    [ObservableProperty] private bool _proyectando;
    [ObservableProperty] private string _descripcionSalida = "Proyección apagada";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMarquesina))]
    private string? _marquesina;

    [ObservableProperty] private bool _relojVisible;

    private (string Texto, DateTime Fin)? _mensajeUrgente;

    public bool HayMarquesina => !string.IsNullOrWhiteSpace(Marquesina);

    // ---------------- Video ----------------

    [ObservableProperty] private double _volumen = 1;
    [ObservableProperty] private bool _videoPausado;
    private DateTime _ultimoError = DateTime.MinValue;

    public bool HayVideo => DiapositivaVivo?.Tipo == TipoDiapositiva.Video;

    /// <summary>Un video o audio no se pudo reproducir.</summary>
    public event EventHandler<string>? ErrorMedio;

    partial void OnVolumenChanged(double value)
    {
        foreach (var s in _salidas) s.Volumen = value;
    }

    public void ControlarVideo(AccionVideo accion)
    {
        foreach (var s in _salidas) s.ControlarVideo(accion);
        VideoPausado = accion == AccionVideo.Pausar;
    }

    /// <summary>Posición y duración del video en la salida principal.</summary>
    public (TimeSpan Posicion, TimeSpan? Duracion)? EstadoVideo()
    {
        var video = SalidaPrincipal?.VideoActual;
        if (video is null) return null;
        return (video.Position, video.NaturalDuration.HasTimeSpan ? video.NaturalDuration.TimeSpan : null);
    }

    private SalidaProyeccion? SalidaPrincipal => _ventana?.Salida ?? _salidas.FirstOrDefault();

    /// <summary>Solo suena la salida principal (el Video Beam; o el monitor del operador si no se proyecta).</summary>
    private void ActualizarAudio()
    {
        var principal = SalidaPrincipal;
        foreach (var s in _salidas) s.SinAudio = !ReferenceEquals(s, principal);
    }

    private void AlErrorMedio(object? sender, string mensaje)
    {
        if ((DateTime.Now - _ultimoError).TotalSeconds < 2) return; // la salida y el monitor avisan del mismo error
        _ultimoError = DateTime.Now;
        Log.Warning("No se pudo reproducir un medio: {Mensaje}", mensaje);
        ErrorMedio?.Invoke(this, mensaje);
    }

    // ---------------- Música de fondo ----------------

    private readonly MediaPlayer _musica = new();
    private bool _bucleMusica = true;

    [ObservableProperty] private string? _musicaActual;
    [ObservableProperty] private bool _musicaSonando;
    [ObservableProperty] private double _volumenMusica = 0.6;

    public void ReproducirMusica(string ruta, string nombre, bool bucle = true)
    {
        _musica.Open(new Uri(ruta));
        _musica.Volume = VolumenMusica;
        _musica.Play();
        _bucleMusica = bucle;
        MusicaActual = nombre;
        MusicaSonando = true;
    }

    public void AlternarMusica()
    {
        if (MusicaActual is null) return;
        if (MusicaSonando) _musica.Pause();
        else _musica.Play();
        MusicaSonando = !MusicaSonando;
    }

    public void DetenerMusica()
    {
        _musica.Stop();
        _musica.Close();
        MusicaActual = null;
        MusicaSonando = false;
    }

    partial void OnVolumenMusicaChanged(double value) => _musica.Volume = value;

    partial void OnMarquesinaChanged(string? value)
    {
        foreach (var s in _salidas) s.MostrarMarquesina(value);
    }

    partial void OnRelojVisibleChanged(bool value)
    {
        foreach (var s in _salidas) s.MostrarReloj(value);
    }

    /// <summary>Mensaje urgente sobre cualquier contenido (por ejemplo, avisos a padres) que desaparece solo.</summary>
    public void MensajeUrgente(string texto, int segundos = 20)
    {
        _mensajeUrgente = (texto, DateTime.Now.AddSeconds(segundos));
        foreach (var s in _salidas) s.MostrarMensaje(texto, TimeSpan.FromSeconds(segundos));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AltoLienzo))]
    private double _proporcion = 16.0 / 9;

    public ControladorProyeccion(Contexto ctx)
    {
        _ctx = ctx;
        ActualizarProporcion();
        _musica.MediaEnded += (_, _) =>
        {
            if (_bucleMusica)
            {
                _musica.Position = TimeSpan.Zero;
                _musica.Play();
            }
            else
            {
                MusicaSonando = false;
            }
        };
        _musica.MediaFailed += (_, e) =>
        {
            DetenerMusica();
            AlErrorMedio(this, e.ErrorException?.Message ?? "formato de audio no compatible");
        };
    }

    /// <summary>Alto del lienzo lógico (ancho 1920) con la proporción de la salida actual.</summary>
    public double AltoLienzo => Math.Round(AnchoLienzo / Proporcion);

    /// <summary>¿La salida ocupa una pantalla completa (Video Beam o la pantalla elegida)?</summary>
    public bool PantallaCompleta => _ventana?.PantallaCompleta == true;

    public event KeyEventHandler? TeclaEnSalida;

    public event EventHandler? CierreSolicitado;

    public void Registrar(SalidaProyeccion salida)
    {
        if (_salidas.Contains(salida)) return;
        _salidas.Add(salida);
        salida.ErrorMedio += AlErrorMedio;
        salida.Volumen = Volumen;
        ActualizarAudio();
        Sincronizar(salida);
    }

    public void Quitar(SalidaProyeccion salida)
    {
        salida.ErrorMedio -= AlErrorMedio;
        _salidas.Remove(salida);
        ActualizarAudio();
    }

    public void Mostrar(Diapositiva? diapositiva, Tema tema)
    {
        Negro = false;
        Logo = false;
        PatronPrueba = false;
        VideoPausado = false;
        DiapositivaVivo = diapositiva;
        TemaVivo = tema;
        var p = _ctx.Preferencias;
        foreach (var salida in _salidas.ToArray())
        {
            try
            {
                salida.Mostrar(diapositiva, tema, p.TransicionForzada, p.TransicionForzada is null ? null : p.DuracionForzadaMs);
            }
            catch (Exception ex)
            {
                // Si algo falla se muestra el fondo del tema, nunca una pantalla con error.
                Log.Error(ex, "Error al mostrar una diapositiva");
                salida.Mostrar(null, tema, instantaneo: true);
            }
        }
    }

    partial void OnNegroChanged(bool value)
    {
        foreach (var s in _salidas) s.Negro = value;
    }

    partial void OnLogoChanged(bool value)
    {
        foreach (var s in _salidas) s.Logo = value;
    }

    partial void OnTextoOcultoChanged(bool value)
    {
        foreach (var s in _salidas) s.TextoOculto = value;
    }

    partial void OnPatronPruebaChanged(bool value)
    {
        foreach (var s in _salidas) s.PatronPrueba = value;
    }

    /// <summary>Aplica cambios de la configuración (logo, movimiento, proporción, pantalla).</summary>
    public void AplicarPreferencias()
    {
        var logo = CargarLogo();
        foreach (var s in _salidas)
        {
            s.ReducirMovimiento = _ctx.Preferencias.ReducirMovimiento;
            s.ConfigurarLogo(logo, _ctx.Preferencias.TextoLogo);
        }
        if (_ventana is not null)
        {
            _ventana.ProporcionForzada = ProporcionForzada();
            _ventana.OcultarCursor = _ctx.Preferencias.OcultarCursor;
        }
        if (Proyectando) Recolocar();
        else ActualizarProporcion();
    }

    public void Iniciar()
    {
        if (Proyectando) return;
        var (pantalla, completa) = ElegirDestino();
        CrearVentana(pantalla, completa);
        Proyectando = true;
        EvitarSuspension.Activar();
        Log.Information("Proyección iniciada: {Destino}", DescripcionSalida);
    }

    public void Detener()
    {
        CerrarVentana();
        Proyectando = false;
        DescripcionSalida = "Proyección apagada";
        EvitarSuspension.Desactivar();
        ActualizarProporcion();
        Log.Information("Proyección detenida");
    }

    /// <summary>
    /// Se llama cuando cambian las pantallas: si el Video Beam se desconecta la salida pasa a una ventana
    /// de ensayo, y cuando se vuelve a conectar regresa automáticamente a esa pantalla.
    /// </summary>
    public void Recolocar()
    {
        if (!Proyectando)
        {
            ActualizarProporcion();
            return;
        }
        var (pantalla, completa) = ElegirDestino();
        if (_ventana is not null && completa && _ventana.PantallaCompleta && pantalla is not null)
        {
            _ventana.ColocarEn(pantalla.Limites);
            DescripcionSalida = Describir(pantalla);
        }
        else if (_ventana is null || _ventana.PantallaCompleta != completa)
        {
            CerrarVentana();
            CrearVentana(pantalla, completa);
        }
        ActualizarProporcion();
    }

    private (InfoPantalla? Pantalla, bool Completa) ElegirDestino()
    {
        var pref = _ctx.Preferencias.Pantalla;
        if (pref == Preferencias.PantallaFlotante) return (null, false);
        var pantallas = GestorPantallas.Enumerar();
        var elegida = pref is null ? null : pantallas.FirstOrDefault(p => string.Equals(p.Dispositivo, pref, StringComparison.OrdinalIgnoreCase));
        elegida ??= pantallas.FirstOrDefault(p => !p.Principal);
        return elegida is null ? (null, false) : (elegida, true);
    }

    private void CrearVentana(InfoPantalla? pantalla, bool completa)
    {
        var guardados = _ctx.Preferencias.VentanaEnsayo;
        Rect? limitesEnsayo = guardados is { Length: 4 } g ? new Rect(g[0], g[1], g[2], g[3]) : null;
        var ventana = new VentanaProyeccion(completa, limitesEnsayo)
        {
            OcultarCursor = _ctx.Preferencias.OcultarCursor,
            ProporcionForzada = ProporcionForzada(),
        };
        if (completa && pantalla is not null) ventana.ColocarEn(pantalla.Limites);
        ventana.Tecla += (s, e) => TeclaEnSalida?.Invoke(s, e);
        ventana.CierreSolicitado += (_, _) => CierreSolicitado?.Invoke(this, EventArgs.Empty);
        ventana.Salida.SizeChanged += (_, _) => ActualizarProporcion();
        ventana.Show();
        _ventana = ventana;
        Registrar(ventana.Salida);
        OnPropertyChanged(nameof(PantallaCompleta));
        DescripcionSalida = completa && pantalla is not null ? Describir(pantalla) : "Ventana de ensayo (una sola pantalla)";
        Application.Current.MainWindow?.Activate();

        // Si la proyección tapa la pantalla del operador, se le recuerda cómo salir.
        if (completa && pantalla is not null && Application.Current.MainWindow is { } principal
            && string.Equals(GestorPantallas.DispositivoDe(principal), pantalla.Dispositivo, StringComparison.OrdinalIgnoreCase))
            ventana.MostrarAviso("Pulsa Esc para salir de la pantalla completa");
    }

    private void CerrarVentana()
    {
        if (_ventana is null) return;
        if (!_ventana.PantallaCompleta && _ventana.WindowState == WindowState.Normal)
        {
            _ctx.Preferencias.VentanaEnsayo = new[] { _ventana.Left, _ventana.Top, _ventana.Width, _ventana.Height };
            _ctx.GuardarPreferencias();
        }
        Quitar(_ventana.Salida);
        _ventana.PermitirCierre = true;
        _ventana.Close();
        _ventana = null;
        OnPropertyChanged(nameof(PantallaCompleta));
        ActualizarAudio();
    }

    private void Sincronizar(SalidaProyeccion s)
    {
        s.ReducirMovimiento = _ctx.Preferencias.ReducirMovimiento;
        s.ConfigurarLogo(CargarLogo(), _ctx.Preferencias.TextoLogo);
        s.Mostrar(DiapositivaVivo, TemaVivo ?? TemaNeutro, instantaneo: true);
        s.Negro = Negro;
        s.Logo = Logo;
        s.TextoOculto = TextoOculto;
        s.PatronPrueba = PatronPrueba;
        s.MostrarMarquesina(Marquesina);
        s.MostrarReloj(RelojVisible);
        if (_mensajeUrgente is { } m && m.Fin > DateTime.Now) s.MostrarMensaje(m.Texto, m.Fin - DateTime.Now);
    }

    private void ActualizarProporcion()
    {
        double valor;
        if (ProporcionForzada() is { } forzada) valor = forzada;
        else if (_ventana is not null && _ventana.Salida.ActualWidth > 0 && _ventana.Salida.ActualHeight > 0)
            valor = _ventana.Salida.ActualWidth / _ventana.Salida.ActualHeight;
        else valor = ElegirDestino().Pantalla?.Proporcion ?? 16.0 / 9;
        valor = Math.Round(Math.Clamp(valor, 1.0, 3.0), 3);
        if (Math.Abs(valor - Proporcion) > 0.001) Proporcion = valor;
    }

    private double? ProporcionForzada() => _ctx.Preferencias.Proporcion switch
    {
        "4:3" => 4.0 / 3,
        "16:9" => 16.0 / 9,
        "16:10" => 16.0 / 10,
        "21:9" => 21.0 / 9,
        _ => null,
    };

    private ImageSource? CargarLogo() =>
        string.IsNullOrWhiteSpace(_ctx.Preferencias.RutaLogo) ? null : CacheImagenes.Obtener(_ctx.Preferencias.RutaLogo);

    private static string Describir(InfoPantalla p) => $"En vivo en {p.Nombre} · {p.Limites.Width}×{p.Limites.Height}";
}
