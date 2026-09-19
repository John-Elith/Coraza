using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Coraza.Rendering;

/// <summary>
/// Texto que se reduce automáticamente (auto-fit) para caber en el espacio disponible,
/// entre un tamaño máximo y uno mínimo. Los números de versículo se escalan con el texto.
/// </summary>
public sealed class TextoAjustable : FrameworkElement
{
    private const double ProporcionSuperindice = 0.55;

    private readonly TextBlock _bloque = new()
    {
        TextWrapping = TextWrapping.Wrap,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    private double _tamanoMaximo = 48;
    private double _tamanoMinimo = 16;
    private double _interlineado = 1.15;
    private bool _ajustar = true;

    public TextoAjustable()
    {
        AddVisualChild(_bloque);
        AddLogicalChild(_bloque);
    }

    public TextBlock Bloque => _bloque;

    /// <summary>Tamaño final aplicado tras el ajuste automático.</summary>
    public double TamanoEfectivo { get; private set; }

    public double TamanoMaximo
    {
        get => _tamanoMaximo;
        set { if (Math.Abs(_tamanoMaximo - value) > 0.01) { _tamanoMaximo = Math.Max(1, value); InvalidateMeasure(); } }
    }

    public double TamanoMinimo
    {
        get => _tamanoMinimo;
        set { if (Math.Abs(_tamanoMinimo - value) > 0.01) { _tamanoMinimo = Math.Max(1, value); InvalidateMeasure(); } }
    }

    public double Interlineado
    {
        get => _interlineado;
        set { _interlineado = Math.Clamp(value, 0.7, 3); InvalidateMeasure(); }
    }

    public bool Ajustar
    {
        get => _ajustar;
        set { _ajustar = value; InvalidateMeasure(); }
    }

    /// <summary>Marca un Run como número de versículo para que se dibuje en superíndice proporcional.</summary>
    public static readonly object MarcaSuperindice = new();

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _bloque;

    protected override System.Collections.IEnumerator LogicalChildren
    {
        get { yield return _bloque; }
    }

    protected override Size MeasureOverride(Size disponible)
    {
        var ancho = double.IsInfinity(disponible.Width) ? 100000 : Math.Max(1, disponible.Width);
        var alto = disponible.Height;
        var tamano = _tamanoMaximo;

        if (_ajustar && !double.IsInfinity(alto) && !Cabe(tamano, ancho, alto))
        {
            var bajo = Math.Min(_tamanoMinimo, _tamanoMaximo);
            var alto2 = _tamanoMaximo;
            for (var i = 0; i < 10; i++)
            {
                var medio = (bajo + alto2) / 2;
                if (Cabe(medio, ancho, alto)) bajo = medio; else alto2 = medio;
            }
            tamano = bajo;
        }

        Aplicar(tamano);
        _bloque.Measure(new Size(ancho, double.PositiveInfinity));
        TamanoEfectivo = tamano;
        var deseado = _bloque.DesiredSize;
        return new Size(Math.Min(deseado.Width, ancho), double.IsInfinity(alto) ? deseado.Height : Math.Min(deseado.Height, alto));
    }

    protected override Size ArrangeOverride(Size final)
    {
        _bloque.Arrange(new Rect(final));
        return final;
    }

    private bool Cabe(double tamano, double ancho, double alto)
    {
        Aplicar(tamano);
        _bloque.Measure(new Size(ancho, double.PositiveInfinity));
        return _bloque.DesiredSize.Height <= alto + 0.5 && _bloque.DesiredSize.Width <= ancho + 0.5;
    }

    private void Aplicar(double tamano)
    {
        _bloque.FontSize = tamano;
        _bloque.LineHeight = tamano * _interlineado;
        foreach (var inline in _bloque.Inlines)
        {
            if (inline is Run run && ReferenceEquals(run.Tag, MarcaSuperindice)) run.FontSize = tamano * ProporcionSuperindice;
        }
    }
}
