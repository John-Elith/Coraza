using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Coraza.App.Infraestructura;
using Coraza.App.ViewModels;
using Coraza.Core.Modelos;

namespace Coraza.App.Vistas;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _cambioPantallas;
    private DateTime _esperandoVersoHasta = DateTime.MinValue;
    private double _anchoIzquierdoGuardado;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        ColIzquierda.Width = new GridLength(Math.Clamp(vm.Ctx.Preferencias.AnchoIzquierdo, 250, 520));
        ColDerecha.Width = new GridLength(Math.Clamp(vm.Ctx.Preferencias.AnchoDerecho, 280, 560));

        // Al conectar un proyector Windows envía varios avisos seguidos: se espera a que termine.
        _cambioPantallas = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _cambioPantallas.Tick += (_, _) =>
        {
            _cambioPantallas.Stop();
            _vm.AlCambiarPantallas();
        };

        Loaded += async (_, _) =>
        {
            _vm.Proyeccion.Registrar(SalidaVivo);
            await _vm.PrepararAsync();
            if (!_vm.Ctx.Preferencias.AsistenteCompletado) Ventanas.Asistente(_vm);
            EnfocarDiapositivas();
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(Gancho);
        StateChanged += (_, _) => AjustarMaximizado();
        Closing += AlCerrar;
        PreviewKeyDown += AlPresionarTecla;
        _vm.Proyeccion.TeclaEnSalida += (_, e) => AlPresionarTecla(this, e);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ModoConcentracion)) AplicarConcentracion();
        };
        _vm.Busqueda.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(BusquedaUniversalViewModel.Abierta)) return;
            if (_vm.Busqueda.Abierta) Dispatcher.BeginInvoke(() => Keyboard.Focus(CajaBusqueda), DispatcherPriority.Input);
            else EnfocarDiapositivas();
        };
        AjustarMaximizado();
    }

    // ================= Vista previa de transiciones =================

    /// <summary>Segunda diapositiva de la demostración: el cambio de texto hace visible el efecto.</summary>
    private static readonly Diapositiva MuestraDemo = new()
    {
        Texto = "Cantaré de tu amor\npara siempre",
        Etiqueta = "Muestra",
    };

    private DispatcherTimer? _finDemo;

    private OpcionTransicion? _demoMostrada;

    private void SelectorTransicion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _demoMostrada = null;
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is OpcionTransicion opcion) MostrarDemoTransicion(opcion);
    }

    /// <summary>
    /// Al pasar el mouse por la lista desplegable se ve la transición antes de elegirla. Se sigue el
    /// movimiento sobre el ComboBox porque MouseEnter es un evento directo y no llega desde el desplegable.
    /// </summary>
    private void SelectorTransicion_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!SelectorTransicion.IsDropDownOpen) return;
        var elemento = e.OriginalSource as DependencyObject;
        while (elemento is not null and not ComboBoxItem)
            elemento = elemento is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(elemento)
                : LogicalTreeHelper.GetParent(elemento);
        if (elemento is not ComboBoxItem { DataContext: OpcionTransicion opcion } || ReferenceEquals(opcion, _demoMostrada)) return;
        _demoMostrada = opcion;
        MostrarDemoTransicion(opcion);
    }

    private void RepetirDemoTransicion_Click(object sender, RoutedEventArgs e) => MostrarDemoTransicion(_vm.TransicionSeleccionada);

    /// <summary>Demuestra la transición sobre la vista previa, sin tocar lo que está proyectado.</summary>
    private void MostrarDemoTransicion(OpcionTransicion opcion)
    {
        var tema = _vm.Proveedor.TemaPara(TipoContenido.Canciones);
        var transicion = opcion.Valor ?? tema.Transicion;
        var duracion = opcion.Valor is null ? tema.DuracionTransicionMs : _vm.DuracionTransicion;

        // La demostración obedece «Reducir movimiento»: la vista previa nunca promete algo que la proyección no hará.
        var reducido = _vm.Ctx.Preferencias.ReducirMovimiento;
        DemoTransicion.ReducirMovimiento = reducido;
        TextoDemo.Text = reducido ? $"{opcion.Nombre} · sin animación («Reducir movimiento»)" : opcion.Nombre;
        EtiquetaDemo.Visibility = Visibility.Visible;
        CajaDemoTransicion.Visibility = Visibility.Visible;

        // La primera aparece de golpe y la segunda con la transición: así se ve el efecto completo.
        DemoTransicion.Mostrar(PanelTemasViewModel.MuestraCancion, tema, TipoTransicion.Corte, 0, instantaneo: true);
        Dispatcher.BeginInvoke(() => DemoTransicion.Mostrar(MuestraDemo, tema, transicion, duracion), DispatcherPriority.Background);

        if (_finDemo is null)
        {
            _finDemo = new DispatcherTimer();
            _finDemo.Tick += (_, _) =>
            {
                _finDemo!.Stop();
                CajaDemoTransicion.Visibility = Visibility.Collapsed;
                EtiquetaDemo.Visibility = Visibility.Collapsed;
            };
        }
        _finDemo.Stop();
        _finDemo.Interval = TimeSpan.FromMilliseconds(Math.Max(1600, duracion + 1000));
        _finDemo.Start();
    }

    public void EnfocarDiapositivas()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (ListaDiapositivas.SelectedItem is not null
                && ListaDiapositivas.ItemContainerGenerator.ContainerFromItem(ListaDiapositivas.SelectedItem) is ListBoxItem item)
                item.Focus();
            else
                ListaDiapositivas.Focus();
        }, DispatcherPriority.Input);
    }

    private IntPtr Gancho(IntPtr hwnd, int mensaje, IntPtr wParam, IntPtr lParam, ref bool manejado)
    {
        if (mensaje == Nativo.WM_DISPLAYCHANGE)
        {
            _cambioPantallas.Stop();
            _cambioPantallas.Start();
        }
        return IntPtr.Zero;
    }

    private void AjustarMaximizado()
    {
        // Con WindowChrome la ventana maximizada sobresale del monitor: se compensa con un margen.
        var maximizada = WindowState == WindowState.Maximized;
        Raiz.Margin = maximizada ? new Thickness(7) : new Thickness(0);
        IconoMaximizar.Datos = (System.Windows.Media.Geometry)FindResource(maximizada ? "Ico.Restaurar" : "Ico.Maximizar");
    }

    private void AlCerrar(object? sender, CancelEventArgs e)
    {
        if (_vm.Proyeccion.Proyectando
            && !Dialogo.Confirmar("Cerrar Coraza", "La proyección está activa. ¿Cerrar Coraza de todas formas?", "Cerrar", peligro: true))
        {
            e.Cancel = true;
            return;
        }
        _vm.GuardarServicioAhora();
        var p = _vm.Ctx.Preferencias;
        if (!_vm.ModoConcentracion && ColIzquierda.ActualWidth > 0) p.AnchoIzquierdo = ColIzquierda.ActualWidth;
        if (ColDerecha.ActualWidth > 0) p.AnchoDerecho = ColDerecha.ActualWidth;
        _vm.Ctx.GuardarPreferencias();
        if (_vm.Proyeccion.Proyectando) _vm.Proyeccion.Detener();

        // Sin esperar: DetenerAsync cierra el escuchador y los flujos abiertos, pero
        // bloquear aquí el hilo de interfaz podría trabar el cierre. Lo que quede se
        // lo lleva el fin del proceso.
        _ = _vm.Remoto.DetenerAsync();
    }

    private void AplicarConcentracion()
    {
        if (_vm.ModoConcentracion)
        {
            _anchoIzquierdoGuardado = ColIzquierda.ActualWidth;
            ColIzquierda.MinWidth = 0;
            ColIzquierda.Width = new GridLength(0);
            ColDivisorIzquierdo.Width = new GridLength(0);
            PanelIzquierdo.Visibility = Visibility.Collapsed;
            DivisorIzquierdo.Visibility = Visibility.Collapsed;
            _vm.MostrarMensaje("Modo concentración: F11 para mostrar de nuevo la biblioteca.");
        }
        else
        {
            PanelIzquierdo.Visibility = Visibility.Visible;
            DivisorIzquierdo.Visibility = Visibility.Visible;
            ColDivisorIzquierdo.Width = new GridLength(6);
            ColIzquierda.MinWidth = 250;
            ColIzquierda.Width = new GridLength(Math.Max(250, _anchoIzquierdoGuardado));
        }
    }

    // ================= Teclado =================

    private void AlPresionarTecla(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _vm.Busqueda.Abierta || _vm.Preparando) return;
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        if (tecla is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;
        var modificadores = Keyboard.Modifiers;

        // En pantalla completa, Esc sale de ella (Mayús + Esc limpia la pantalla sin salir).
        if (tecla == Key.Escape && modificadores == ModifierKeys.None && _vm.Proyeccion.PantallaCompleta)
        {
            _vm.SalirPantallaCompleta();
            e.Handled = true;
            return;
        }

        var foco = Keyboard.FocusedElement;
        var escribiendo = foco is TextBox or PasswordBox or ComboBoxItem;

        if (escribiendo && GestorAtajos.TeclaDeEscritura(tecla, modificadores))
        {
            // Esc en un cuadro de texto: sale del cuadro para que los atajos vuelvan a funcionar.
            if (tecla == Key.Escape && foco is TextBox)
            {
                EnfocarDiapositivas();
                e.Handled = true;
            }
            return;
        }

        // Ctrl + V fuera de un cuadro de texto: pegar imágenes en la biblioteca de medios.
        if (tecla == Key.V && modificadores == ModifierKeys.Control && (Clipboard.ContainsImage() || Clipboard.ContainsFileDropList()))
        {
            Pestanas.SelectedIndex = 2;
            _vm.Medios.PegarCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (tecla == Key.Delete && modificadores == ModifierKeys.None && ListaServicio.IsKeyboardFocusWithin && _vm.ElementoSeleccionado is not null)
        {
            _vm.QuitarElementoCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // «V» seguido de un número: saltar a ese verso.
        if (DateTime.Now < _esperandoVersoHasta && modificadores == ModifierKeys.None && Digito(tecla) is int numero)
        {
            _esperandoVersoHasta = DateTime.MinValue;
            _vm.IrASeccion("V" + numero);
            e.Handled = true;
            return;
        }

        var accion = _vm.Atajos.Buscar(tecla, modificadores);
        if (accion is null) return;
        switch (accion.Value)
        {
            case AccionAtajo.IrVerso:
                _esperandoVersoHasta = DateTime.Now.AddSeconds(1.5);
                _vm.MostrarMensaje("Verso: pulsa el número (1, 2, 3…).");
                break;
            case AccionAtajo.IrBiblia:
                Pestanas.SelectedIndex = 1;
                Dispatcher.BeginInvoke(PanelBiblia.EnfocarBusqueda, DispatcherPriority.Input);
                break;
            default:
                _vm.EjecutarAtajo(accion.Value);
                break;
        }
        e.Handled = true;
    }

    private static int? Digito(Key tecla) => tecla switch
    {
        >= Key.D1 and <= Key.D9 => tecla - Key.D0,
        >= Key.NumPad1 and <= Key.NumPad9 => tecla - Key.NumPad0,
        _ => null,
    };

    // ================= Búsqueda universal =================

    private void CajaBusqueda_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _vm.Busqueda.Mover(1);
                ListaResultados.ScrollIntoView(ListaResultados.SelectedItem);
                break;
            case Key.Up:
                _vm.Busqueda.Mover(-1);
                ListaResultados.ScrollIntoView(ListaResultados.SelectedItem);
                break;
            case Key.Enter:
                _vm.Busqueda.EjecutarCommand.Execute(null);
                break;
            case Key.Escape:
                _vm.Busqueda.CerrarCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void ListaResultados_Click(object sender, MouseButtonEventArgs e)
    {
        if (Arrastre.Contenedor(e.OriginalSource as DependencyObject)?.DataContext is ResultadoBusqueda resultado)
            _vm.Busqueda.EjecutarCommand.Execute(resultado);
    }

    private void Velo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _vm.Busqueda.CerrarCommand.Execute(null);

    // ================= Servicio y diapositivas =================

    private void ListaServicio_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Arrastre.Contenedor(e.OriginalSource as DependencyObject)?.DataContext is ElementoViewModel { EsEncabezado: false } elemento)
        {
            if (!ReferenceEquals(_vm.ElementoActual, elemento.Modelo)) _vm.MostrarElemento(elemento.Modelo);
            _vm.EnviarAlVivoCommand.Execute(null);
        }
    }

    private void ListaDiapositivas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Arrastre.Contenedor(e.OriginalSource as DependencyObject)?.DataContext is DiapositivaViewModel diapositiva)
            _vm.EnviarDiapositivaAlVivo(diapositiva);
    }

    private void ListaServicio_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(Arrastre.FormatoReordenar) ? DragDropEffects.Move
            : e.Data.GetDataPresent(Arrastre.FormatoElemento) || e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ListaServicio_Drop(object sender, DragEventArgs e)
    {
        var destino = Arrastre.IndiceDestino(ListaServicio, e);
        if (e.Data.GetData(Arrastre.FormatoReordenar) is ElementoViewModel mover)
        {
            var actual = _vm.Elementos.IndexOf(mover);
            _vm.MoverElemento(mover, destino > actual ? destino - 1 : destino);
        }
        else if (e.Data.GetData(Arrastre.FormatoElemento) is ElementoServicio elemento)
        {
            _vm.AgregarAlServicio(_vm.ReutilizarActual(elemento), destino);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] archivos)
        {
            var nuevos = await _vm.Medios.ImportarAsync(archivos.Where(a => File.Exists(a) || Directory.Exists(a)));
            foreach (var medio in nuevos.Where(m => m.Tipo != TipoMedio.Audio))
                _vm.AgregarAlServicio(Servicios.FabricaElementos.DeMedio(medio), destino++);
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Arrastre.Habilitar(ListaServicio, item => item as ElementoViewModel, Arrastre.FormatoReordenar);
    }

    // ================= Botones de ventana =================

    private void Minimizar_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximizar_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
