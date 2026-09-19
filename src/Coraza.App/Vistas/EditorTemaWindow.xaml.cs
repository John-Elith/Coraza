using System.Windows;
using Coraza.App.ViewModels;

namespace Coraza.App.Vistas;

public partial class EditorTemaWindow : Window
{
    public EditorTemaWindow(EditorTemaViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Dialogo.Preparar(this);
        vm.CerrarSolicitado += ok => DialogResult = ok;
    }
}
