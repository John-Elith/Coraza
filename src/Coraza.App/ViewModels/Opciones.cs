using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Coraza.Core.Modelos;

namespace Coraza.App.ViewModels;

/// <summary>Opción genérica para listas desplegables (valor + nombre en español).</summary>
public sealed record Opcion<T>(T Valor, string Nombre)
{
    public override string ToString() => Nombre;
}

public sealed record OpcionTema(long? Id, string Nombre)
{
    public override string ToString() => Nombre;
}

public sealed record OpcionPantalla(string? Clave, string Descripcion)
{
    public override string ToString() => Descripcion;
}

public sealed record OpcionTransicion(TipoTransicion? Valor, string Nombre)
{
    public static IReadOnlyList<OpcionTransicion> Todas { get; } =
        new[] { new OpcionTransicion(null, "Según el tema") }
            .Concat(Listas.Transiciones.Select(t => new OpcionTransicion(t.Valor, t.Nombre)))
            .ToList();

    public override string ToString() => Nombre;
}

public static class Listas
{
    /// <summary>Catálogo de transiciones del informe (sección 6.2).</summary>
    public static IReadOnlyList<Opcion<TipoTransicion>> Transiciones { get; } = new[]
    {
        new Opcion<TipoTransicion>(TipoTransicion.Fundido, "Fundido cruzado"),
        new Opcion<TipoTransicion>(TipoTransicion.Desenfoque, "Desenfoque suave"),
        new Opcion<TipoTransicion>(TipoTransicion.Deslizar, "Deslizamiento con profundidad"),
        new Opcion<TipoTransicion>(TipoTransicion.LineaPorLinea, "Revelado línea por línea"),
        new Opcion<TipoTransicion>(TipoTransicion.PalabraPorPalabra, "Revelado palabra por palabra"),
        new Opcion<TipoTransicion>(TipoTransicion.MaquinaEscribir, "Máquina de escribir"),
        new Opcion<TipoTransicion>(TipoTransicion.BarridoCoraza, "Barrido «Coraza»"),
        new Opcion<TipoTransicion>(TipoTransicion.RayoLuz, "Rayo de luz"),
        new Opcion<TipoTransicion>(TipoTransicion.Persiana, "Persiana"),
        new Opcion<TipoTransicion>(TipoTransicion.Cortina, "Cortina"),
        new Opcion<TipoTransicion>(TipoTransicion.Giro, "Giro"),
        new Opcion<TipoTransicion>(TipoTransicion.Particulas, "Partículas"),
        new Opcion<TipoTransicion>(TipoTransicion.KenBurns, "Acercamiento lento (Ken Burns)"),
        new Opcion<TipoTransicion>(TipoTransicion.Zoom, "Acercamiento"),
        new Opcion<TipoTransicion>(TipoTransicion.Corte, "Corte (sin animación)"),
    };

    public static IReadOnlyList<Opcion<AnimacionFondo>> AnimacionesFondo { get; } = new[]
    {
        new Opcion<AnimacionFondo>(AnimacionFondo.Ninguna, "Sin movimiento"),
        new Opcion<AnimacionFondo>(AnimacionFondo.Aurora, "Aurora (degradado vivo)"),
        new Opcion<AnimacionFondo>(AnimacionFondo.Bokeh, "Luces bokeh"),
        new Opcion<AnimacionFondo>(AnimacionFondo.Ondas, "Ondas suaves"),
    };

    public static IReadOnlyList<Opcion<TipoFondo>> TiposFondo { get; } = new[]
    {
        new Opcion<TipoFondo>(TipoFondo.Color, "Color sólido"),
        new Opcion<TipoFondo>(TipoFondo.Degradado, "Degradado lineal"),
        new Opcion<TipoFondo>(TipoFondo.Radial, "Degradado radial"),
        new Opcion<TipoFondo>(TipoFondo.Imagen, "Imagen"),
        new Opcion<TipoFondo>(TipoFondo.Video, "Video en bucle"),
    };

    public static IReadOnlyList<Opcion<int>> Pesos { get; } = new[]
    {
        new Opcion<int>(300, "Fino"),
        new Opcion<int>(400, "Normal"),
        new Opcion<int>(600, "Seminegrita"),
        new Opcion<int>(700, "Negrita"),
        new Opcion<int>(900, "Extra negrita"),
    };

    public static IReadOnlyList<Opcion<AlineacionHorizontal>> AlineacionesH { get; } = new[]
    {
        new Opcion<AlineacionHorizontal>(AlineacionHorizontal.Izquierda, "Izquierda"),
        new Opcion<AlineacionHorizontal>(AlineacionHorizontal.Centro, "Centro"),
        new Opcion<AlineacionHorizontal>(AlineacionHorizontal.Derecha, "Derecha"),
        new Opcion<AlineacionHorizontal>(AlineacionHorizontal.Justificado, "Justificado"),
    };

    public static IReadOnlyList<Opcion<AlineacionVertical>> AlineacionesV { get; } = new[]
    {
        new Opcion<AlineacionVertical>(AlineacionVertical.Arriba, "Arriba"),
        new Opcion<AlineacionVertical>(AlineacionVertical.Centro, "Centro"),
        new Opcion<AlineacionVertical>(AlineacionVertical.Abajo, "Abajo"),
    };

    public static IReadOnlyList<Opcion<PosicionPie>> PosicionesPie { get; } = new[]
    {
        new Opcion<PosicionPie>(PosicionPie.AbajoIzquierda, "Abajo a la izquierda"),
        new Opcion<PosicionPie>(PosicionPie.AbajoCentro, "Abajo al centro"),
        new Opcion<PosicionPie>(PosicionPie.AbajoDerecha, "Abajo a la derecha"),
        new Opcion<PosicionPie>(PosicionPie.ArribaIzquierda, "Arriba a la izquierda"),
        new Opcion<PosicionPie>(PosicionPie.ArribaCentro, "Arriba al centro"),
        new Opcion<PosicionPie>(PosicionPie.ArribaDerecha, "Arriba a la derecha"),
    };

    public static IReadOnlyList<Opcion<ModoAjuste>> Ajustes { get; } = new[]
    {
        new Opcion<ModoAjuste>(ModoAjuste.Ajustar, "Ajustar (sin recortar)"),
        new Opcion<ModoAjuste>(ModoAjuste.Rellenar, "Rellenar (recorta bordes)"),
        new Opcion<ModoAjuste>(ModoAjuste.Estirar, "Estirar"),
        new Opcion<ModoAjuste>(ModoAjuste.Centrar, "Centrar (tamaño real)"),
    };

    public static IReadOnlyList<string> Proporciones { get; } = new[] { "Automática", "4:3", "16:9", "16:10", "21:9" };
}

/// <summary>Un elemento del orden del servicio tal como se muestra en la lista.</summary>
public sealed partial class ElementoViewModel : ObservableObject
{
    private readonly Action _alCambiar;

    [ObservableProperty] private bool _enVivo;

    public ElementoViewModel(ElementoServicio modelo, Action alCambiar)
    {
        Modelo = modelo;
        _alCambiar = alCambiar;
    }

    public ElementoServicio Modelo { get; }

    public TipoElemento Tipo => Modelo.Tipo;

    public bool EsEncabezado => Modelo.Tipo == TipoElemento.Encabezado;

    public string Titulo
    {
        get => Modelo.Titulo;
        set
        {
            if (Modelo.Titulo == value) return;
            Modelo.Titulo = value;
            OnPropertyChanged();
            _alCambiar();
        }
    }

    public string? Notas
    {
        get => Modelo.Notas;
        set
        {
            if (Modelo.Notas == value) return;
            Modelo.Notas = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TieneNotas));
            _alCambiar();
        }
    }

    public bool TieneNotas => !string.IsNullOrWhiteSpace(Modelo.Notas);

    public int? DuracionMin
    {
        get => Modelo.DuracionMin;
        set
        {
            if (Modelo.DuracionMin == value) return;
            Modelo.DuracionMin = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DuracionTexto));
            _alCambiar();
        }
    }

    public string DuracionTexto => Modelo.DuracionMin is > 0 ? $"{Modelo.DuracionMin} min" : "";

    public int? AvanceSegundos
    {
        get => Modelo.AvanceSegundos;
        set
        {
            if (Modelo.AvanceSegundos == value) return;
            Modelo.AvanceSegundos = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvanceTexto));
            _alCambiar();
        }
    }

    public bool Bucle
    {
        get => Modelo.Bucle;
        set
        {
            if (Modelo.Bucle == value) return;
            Modelo.Bucle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvanceTexto));
            _alCambiar();
        }
    }

    /// <summary>Indicador del avance automático: «8 s ⟳».</summary>
    public string AvanceTexto => ((Modelo.AvanceSegundos is > 0 ? $"{Modelo.AvanceSegundos} s" : "") + (Modelo.Bucle ? " ⟳" : "")).Trim();

    public string Color => Modelo.Color ?? "#B44446";

    public string TipoTexto => Tipo switch
    {
        TipoElemento.Cancion => "Canción",
        TipoElemento.Pasaje => "Biblia",
        TipoElemento.Imagen => "Medio",
        TipoElemento.Texto => "Texto",
        TipoElemento.CuentaRegresiva => "Cuenta regresiva",
        _ => "Sección",
    };

    public Geometry? Icono => Application.Current.TryFindResource(Tipo switch
    {
        TipoElemento.Cancion => "Ico.Musica",
        TipoElemento.Pasaje => "Ico.Biblia",
        TipoElemento.Imagen => "Ico.Imagen",
        TipoElemento.Texto => "Ico.Texto",
        TipoElemento.CuentaRegresiva => "Ico.Reloj",
        _ => "Ico.Marcador",
    }) as Geometry;

    public void NotificarTitulo() => OnPropertyChanged(nameof(Titulo));
}

/// <summary>Una diapositiva en la cuadrícula de miniaturas.</summary>
public sealed partial class DiapositivaViewModel : ObservableObject
{
    [ObservableProperty] private bool _esVivo;

    public DiapositivaViewModel(Diapositiva diapositiva, Tema tema, int indice)
    {
        Diapositiva = diapositiva;
        Tema = tema;
        Indice = indice;
    }

    public Diapositiva Diapositiva { get; }

    public Tema Tema { get; }

    public int Indice { get; }

    public int Numero => Indice + 1;

    public string Etiqueta => Diapositiva.Etiqueta ?? $"Diapositiva {Numero}";

    /// <summary>Color de la etiqueta según la sección: coro en coral, versos en lino, puente en rojo teja…</summary>
    public string ColorEtiqueta
    {
        get
        {
            var codigo = (Diapositiva.CodigoSeccion ?? "").ToUpperInvariant();
            if (codigo.StartsWith("PC")) return "#D9A441";
            if (codigo.StartsWith('C')) return "#FC8F8F";
            if (codigo.StartsWith('V')) return "#DFD9D8";
            if (codigo.StartsWith('P')) return "#E0686A";
            if (codigo.StartsWith('F')) return "#4E8F6A";
            return "#A99597";
        }
    }

    public string TextoPlano => Diapositiva.Tipo switch
    {
        TipoDiapositiva.Imagen => $"[Imagen] {Diapositiva.Etiqueta}",
        TipoDiapositiva.Video => $"[Video] {Diapositiva.Etiqueta}",
        TipoDiapositiva.CuentaRegresiva => $"[Cuenta regresiva] {Diapositiva.Texto}",
        _ => Diapositiva.ConFormato ? Coraza.Core.Diapositivas.FormatoTexto.QuitarMarcas(Diapositiva.Texto) : Diapositiva.Texto,
    };
}
