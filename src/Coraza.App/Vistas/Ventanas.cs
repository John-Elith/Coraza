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
