using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Coraza.App.ViewModels;

namespace Coraza.App.Vistas;

public partial class PanelTemas : UserControl
{
    public PanelTemas()
    {
        InitializeComponent();
    }

    private void Lista_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Arrastre.Contenedor(e.OriginalSource as DependencyObject)?.DataContext is TemaItem tema && DataContext is PanelTemasViewModel vm)
            vm.EditarCommand.Execute(tema);
    }
}
