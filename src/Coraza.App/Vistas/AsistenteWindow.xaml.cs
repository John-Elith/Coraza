using System.Windows;
using Coraza.App.ViewModels;

namespace Coraza.App.Vistas;

public partial class AsistenteWindow : Window
{
    private bool _cerradoPorAsistente;

    public AsistenteWindow(AsistenteViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Dialogo.Preparar(this);
        vm.Cerrar += () =>
        {
            _cerradoPorAsistente = true;
            Close();
        };
        // Cerrar con la X equivale a «Omitir»: el asistente no vuelve a aparecer solo.
        Closing += (_, _) =>
        {
            if (!_cerradoPorAsistente) vm.Omitir();
        };
        Loaded += (_, _) => BotonSiguiente.Focus();
    }
}
