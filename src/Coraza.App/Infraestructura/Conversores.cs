using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Coraza.Rendering;

namespace Coraza.App.Infraestructura;

/// <summary>true → Visible. Con el parámetro «invertir» funciona al revés.</summary>
public sealed class BoolAVisibilidad : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var v = value is true;
        if (parameter as string == "invertir") v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ (parameter as string == "invertir");
}

/// <summary>Oculta el elemento si el valor es nulo, texto vacío, cero o una colección vacía.</summary>
public sealed class VacioAVisibilidad : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var vacio = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            int n => n == 0,
            ICollection c => c.Count == 0,
            _ => false,
        };
        if (parameter as string == "invertir") vacio = !vacio;
        return vacio ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class HexAPincel : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Colores.Pincel(value as string ?? parameter as string ?? "#B44446");

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible si el valor (como texto) coincide con el parámetro.</summary>
public sealed class IgualAVisibilidad : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter as string, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
