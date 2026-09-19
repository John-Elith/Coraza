using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coraza.App.Infraestructura;
using Coraza.App.Servicios;
using Coraza.Remoto.Red;
using Coraza.Rendering;

namespace Coraza.App.ViewModels;

/// <summary>
/// La ventana «Control remoto»: enciende el servidor, muestra el QR y el PIN, dice
/// quién está conectado y ofrece arreglar el firewall.
///
/// Se abre con <c>Show()</c>, no <c>ShowDialog()</c>: si fuera modal, el operador
/// quedaría bloqueado del proyector justo mientras empareja el teléfono.
/// </summary>
public sealed partial class ControlRemotoViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private bool _activo;
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _pin = "";
    [ObservableProperty] private BitmapSource? _qr;
    [ObservableProperty] private string _conectado = "Nadie conectado todavía.";
    [ObservableProperty] private string _avisoFirewall = "";
    [ObservableProperty] private bool _mostrarBotonFirewall;
    [ObservableProperty] private InterfazLocal? _direccionElegida;
    [ObservableProperty] private bool _permitirRedesPublicas;

    public ControlRemotoViewModel(MainViewModel main)
    {
        _main = main;
        _permitirRedesPublicas = main.Ctx.Preferencias.ControlRemoto.PermitirRedesPublicas;
        Direcciones = _main.Remoto.Direcciones;

        var guardada = main.Ctx.Preferencias.ControlRemoto.DireccionElegida;
        _direccionElegida = Direcciones.FirstOrDefault(d => d.Direccion == guardada) ?? Direcciones.FirstOrDefault();

        Refrescar();
        RevisarFirewall();
    }

    public event Action? CerrarSolicitado;

    /// <summary>Todas las IPv4 candidatas. Con Hyper-V o WSL hay varias y solo una lleva al teléfono.</summary>
    public IReadOnlyList<InterfazLocal> Direcciones { get; }

    public bool HayVariasDirecciones => Direcciones.Count > 1;

    partial void OnDireccionElegidaChanged(InterfazLocal? value)
    {
        _main.Ctx.Preferencias.ControlRemoto.DireccionElegida = value?.Direccion;
        _main.Ctx.GuardarPreferencias();
        Refrescar();
    }

    partial void OnPermitirRedesPublicasChanged(bool value)
    {
        _main.Ctx.Preferencias.ControlRemoto.PermitirRedesPublicas = value;
        _main.Ctx.GuardarPreferencias();
    }

    [RelayCommand]
    private void Encender()
    {
        if (_main.Remoto.Activo) return;
        if (!_main.Remoto.Iniciar()) return;

        _main.Ctx.Preferencias.ControlRemoto.Activado = true;
        _main.Ctx.GuardarPreferencias();
        Refrescar();
        RevisarFirewall();
    }

    [RelayCommand]
    private async Task ApagarAsync()
    {
        await _main.Remoto.DetenerAsync();
        _main.Ctx.Preferencias.ControlRemoto.Activado = false;
        _main.Ctx.GuardarPreferencias();
        Refrescar();
    }

    /// <summary>Corta la sesión del teléfono y cambia el PIN, sin apagar el servidor.</summary>
    [RelayCommand]
    private void Desconectar()
    {
        _main.Remoto.Desconectar();
        Refrescar();
    }

    [RelayCommand]
    private void PermitirEnFirewall()
    {
        var puerto = _main.Remoto.Activo ? _main.Remoto.Puerto : _main.Ctx.Preferencias.ControlRemoto.Puerto;
        var ok = Cortafuegos.Permitir(puerto, PermitirRedesPublicas, out var cancelado);

        _main.Ctx.Preferencias.ControlRemoto.ReglaFirewallIntentada = true;
        _main.Ctx.GuardarPreferencias();

        if (cancelado)
        {
            // Cancelar el UAC no es un error: se dice con calma y se ofrece la vía manual.
            AvisoFirewall = "No se creó la regla porque se canceló el permiso de administrador. " +
                            "Puedes volver a intentarlo o pedirle a alguien con permisos que ejecute el comando de abajo.";
            return;
        }

        if (!ok)
        {
            AvisoFirewall = "No se pudo crear la regla. Copia el comando de abajo y ejecútalo en " +
                            "PowerShell como administrador.";
            return;
        }

        RevisarFirewall();
    }

    [RelayCommand]
    private void Cerrar() => CerrarSolicitado?.Invoke();

    /// <summary>El comando de netsh, para copiar y pegar si la elevación no es posible.</summary>
    public string ComandoManual => Cortafuegos.ComandoManual(
        _main.Remoto.Activo ? _main.Remoto.Puerto : _main.Ctx.Preferencias.ControlRemoto.Puerto,
        PermitirRedesPublicas);

    public void Refrescar()
    {
        Activo = _main.Remoto.Activo;
        Pin = _main.Remoto.Pin;
        Url = _main.Remoto.Url ?? "";
        Qr = string.IsNullOrEmpty(Url) ? null : ImagenQr.Crear(Url, escala: 6);

        var sesion = _main.Remoto.Sesion;
        Conectado = sesion is null
            ? (Activo ? "Nadie conectado todavía. Escanea el código con el teléfono." : "El control remoto está apagado.")
            : $"Conectado desde {sesion.Ip} · {sesion.Agente}";

        OnPropertyChanged(nameof(ComandoManual));
    }

    /// <summary>
    /// Lee el estado del firewall (no requiere administrador) y avisa del caso que más
    /// desconcierta: que la red esté en perfil Público, donde la regla no se aplica.
    /// </summary>
    private void RevisarFirewall()
    {
        var estado = Cortafuegos.LeerEstado();
        var perfil = Cortafuegos.PerfilActivo();

        MostrarBotonFirewall = estado != EstadoRegla.Permitida;

        AvisoFirewall = estado switch
        {
            EstadoRegla.Permitida when perfil == "Público" =>
                "La regla existe, pero esta red está como «Pública» y la regla no se aplica ahí. " +
                "Cambia la red a «Privada» en la configuración de Windows, o marca la casilla de redes públicas.",
            EstadoRegla.Permitida => "El Firewall de Windows ya permite el control remoto.",
            EstadoRegla.Bloqueada =>
                "El Firewall de Windows está BLOQUEANDO el control remoto. Suele pasar cuando se pulsó " +
                "«Cancelar» en el aviso. Pulsa el botón para reemplazar esa regla.",
            EstadoRegla.NoExiste =>
                "Si el teléfono no conecta, casi siempre es el Firewall de Windows. Pulsa el botón: " +
                "pedirá la contraseña de administrador una sola vez y queda guardado.",
            _ => "No se pudo leer el estado del Firewall. Si el teléfono no conecta, prueba el botón.",
        };
    }
}
