using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Coraza.App.Infraestructura;

namespace Coraza.App.Vistas;

/// <summary>Diálogos con el estilo de Coraza: confirmar, informar y pedir un texto.</summary>
public static class Dialogo
{
    public static bool Confirmar(string titulo, string mensaje, string aceptar = "Aceptar", string cancelar = "Cancelar", bool peligro = false) =>
        Mostrar(titulo, mensaje, aceptar, cancelar, peligro, null, out _);

    public static void Informar(string titulo, string mensaje) =>
        Mostrar(titulo, mensaje, "Entendido", null, false, null, out _);

    public static string? PedirTexto(string titulo, string mensaje, string valor = "") =>
        Mostrar(titulo, mensaje, "Aceptar", "Cancelar", false, valor, out var texto) ? texto : null;

    /// <summary>Aplica fondo, tipografía y barra de título (según la paleta) a una ventana secundaria.</summary>
    public static void Preparar(Window ventana)
    {
        // Referencias dinámicas: si la paleta cambia con la ventana abierta, se actualiza al instante.
        ventana.SetResourceReference(Control.BackgroundProperty, "B.Panel");
        ventana.SetResourceReference(Control.ForegroundProperty, "B.Texto");
        ventana.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        ventana.FontSize = 13;
        ventana.ShowInTaskbar = false;
        var propietario = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != ventana)
                          ?? Application.Current.MainWindow;
        if (propietario is not null && propietario != ventana && propietario.IsVisible)
        {
            ventana.Owner = propietario;
            ventana.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            ventana.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        Nativo.BarraTituloOscura(ventana, () => Apariencia.EsOscura);
    }

    private static bool Mostrar(string titulo, string mensaje, string aceptar, string? cancelar, bool peligro, string? valorTexto, out string texto)
    {
        var ventana = new Window
        {
            Title = titulo,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
        };
        Preparar(ventana);

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20), MinWidth = 360 };
        panel.Children.Add(new TextBlock { Text = titulo, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock
        {
            Text = mensaje,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            Foreground = (Brush)Application.Current.FindResource("B.TextoSuave"),
        });

        TextBox? caja = null;
        if (valorTexto is not null)
        {
            caja = new TextBox { Text = valorTexto, Margin = new Thickness(0, 14, 0, 0) };
            panel.Children.Add(caja);
        }

        var resultado = false;
        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var botonAceptar = new Button
        {
            Content = aceptar,
            IsDefault = true,
            MinWidth = 100,
            Style = (Style)Application.Current.FindResource(peligro ? "Boton.Peligro" : "Boton.Primario"),
        };
        botonAceptar.Click += (_, _) =>
        {
            resultado = true;
            ventana.Close();
        };
        if (cancelar is not null)
        {
            var botonCancelar = new Button { Content = cancelar, IsCancel = true, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
            botonCancelar.Click += (_, _) => ventana.Close();
            botones.Children.Add(botonCancelar);
        }
        else
        {
            botonAceptar.IsCancel = true;
        }
        botones.Children.Add(botonAceptar);
        panel.Children.Add(botones);
        ventana.Content = panel;

        ventana.Loaded += (_, _) =>
        {
            if (caja is not null)
            {
                caja.Focus();
                caja.SelectAll();
            }
            else
            {
                botonAceptar.Focus();
            }
        };
        ventana.ShowDialog();
        texto = caja?.Text.Trim() ?? "";
        return resultado;
    }
}
