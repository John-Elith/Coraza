namespace Coraza.Remoto.Contratos;

/// <summary>
/// El puerto entre el servidor y la aplicación de escritorio.
///
/// Coraza.Remoto no sabe que WPF existe: solo conoce esta interfaz. La implementa
/// <c>AdaptadorControlRemoto</c>, en Coraza.App, y <b>todos</b> sus métodos empiezan
/// saltando al Dispatcher, porque el servidor corre en hilos del grupo y la capa
/// de interfaz no tiene ninguna protección frente a llamadas de otro hilo.
///
/// Esa separación es también lo que permite probar el servidor entero con xUnit
/// contra una implementación falsa, sin levantar una ventana.
/// </summary>
public interface IControlRemoto
{
    /// <summary>Retrato del estado actual, ya copiado y seguro de leer desde cualquier hilo.</summary>
    Task<EstadoRemoto> LeerEstadoAsync();

    /// <summary>Ejecuta una orden del teléfono.</summary>
    Task<ResultadoComando> EjecutarAsync(ComandoRemoto comando);

    /// <summary>
    /// Busca en la biblioteca reutilizando el buscador universal del escritorio
    /// (canciones, citas bíblicas, textos, medios y servicios). Los resultados se
    /// recuerdan hasta la siguiente búsqueda: el teléfono los elige por índice.
    /// </summary>
    Task<IReadOnlyList<ResultadoRemoto>> BuscarAsync(string consulta);

    /// <summary>
    /// Lista una categoría entera de la biblioteca («canciones», «medios», «textos»)
    /// sin escribir nada. Buscar por nombre no sirve para hojear: si el operador no
    /// recuerda cómo se llama el video, no hay forma de llegar a él.
    ///
    /// Devuelve lo mismo que <see cref="BuscarAsync"/> y se elige igual, por índice.
    /// </summary>
    Task<IReadOnlyList<ResultadoRemoto>> ListarAsync(string categoria);

    /// <summary>
    /// Avisa de que algo cambió y hay que difundir un estado nuevo. Llega ya agrupado
    /// (~100 ms): poner una diapositiva en vivo dispara media docena de notificaciones
    /// y sin agrupar cada flecha generaría seis tramas.
    /// </summary>
    event EventHandler? EstadoCambio;
}
