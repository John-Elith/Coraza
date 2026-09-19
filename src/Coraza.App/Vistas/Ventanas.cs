using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Coraza.App.ViewModels;
using Coraza.Core.Modelos;

namespace Coraza.App.Vistas;

/// <summary>Abre las ventanas secundarias (editores, servicios, configuración).</summary>
public static class Ventanas
{
    public static long? EditarCancion(MainViewModel main, long? id, string? letraInicial = null)
    {
        var vm = new EditorCancionViewModel(main.Ctx, main.Proveedor, id, letraInicial);
        var ventana = new EditorCancionWindow(vm);
        ventana.ShowDialog();
        return vm.IdGuardado;
    }

    public static bool EditarTexto(MainViewModel main, TextoLibre texto)
    {
        var vm = new EditorTextoViewModel(main.Ctx, main.Proveedor, texto);
        return new EditorTextoWindow(vm).ShowDialog() == true;
    }

    public static bool EditarTema(MainViewModel main, Tema tema)
    {
        var vm = new EditorTemaViewModel(main.Ctx, tema);
        var ok = new EditorTemaWindow(vm).ShowDialog() == true;
        if (ok && vm.Resultado is not null) tema.Id = vm.Resultado.Id;
        return ok;
    }

    public static bool Configuracion(MainViewModel main) =>
        new ConfiguracionWindow(new ConfiguracionViewModel(main)).ShowDialog() == true;

    /// <summary>
    /// Ventana del control remoto: código QR, PIN, quién está conectado y el firewall.
    ///
    /// Se abre con <c>Show()</c> y NO con <c>ShowDialog()</c>, a diferencia del resto:
    /// si fuera modal, el operador quedaría bloqueado del proyector justo mientras
    /// empareja el teléfono, que es cuando más falta le hace poder seguir trabajando.
    /// </summary>
    public static void ControlRemoto(MainViewModel main)
    {
        var vm = new ControlRemotoViewModel(main);
        var ventana = new Window { Title = "Control remoto", Width = 460, Height = 660, ResizeMode = ResizeMode.CanResize };
        Dialogo.Preparar(ventana);

        var qr = new Image { Width = 220, Height = 220, Margin = new Thickness(0, 10, 0, 10), HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(qr, BitmapScalingMode.NearestNeighbor);   // sin suavizado: un QR borroso no se lee

        var pin = new TextBlock { FontSize = 34, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Consolas") };
        var url = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 10) };
        var conectado = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        var aviso = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6) };
        aviso.SetResourceReference(TextBlock.StyleProperty, "T.Suave");

        var direcciones = new ComboBox { Margin = new Thickness(0, 6, 0, 0), DisplayMemberPath = "Direccion" };
        direcciones.ItemsSource = vm.Direcciones;
        direcciones.SelectedItem = vm.DireccionElegida;
        direcciones.SelectionChanged += (_, _) => vm.DireccionElegida = direcciones.SelectedItem as Coraza.Remoto.Red.InterfazLocal;
        direcciones.Visibility = vm.HayVariasDirecciones ? Visibility.Visible : Visibility.Collapsed;

        var comando = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxHeight = 70 };

        Button Boton2(string texto, string? estilo = null)
        {
            var b = new Button { Content = texto, Margin = new Thickness(0, 0, 8, 0), MinWidth = 96 };
            if (estilo is not null) b.Style = (Style)Application.Current.FindResource(estilo);
            return b;
        }

        var encender = Boton2("Encender", "Boton.Primario");
        var desconectar = Boton2("Desconectar", "Boton.Peligro");
        var firewall = Boton2("Permitir en el Firewall…");
        var cerrar = Boton2("Cerrar");
        cerrar.IsCancel = true;

        void Pintar()
        {
            qr.Source = vm.Qr;
            qr.Visibility = vm.Qr is null ? Visibility.Collapsed : Visibility.Visible;
            pin.Text = vm.Activo ? vm.Pin : "——————";
            url.Text = vm.Activo ? vm.Url : "Enciende el control remoto para empezar.";
            conectado.Text = vm.Conectado;
            aviso.Text = vm.AvisoFirewall;
            comando.Text = vm.ComandoManual;
            firewall.Visibility = vm.MostrarBotonFirewall ? Visibility.Visible : Visibility.Collapsed;
            comando.Visibility = vm.MostrarBotonFirewall ? Visibility.Visible : Visibility.Collapsed;
            desconectar.IsEnabled = vm.Activo && main.Remoto.Sesion is not null;
            encender.Content = vm.Activo ? "Apagar" : "Encender";
        }

        encender.Click += async (_, _) =>
        {
            if (vm.Activo) await vm.ApagarCommand.ExecuteAsync(null);
            else vm.EncenderCommand.Execute(null);
            Pintar();
        };
        desconectar.Click += (_, _) => { vm.DesconectarCommand.Execute(null); Pintar(); };
        firewall.Click += (_, _) => { vm.PermitirEnFirewallCommand.Execute(null); Pintar(); };
        cerrar.Click += (_, _) => ventana.Close();

        // El PIN cambia al conceder o revocar, y la sesión aparece cuando el teléfono
        // entra: se repinta cada segundo en vez de cablear notificaciones.
        var reloj = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        reloj.Tick += (_, _) => { vm.Refrescar(); Pintar(); };

        var botones = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        botones.Children.Add(encender);
        botones.Children.Add(desconectar);
        botones.Children.Add(cerrar);

        var contenido = new StackPanel();
        contenido.Children.Add(new TextBlock { Text = "Control remoto", FontSize = 16, FontWeight = FontWeights.SemiBold });
        contenido.Children.Add(new TextBlock
        {
            Text = "Escanea el código con la cámara del teléfono y escribe el PIN. Solo puede haber un teléfono conectado a la vez.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });
        contenido.Children.Add(qr);
        contenido.Children.Add(pin);
        contenido.Children.Add(url);
        contenido.Children.Add(direcciones);
        contenido.Children.Add(conectado);
        contenido.Children.Add(aviso);
        contenido.Children.Add(firewall);
        contenido.Children.Add(comando);
        contenido.Children.Add(botones);

        var raiz = new DockPanel { Margin = new Thickness(18) };
        raiz.Children.Add(new ScrollViewer { Content = contenido, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        ventana.Content = raiz;

        vm.CerrarSolicitado += ventana.Close;
        ventana.Closed += (_, _) => reloj.Stop();
        Pintar();
        reloj.Start();
        ventana.Show();
    }

    /// <summary>Asistente de primer inicio (idioma, Video Beam, tema y versión bíblica).</summary>
    public static void Asistente(MainViewModel main) =>
        new AsistenteWindow(new AsistenteViewModel(main)).ShowDialog();

    /// <summary>Lista de servicios guardados: abrir, duplicar o eliminar.</summary>
    public static long? ElegirServicio(MainViewModel main)
    {
        long? elegido = null;
        var ventana = new Window { Title = "Servicios guardados", Width = 520, Height = 520, ResizeMode = ResizeMode.CanResize };
        Dialogo.Preparar(ventana);

        var lista = new ListBox { Margin = new Thickness(0, 10, 0, 10) };
        lista.ItemTemplate = CrearPlantillaServicio();
        void Cargar() => lista.ItemsSource = main.Ctx.Servicios.Listar();
        Cargar();

        Button Boton(string texto, string? estilo = null)
        {
            var b = new Button { Content = texto, Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
            if (estilo is not null) b.Style = (Style)Application.Current.FindResource(estilo);
            return b;
        }

        var abrir = Boton("Abrir", "Boton.Primario");
        abrir.IsDefault = true;
        abrir.Click += (_, _) =>
        {
            if (lista.SelectedItem is not Servicio s) return;
            elegido = s.Id;
            ventana.Close();
        };
        var duplicar = Boton("Duplicar");
        duplicar.Click += (_, _) =>
        {
            if (lista.SelectedItem is not Servicio s) return;
            main.Ctx.Servicios.Duplicar(s.Id, $"{s.Nombre} (copia)");
            Cargar();
        };
        var eliminar = Boton("Eliminar", "Boton.Peligro");
        eliminar.Click += (_, _) =>
        {
            if (lista.SelectedItem is not Servicio s) return;
            if (s.Id == main.Servicio.Id)
            {
                Dialogo.Informar("Servicio abierto", "No se puede eliminar el servicio que está abierto.");
                return;
            }
            if (!Dialogo.Confirmar("Eliminar servicio", $"¿Eliminar «{s.Nombre}»?", "Eliminar", peligro: true)) return;
            main.Ctx.Servicios.Eliminar(s.Id);
            Cargar();
        };
        var cerrar = Boton("Cerrar");
        cerrar.IsCancel = true;
        cerrar.Click += (_, _) => ventana.Close();
        lista.MouseDoubleClick += (_, _) => abrir.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var botones = new StackPanel { Orientation = Orientation.Horizontal };
        botones.Children.Add(abrir);
        botones.Children.Add(duplicar);
        botones.Children.Add(eliminar);
        botones.Children.Add(cerrar);

        var raiz = new DockPanel { Margin = new Thickness(18) };
        var titulo = new TextBlock { Text = "Servicios guardados", FontSize = 16, FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(titulo, Dock.Top);
        DockPanel.SetDock(botones, Dock.Bottom);
        raiz.Children.Add(titulo);
        raiz.Children.Add(botones);
        raiz.Children.Add(lista);
        ventana.Content = raiz;
        ventana.Loaded += (_, _) => lista.Focus();
        ventana.ShowDialog();
        return elegido;
    }

    private static DataTemplate CrearPlantillaServicio()
    {
        var plantilla = new DataTemplate(typeof(Servicio));
        var pila = new FrameworkElementFactory(typeof(StackPanel));
        var nombre = new FrameworkElementFactory(typeof(TextBlock));
        nombre.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Servicio.Nombre)));
        nombre.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        var fecha = new FrameworkElementFactory(typeof(TextBlock));
        fecha.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Servicio.Modificado)) { StringFormat = "Modificado: {0:g}" });
        fecha.SetValue(TextBlock.ForegroundProperty, Application.Current.FindResource("B.TextoSuave") as Brush);
        fecha.SetValue(TextBlock.FontSizeProperty, 12.0);
        pila.AppendChild(nombre);
        pila.AppendChild(fecha);
        plantilla.VisualTree = pila;
        return plantilla;
    }
}

/// <summary>Arrastrar y soltar entre las bibliotecas y el orden del servicio.</summary>
public static class Arrastre
{
    public const string FormatoElemento = "Coraza.ElementoServicio";
    public const string FormatoReordenar = "Coraza.Reordenar";

    public static void Habilitar(ListBox lista, Func<object, object?> datos, string formato)
    {
        var inicio = default(Point);
        object? item = null;
        lista.PreviewMouseLeftButtonDown += (_, e) =>
        {
            inicio = e.GetPosition(null);
            item = Contenedor(e.OriginalSource as DependencyObject)?.DataContext;
        };
        lista.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || item is null) return;
            var d = e.GetPosition(null) - inicio;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var carga = datos(item);
            item = null;
            if (carga is not null) DragDrop.DoDragDrop(lista, new DataObject(formato, carga), DragDropEffects.Copy | DragDropEffects.Move);
        };
    }

    public static ListBoxItem? Contenedor(DependencyObject? d)
    {
        while (d is not null and not ListBoxItem)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as ListBoxItem;
    }

    /// <summary>Posición donde se soltó: antes o después del elemento bajo el puntero.</summary>
    public static int IndiceDestino(ListBox lista, DragEventArgs e)
    {
        var contenedor = Contenedor(e.OriginalSource as DependencyObject);
        if (contenedor is null) return lista.Items.Count;
        var indice = lista.ItemContainerGenerator.IndexFromContainer(contenedor);
        return e.GetPosition(contenedor).Y > contenedor.ActualHeight / 2 ? indice + 1 : indice;
    }
}
