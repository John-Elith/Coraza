using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Coraza.App.Servicios;
using Coraza.App.ViewModels;
using Coraza.Core.Modelos;

namespace Coraza.App.Vistas;

public partial class PanelTextos : UserControl
{
    public PanelTextos()
    {
        InitializeComponent();
        Arrastre.Habilitar(Lista, item => item is TextoLibre t ? FabricaElementos.DeTexto(t) : null, Arrastre.FormatoElemento);
    }

    private void Lista_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Arrastre.Contenedor(e.OriginalSource as DependencyObject)?.DataContext is TextoLibre texto && DataContext is PanelTextosViewModel vm)
            vm.AgregarAlServicioCommand.Execute(texto);
    }
}
