using Coraza.App.ViewModels;
using Coraza.Remoto;
using Coraza.Remoto.Red;
using Coraza.Remoto.Sesiones;
using Serilog;

namespace Coraza.App.Servicios;

/// <summary>
/// Enciende y apaga el control remoto, y es lo que la interfaz consulta para saber
/// qué mostrar (la dirección, el PIN, quién está conectado).
///
/// Todo aquí es aditivo: si el servidor no arranca —el puerto ocupado, el firewall,
/// una tarjeta de red rara— <b>la proyección desde el teclado sigue funcionando
/// exactamente igual</b>. Por eso ningún fallo se propaga: se registra, se avisa en
/// la barra de la aplicación y se sigue.
/// </summary>
public sealed class ServicioControlRemoto
{
    private readonly MainViewModel _vm;
    private AdaptadorControlRemoto? _adaptador;
    private ServidorRemoto? _servidor;

    public ServicioControlRemoto(MainViewModel vm) => _vm = vm;

    public bool Activo => _servidor?.Activo == true;

    public int Puerto => _servidor?.Puerto ?? 0;

    /// <summary>PIN que hay que teclear en el teléfono. Vacío si está apagado.</summary>
    public string Pin => _servidor?.Sesiones.Pin ?? "";

    /// <summary>Quién controla ahora mismo, o <c>null</c> si no hay nadie emparejado.</summary>
    public SesionActiva? Sesion => _servidor?.Sesiones.Actual;

    /// <summary>Direcciones por las que el teléfono podría entrar, ya filtradas.</summary>
    public IReadOnlyList<InterfazLocal> Direcciones => DireccionesLocales.Detectar();

    /// <summary>La que va en el código QR: la elegida si sigue disponible, o la primera.</summary>
    public string? Url
    {
        get
        {
            if (!Activo) return null;
            var candidatas = Direcciones;
            if (candidatas.Count == 0) return null;
            var elegida = _vm.Ctx.Preferencias.ControlRemoto.DireccionElegida;
            var direccion = candidatas.FirstOrDefault(d => d.Direccion == elegida) ?? candidatas[0];
            return $"http://{direccion.Direccion}:{Puerto}/";
        }
    }

    /// <summary>Arranca si la preferencia lo pide. Se llama al terminar de preparar la app.</summary>
    public void IniciarSiProcede()
    {
        if (_vm.Ctx.Preferencias.ControlRemoto.Activado) Iniciar();
    }

    public bool Iniciar()
    {
        if (Activo) return true;
        try
        {
            _adaptador = new AdaptadorControlRemoto(_vm);
            _servidor = new ServidorRemoto(_adaptador, new GestorSesiones(),
                (mensaje, error) =>
                {
                    if (error is null) Log.Information("{Mensaje}", mensaje);
                    else Log.Error(error, "{Mensaje}", mensaje);
                });

            _servidor.Sesiones.SesionCambio += AlCambiarLaSesion;
            var puerto = _servidor.Iniciar(_vm.Ctx.Preferencias.ControlRemoto.Puerto);

            // Si hubo que usar otro puerto porque el preferido estaba ocupado, se
            // recuerda: así el QR y la dirección escrita siguen coincidiendo mañana.
            if (puerto != _vm.Ctx.Preferencias.ControlRemoto.Puerto)
            {
                _vm.Ctx.Preferencias.ControlRemoto.Puerto = puerto;
                _vm.Ctx.GuardarPreferencias();
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Error(e, "No se pudo iniciar el control remoto");
            _vm.MostrarMensaje("No se pudo iniciar el control remoto. La proyección no se ve afectada.", esError: true);
            LimpiarTrasFallo();
            return false;
        }
    }

    public async Task DetenerAsync()
    {
        var servidor = _servidor;
        var adaptador = _adaptador;
        _servidor = null;
        _adaptador = null;

        if (servidor is not null)
        {
            servidor.Sesiones.SesionCambio -= AlCambiarLaSesion;
            // ConfigureAwait(false): esto se llama al cerrar la ventana y no debe
            // volver al hilo de interfaz para terminar.
            try { await servidor.DisposeAsync().ConfigureAwait(false); }
            catch (Exception e) { Log.Warning(e, "Fallo al detener el control remoto"); }
        }
        adaptador?.Dispose();
    }

    /// <summary>Corta la sesión del teléfono y cambia el PIN, sin apagar el servidor.</summary>
    public void Desconectar() => _servidor?.Sesiones.Revocar();

    private void AlCambiarLaSesion(object? remitente, EventArgs e)
    {
        var sesion = Sesion;
        // Se avisa solo al emparejar y al desconectar. Un aviso por cada flecha del
        // teléfono sería ruido insoportable durante el culto.
        _vm.MostrarMensaje(sesion is null
            ? "Control remoto: el teléfono se desconectó."
            : $"Control remoto: un teléfono tomó el control ({sesion.Ip}).");
    }

    private void LimpiarTrasFallo()
    {
        _adaptador?.Dispose();
        _adaptador = null;
        _servidor = null;
    }
}
