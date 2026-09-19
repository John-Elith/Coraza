using System.Windows;
using System.Windows.Threading;
using Coraza.App.Infraestructura;
using Coraza.App.ViewModels;
using Coraza.Remoto.Contratos;

namespace Coraza.App.Servicios;

/// <summary>
/// El puente entre el servidor del control remoto y la aplicación.
///
/// Es el ÚNICO sitio donde se cruzan los dos mundos, y por eso tiene una regla que no
/// se rompe: <b>todos los métodos empiezan saltando al Dispatcher</b>. El servidor
/// corre en hilos del grupo y la capa de interfaz de Coraza no tiene ninguna
/// protección frente a llamadas de otro hilo (no hay locks, ni
/// EnableCollectionSynchronization, y los ObservableCollection se mutan a pelo).
///
/// El estado se construye ENTERO dentro de la lambda, copiando las colecciones a
/// arreglos ahí mismo, para que el hilo del servidor nunca toque un objeto vivo.
///
/// Segunda regla: un comando remoto nunca puede abrir un diálogo modal. Un modal
/// levantado desde una petición HTTP colgaría el hilo que la atiende y dejaría una
/// ventana en una pantalla que nadie está mirando.
/// </summary>
public sealed class AdaptadorControlRemoto : IControlRemoto, IDisposable
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _agrupador;
    private readonly object _candado = new();

    private bool _sucio;
    private IReadOnlyList<ResultadoBusqueda> _ultimaBusqueda = Array.Empty<ResultadoBusqueda>();

    public AdaptadorControlRemoto(MainViewModel vm)
    {
        _vm = vm;

        // Poner una diapositiva en vivo dispara media docena de notificaciones. Sin
        // agrupar, cada flecha del teléfono generaría seis tramas por el flujo.
        _agrupador = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _agrupador.Tick += (_, _) =>
        {
            if (!_sucio) return;
            _sucio = false;
            EstadoCambio?.Invoke(this, EventArgs.Empty);
        };
        _agrupador.Start();

        _vm.PropertyChanged += Ensuciar;
        _vm.Proyeccion.PropertyChanged += Ensuciar;
        _vm.Elementos.CollectionChanged += Ensuciar;
        _vm.SeccionesVivo.CollectionChanged += Ensuciar;
    }

    public event EventHandler? EstadoCambio;

    public Task<EstadoRemoto> LeerEstadoAsync() => EnInterfazAsync(Retratar);

    public Task<ResultadoComando> EjecutarAsync(ComandoRemoto comando) => EnInterfazAsync(() => Aplicar(comando));

    public Task<IReadOnlyList<ResultadoRemoto>> BuscarAsync(string consulta) => EnInterfazAsync(() =>
    {
        // Se reutiliza el buscador universal (Ctrl+K), que ya unifica canciones,
        // citas bíblicas, textos, medios y servicios en una sola consulta.
        _vm.Busqueda.Consulta = consulta ?? "";

        // Se excluye el grupo «Comando»: varios de ellos abren diálogos modales.
        var resultados = _vm.Busqueda.Resultados.Where(r => r.Grupo != "Comando").ToArray();
        lock (_candado) _ultimaBusqueda = resultados;

        IReadOnlyList<ResultadoRemoto> salida = resultados
            .Select((r, i) => new ResultadoRemoto(i, r.Grupo, r.Titulo, r.Detalle))
            .ToArray();
        return salida;
    });

    public Task<IReadOnlyList<ResultadoRemoto>> ListarAsync(string categoria) => EnInterfazAsync(() =>
    {
        // Se construyen resultados con la misma forma que los del buscador, incluida su
        // acción: así elegirlos y proyectarlos reutiliza el camino que ya existe.
        var resultados = categoria switch
        {
            "canciones" => _vm.Canciones.Canciones.Select(x => new ResultadoBusqueda(
                "Canción", x.Titulo, x.PrimeraLinea, "Ico.Musica",
                () => _vm.MostrarElemento(FabricaElementos.DeCancion(x)))).ToArray(),

            "medios" => _vm.Medios.Medios.Select(x => new ResultadoBusqueda(
                "Medio", x.Nombre, x.Detalle, "Ico.Imagen",
                () => _vm.MostrarElemento(FabricaElementos.DeMedio(x.Modelo)))).ToArray(),

            "textos" => _vm.Textos.Textos.Select(x => new ResultadoBusqueda(
                "Texto", x.Titulo, Recortar(x.Contenido), "Ico.Texto",
                () => _vm.MostrarElemento(FabricaElementos.DeTexto(x)))).ToArray(),

            _ => Array.Empty<ResultadoBusqueda>(),
        };

        lock (_candado) _ultimaBusqueda = resultados;

        IReadOnlyList<ResultadoRemoto> salida = resultados
            .Select((r, i) => new ResultadoRemoto(i, r.Grupo, r.Titulo, r.Detalle))
            .ToArray();
        return salida;
    });

    private void Ensuciar(object? remitente, EventArgs e) => _sucio = true;

    private static Task<T> EnInterfazAsync<T>(Func<T> trabajo) =>
        Application.Current.Dispatcher.InvokeAsync(trabajo, DispatcherPriority.Background).Task;

    /// <summary>Copia inmutable del estado. Se ejecuta siempre en el hilo de interfaz.</summary>
    private EstadoRemoto Retratar()
    {
        var p = _vm.Proyeccion;
        var previa = _vm.DiapositivaPrevia;

        var servicio = _vm.Elementos.Select((e, i) => new ElementoRemoto(
            Indice: i,
            Titulo: e.Titulo,
            Tipo: e.TipoTexto,
            EsEncabezado: e.EsEncabezado,
            EnVivo: e.EnVivo,
            Color: e.Color,
            Notas: e.Notas)).ToArray();

        // Las diapositivas de la vista previa: es lo que permite elegir qué parte
        // proyectar. Se recorta el texto porque el retrato viaja entero en cada cambio.
        var listaPrevia = _vm.Diapositivas.Select(d => new DiapositivaRemota(
            Indice: d.Indice,
            Etiqueta: d.Etiqueta,
            Seccion: d.Diapositiva.CodigoSeccion,
            Texto: Recortar(d.TextoPlano),
            EsVivo: d.EsVivo)).ToArray();

        var versiones = _vm.VersionesBiblia.Select((v, i) => new VersionRemota(
            Indice: i,
            Nombre: v.Nombre,
            Abreviatura: v.Abreviatura,
            Actual: _vm.VersionElegida is not null && _vm.VersionElegida.Id == v.Id)).ToArray();

        return new EstadoRemoto(
            Proyectando: p.Proyectando,
            DescripcionSalida: p.DescripcionSalida ?? "",
            Negro: p.Negro,
            Logo: p.Logo,
            TextoOculto: p.TextoOculto,
            PatronPrueba: p.PatronPrueba,
            ElementoVivoTitulo: _vm.ElementoVivoTitulo,
            EtiquetaVivo: _vm.EtiquetaVivo,
            NumeroVivo: _vm.NumeroVivo,
            TotalVivo: _vm.TotalVivo,
            TextoVivo: _vm.TextoVivo,
            TextoSiguiente: _vm.SiguienteTexto,
            TituloPrevia: _vm.TituloActual,
            TextoPrevia: previa?.TextoPlano ?? "",
            NumeroPrevia: previa?.Numero ?? 0,
            TotalPrevia: _vm.Diapositivas.Count,
            Secciones: _vm.SeccionesVivo.ToArray(),
            Servicio: servicio,
            NombreServicio: _vm.NombreServicio,
            Previa: listaPrevia,
            Versiones: versiones,
            EsPasaje: _vm.EsPasaje,
            HayMarquesina: p.HayMarquesina,
            RelojVisible: p.RelojVisible,
            MusicaSonando: p.MusicaSonando,
            HayVideo: p.HayVideo,
            VideoPausado: p.VideoPausado,
            Reloj: _vm.Reloj,
            Mensaje: _vm.Mensaje,
            MensajeEsError: _vm.MensajeEsError,
            Version: 0);   // la numera el servidor al difundir
    }

    private ResultadoComando Aplicar(ComandoRemoto c)
    {
        if (_vm.Preparando) return ResultadoComando.Rechazado("Coraza todavía está preparándose.");

        switch (c.Accion)
        {
            // La mayoría entra por el mismo embudo que los atajos de teclado.
            case AccionRemota.Siguiente: return Atajo(AccionAtajo.Siguiente);
            case AccionRemota.Anterior: return Atajo(AccionAtajo.Anterior);
            case AccionRemota.SiguienteElemento: return Atajo(AccionAtajo.SiguienteElemento);
            case AccionRemota.ElementoAnterior: return Atajo(AccionAtajo.ElementoAnterior);
            case AccionRemota.EnviarAlVivo: return Atajo(AccionAtajo.EnviarAlVivo);
            // Negro y Logo son pantallas de pánico: si la proyección está apagada hay que
            // ENCENDERLA antes de activar la capa. Si no, la bandera se pone pero no hay
            // ninguna salida registrada a la que aplicársela (Detener() la quitó), y al
            // reanudar aparece la última diapositiva —la canción— en vez del logo, porque
            // Mostrar() reinicia negro, logo y patrón de prueba.
            // Es el mismo patrón que ya usa AlternarPatronPrueba en MainViewModel.
            case AccionRemota.Negro:
            case AccionRemota.Logo:
                var esNegro = c.Accion == AccionRemota.Negro;
                var seVaAEncender = esNegro ? !_vm.Proyeccion.Negro : !_vm.Proyeccion.Logo;
                if (seVaAEncender && !_vm.Proyeccion.Proyectando) _vm.Proyeccion.Iniciar();
                return Atajo(esNegro ? AccionAtajo.PantallaNegra : AccionAtajo.PantallaLogo);
            case AccionRemota.OcultarTexto: return Atajo(AccionAtajo.OcultarTexto);
            case AccionRemota.Limpiar: return Atajo(AccionAtajo.Limpiar);
            case AccionRemota.PatronPrueba: return Atajo(AccionAtajo.PatronPrueba);

            // Iniciar y detener van directos a las primitivas: AlternarProyeccion abre
            // un diálogo de confirmación, y eso no puede pasar desde una petición HTTP.
            case AccionRemota.IniciarProyeccion:
                _vm.Proyeccion.Iniciar();
                return ResultadoComando.Hecho;
            case AccionRemota.DetenerProyeccion:
                _vm.Proyeccion.Detener();
                return ResultadoComando.Hecho;

            case AccionRemota.IrASeccion:
                if (string.IsNullOrWhiteSpace(c.Texto)) return ResultadoComando.Rechazado("Falta la sección.");
                _vm.IrASeccion(c.Texto);
                return ResultadoComando.Hecho;

            case AccionRemota.MostrarElemento:
            case AccionRemota.EnviarElementoAlVivo:
                if (c.Indice is not { } i || i < 0 || i >= _vm.Elementos.Count)
                    return ResultadoComando.Rechazado("Ese elemento ya no está en el servicio.");
                _vm.MostrarElemento(_vm.Elementos[i].Modelo);
                if (c.Accion == AccionRemota.EnviarElementoAlVivo) _vm.EnviarAlVivoCommand.Execute(null);
                return ResultadoComando.Hecho;

            case AccionRemota.ElegirResultado:
            case AccionRemota.EnviarResultadoAlVivo:
                IReadOnlyList<ResultadoBusqueda> ultima;
                lock (_candado) ultima = _ultimaBusqueda;
                if (c.Indice is not { } j || j < 0 || j >= ultima.Count)
                    return ResultadoComando.Rechazado("Vuelve a buscar: ese resultado ya no está.");
                // Ejecutar() deja el contenido en vista previa; enviarlo es un paso aparte.
                ultima[j].Ejecutar();
                if (c.Accion == AccionRemota.EnviarResultadoAlVivo) _vm.EnviarAlVivoCommand.Execute(null);
                return ResultadoComando.Hecho;

            case AccionRemota.MostrarDiapositivaPrevia:
                if (c.Indice is not { } dp || dp < 0 || dp >= _vm.Diapositivas.Count)
                    return ResultadoComando.Rechazado("Esa diapositiva ya no está.");
                _vm.DiapositivaPrevia = _vm.Diapositivas[dp];
                return ResultadoComando.Hecho;

            case AccionRemota.EnviarDiapositivaAlVivo:
                if (c.Indice is not { } dv || dv < 0 || dv >= _vm.Diapositivas.Count)
                    return ResultadoComando.Rechazado("Esa diapositiva ya no está.");
                // Arranca la proyección solo si hacía falta; lo resuelve EnviarDiapositivaAlVivo.
                _vm.EnviarDiapositivaAlVivo(_vm.Diapositivas[dv]);
                return ResultadoComando.Hecho;

            case AccionRemota.CambiarVersionBiblia:
                if (!_vm.EsPasaje) return ResultadoComando.Rechazado("Lo que está preparado no es un pasaje bíblico.");
                if (c.Indice is not { } vb || vb < 0 || vb >= _vm.VersionesBiblia.Count)
                    return ResultadoComando.Rechazado("Esa versión ya no está instalada.");
                _vm.VersionElegida = _vm.VersionesBiblia[vb];
                return ResultadoComando.Hecho;

            case AccionRemota.ProyectarTexto:
                if (string.IsNullOrWhiteSpace(c.Texto)) return ResultadoComando.Rechazado("Escribe algo primero.");
                // Se guarda en la biblioteca, como cualquier texto del escritorio: un
                // anuncio improvisado suele repetirse, y así queda para reutilizarlo.
                var libre = new Coraza.Core.Modelos.TextoLibre
                {
                    Titulo = Recortar(c.Texto, 40),
                    Contenido = c.Texto,
                };
                _vm.Ctx.Textos.Guardar(libre);
                _vm.Textos.Recargar(libre.Id);
                _vm.MostrarElemento(FabricaElementos.DeTexto(libre));
                _vm.EnviarAlVivoCommand.Execute(null);
                return ResultadoComando.Hecho;

            case AccionRemota.AlternarReloj:
                _vm.AlternarRelojCommand.Execute(null);
                return ResultadoComando.Hecho;
            case AccionRemota.MusicaAlternar:
                _vm.MusicaAlternarCommand.Execute(null);
                return ResultadoComando.Hecho;
            case AccionRemota.VideoReproducirPausar:
                _vm.VideoReproducirPausarCommand.Execute(null);
                return ResultadoComando.Hecho;

            default:
                return ResultadoComando.Rechazado("Ese comando no existe.");
        }
    }

    private ResultadoComando Atajo(AccionAtajo accion) =>
        _vm.EjecutarAtajo(accion) ? ResultadoComando.Hecho : ResultadoComando.Rechazado("Ahora mismo no se puede.");

    /// <summary>
    /// Primera línea y poco más. En la lista de la vista previa solo hace falta
    /// reconocer la diapositiva, y el retrato completo viaja en CADA cambio de estado:
    /// mandar la letra entera de cada diapositiva engordaría el flujo sin aportar nada.
    /// </summary>
    private static string Recortar(string texto, int maximo = 70)
    {
        var plano = texto.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return plano.Length <= maximo ? plano : plano[..maximo].TrimEnd() + "…";
    }

    public void Dispose()
    {
        _agrupador.Stop();
        _vm.PropertyChanged -= Ensuciar;
        _vm.Proyeccion.PropertyChanged -= Ensuciar;
        _vm.Elementos.CollectionChanged -= Ensuciar;
        _vm.SeccionesVivo.CollectionChanged -= Ensuciar;
    }
}
