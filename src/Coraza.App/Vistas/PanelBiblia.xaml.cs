using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Coraza.App.ViewModels;
using Coraza.Core.Modelos;

namespace Coraza.App.Vistas;

public partial class PanelBiblia : UserControl
{
    public PanelBiblia()
    {
        InitializeComponent();
    }

    private PanelBibliaViewModel? Vm => DataContext as PanelBibliaViewModel;

    public void EnfocarBusqueda()
    {
        Cita.Focus();
        Cita.SelectAll();
    }

    /// <summary>Enter: muestra el pasaje y lleva el foco a las diapositivas (un segundo Enter lo envía al vivo).</summary>
    private void Cita_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null) return;
        switch (e.Key)
        {
            case Key.Enter:
                Vm.Interpretar();
                Vm.Mostrar();
                (Window.GetWindow(this) as MainWindow)?.EnfocarDiapositivas();
                e.Handled = true;
                break;
            case Key.Down when ListaVersiculos.IsVisible && ListaVersiculos.Items.Count > 0:
                ListaVersiculos.Focus();
                e.Handled = true;
                break;
        }
    }

    private void ListaVersiculos_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm?.EstablecerSeleccion(ListaVersiculos.SelectedItems.Cast<Versiculo>().ToList());

    private void ListaVersiculos_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Vm?.Mostrar();

    private void ListaResultados_Click(object sender, MouseButtonEventArgs e)
    {
        if (Arrastre.Contenedor(e.OriginalSource as DependencyObject)?.DataContext is Versiculo versiculo)
            Vm?.ElegirResultadoCommand.Execute(versiculo);
    }
}
