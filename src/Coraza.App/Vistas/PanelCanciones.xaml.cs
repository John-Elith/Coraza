using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Coraza.App.Servicios;
using Coraza.App.ViewModels;
using Coraza.Core.Modelos;

namespace Coraza.App.Vistas;

public partial class PanelCanciones : UserControl
{
    public PanelCanciones()
    {
        InitializeComponent();
        Arrastre.Habilitar(Lista, item => item is Cancion c ? FabricaElementos.DeCancion(c) : null, Arrastre.FormatoElemento);
    }

    private void Lista_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Arrastre.Contenedor(e.OriginalSource as DependencyObject)?.DataContext is Cancion cancion && DataContext is PanelCancionesViewModel vm)
            vm.AgregarAlServicioCommand.Execute(cancion);
    }
}
