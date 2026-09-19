using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Coraza.App.Servicios;
using Coraza.App.ViewModels;

namespace Coraza.App.Vistas;

public partial class PanelMedios : UserControl
{
    public PanelMedios()
    {
        InitializeComponent();
        Arrastre.Habilitar(Lista, item => item is MedioViewModel m ? FabricaElementos.DeMedio(m.Modelo) : null, Arrastre.FormatoElemento);
    }

    private PanelMediosViewModel? Vm => DataContext as PanelMediosViewModel;

    private void Lista_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Lista_Drop(object sender, DragEventArgs e)
    {
        if (Vm is not null && e.Data.GetData(DataFormats.FileDrop) is string[] archivos) await Vm.ImportarAsync(archivos);
    }

    private void Lista_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Arrastre.Contenedor(e.OriginalSource as DependencyObject)?.DataContext is not MedioViewModel medio) return;
        if (medio.EsAudio) Vm?.ReproducirMusicaCommand.Execute(medio);
        else Vm?.AgregarAlServicioCommand.Execute(medio);
    }
}
