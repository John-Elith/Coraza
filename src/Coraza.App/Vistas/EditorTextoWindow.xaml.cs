using System.Windows;
using System.Windows.Controls;
using Coraza.App.ViewModels;

namespace Coraza.App.Vistas;

public partial class EditorTextoWindow : Window
{
    public EditorTextoWindow(EditorTextoViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Dialogo.Preparar(this);
        vm.CerrarSolicitado += ok => DialogResult = ok;
        Loaded += (_, _) => CajaTitulo.Focus();
    }

    /// <summary>Envuelve la selección con las marcas de formato (o inserta un ejemplo si no hay selección).</summary>
    private void Formato_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string marca }) return;
        var caja = CajaContenido;
        var texto = caja.Text ?? "";
        var inicio = caja.SelectionStart;

        if (marca == "lista")
        {
            var inicioLinea = inicio == 0 ? 0 : texto.LastIndexOf('\n', inicio - 1) + 1;
            caja.Text = texto.Insert(inicioLinea, "- ");
            caja.CaretIndex = inicio + 2;
            caja.Focus();
            return;
        }

        var (abre, cierra) = marca switch
        {
            "b" => ("**", "**"),
            "i" => ("*", "*"),
            "u" => ("__", "__"),
            "r" => ("==", "=="),
            _ when marca.StartsWith("c:") => ("{" + marca[2..] + "}", "{/}"),
            _ => ("", ""),
        };
        var largo = caja.SelectionLength;
        var contenido = largo > 0 ? texto.Substring(inicio, largo) : "texto";
        caja.Text = texto.Remove(inicio, largo).Insert(inicio, abre + contenido + cierra);
        caja.Select(inicio + abre.Length, contenido.Length);
        caja.Focus();
    }
}
