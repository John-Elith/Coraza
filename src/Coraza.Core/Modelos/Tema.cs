using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coraza.Core.Modelos;

public enum TipoFondo { Color, Degradado, Radial, Imagen, Video }
public enum AlineacionHorizontal { Izquierda, Centro, Derecha, Justificado }
public enum AlineacionVertical { Arriba, Centro, Abajo }
public enum PosicionPie { AbajoIzquierda, AbajoCentro, AbajoDerecha, ArribaIzquierda, ArribaCentro, ArribaDerecha }
public enum TipoTransicion
{
    Corte, Fundido, Desenfoque, Deslizar, Zoom,
    LineaPorLinea, PalabraPorPalabra, MaquinaEscribir, BarridoCoraza, RayoLuz, Persiana, Cortina, Giro, KenBurns, Particulas,
}

/// <summary>Movimiento del fondo; continúa sin reiniciarse al cambiar de diapositiva.</summary>
public enum AnimacionFondo { Ninguna, Aurora, Bokeh, Ondas }
public enum TipoContenido { Canciones, Biblia, Textos, Imagenes }

public sealed class FondoTema
{
    public TipoFondo Tipo { get; set; } = TipoFondo.Radial;
    public string Color1 { get; set; } = "#64242F";
    public string Color2 { get; set; } = "#1E0B0F";
    /// <summary>Ángulo del degradado lineal en grados.</summary>
    public double Angulo { get; set; } = 90;
    public string? RutaImagen { get; set; }
    /// <summary>Capa negra encima del fondo para mejorar la lectura (0 a 1).</summary>
    public double Oscurecer { get; set; }
    /// <summary>Desenfoque de la imagen de fondo en porcentaje de la altura (0 a 5).</summary>
    public double Desenfoque { get; set; }
    public AnimacionFondo Animacion { get; set; }
    /// <summary>Velocidad del fondo animado (0,25 lento a 3 rápido).</summary>
    public double Velocidad { get; set; } = 1;
    /// <summary>Intensidad del fondo animado (0 a 1): cantidad y brillo de los elementos.</summary>
    public double Intensidad { get; set; } = 0.6;
}

public sealed class EstiloTexto
{
    public string Fuente { get; set; } = "Segoe UI";
    /// <summary>Tamaño de la letra como porcentaje de la altura de la pantalla.</summary>
    public double TamanoPct { get; set; } = 7.5;
    /// <summary>Tamaño mínimo al que puede reducirse con el ajuste automático.</summary>
    public double TamanoMinimoPct { get; set; } = 3.5;
    public bool AjusteAutomatico { get; set; } = true;
    public int Peso { get; set; } = 600;
    public bool Cursiva { get; set; }
    public bool Mayusculas { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public double Interlineado { get; set; } = 1.15;
    public AlineacionHorizontal AlineacionH { get; set; } = AlineacionHorizontal.Centro;
    public AlineacionVertical AlineacionV { get; set; } = AlineacionVertical.Centro;
}

public sealed class EstiloSombra
{
    public bool Activa { get; set; } = true;
    public string Color { get; set; } = "#000000";
    public double Opacidad { get; set; } = 0.75;
    /// <summary>Desenfoque y distancia en porcentaje de la altura.</summary>
    public double Desenfoque { get; set; } = 0.9;
    public double Distancia { get; set; } = 0.25;
}

public sealed class EstiloCaja
{
    public bool Activa { get; set; }
    public string Color { get; set; } = "#1E0B0F";
    public double Opacidad { get; set; } = 0.55;
    public double RadioPct { get; set; } = 1.2;
}

public sealed class EstiloPie
{
    public bool Mostrar { get; set; } = true;
    public string Fuente { get; set; } = "Segoe UI";
    public double TamanoPct { get; set; } = 3.2;
    public string Color { get; set; } = "#FC8F8F";
    public PosicionPie Posicion { get; set; } = PosicionPie.AbajoDerecha;
}

/// <summary>Un tema agrupa fondo, tipografía, colores, sombra, posición del texto y transición.</summary>
public sealed class Tema
{
    public long Id { get; set; }
    public string Nombre { get; set; } = "Nuevo tema";
    public bool Predefinido { get; set; }

    public FondoTema Fondo { get; set; } = new();
    public EstiloTexto Texto { get; set; } = new();
    public EstiloSombra Sombra { get; set; } = new();
    public EstiloCaja Caja { get; set; } = new();
    public EstiloPie Pie { get; set; } = new();

    /// <summary>Margen de seguridad en porcentaje, para proyectores que recortan los bordes.</summary>
    public double MargenPct { get; set; } = 5;
    public int LineasMaximas { get; set; } = 4;
    public int CaracteresMaximos { get; set; } = 240;
    public int VersiculosPorDiapositiva { get; set; } = 1;
    public bool NumerosVersiculo { get; set; } = true;

    public TipoTransicion Transicion { get; set; } = TipoTransicion.Fundido;
    public int DuracionTransicionMs { get; set; } = 600;

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public string AJson() => JsonSerializer.Serialize(this, OpcionesJson);

    public static Tema DesdeJson(string json) =>
        JsonSerializer.Deserialize<Tema>(json, OpcionesJson) ?? new Tema();

    public Tema Clonar()
    {
        var copia = DesdeJson(AJson());
        copia.Id = Id;
        return copia;
    }

    /// <summary>Clave que identifica el fondo: si no cambia, el fondo no se reinicia entre diapositivas.</summary>
    public string ClaveFondo => JsonSerializer.Serialize(Fondo, OpcionesJson);

    /// <summary>Regla práctica: la letra debería medir al menos 1/20 de la altura de la pantalla.</summary>
    public bool LetraPequenaParaDistancia => Texto.TamanoPct < 5;
}
