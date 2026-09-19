using System.Windows;
using System.Windows.Controls;

namespace Coraza.App.Controles;

/// <summary>Selector de color: muestra, código hexadecimal editable y paleta con los colores de Coraza.</summary>
public partial class SelectorColor : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(string), typeof(SelectorColor),
        new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly string[] ColoresPaleta =
    {
        "#64242F", "#B44446", "#FC8F8F", "#DFD9D8", "#1E0B0F", "#2A1216", "#3A1820", "#7A2C39",
        "#FFFFFF", "#F4F1F0", "#D9D9D9", "#A99597", "#6B6B6B", "#333333", "#111111", "#000000",
        "#4E8F6A", "#D9A441", "#E8C9A0", "#8C6E4E", "#1F3A5F", "#3E6B8A", "#5B3A6E", "#9B5DE5",
    };

    public SelectorColor()
    {
        InitializeComponent();
        Paleta.ItemsSource = ColoresPaleta;
    }

    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private void Muestra_Click(object sender, RoutedEventArgs e) => Ventana.IsOpen = true;

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color }) Color = color;
        Ventana.IsOpen = false;
    }
}
