using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Coraza.Core.Modelos;

namespace Coraza.Rendering;

/// <summary>Entrada animada del texto: todo junto, por líneas, por palabras o letra por letra.</summary>
public enum ModoRevelado { Ninguno, Lineas, Palabras, Letras }

/// <summary>
/// Dibuja una diapositiva con su tema. Todas las medidas son relativas a la altura
/// (porcentajes), así la misma diapositiva se ve igual en 800×600 o en 4K.
/// </summary>
public sealed class LienzoDiapositiva : Grid
{
    public static readonly DependencyProperty DiapositivaProperty = DependencyProperty.Register(
        nameof(Diapositiva), typeof(Diapositiva), typeof(LienzoDiapositiva), new PropertyMetadata(null, AlCambiar));

    public static readonly DependencyProperty TemaProperty = DependencyProperty.Register(
        nameof(Tema), typeof(Tema), typeof(LienzoDiapositiva), new PropertyMetadata(null, AlCambiar));

    public static readonly DependencyProperty MostrarFondoProperty = DependencyProperty.Register(
        nameof(MostrarFondo), typeof(bool), typeof(LienzoDiapositiva), new PropertyMetadata(true, AlCambiar));

    public static readonly DependencyProperty MiniaturaProperty = DependencyProperty.Register(
        nameof(Miniatura), typeof(bool), typeof(LienzoDiapositiva), new PropertyMetadata(false, AlCambiar));

    public static readonly DependencyProperty TextoOcultoProperty = DependencyProperty.Register(
        nameof(TextoOculto), typeof(bool), typeof(LienzoDiapositiva), new PropertyMetadata(false, AlCambiarTextoOculto));

    private static readonly CultureInfo Espanol = new("es-ES");

    private readonly List<SolidColorBrush> _revelables = new();
    private Grid? _areaTexto;
    private Border? _caja;
    private TextoAjustable? _texto;
    private TextBlock? _pie;
    private DropShadowEffect? _sombraTexto;
    private DropShadowEffect? _sombraPie;
    private bool _pendiente = true;
    private DispatcherTimer? _reloj;
    private Action<double, double>? _medirCuenta;
    private MediaElement? _video;
    private bool _sinAudio;
    private double _volumenVideo = 1;

    public LienzoDiapositiva()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        SizeChanged += (_, _) => ActualizarMedidas();
        Loaded += (_, _) =>
        {
            if (_pendiente) Reconstruir();
            else _reloj?.Start();
        };
        Unloaded += (_, _) => _reloj?.Stop();
    }

    public Diapositiva? Diapositiva
    {
        get => (Diapositiva?)GetValue(DiapositivaProperty);
        set => SetValue(DiapositivaProperty, value);
    }

    public Tema? Tema
    {
        get => (Tema?)GetValue(TemaProperty);
        set => SetValue(TemaProperty, value);
    }

    public bool MostrarFondo
    {
        get => (bool)GetValue(MostrarFondoProperty);
        set => SetValue(MostrarFondoProperty, value);
    }

    /// <summary>En miniaturas se decodifican imágenes pequeñas y se omiten efectos costosos.</summary>
    public bool Miniatura
    {
        get => (bool)GetValue(MiniaturaProperty);
        set => SetValue(MiniaturaProperty, value);
    }

    public bool TextoOculto
    {
        get => (bool)GetValue(TextoOcultoProperty);
        set => SetValue(TextoOcultoProperty, value);
    }

    /// <summary>Entrada animada del texto; se configura antes de mostrar la diapositiva.</summary>
    public ModoRevelado ModoRevelado { get; set; }

    /// <summary>Movimiento y zoom sutil y continuo sobre las fotos (Ken Burns).</summary>
    public bool MovimientoLento { get; set; }

    /// <summary>Muestra los videos como imagen fija (vista previa del operador) en lugar de reproducirlos.</summary>
    public bool VistaEstatica { get; set; }

    /// <summary>Video de la diapositiva (si la hay) para controlarlo desde el operador.</summary>
    public MediaElement? Video => _video;

    /// <summary>Solo la salida principal suena; el monitor del operador va en silencio.</summary>
    public bool SinAudio
    {
        get => _sinAudio;
        set
        {
            _sinAudio = value;
            if (_video is not null) _video.IsMuted = value;
        }
    }

    public double VolumenVideo
    {
        get => _volumenVideo;
        set
        {
            _volumenVideo = Math.Clamp(value, 0, 1);
            if (_video is not null) _video.Volume = _volumenVideo;
        }
    }

    /// <summary>Un video no se pudo reproducir (formato o códec no compatible).</summary>
    public event EventHandler<string>? ErrorMedio;

    /// <summary>Contenedor del texto y del pie (para ocultar solo el texto manteniendo el fondo).</summary>
    public FrameworkElement? CapaTexto => _areaTexto;

    /// <summary>Tamaño final de la letra en porcentaje de la altura (tras el ajuste automático).</summary>
    public double TamanoEfectivoPct => _texto is null || ActualHeight <= 0 ? 0 : _texto.TamanoEfectivo / ActualHeight * 100;

    /// <summary>Muestra al instante todo el texto que se estaba revelando.</summary>
    public void CompletarAnimaciones()
    {
        foreach (var pincel in _revelables)
        {
            pincel.BeginAnimation(Brush.OpacityProperty, null);
            pincel.Opacity = 1;
        }
    }

    private static void AlCambiar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Antes de cargarse se acumulan los cambios y se reconstruye una sola vez en Loaded.
        var lienzo = (LienzoDiapositiva)d;
        if (lienzo.IsLoaded) lienzo.Reconstruir();
        else lienzo._pendiente = true;
    }

    private static void AlCambiarTextoOculto(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var lienzo = (LienzoDiapositiva)d;
        if (lienzo._areaTexto is not null) lienzo._areaTexto.Opacity = (bool)e.NewValue ? 0 : 1;
    }

    private void Reconstruir()
    {
        _pendiente = false;
        Children.Clear();
        _revelables.Clear();
        _reloj?.Stop();
        _reloj = null;
        _medirCuenta = null;
        _video = null;
        _areaTexto = null;
        _caja = null;
        _texto = null;
        _pie = null;
        _sombraTexto = null;
        _sombraPie = null;

        var tema = Tema ?? new Tema();
        var d = Diapositiva;

        if (MostrarFondo) Children.Add(FabricaFondos.Crear(tema.Fondo, Miniatura));
        if (d is null || d.Tipo == TipoDiapositiva.Vacia) return;

        if (d.Tipo == TipoDiapositiva.Video)
        {
            ConstruirVideo(d);
            return;
        }

        if (d.Tipo == TipoDiapositiva.Imagen)
        {
            var fuente = CacheImagenes.Obtener(d.RutaImagen, Miniatura ? 360 : 0);
            if (fuente is not null)
            {
                var imagen = new Image
                {
                    Source = fuente,
                    Stretch = d.Ajuste switch
                    {
                        ModoAjuste.Rellenar => Stretch.UniformToFill,
                        ModoAjuste.Estirar => Stretch.Fill,
                        ModoAjuste.Centrar => Stretch.None,
                        _ => Stretch.Uniform,
                    },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                RenderOptions.SetBitmapScalingMode(imagen, BitmapScalingMode.HighQuality);
                if (MovimientoLento && !Miniatura) AplicarKenBurns(imagen);
                Children.Add(imagen);
            }
            // Si la imagen falla se ve el fondo del tema, nunca una pantalla de error.
            return;
        }

        if (d.Tipo == TipoDiapositiva.CuentaRegresiva)
        {
            ConstruirCuenta(d, tema);
            ActualizarMedidas();
            return;
        }

        ConstruirTexto(d, tema);
        ActualizarMedidas();
        if (_revelables.Count > 0) IniciarRevelado();
    }

    /// <summary>Video a pantalla completa; en miniaturas y en la vista previa se muestra un fotograma con el símbolo de reproducir.</summary>
    private void ConstruirVideo(Diapositiva d)
    {
        var estiramiento = d.Ajuste switch
        {
            ModoAjuste.Rellenar => Stretch.UniformToFill,
            ModoAjuste.Estirar => Stretch.Fill,
            ModoAjuste.Centrar => Stretch.None,
            _ => Stretch.Uniform,
        };
        if (string.IsNullOrWhiteSpace(d.RutaImagen) || !System.IO.File.Exists(d.RutaImagen))
        {
            ErrorMedio?.Invoke(this, "el archivo de video no existe");
            return;
        }

        if (Miniatura || VistaEstatica)
        {
            var fotograma = new Image { Stretch = estiramiento, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Children.Add(fotograma);
            CargarFotograma(fotograma, d.RutaImagen);
            var simbolo = new Viewbox
            {
                Width = 160,
                Height = 160,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Canvas
                {
                    Width = 24,
                    Height = 24,
                    Children =
                    {
                        new System.Windows.Shapes.Ellipse { Width = 24, Height = 24, Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)) },
                        new System.Windows.Shapes.Path { Data = Geometry.Parse("M9.5,7 L17.5,12 L9.5,17 Z"), Fill = Brushes.White },
                    },
                },
            };
            Children.Add(simbolo);
            return;
        }

        var video = new MediaElement
        {
            Source = new Uri(d.RutaImagen),
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            Stretch = estiramiento,
            IsMuted = _sinAudio,
            Volume = _volumenVideo,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        video.MediaEnded += (_, _) =>
        {
            if (!d.BucleVideo) return;
            video.Position = TimeSpan.Zero;
            video.Play();
        };
        video.MediaFailed += (_, e) =>
        {
            // Si el video falla se ve el fondo del tema, nunca una pantalla de error.
            Children.Remove(video);
            if (ReferenceEquals(_video, video)) _video = null;
            ErrorMedio?.Invoke(this, e.ErrorException?.Message ?? "formato no compatible");
        };
        video.Loaded += (_, _) => video.Play();
        _video = video;
        Children.Add(video);
    }

    private static async void CargarFotograma(Image imagen, string ruta) =>
        imagen.Source = await MiniaturasShell.ObtenerAsync(ruta, 480);

    /// <summary>Cuenta regresiva grande que se actualiza sola; al terminar muestra el texto final.</summary>
    private void ConstruirCuenta(Diapositiva d, Tema tema)
    {
        var estilo = tema.Texto;
        var fin = d.CalcularFinCuenta(DateTime.Now);
        var numeros = new TextBlock
        {
            FontFamily = FuentesCoraza.Obtener(estilo.Fuente),
            FontWeight = FontWeights.Bold,
            Foreground = Colores.Pincel(estilo.Color),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        Typography.SetNumeralAlignment(numeros, FontNumeralAlignment.Tabular);
        var leyenda = new TextBlock
        {
            Text = Transformar(d.Texto, estilo),
            FontFamily = FuentesCoraza.Obtener(estilo.Fuente),
            FontWeight = FontWeight.FromOpenTypeWeight(Math.Clamp(estilo.Peso, 100, 950)),
            Foreground = Colores.Pincel(tema.Pie.Color),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        if (tema.Sombra.Activa && !Miniatura)
        {
            _sombraTexto = CrearSombra(tema.Sombra);
            numeros.Effect = _sombraTexto;
            leyenda.Effect = CrearSombra(tema.Sombra);
        }
        var pila = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        pila.Children.Add(leyenda);
        pila.Children.Add(numeros);
        _areaTexto = new Grid { Opacity = TextoOculto ? 0 : 1 };
        _areaTexto.Children.Add(pila);
        Children.Add(_areaTexto);

        void Actualizar()
        {
            var resto = fin - DateTime.Now;
            if (resto <= TimeSpan.Zero)
            {
                numeros.Text = "00:00";
                if (!string.IsNullOrWhiteSpace(d.TextoFinal)) leyenda.Text = Transformar(d.TextoFinal, estilo);
                return;
            }
            resto = TimeSpan.FromSeconds(Math.Ceiling(resto.TotalSeconds));
            numeros.Text = resto.TotalHours >= 1 ? resto.ToString(@"h\:mm\:ss") : resto.ToString(@"mm\:ss");
        }

        _medirCuenta = (ancho, alto) =>
        {
            var tam = alto * Math.Clamp(estilo.TamanoPct, 2, 20) / 100;
            numeros.FontSize = Math.Min(tam * 2.6, alto * 0.32);
            leyenda.FontSize = tam * 0.75;
            leyenda.MaxWidth = ancho * 0.85;
            leyenda.Margin = new Thickness(0, 0, 0, alto * 0.02);
            if (_sombraTexto is not null) AjustarSombra(_sombraTexto, tema.Sombra, alto);
        };
        Actualizar();
        if (Miniatura) return;
        _reloj = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(250) };
        _reloj.Tick += (_, _) => Actualizar();
        _reloj.Start();
    }

    private void ConstruirTexto(Diapositiva d, Tema tema)
    {
        var estilo = tema.Texto;
        var revelar = ModoRevelado != ModoRevelado.Ninguno && !Miniatura;
        var colorTexto = Colores.DesdeHex(estilo.Color);
        _texto = new TextoAjustable
        {
            Ajustar = estilo.AjusteAutomatico,
            Interlineado = estilo.Interlineado,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var bloque = _texto.Bloque;
        bloque.FontFamily = FuentesCoraza.Obtener(estilo.Fuente);
        bloque.FontWeight = FontWeight.FromOpenTypeWeight(Math.Clamp(estilo.Peso, 100, 950));
        bloque.FontStyle = estilo.Cursiva ? FontStyles.Italic : FontStyles.Normal;
        bloque.Foreground = Colores.Pincel(estilo.Color);
        bloque.TextAlignment = estilo.AlineacionH switch
        {
            AlineacionHorizontal.Izquierda => TextAlignment.Left,
            AlineacionHorizontal.Derecha => TextAlignment.Right,
            AlineacionHorizontal.Justificado => TextAlignment.Justify,
            _ => TextAlignment.Center,
        };
        TextOptions.SetTextFormattingMode(bloque, TextFormattingMode.Ideal);

        if (d.Versiculos is { Count: > 0 })
        {
            var colorNumero = Colores.DesdeHex(tema.Pie.Color);
            foreach (var segmento in d.Versiculos)
            {
                if (tema.NumerosVersiculo && segmento.Numero > 0)
                {
                    bloque.Inlines.Add(new Run(segmento.Numero.ToString(CultureInfo.InvariantCulture))
                    {
                        Tag = TextoAjustable.MarcaSuperindice,
                        BaselineAlignment = BaselineAlignment.Superscript,
                        Foreground = revelar ? Revelable(colorNumero) : Colores.Pincel(tema.Pie.Color),
                        FontWeight = FontWeights.SemiBold,
                    });
                    bloque.Inlines.Add(new Run(" "));
                }
                AgregarTexto(bloque, Transformar(segmento.Texto, estilo) + " ", colorTexto, revelar);
            }
        }
        else if (d.ConFormato)
        {
            AgregarFormateado(bloque, Transformar(d.Texto, estilo), colorTexto, Colores.DesdeHex(tema.Pie.Color, colorTexto), revelar);
        }
        else if (revelar)
        {
            AgregarTexto(bloque, Transformar(d.Texto, estilo), colorTexto, true);
        }
        else
        {
            bloque.Text = Transformar(d.Texto, estilo);
        }

        if (tema.Sombra.Activa && !Miniatura)
        {
            _sombraTexto = CrearSombra(tema.Sombra);
            bloque.Effect = _sombraTexto;
        }

        _caja = new Border
        {
            Child = _texto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = estilo.AlineacionV switch
            {
                AlineacionVertical.Arriba => VerticalAlignment.Top,
                AlineacionVertical.Abajo => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Center,
            },
        };
        if (tema.Caja.Activa) _caja.Background = Colores.Pincel(tema.Caja.Color, Math.Clamp(tema.Caja.Opacidad, 0, 1));

        _areaTexto = new Grid { Opacity = TextoOculto ? 0 : 1 };
        _areaTexto.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _areaTexto.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _areaTexto.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetRow(_caja, 1);
        _areaTexto.Children.Add(_caja);

        if (tema.Pie.Mostrar && !string.IsNullOrWhiteSpace(d.Pie))
        {
            var arriba = tema.Pie.Posicion is PosicionPie.ArribaIzquierda or PosicionPie.ArribaCentro or PosicionPie.ArribaDerecha;
            _pie = new TextBlock
            {
                Text = d.Pie,
                FontFamily = FuentesCoraza.Obtener(tema.Pie.Fuente),
                FontWeight = FontWeights.SemiBold,
                Foreground = Colores.Pincel(tema.Pie.Color),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = tema.Pie.Posicion switch
                {
                    PosicionPie.AbajoIzquierda or PosicionPie.ArribaIzquierda => HorizontalAlignment.Left,
                    PosicionPie.AbajoDerecha or PosicionPie.ArribaDerecha => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Center,
                },
            };
            if (tema.Sombra.Activa && !Miniatura)
            {
                _sombraPie = CrearSombra(tema.Sombra);
                _pie.Effect = _sombraPie;
            }
            SetRow(_pie, arriba ? 0 : 2);
            _areaTexto.Children.Add(_pie);
        }

        Children.Add(_areaTexto);
    }

    /// <summary>Agrega texto al bloque; para revelarlo se divide en líneas, palabras o letras con su propio pincel.</summary>
    private void AgregarTexto(TextBlock bloque, string texto, Color color, bool revelar)
    {
        if (!revelar)
        {
            bloque.Inlines.Add(new Run(texto));
            return;
        }
        var lineas = texto.Split('\n');
        for (var i = 0; i < lineas.Length; i++)
        {
            if (i > 0) bloque.Inlines.Add(new LineBreak());
            switch (ModoRevelado)
            {
                case ModoRevelado.Lineas:
                    bloque.Inlines.Add(new Run(lineas[i]) { Foreground = Revelable(color) });
                    break;
                case ModoRevelado.Palabras:
                    foreach (var palabra in PalabrasRegex().Split(lineas[i]))
                        if (palabra.Length > 0) bloque.Inlines.Add(new Run(palabra) { Foreground = Revelable(color) });
                    break;
                default:
                    var elementos = StringInfo.GetTextElementEnumerator(lineas[i]);
                    while (elementos.MoveNext()) bloque.Inlines.Add(new Run(elementos.GetTextElement()) { Foreground = Revelable(color) });
                    break;
            }
        }
    }

    /// <summary>Texto con formato sencillo (negrita, cursiva, subrayado, resaltado y colores); también se puede revelar.</summary>
    private void AgregarFormateado(TextBlock bloque, string texto, Color colorBase, Color colorResaltado, bool revelar)
    {
        var lineas = texto.Split('\n');
        for (var i = 0; i < lineas.Length; i++)
        {
            if (i > 0) bloque.Inlines.Add(new LineBreak());
            foreach (var fragmento in Coraza.Core.Diapositivas.FormatoTexto.Analizar(lineas[i]))
            {
                var color = fragmento.Color is not null ? Colores.DesdeHex(fragmento.Color, colorBase)
                    : fragmento.Resaltado ? colorResaltado
                    : colorBase;
                IEnumerable<string> piezas = !revelar || ModoRevelado == ModoRevelado.Lineas
                    ? new[] { fragmento.Texto }
                    : ModoRevelado == ModoRevelado.Palabras
                        ? PalabrasRegex().Split(fragmento.Texto).Where(p => p.Length > 0)
                        : ElementosDeTexto(fragmento.Texto);
                foreach (var pieza in piezas)
                {
                    var pincel = revelar ? Revelable(color) : Congelar(new SolidColorBrush(color));
                    var run = new Run(pieza) { Foreground = pincel };
                    if (fragmento.Negrita || fragmento.Resaltado) run.FontWeight = FontWeights.Bold;
                    if (fragmento.Cursiva) run.FontStyle = FontStyles.Italic;
                    if (fragmento.Subrayado) run.TextDecorations = TextDecorations.Underline;
                    bloque.Inlines.Add(run);
                }
            }
        }
    }

    private static IEnumerable<string> ElementosDeTexto(string texto)
    {
        var elementos = StringInfo.GetTextElementEnumerator(texto);
        while (elementos.MoveNext()) yield return elementos.GetTextElement();
    }

    private static SolidColorBrush Congelar(SolidColorBrush pincel)
    {
        pincel.Freeze();
        return pincel;
    }

    private SolidColorBrush Revelable(Color color)
    {
        var pincel = new SolidColorBrush(color) { Opacity = 0 };
        _revelables.Add(pincel);
        return pincel;
    }

    private void IniciarRevelado()
    {
        var n = _revelables.Count;
        var (paso, fundido) = ModoRevelado switch
        {
            ModoRevelado.Lineas => (Math.Min(420, 2400.0 / n), 420.0),
            ModoRevelado.Palabras => (Math.Min(150, 3200.0 / n), 280.0),
            _ => (Math.Min(38, 3800.0 / n), 90.0),
        };
        var suave = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (var i = 0; i < n; i++)
        {
            _revelables[i].BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fundido))
            {
                BeginTime = TimeSpan.FromMilliseconds(100 + i * paso),
                EasingFunction = suave,
            });
        }
    }

    private static void AplicarKenBurns(Image imagen)
    {
        var escala = new ScaleTransform(1, 1);
        var desplazamiento = new TranslateTransform();
        imagen.RenderTransformOrigin = new Point(0.5, 0.5);
        imagen.RenderTransform = new TransformGroup { Children = { escala, desplazamiento } };
        var suave = new SineEase { EasingMode = EasingMode.EaseInOut };
        var zoom = new DoubleAnimation(1, 1.12, TimeSpan.FromSeconds(18)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = suave };
        escala.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        escala.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
        imagen.SizeChanged += (_, e) =>
        {
            var recorrido = e.NewSize.Width * 0.03;
            desplazamiento.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-recorrido, recorrido, TimeSpan.FromSeconds(23))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = suave,
            });
        };
    }

    private void ActualizarMedidas()
    {
        var alto = ActualHeight > 0 ? ActualHeight : (double.IsNaN(Height) ? 0 : Height);
        var ancho = ActualWidth > 0 ? ActualWidth : (double.IsNaN(Width) ? 0 : Width);
        if (alto <= 0 || ancho <= 0) return;
        if (_medirCuenta is not null)
        {
            _medirCuenta(ancho, alto);
            return;
        }
        if (_areaTexto is null || _texto is null) return;

        var tema = Tema ?? new Tema();
        var margen = Math.Clamp(tema.MargenPct, 0, 25) / 100;
        _areaTexto.Margin = new Thickness(ancho * margen, alto * margen, ancho * margen, alto * margen);

        _texto.TamanoMaximo = alto * Math.Clamp(tema.Texto.TamanoPct, 0.5, 60) / 100;
        _texto.TamanoMinimo = alto * Math.Clamp(tema.Texto.TamanoMinimoPct, 0.5, 60) / 100;

        if (_caja is not null && tema.Caja.Activa)
        {
            _caja.Padding = new Thickness(alto * 0.025, alto * 0.018, alto * 0.025, alto * 0.018);
            _caja.CornerRadius = new CornerRadius(alto * tema.Caja.RadioPct / 100);
        }

        if (_sombraTexto is not null) AjustarSombra(_sombraTexto, tema.Sombra, alto);
        if (_sombraPie is not null) AjustarSombra(_sombraPie, tema.Sombra, alto * 0.6);

        if (_pie is not null)
        {
            _pie.FontSize = Math.Max(1, alto * tema.Pie.TamanoPct / 100);
            var separacion = alto * 0.02;
            _pie.Margin = GetRow(_pie) == 0 ? new Thickness(0, 0, 0, separacion) : new Thickness(0, separacion, 0, 0);
        }
    }

    private static string Transformar(string texto, EstiloTexto estilo) =>
        estilo.Mayusculas ? texto.ToUpper(Espanol) : texto;

    private static DropShadowEffect CrearSombra(EstiloSombra s) => new()
    {
        Color = Colores.DesdeHex(s.Color, Colors.Black),
        Opacity = Math.Clamp(s.Opacidad, 0, 1),
        Direction = 300,
        RenderingBias = RenderingBias.Performance,
    };

    private static void AjustarSombra(DropShadowEffect efecto, EstiloSombra s, double alto)
    {
        efecto.BlurRadius = Math.Max(0, alto * s.Desenfoque / 100);
        efecto.ShadowDepth = Math.Max(0, alto * s.Distancia / 100);
    }

    private static readonly Regex PalabrasRegexInstancia = new(@"(?<=\s)", RegexOptions.Compiled);

    private static Regex PalabrasRegex() => PalabrasRegexInstancia;
}
