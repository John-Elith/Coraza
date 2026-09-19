using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Coraza.App.Infraestructura;
using Coraza.Rendering;

namespace Coraza.App.Servicios;

/// <summary>
/// Ventana de salida: sin bordes y a pantalla completa en el Video Beam, o flotante y redimensionable
/// para ensayar cuando solo hay una pantalla. Nunca roba el foco al operador y no se cierra por error.
/// </summary>
public sealed class VentanaProyeccion : Window
{
    private readonly Grid _raiz = new() { Background = Brushes.Black };
    private Int32Rect? _limites;
    private double? _proporcionForzada;

    public VentanaProyeccion(bool pantallaCompleta, Rect? limitesEnsayo = null)
    {
        PantallaCompleta = pantallaCompleta;
        Title = pantallaCompleta ? "Coraza · Proyección" : "Coraza · Ventana de ensayo";
        Background = Brushes.Black;
        ShowActivated = false;
        ShowInTaskbar = false;
        Content = _raiz;
        _raiz.Children.Add(Salida);

        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Recursos/coraza.ico"));
        }
        catch (Exception)
        {
            // El icono es opcional.
        }

        if (pantallaCompleta)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
        }
        else
        {
            // Ventana de ensayo: pequeña, sobre el monitor «En vivo» (que muestra lo mismo) y recordando
            // dónde la dejó el operador. Pertenece a la ventana principal: queda encima de ella sin tapar otras aplicaciones.
            WindowStyle = WindowStyle.ToolWindow;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 240;
            MinHeight = 160;
            var area = SystemParameters.WorkArea;
            if (limitesEnsayo is { } r && r.Width >= MinWidth && r.Height >= MinHeight && area.IntersectsWith(r))
            {
                Left = r.X;
                Top = r.Y;
                Width = r.Width;
                Height = r.Height;
            }
            else
            {
                Width = 336;
                Height = 220;
                Left = area.Right - Width - 10;
                Top = area.Bottom - Height - 36;
            }
            if (Application.Current?.MainWindow is { IsLoaded: true } principal && principal != this) Owner = principal;
            Nativo.BarraTituloOscura(this);
        }

        _raiz.SizeChanged += (_, _) => AjustarSalida();
        SourceInitialized += AlInicializar;
        DpiChanged += (_, _) =>
        {
            if (PantallaCompleta) Dispatcher.BeginInvoke(AplicarLimites, DispatcherPriority.Loaded);
        };
        Closing += (_, e) =>
        {
            if (PermitirCierre) return;
            e.Cancel = true;
            CierreSolicitado?.Invoke(this, EventArgs.Empty);
        };
        PreviewKeyDown += (s, e) => Tecla?.Invoke(s, e);
    }

    public SalidaProyeccion Salida { get; } = new();

    public bool PantallaCompleta { get; }

    public bool PermitirCierre { get; set; }

    public bool OcultarCursor
    {
        set => Cursor = value && PantallaCompleta ? Cursors.None : null;
    }

    /// <summary>Proporción forzada (4:3, 16:9…); el resto se rellena de negro.</summary>
    public double? ProporcionForzada
    {
        get => _proporcionForzada;
        set
        {
            _proporcionForzada = value;
            AjustarSalida();
        }
    }

    public event EventHandler? CierreSolicitado;

    public event KeyEventHandler? Tecla;

    /// <summary>Coloca la ventana exactamente sobre la pantalla (en píxeles físicos).</summary>
    public void ColocarEn(Int32Rect limites)
    {
        _limites = limites;
        AplicarLimites();
    }

    /// <summary>Muestra un aviso breve sobre la proyección que se desvanece solo.</summary>
    public void MostrarAviso(string texto)
    {
        var aviso = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x1E, 0x0B, 0x0F)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0x8F, 0x8F)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24, 12, 24, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 48, 0, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = texto,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0x8F, 0x8F)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
            },
        };
        _raiz.Children.Add(aviso);
        var desvanecer = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromSeconds(4) };
        desvanecer.Completed += (_, _) => _raiz.Children.Remove(aviso);
        aviso.BeginAnimation(OpacityProperty, desvanecer);
    }

    private void AlInicializar(object? sender, EventArgs e)
    {
        if (!PantallaCompleta) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        Nativo.AgregarEstiloExtendido(hwnd, Nativo.WS_EX_NOACTIVATE | Nativo.WS_EX_TOOLWINDOW);
        AplicarLimites();
    }

    private void AplicarLimites()
    {
        if (_limites is not { } r) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Nativo.SetWindowPos(hwnd, Nativo.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, Nativo.SWP_NOACTIVATE);
    }

    private void AjustarSalida()
    {
        var ancho = _raiz.ActualWidth;
        var alto = _raiz.ActualHeight;
        if (ancho <= 0 || alto <= 0) return;
        if (_proporcionForzada is { } p)
        {
            var w = ancho;
            var h = ancho / p;
            if (h > alto)
            {
                h = alto;
                w = alto * p;
            }
            Salida.Width = w;
            Salida.Height = h;
        }
        else
        {
            Salida.Width = double.NaN;
            Salida.Height = double.NaN;
        }
    }
}
