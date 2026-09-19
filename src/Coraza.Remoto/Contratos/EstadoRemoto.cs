namespace Coraza.Remoto.Contratos;

/// <summary>
/// Retrato completo de lo que el teléfono necesita mostrar, tomado en un instante.
/// Es inmutable y se construye siempre dentro del hilo de interfaz: el servidor
/// nunca toca un objeto de WPF, solo copias ya congeladas de su estado.
/// </summary>
/// <param name="Version">
/// Contador monótono. El teléfono descarta cualquier trama con una versión menor
/// que la última que pintó, así una trama retrasada no puede retroceder la vista.
/// </param>
public sealed record EstadoRemoto(
    // Proyección
    bool Proyectando,
    string DescripcionSalida,
    bool Negro,
    bool Logo,
    bool TextoOculto,
    bool PatronPrueba,

    // Lo que ve la congregación ahora mismo
    string ElementoVivoTitulo,
    string EtiquetaVivo,
    int NumeroVivo,
    int TotalVivo,
    string TextoVivo,
    string TextoSiguiente,

    // Lo que está preparado para enviar
    string TituloPrevia,
    string TextoPrevia,
    int NumeroPrevia,
    int TotalPrevia,

    // Navegación
    IReadOnlyList<string> Secciones,
    IReadOnlyList<ElementoRemoto> Servicio,
    string NombreServicio,

    /// <summary>Diapositivas de lo que está en vista previa, para elegir qué parte proyectar.</summary>
    IReadOnlyList<DiapositivaRemota> Previa,

    /// <summary>Versiones bíblicas instaladas. Solo tienen sentido si <see cref="EsPasaje"/>.</summary>
    IReadOnlyList<VersionRemota> Versiones,
    bool EsPasaje,

    // Superposiciones y medios
    bool HayMarquesina,
    bool RelojVisible,
    bool MusicaSonando,
    bool HayVideo,
    bool VideoPausado,

    // Eco de la interfaz del operador
    string Reloj,
    string? Mensaje,
    bool MensajeEsError,

    long Version)
{
    /// <summary>Estado de arranque, antes de que el escritorio haya publicado nada.</summary>
    public static EstadoRemoto Vacio { get; } = new(
        Proyectando: false, DescripcionSalida: "", Negro: false, Logo: false,
        TextoOculto: false, PatronPrueba: false,
        ElementoVivoTitulo: "", EtiquetaVivo: "", NumeroVivo: 0, TotalVivo: 0,
        TextoVivo: "", TextoSiguiente: "",
        TituloPrevia: "", TextoPrevia: "", NumeroPrevia: 0, TotalPrevia: 0,
        Secciones: Array.Empty<string>(), Servicio: Array.Empty<ElementoRemoto>(),
        NombreServicio: "",
        Previa: Array.Empty<DiapositivaRemota>(), Versiones: Array.Empty<VersionRemota>(),
        EsPasaje: false,
        HayMarquesina: false, RelojVisible: false, MusicaSonando: false,
        HayVideo: false, VideoPausado: false,
        Reloj: "", Mensaje: null, MensajeEsError: false,
        Version: 0);

    /// <summary>Hay algo proyectándose de verdad (no solo la salida abierta).</summary>
    public bool HayVivo => TotalVivo > 0;
}

/// <summary>Una fila del orden del servicio, tal como la pinta el teléfono.</summary>
/// <param name="Indice">Posición en la lista del escritorio; es lo que viaja de vuelta en los comandos.</param>
public sealed record ElementoRemoto(
    int Indice,
    string Titulo,
    string Tipo,
    bool EsEncabezado,
    bool EnVivo,
    string? Color,
    string? Notas);

/// <summary>
/// Una diapositiva de lo que está en vista previa. Con esto el teléfono puede elegir
/// qué parte proyectar —el coro, el final, la estrofa que pida el director— en vez de
/// tener que avanzar una por una desde el principio.
/// </summary>
/// <param name="Seccion">Código de sección (V1, C, P, F…) cuando la diapositiva pertenece a una canción.</param>
public sealed record DiapositivaRemota(
    int Indice,
    string Etiqueta,
    string? Seccion,
    string Texto,
    bool EsVivo);

/// <summary>Una versión bíblica instalada, para cambiar de traducción desde el teléfono.</summary>
public sealed record VersionRemota(
    int Indice,
    string Nombre,
    string Abreviatura,
    bool Actual);

/// <summary>
/// Un resultado de la búsqueda universal. El teléfono no sabe qué hay detrás:
/// devuelve el <paramref name="Indice"/> y el escritorio ejecuta la acción que ya
/// traía ese resultado.
/// </summary>
public sealed record ResultadoRemoto(
    int Indice,
    string Grupo,
    string Titulo,
    string? Detalle);
