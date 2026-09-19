using System.Windows;
using System.Windows.Controls;
using Coraza.App.ViewModels;

namespace Coraza.App.Vistas;

public partial class EditorCancionWindow : Window
{
    private readonly EditorCancionViewModel _vm;

    public EditorCancionWindow(EditorCancionViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Dialogo.Preparar(this);
        vm.CerrarSolicitado += ok => DialogResult = ok;
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(vm.Titulo)) CajaTitulo.Focus();
            else CajaLetra.Focus();
        };
    }

    /// <summary>Inserta una etiqueta de sección en la posición del cursor («Verso» se numera solo).</summary>
    private void InsertarEtiqueta_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tipo }) return;
        var etiqueta = tipo switch
        {
            "---" => "---",
            "Verso" => $"[Verso {_vm.Codigos.Count(c => c.StartsWith('V')) + 1}]",
            _ => $"[{tipo}]",
        };
        var caja = CajaLetra;
        var posicion = caja.CaretIndex;
        var texto = caja.Text ?? "";
        var prefijo = posicion == 0 ? "" : texto[..posicion].EndsWith("\n\n") ? "" : texto[..posicion].EndsWith('\n') ? "\n" : "\n\n";
        var insertar = prefijo + etiqueta + "\n";
        caja.Text = texto.Insert(posicion, insertar);
        caja.CaretIndex = posicion + insertar.Length;
        caja.Focus();
    }
}
