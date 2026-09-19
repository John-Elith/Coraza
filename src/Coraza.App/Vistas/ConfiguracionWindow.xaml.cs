using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Coraza.App.ViewModels;

namespace Coraza.App.Vistas;

public partial class ConfiguracionWindow : Window
{
    public ConfiguracionWindow(ConfiguracionViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Dialogo.Preparar(this);
        vm.CerrarSolicitado += ok => DialogResult = ok;
        Closed += (_, _) =>
        {
            if (DialogResult != true) vm.RevertirApariencia();
        };
    }

    /// <summary>Captura la tecla pulsada (con Ctrl/Mayús/Alt) y la agrega al atajo.</summary>
    private void Atajo_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: AtajoEditable atajo }) return;
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        if (tecla is Key.Tab or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;
        atajo.Agregar(tecla, Keyboard.Modifiers);
        e.Handled = true;
    }
}
