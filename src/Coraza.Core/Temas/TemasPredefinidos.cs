using Coraza.Core.Modelos;

namespace Coraza.Core.Temas;

/// <summary>Temas incluidos, construidos con la paleta Coraza (vino, rojo teja, coral y gris lino).</summary>
public static class TemasPredefinidos
{
    public const string VinoCoraza = "Vino Coraza";
    public const string LinoClasico = "Lino Clásico";
    public const string CoralAurora = "Coral Aurora";
    public const string NegroSencillo = "Negro sencillo";
    public const string AuroraCoraza = "Aurora Coraza";
    public const string LuzDeGracia = "Luz de gracia";

    public static IReadOnlyList<Tema> Crear() => new[]
    {
        // Canciones: degradado radial vino, letra blanca con sombra.
        new Tema
        {
            Nombre = VinoCoraza,
            Predefinido = true,
            Fondo = new FondoTema { Tipo = TipoFondo.Radial, Color1 = "#7A2C39", Color2 = "#1E0B0F" },
            Texto = new EstiloTexto { Fuente = "Segoe UI", Peso = 600, TamanoPct = 7.5, Color = "#FFFFFF" },
            Pie = new EstiloPie { Color = "#FC8F8F", Posicion = PosicionPie.AbajoCentro, TamanoPct = 2.6 },
            Transicion = TipoTransicion.Fundido,
            DuracionTransicionMs = 600,
        },
        // Biblia: fondo oscuro sobrio, tipografía serif, referencia en coral.
        new Tema
        {
            Nombre = LinoClasico,
            Predefinido = true,
            Fondo = new FondoTema { Tipo = TipoFondo.Degradado, Color1 = "#2A1216", Color2 = "#1E0B0F", Angulo = 90 },
            Texto = new EstiloTexto
            {
                Fuente = "Georgia", Peso = 400, TamanoPct = 6.2, Color = "#F4F1F0",
                Interlineado = 1.25, AlineacionH = AlineacionHorizontal.Izquierda,
            },
            Pie = new EstiloPie { Fuente = "Segoe UI", Color = "#FC8F8F", Posicion = PosicionPie.AbajoDerecha, TamanoPct = 3.4 },
            MargenPct = 7,
            CaracteresMaximos = 260,
            Transicion = TipoTransicion.Fundido,
            DuracionTransicionMs = 700,
        },
        // Anuncios: degradado rojo teja → vino, letra condensada en mayúsculas.
        new Tema
        {
            Nombre = CoralAurora,
            Predefinido = true,
            Fondo = new FondoTema { Tipo = TipoFondo.Degradado, Color1 = "#B44446", Color2 = "#64242F", Angulo = 135 },
            Texto = new EstiloTexto
            {
                Fuente = "Bahnschrift SemiBold Condensed", Peso = 600, TamanoPct = 9, Color = "#FFFFFF",
                Mayusculas = true, Interlineado = 1.05,
            },
            Sombra = new EstiloSombra { Opacidad = 0.45 },
            Pie = new EstiloPie { Color = "#DFD9D8", Posicion = PosicionPie.AbajoCentro },
            Transicion = TipoTransicion.Deslizar,
            DuracionTransicionMs = 650,
        },
        // Equipos lentos o transmisión: negro puro, sin sombra, cambio instantáneo.
        new Tema
        {
            Nombre = NegroSencillo,
            Predefinido = true,
            Fondo = new FondoTema { Tipo = TipoFondo.Color, Color1 = "#000000", Color2 = "#000000" },
            Texto = new EstiloTexto { Fuente = "Segoe UI", Peso = 600, TamanoPct = 7.5, Color = "#FFFFFF" },
            Sombra = new EstiloSombra { Activa = false },
            Pie = new EstiloPie { Color = "#DFD9D8", Posicion = PosicionPie.AbajoCentro, TamanoPct = 2.6 },
            Transicion = TipoTransicion.Corte,
            DuracionTransicionMs = 0,
        },
        // Adoración: aurora vino que se mueve lentamente, entrada con desenfoque.
        new Tema
        {
            Nombre = AuroraCoraza,
            Predefinido = true,
            Fondo = new FondoTema
            {
                Tipo = TipoFondo.Radial, Color1 = "#5A1F2A", Color2 = "#120508",
                Animacion = AnimacionFondo.Aurora, Velocidad = 1, Intensidad = 0.7,
            },
            Texto = new EstiloTexto { Fuente = "Segoe UI", Peso = 600, TamanoPct = 7.5, Color = "#FFFFFF" },
            Pie = new EstiloPie { Color = "#FC8F8F", Posicion = PosicionPie.AbajoCentro, TamanoPct = 2.6 },
            Transicion = TipoTransicion.Desenfoque,
            DuracionTransicionMs = 800,
        },
        // Momentos íntimos: luces bokeh que flotan, texto que aparece línea por línea.
        new Tema
        {
            Nombre = LuzDeGracia,
            Predefinido = true,
            Fondo = new FondoTema
            {
                Tipo = TipoFondo.Degradado, Color1 = "#2A1216", Color2 = "#0C0405", Angulo = 90,
                Animacion = AnimacionFondo.Bokeh, Velocidad = 0.8, Intensidad = 0.6,
            },
            Texto = new EstiloTexto { Fuente = "Georgia", Peso = 400, TamanoPct = 7, Color = "#F4F1F0", Interlineado = 1.2 },
            Pie = new EstiloPie { Color = "#FC8F8F", Posicion = PosicionPie.AbajoCentro, TamanoPct = 2.6 },
            Transicion = TipoTransicion.LineaPorLinea,
            DuracionTransicionMs = 700,
        },
    };

    /// <summary>Tema que se asigna por defecto a cada tipo de contenido.</summary>
    public static string NombrePara(TipoContenido tipo) => tipo switch
    {
        TipoContenido.Biblia => LinoClasico,
        TipoContenido.Textos => CoralAurora,
        TipoContenido.Imagenes => NegroSencillo,
        _ => VinoCoraza,
    };
}
