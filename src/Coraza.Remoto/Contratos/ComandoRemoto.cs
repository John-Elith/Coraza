namespace Coraza.Remoto.Contratos;

/// <summary>
/// Lo que el teléfono puede pedir. Cada valor se traduce en el escritorio a una
/// llamada que ya existe (casi todas a <c>MainViewModel.EjecutarAtajo</c>).
///
/// Regla que no se rompe: <b>ningún comando remoto puede abrir un diálogo modal.</b>
/// Un modal levantado por una petición HTTP colgaría el hilo que la atiende y
/// dejaría una ventana en una pantalla que nadie está mirando. Por eso quedan
/// fuera la marquesina, el mensaje urgente, la configuración y todo lo destructivo.
/// </summary>
public enum AccionRemota
{
    // Navegación
    Siguiente,
    Anterior,
    SiguienteElemento,
    ElementoAnterior,
    EnviarAlVivo,

    // Pánico
    Negro,
    Logo,
    OcultarTexto,
    Limpiar,
    PatronPrueba,

    /// <summary>Iniciar y detener son comandos separados a propósito, no un conmutador:
    /// así el teléfono es idempotente y no hay que leer el estado para saber qué pasará.
    /// Además evitan el diálogo de confirmación de <c>AlternarProyeccion</c>.</summary>
    IniciarProyeccion,
    DetenerProyeccion,

    /// <summary>Salta a una sección de la canción. <c>Texto</c> lleva el código: V1, C, P, PC, F…</summary>
    IrASeccion,

    /// <summary>Deja un elemento del servicio en vista previa. <c>Indice</c> es su posición.</summary>
    MostrarElemento,

    /// <summary>Deja un elemento en vista previa y además lo envía al vivo.</summary>
    EnviarElementoAlVivo,

    /// <summary>Elige un resultado de la última búsqueda y lo deja en vista previa.</summary>
    ElegirResultado,

    /// <summary>
    /// Elige un resultado y lo proyecta de una vez. Es el camino de un solo toque para
    /// versículos, anuncios, imágenes, videos y PDF: sin esto el contenido se quedaba
    /// preparado en la vista previa y nunca llegaba a la pantalla.
    /// </summary>
    EnviarResultadoAlVivo,

    /// <summary>Mueve la vista previa a una diapositiva concreta. <c>Indice</c> es su posición.</summary>
    MostrarDiapositivaPrevia,

    /// <summary>
    /// Proyecta una diapositiva concreta de lo que está en vista previa: el coro, el
    /// final, la estrofa que pida el director.
    /// </summary>
    EnviarDiapositivaAlVivo,

    /// <summary>Cambia la versión bíblica del pasaje actual. <c>Indice</c> es su posición en la lista.</summary>
    CambiarVersionBiblia,

    /// <summary>
    /// Escribe un anuncio en el teléfono y lo proyecta. <c>Texto</c> lleva el contenido.
    /// Queda guardado en la biblioteca de textos, igual que si se hubiera creado en el
    /// escritorio: así se puede reutilizar el domingo siguiente.
    /// </summary>
    ProyectarTexto,

    // Superposiciones y medios
    AlternarReloj,
    MusicaAlternar,
    VideoReproducirPausar,
}

/// <summary>Una orden del teléfono. <c>Texto</c> e <c>Indice</c> solo los usan algunas acciones.</summary>
public sealed record ComandoRemoto(AccionRemota Accion, string? Texto = null, int? Indice = null);

/// <summary>
/// Resultado de ejecutar una orden. Un <c>false</c> aquí no es una excepción: es el
/// escritorio diciendo «ahora no puedo», y el teléfono lo muestra sin alarmarse.
/// </summary>
public sealed record ResultadoComando(bool Ok, string? Mensaje = null)
{
    public static ResultadoComando Hecho { get; } = new(true);

    public static ResultadoComando Rechazado(string motivo) => new(false, motivo);
}
