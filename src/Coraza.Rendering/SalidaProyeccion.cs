using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Coraza.Core.Modelos;

namespace Coraza.Rendering;

public enum AccionVideo { Reproducir, Pausar, Reiniciar }

/// <summary>
/// Salida de proyección compuesta por capas (de abajo hacia arriba): fondo, contenido (medios y texto),
/// logotipo y pantalla negra. Cambiar la letra no reinicia el fondo. Las transiciones corren en la GPU
/// y, si el operador avanza durante una transición, la anterior se completa al instante sin saltos.
/// </summary>
public sealed class SalidaProyeccion : Grid
{
    private static readonly Color Coral = Color.FromRgb(0xFC, 0x8F, 0x8F);

    private readonly Grid _capaFondo = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Grid _capaContenido = new() { IsHitTestVisible = false };
    private readonly Grid _capaSuperposiciones = new() { IsHitTestVisible = false };
    private readonly Grid _capaMensajes = new() { IsHitTestVisible = false };
    private readonly Grid _capaLogo = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly Rectangle _capaNegro = new() { Fill = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
    private readonly List<Action> _finalizadores = new();

    private FrameworkElement? _fondoActual;
    private string? _claveFondo;
    private LienzoDiapositiva? _contenidoActual;
    private PatronPrueba? _patron;
    private bool _negro;
    private bool _logo;
    private bool _textoOculto;
    private ImageSource? _imagenLogo;
    private string? _textoLogo;
    private Border? _marquesina;
    private Action? _animarMarquesina;
    private TextBlock? _reloj;
    private DispatcherTimer? _relojTemporizador;
    private Border? _mensaje;

    public SalidaProyeccion()
    {
        ClipToBounds = true;
        Background = Brushes.Black;
        // Capas de abajo hacia arriba: fondo, contenido, superposiciones, mensajes, logotipo y negro.
        Children.Add(_capaFondo);
        Children.Add(_capaContenido);
        Children.Add(_capaSuperposiciones);
        Children.Add(_capaMensajes);
        Children.Add(_capaLogo);
        Children.Add(_capaNegro);
        ConstruirLogo();
        SizeChanged += (_, _) =>
        {
            _animarMarquesina?.Invoke();
            AjustarReloj();
        };
        Unloaded += (_, _) => _relojTemporizador?.Stop();
    }

    /// <summary>Marquesina: texto que se desplaza en la parte inferior (null la oculta).</summary>
    public void MostrarMarquesina(string? texto)
    {
        if (_marquesina is not null) _capaSuperposiciones.Children.Remove(_marquesina);
        _marquesina = null;
        _animarMarquesina = null;
        if (string.IsNullOrWhiteSpace(texto)) return;

        var linea = new TextBlock
        {
            Text = texto.Replace('\n', ' ').Trim(),
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
        };
        var mover = new TranslateTransform();
        linea.RenderTransform = mover;
        var lienzo = new Canvas { ClipToBounds = true };
        lienzo.Children.Add(linea);
        var barra = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x64, 0x24, 0x2F)),
            BorderBrush = new SolidColorBrush(Coral),
            Child = lienzo,
        };
        _animarMarquesina = () =>
        {
            var alto = Math.Max(1, ActualHeight);
            var ancho = Math.Max(1, ActualWidth);
            barra.Height = alto * 0.075;
            barra.BorderThickness = new Thickness(0, Math.Max(1, alto * 0.003), 0, 0);
            linea.FontSize = alto * 0.037;
            linea.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetTop(linea, (barra.Height - linea.DesiredSize.Height) / 2);
            var velocidad = alto * 0.13; // píxeles por segundo, proporcional a la pantalla
            var recorrido = ancho + linea.DesiredSize.Width;
            mover.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(ancho, -linea.DesiredSize.Width, TimeSpan.FromSeconds(recorrido / velocidad)) { RepeatBehavior = RepeatBehavior.Forever });
        };
        _capaSuperposiciones.Children.Add(barra);
        _marquesina = barra;
        _animarMarquesina();
    }

    /// <summary>Reloj en la esquina superior derecha.</summary>
    public void MostrarReloj(bool mostrar)
    {
        if (_reloj is not null) _capaSuperposiciones.Children.Remove(_reloj);
        _relojTemporizador?.Stop();
        _reloj = null;
        _relojTemporizador = null;
        if (!mostrar) return;
        _reloj = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Text = DateTime.Now.ToString("HH:mm"),
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.7 },
        };
        _capaSuperposiciones.Children.Add(_reloj);
        _relojTemporizador = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _relojTemporizador.Tick += (_, _) => { if (_reloj is not null) _reloj.Text = DateTime.Now.ToString("HH:mm"); };
        _relojTemporizador.Start();
        AjustarReloj();
    }

    private void AjustarReloj()
    {
        if (_reloj is null) return;
        var alto = Math.Max(1, ActualHeight);
        _reloj.FontSize = alto * 0.042;
        _reloj.Margin = new Thickness(0, alto * 0.03, alto * 0.04, 0);
    }

    /// <summary>Mensaje urgente sobre cualquier contenido que desaparece solo (null lo retira).</summary>
    public void MostrarMensaje(string? texto, TimeSpan duracion)
    {
        if (_mensaje is not null) _capaMensajes.Children.Remove(_mensaje);
        _mensaje = null;
        if (string.IsNullOrWhiteSpace(texto) || duracion <= TimeSpan.Zero) return;
        var alto = Math.Max(1, ActualHeight);
        var ancho = Math.Max(1, ActualWidth);
        var aviso = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0xB4, 0x44, 0x46)),
            BorderBrush = new SolidColorBrush(Coral),
            BorderThickness = new Thickness(Math.Max(1, alto * 0.003)),
            CornerRadius = new CornerRadius(alto * 0.015),
            Padding = new Thickness(alto * 0.035, alto * 0.018, alto * 0.035, alto * 0.018),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, alto * 0.04, 0, 0),
            MaxWidth = ancho * 0.86,
            Child = new TextBlock
            {
                Text = texto.Trim(),
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.Bold,
                FontSize = alto * 0.045,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
            },
            Effect = new DropShadowEffect { BlurRadius = alto * 0.02, ShadowDepth = 2, Opacity = 0.6 },
            Opacity = 0,
        };
        var bajar = new TranslateTransform(0, -alto * 0.12);
        aviso.RenderTransform = bajar;
        _capaMensajes.Children.Add(aviso);
        _mensaje = aviso;

        var suave = new CubicEase { EasingMode = EasingMode.EaseOut };
        bajar.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-alto * 0.12, 0, TimeSpan.FromMilliseconds(450)) { EasingFunction = suave });
        var opacidad = new DoubleAnimationUsingKeyFrames { Duration = duracion };
        opacidad.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350))));
        opacidad.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(duracion - TimeSpan.FromMilliseconds(Math.Min(600, duracion.TotalMilliseconds / 2)))));
        opacidad.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(duracion)));
        opacidad.Completed += (_, _) =>
        {
            _capaMensajes.Children.Remove(aviso);
            if (ReferenceEquals(_mensaje, aviso)) _mensaje = null;
        };
        aviso.BeginAnimation(OpacityProperty, opacidad);
    }

    /// <summary>Opción «Reducir movimiento»: todos los cambios son instantáneos.</summary>
    public bool ReducirMovimiento { get; set; }

    private bool _sinAudio;
    private double _volumen = 1;

    /// <summary>Silencia los videos de esta salida (el monitor del operador no debe sonar a la vez que el Video Beam).</summary>
    public bool SinAudio
    {
        get => _sinAudio;
        set
        {
            _sinAudio = value;
            foreach (var lienzo in _capaContenido.Children.OfType<LienzoDiapositiva>()) lienzo.SinAudio = value;
        }
    }

    public double Volumen
    {
        get => _volumen;
        set
        {
            _volumen = Math.Clamp(value, 0, 1);
            foreach (var lienzo in _capaContenido.Children.OfType<LienzoDiapositiva>()) lienzo.VolumenVideo = _volumen;
        }
    }

    /// <summary>Video que se está mostrando (si la diapositiva actual es un video).</summary>
    public MediaElement? VideoActual => _contenidoActual?.Video;

    public event EventHandler<string>? ErrorMedio;

    public void ControlarVideo(AccionVideo accion)
    {
        var video = VideoActual;
        if (video is null) return;
        switch (accion)
        {
            case AccionVideo.Reproducir:
                video.Play();
                break;
            case AccionVideo.Pausar:
                video.Pause();
                break;
            case AccionVideo.Reiniciar:
                video.Position = TimeSpan.Zero;
                video.Play();
                break;
        }
    }

    /// <summary>Para los monitores pequeños del operador: imágenes reducidas y sin sombras.</summary>
    public bool Miniatura { get; set; }

    public Diapositiva? DiapositivaActual { get; private set; }

    public Tema? TemaActual { get; private set; }

    public void Mostrar(Diapositiva? diapositiva, Tema tema, TipoTransicion? transicion = null, int? duracionMs = null, bool instantaneo = false)
    {
        CompletarTransiciones();
        DiapositivaActual = diapositiva;
        TemaActual = tema;

        var tipo = transicion ?? tema.Transicion;
        var duracion = TimeSpan.FromMilliseconds(Math.Clamp(duracionMs ?? tema.DuracionTransicionMs, 0, 5000));
        if (instantaneo || ReducirMovimiento || (duracion <= TimeSpan.Zero && !EsRevelado(tipo))) tipo = TipoTransicion.Corte;
        if (duracion <= TimeSpan.Zero) duracion = TimeSpan.FromMilliseconds(400);

        var clave = tema.ClaveFondo;
        if (clave != _claveFondo || _fondoActual is null)
        {
            var nuevoFondo = FabricaFondos.Crear(tema.Fondo, Miniatura);
            Transicionar(_capaFondo, _fondoActual, nuevoFondo, tipo == TipoTransicion.Corte ? TipoTransicion.Corte : TipoTransicion.Fundido, duracion);
            _fondoActual = nuevoFondo;
            _claveFondo = clave;
        }
        else if (tipo == TipoTransicion.Deslizar)
        {
            Paralaje(_fondoActual, duracion);
        }

        LienzoDiapositiva? nuevo = null;
        if (diapositiva is not null && diapositiva.Tipo != TipoDiapositiva.Vacia)
        {
            nuevo = new LienzoDiapositiva
            {
                MostrarFondo = false,
                Miniatura = Miniatura,
                TextoOculto = _textoOculto,
                SinAudio = _sinAudio,
                VolumenVideo = _volumen,
                ModoRevelado = tipo switch
                {
                    TipoTransicion.LineaPorLinea => ModoRevelado.Lineas,
                    TipoTransicion.PalabraPorPalabra => ModoRevelado.Palabras,
                    TipoTransicion.MaquinaEscribir => ModoRevelado.Letras,
                    _ => ModoRevelado.Ninguno,
                },
                MovimientoLento = tipo == TipoTransicion.KenBurns,
                Tema = tema,
                Diapositiva = diapositiva,
            };
            nuevo.ErrorMedio += (_, mensaje) => ErrorMedio?.Invoke(this, mensaje);
        }
        Transicionar(_capaContenido, _contenidoActual, nuevo, tipo, duracion);
        _contenidoActual = nuevo;
    }

    public bool Negro
    {
        get => _negro;
        set
        {
            if (_negro == value) return;
            _negro = value;
            AnimarOpacidad(_capaNegro, value ? 1 : 0, 450);
        }
    }

    public bool Logo
    {
        get => _logo;
        set
        {
            if (_logo == value) return;
            _logo = value;
            AnimarOpacidad(_capaLogo, value ? 1 : 0, 500);
        }
    }

    /// <summary>Oculta solo el texto; el fondo y los medios siguen visibles.</summary>
    public bool TextoOculto
    {
        get => _textoOculto;
        set
        {
            if (_textoOculto == value) return;
            _textoOculto = value;
            foreach (var lienzo in _capaContenido.Children.OfType<LienzoDiapositiva>())
            {
                if (lienzo.CapaTexto is { } capa) AnimarOpacidad(capa, value ? 0 : 1, 350);
                lienzo.SetCurrentValue(LienzoDiapositiva.TextoOcultoProperty, value);
            }
        }
    }

    public void ConfigurarLogo(ImageSource? imagen, string? texto)
    {
        _imagenLogo = imagen;
        _textoLogo = texto;
        ConstruirLogo();
    }

    public bool PatronPrueba
    {
        get => _patron is not null;
        set
        {
            if (value == (_patron is not null)) return;
            if (value)
            {
                _patron = new PatronPrueba();
                Children.Add(_patron);
            }
            else
            {
                Children.Remove(_patron);
                _patron = null;
            }
        }
    }

    /// <summary>Termina al instante cualquier transición en curso.</summary>
    public void CompletarTransiciones()
    {
        if (_finalizadores.Count == 0) return;
        var pendientes = _finalizadores.ToArray();
        _finalizadores.Clear();
        foreach (var finalizar in pendientes) finalizar();
    }

    private static bool EsRevelado(TipoTransicion t) =>
        t is TipoTransicion.LineaPorLinea or TipoTransicion.PalabraPorPalabra or TipoTransicion.MaquinaEscribir;

    private void Transicionar(Grid capa, FrameworkElement? viejo, FrameworkElement? nuevo, TipoTransicion tipo, TimeSpan duracion)
    {
        if (nuevo is not null) capa.Children.Add(nuevo);
        if (tipo == TipoTransicion.Corte || (viejo is null && nuevo is null))
        {
            if (viejo is not null) capa.Children.Remove(viejo);
            return;
        }

        UIElement? adorno = null;
        var terminado = false;
        void Finalizar()
        {
            if (terminado) return;
            terminado = true;
            _finalizadores.Remove(Finalizar);
            if (nuevo is not null)
            {
                nuevo.BeginAnimation(OpacityProperty, null);
                nuevo.Opacity = 1;
                nuevo.Effect = null;
                nuevo.RenderTransform = Transform.Identity;
                nuevo.Clip = null;
                nuevo.OpacityMask = null;
            }
            if (viejo is not null)
            {
                viejo.BeginAnimation(OpacityProperty, null);
                capa.Children.Remove(viejo);
            }
            if (adorno is not null) capa.Children.Remove(adorno);
        }
        _finalizadores.Add(Finalizar);

        var suave = new CubicEase { EasingMode = EasingMode.EaseOut };
        var alto = Math.Max(1, ActualHeight);
        var ancho = Math.Max(1, ActualWidth);
        var ms = duracion.TotalMilliseconds;
        Timeline? fin = null;

        // Anima la opacidad del elemento nuevo (y opcionalmente del viejo); devuelve la animación principal.
        DoubleAnimation Aparecer(double inicioMs, double duracionMs)
        {
            var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duracionMs)) { BeginTime = TimeSpan.FromMilliseconds(inicioMs), EasingFunction = suave };
            if (nuevo is not null)
            {
                nuevo.Opacity = 0;
                nuevo.BeginAnimation(OpacityProperty, a);
            }
            return a;
        }

        void Desaparecer(double inicioMs, double duracionMs)
        {
            if (viejo is null) return;
            viejo.BeginAnimation(OpacityProperty, new DoubleAnimation(viejo.Opacity, 0, TimeSpan.FromMilliseconds(duracionMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(inicioMs),
                EasingFunction = suave,
            });
        }

        switch (tipo)
        {
            case TipoTransicion.Desenfoque:
            {
                var radio = alto * 0.025;
                if (nuevo is not null)
                {
                    var efecto = new BlurEffect { Radius = radio, RenderingBias = RenderingBias.Performance };
                    nuevo.Effect = efecto;
                    efecto.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(radio, 0, duracion) { EasingFunction = suave });
                }
                if (viejo is not null)
                {
                    var efecto = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
                    viejo.Effect = efecto;
                    efecto.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, radio, TimeSpan.FromMilliseconds(ms * 0.7)) { EasingFunction = suave });
                }
                fin = Aparecer(0, ms);
                Desaparecer(0, ms * 0.7);
                break;
            }
            case TipoTransicion.Deslizar:
            {
                var d = ancho * 0.06;
                Mover(nuevo, d, 0, ms, 0);
                Mover(viejo, 0, -d, ms * 0.7, 0);
                fin = Aparecer(0, ms);
                Desaparecer(0, ms * 0.7);
                break;
            }
            case TipoTransicion.Zoom:
            {
                Escalar(nuevo, 1.06, 1, ms);
                Escalar(viejo, 1, 0.96, ms * 0.7);
                fin = Aparecer(0, ms);
                Desaparecer(0, ms * 0.7);
                break;
            }
            case TipoTransicion.LineaPorLinea or TipoTransicion.PalabraPorPalabra or TipoTransicion.MaquinaEscribir:
                // El texto se revela solo dentro del lienzo; aquí basta una aparición corta del contenedor.
                fin = Aparecer(0, Math.Min(250, ms));
                Desaparecer(0, Math.Min(300, ms * 0.7));
                break;
            case TipoTransicion.BarridoCoraza when nuevo is not null:
            {
                // Una máscara con forma de escudo se abre desde el centro revelando el contenido.
                var escudo = LogoCoraza.Escudo.Clone();
                var escala = new ScaleTransform(0.001, 0.001, 50, 60);
                escudo.Transform = new TransformGroup { Children = { escala, new TranslateTransform(ancho / 2 - 50, alto / 2 - 60) } };
                nuevo.Clip = escudo;
                var destino = Math.Max(ancho, alto) / 100 * 2.8;
                var abrir = new DoubleAnimation(0.001, destino, duracion) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                escala.BeginAnimation(ScaleTransform.ScaleXProperty, abrir);
                escala.BeginAnimation(ScaleTransform.ScaleYProperty, abrir);
                Desaparecer(ms * 0.5, ms * 0.5);
                fin = abrir;
                break;
            }
            case TipoTransicion.RayoLuz:
            {
                // Un destello coral cruza la pantalla y deja el nuevo contenido.
                var mover = new TranslateTransform(-ancho * 0.5, 0);
                var rayo = new Rectangle
                {
                    Width = ancho * 0.45,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    IsHitTestVisible = false,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0.5),
                        EndPoint = new Point(1, 0.5),
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(0, Coral.R, Coral.G, Coral.B), 0),
                            new GradientStop(Color.FromArgb(140, Coral.R, Coral.G, Coral.B), 0.35),
                            new GradientStop(Color.FromArgb(220, 255, 255, 255), 0.5),
                            new GradientStop(Color.FromArgb(140, Coral.R, Coral.G, Coral.B), 0.65),
                            new GradientStop(Color.FromArgb(0, Coral.R, Coral.G, Coral.B), 1),
                        },
                    },
                    RenderTransform = new TransformGroup { Children = { new SkewTransform(-15, 0), mover } },
                };
                capa.Children.Add(rayo);
                adorno = rayo;
                var cruzar = new DoubleAnimation(-ancho * 0.5, ancho * 1.1, duracion) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                mover.BeginAnimation(TranslateTransform.XProperty, cruzar);
                Aparecer(ms * 0.35, ms * 0.35);
                Desaparecer(ms * 0.3, ms * 0.3);
                fin = cruzar;
                break;
            }
            case TipoTransicion.Persiana or TipoTransicion.Cortina when nuevo is not null:
            {
                // Barras verticales (persiana) u horizontales (cortina) que se abren.
                var bandas = tipo == TipoTransicion.Persiana ? 9.0 : 6.0;
                var borde1 = new GradientStop(Colors.Black, 0);
                var borde2 = new GradientStop(Colors.Transparent, 0);
                nuevo.OpacityMask = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = tipo == TipoTransicion.Persiana ? new Point(1 / bandas, 0) : new Point(0, 1 / bandas),
                    SpreadMethod = GradientSpreadMethod.Repeat,
                    GradientStops = { new GradientStop(Colors.Black, 0), borde1, borde2, new GradientStop(Colors.Transparent, 1) },
                };
                var abrir = new DoubleAnimation(0, 1, duracion) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                borde1.BeginAnimation(GradientStop.OffsetProperty, abrir);
                borde2.BeginAnimation(GradientStop.OffsetProperty, abrir);
                Desaparecer(ms * 0.4, ms * 0.6);
                fin = abrir;
                break;
            }
            case TipoTransicion.Giro:
            {
                // La diapositiva gira como una tarjeta.
                var mitad = TimeSpan.FromMilliseconds(ms / 2);
                if (viejo is not null)
                {
                    var t = new ScaleTransform(1, 1);
                    viejo.RenderTransformOrigin = new Point(0.5, 0.5);
                    viejo.RenderTransform = t;
                    t.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, mitad) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } });
                }
                if (nuevo is not null)
                {
                    var t = new ScaleTransform(0, 1);
                    nuevo.RenderTransformOrigin = new Point(0.5, 0.5);
                    nuevo.RenderTransform = t;
                    var abrir = new DoubleAnimation(0, 1, mitad)
                    {
                        BeginTime = viejo is null ? TimeSpan.Zero : mitad,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                    };
                    t.BeginAnimation(ScaleTransform.ScaleXProperty, abrir);
                    fin = abrir;
                }
                else
                {
                    fin = new DoubleAnimation(0, 1, mitad);
                    Desaparecer(0, ms / 2);
                }
                break;
            }
            case TipoTransicion.Particulas:
            {
                // El texto se disuelve en partículas luminosas y se recompone.
                var total = Math.Max(ms, 1300);
                var particulas = CrearParticulas(ancho, alto, total);
                capa.Children.Add(particulas);
                adorno = particulas;
                if (viejo is not null)
                {
                    var efecto = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
                    viejo.Effect = efecto;
                    efecto.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, alto * 0.02, TimeSpan.FromMilliseconds(total * 0.45)));
                }
                Desaparecer(0, total * 0.45);
                if (nuevo is not null)
                {
                    var efecto = new BlurEffect { Radius = alto * 0.02, RenderingBias = RenderingBias.Performance };
                    nuevo.Effect = efecto;
                    efecto.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(alto * 0.02, 0, TimeSpan.FromMilliseconds(total * 0.5))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(total * 0.45),
                    });
                }
                var aparecer = Aparecer(total * 0.45, total * 0.5);
                fin = nuevo is null ? new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(total)) : aparecer;
                break;
            }
            default:
                // Fundido cruzado (y Ken Burns, cuyo movimiento continúa dentro del lienzo).
                fin = Aparecer(0, ms);
                Desaparecer(0, ms * 0.7);
                break;
        }

        if (fin is null)
        {
            fin = Aparecer(0, ms);
            Desaparecer(0, ms * 0.7);
        }

        if (nuevo is null)
        {
            // Limpiar: el final lo marca la desaparición del contenido anterior (una animación aplicada de verdad).
            if (viejo is null)
            {
                Finalizar();
                return;
            }
            var salida = new DoubleAnimation(viejo.Opacity, 0, duracion) { EasingFunction = suave };
            salida.Completed += (_, _) => Finalizar();
            viejo.BeginAnimation(OpacityProperty, salida);
            return;
        }

        fin.Completed += (_, _) => Finalizar();
    }

    private static void Mover(FrameworkElement? elemento, double desde, double hasta, double ms, double inicioMs)
    {
        if (elemento is null) return;
        var t = new TranslateTransform(desde, 0);
        elemento.RenderTransform = t;
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(desde, hasta, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(inicioMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private static void Escalar(FrameworkElement? elemento, double desde, double hasta, double ms)
    {
        if (elemento is null) return;
        var t = new ScaleTransform(desde, desde);
        elemento.RenderTransformOrigin = new Point(0.5, 0.5);
        elemento.RenderTransform = t;
        var a = new DoubleAnimation(desde, hasta, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        t.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Deslizamiento con profundidad: el fondo se mueve menos que el texto.</summary>
    private void Paralaje(FrameworkElement fondo, TimeSpan duracion)
    {
        var mover = new TranslateTransform();
        fondo.RenderTransformOrigin = new Point(0.5, 0.5);
        fondo.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1.05, 1.05), mover } };
        mover.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(Math.Max(1, ActualWidth) * 0.015, 0, duracion)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private Canvas CrearParticulas(double ancho, double alto, double totalMs)
    {
        var lienzo = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        var brillo = new RadialGradientBrush(Color.FromArgb(255, 255, 250, 245), Color.FromArgb(0, Coral.R, Coral.G, Coral.B))
        {
            GradientStops = { new GradientStop(Color.FromArgb(200, Coral.R, Coral.G, Coral.B), 0.45) },
        };
        brillo.Freeze();
        var azar = new Random(11);
        var cantidad = Miniatura ? 40 : 110;
        var mitad = totalMs / 2;
        for (var i = 0; i < cantidad; i++)
        {
            var dispersar = i % 2 == 0; // la mitad se dispersa desde el texto viejo, la otra mitad forma el nuevo
            var tam = alto * (0.004 + azar.NextDouble() * 0.011);
            var cx = ancho / 2 + (azar.NextDouble() - 0.5) * ancho * 0.62;
            var cy = alto / 2 + (azar.NextDouble() - 0.5) * alto * 0.36;
            var angulo = azar.NextDouble() * Math.PI * 2;
            var distancia = alto * (0.1 + azar.NextDouble() * 0.28);
            var fx = cx + Math.Cos(angulo) * distancia;
            var fy = cy + Math.Sin(angulo) * distancia - alto * 0.04;
            var particula = new Ellipse { Width = tam, Height = tam, Fill = brillo, Opacity = 0 };
            var t = new TranslateTransform(cx, cy);
            particula.RenderTransform = t;
            lienzo.Children.Add(particula);

            var inicio = TimeSpan.FromMilliseconds(dispersar ? azar.NextDouble() * mitad * 0.3 : mitad * (0.7 + azar.NextDouble() * 0.3));
            var dur = TimeSpan.FromMilliseconds(mitad * (0.9 + azar.NextDouble() * 0.4));
            var (x0, y0, x1, y1) = dispersar ? (cx, cy, fx, fy) : (fx, fy, cx, cy);
            var suave = new CubicEase { EasingMode = dispersar ? EasingMode.EaseOut : EasingMode.EaseIn };
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x0, x1, dur) { BeginTime = inicio, EasingFunction = suave });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y0, y1, dur) { BeginTime = inicio, EasingFunction = suave });
            var opacidad = new DoubleAnimationUsingKeyFrames { BeginTime = inicio, Duration = dur };
            opacidad.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            opacidad.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.25)));
            opacidad.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            particula.BeginAnimation(OpacityProperty, opacidad);
        }
        return lienzo;
    }

    private void AnimarOpacidad(UIElement elemento, double destino, int milisegundos)
    {
        if (ReducirMovimiento) milisegundos = 0;
        var animacion = new DoubleAnimation(destino, TimeSpan.FromMilliseconds(milisegundos))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        elemento.BeginAnimation(OpacityProperty, animacion);
    }

    private void ConstruirLogo()
    {
        _capaLogo.Children.Clear();
        _capaLogo.Background = new RadialGradientBrush(Color.FromRgb(0x64, 0x24, 0x2F), Color.FromRgb(0x1E, 0x0B, 0x0F))
        {
            Center = new Point(0.5, 0.45),
            GradientOrigin = new Point(0.5, 0.45),
            RadiusX = 0.8,
            RadiusY = 0.95,
        };

        var pila = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        pila.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        pila.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var imagen = new Image
        {
            Source = _imagenLogo ?? LogoCoraza.Crear(),
            Stretch = Stretch.Uniform,
        };
        RenderOptions.SetBitmapScalingMode(imagen, BitmapScalingMode.HighQuality);
        pila.Children.Add(imagen);

        TextBlock? texto = null;
        if (!string.IsNullOrWhiteSpace(_textoLogo))
        {
            texto = new TextBlock
            {
                Text = _textoLogo,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDF, 0xD9, 0xD8)),
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            SetRow(texto, 1);
            pila.Children.Add(texto);
        }

        _capaLogo.SizeChanged += (_, e) =>
        {
            var alto = e.NewSize.Height;
            imagen.MaxHeight = alto * (_imagenLogo is null ? 0.42 : 0.55);
            imagen.MaxWidth = e.NewSize.Width * 0.7;
            if (texto is not null)
            {
                texto.FontSize = Math.Max(1, alto * 0.05);
                texto.Margin = new Thickness(0, alto * 0.04, 0, 0);
                texto.MaxWidth = e.NewSize.Width * 0.8;
            }
        };
        _capaLogo.Children.Add(pila);
        if (_capaLogo.ActualHeight > 0)
        {
            imagen.MaxHeight = _capaLogo.ActualHeight * (_imagenLogo is null ? 0.42 : 0.55);
            if (texto is not null) texto.FontSize = Math.Max(1, _capaLogo.ActualHeight * 0.05);
        }
    }
}
