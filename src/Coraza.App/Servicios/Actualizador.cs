using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using Coraza.App.ViewModels;
using Coraza.App.Vistas;
using Coraza.Core.Actualizacion;
using Serilog;

namespace Coraza.App.Servicios;

/// <summary>
/// Avisa de versiones nuevas y las descarga en segundo plano.
///
/// Tres reglas que mandan sobre todo lo demás:
///
/// 1. <b>Nunca durante una proyección.</b> Ni se avisa ni, por supuesto, se instala.
///    Aplicar una actualización cierra Coraza, y hacerlo en mitad del culto sería
///    exactamente el desastre que este programa existe para evitar.
/// 2. <b>La descarga es en segundo plano</b>, dentro del programa. Al operador no se
///    le manda a una página web ni se le pide que busque el archivo en Descargas.
/// 3. <b>Fallar es normal y silencioso.</b> Sin internet, con un portal cautivo o con
///    GitHub caído no pasa nada: no hay aviso, no hay error, no hay espera.
///
/// Es lo único de Coraza que sale a internet, y se puede apagar por completo.
/// </summary>
public sealed class Actualizador
{
    private const string UrlRelease = "https://api.github.com/repos/John-Elith/Coraza/releases/latest";
    private const string NombreInstalador = "CorazaInstalador.exe";

    private readonly MainViewModel _vm;
    private string? _archivoListo;

    public Actualizador(MainViewModel vm) => _vm = vm;

    /// <summary>La versión publicada que está descargada y lista, si la hay.</summary>
    public InfoActualizacion? Disponible { get; private set; }

    public bool HayActualizacionLista => Disponible is not null && _archivoListo is not null;

    /// <summary>Versión con la que se identifica este ejecutable.</summary>
    public static Version VersionActual =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    /// <summary>
    /// Comprueba y, si procede, descarga. Se llama al terminar de preparar la
    /// aplicación y no bloquea nada: cualquier fallo se traga en silencio.
    /// </summary>
    public async Task ComprobarAsync()
    {
        var p = _vm.Ctx.Preferencias.Actualizaciones;
        if (!p.Comprobar) return;
        if (p.UltimaComprobacion is { } ultima && (DateTime.Now - ultima).TotalHours < 24) return;

        try
        {
            p.UltimaComprobacion = DateTime.Now;
            _vm.Ctx.GuardarPreferencias();

            using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // GitHub rechaza las peticiones sin identificación de cliente.
            cliente.DefaultRequestHeaders.Add("User-Agent", "Coraza");
            cliente.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            var json = await cliente.GetStringAsync(UrlRelease).ConfigureAwait(false);
            var info = ComprobadorVersiones.LeerRelease(json, NombreInstalador);
            if (info is null) return;
            if (!ComprobadorVersiones.EsMasNueva(info.Etiqueta, VersionActual)) return;
            if (string.Equals(info.Etiqueta, p.VersionOmitida, StringComparison.OrdinalIgnoreCase)) return;

            var destino = await DescargarAsync(cliente, info).ConfigureAwait(false);
            if (destino is null) return;

            Disponible = info;
            _archivoListo = destino;
            Log.Information("Actualización descargada: {Etiqueta}", info.Etiqueta);
            Avisar();
        }
        catch (Exception e)
        {
            // Sin internet, portal cautivo, DNS caído… nada de esto es un problema
            // del programa y el operador no tiene por qué enterarse.
            Log.Debug(e, "No se pudo comprobar si hay actualizaciones");
        }
    }

    /// <summary>
    /// Avisa en la barra de mensajes, nunca con una ventana modal, y <b>solo si no se
    /// está proyectando</b>. Si el culto está en marcha el aviso espera: el indicador
    /// queda puesto y se vuelve a ofrecer cuando la proyección se detenga.
    /// </summary>
    public void Avisar()
    {
        if (!HayActualizacionLista || _vm.Proyeccion.Proyectando) return;
        _vm.MostrarMensaje($"Hay una versión nueva ({Disponible!.Nombre}) descargada y lista. " +
                           "Ve a Configuración para instalarla.");
    }

    /// <summary>
    /// Instala la versión descargada: lanza el instalador y cierra Coraza.
    /// Se niega en redondo si hay una proyección activa.
    /// </summary>
    public bool Aplicar()
    {
        if (!HayActualizacionLista) return false;

        if (_vm.Proyeccion.Proyectando)
        {
            _vm.MostrarMensaje("No se puede actualizar con la proyección activa. Deténla primero.", esError: true);
            return false;
        }

        if (!Dialogo.Confirmar(
                "Instalar la actualización",
                $"Coraza se cerrará para instalar {Disponible!.Nombre}. Tus canciones, servicios y respaldos no se tocan. ¿Continuar?",
                "Instalar"))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(_archivoListo!) { UseShellExecute = true });
            Application.Current.Shutdown();
            return true;
        }
        catch (Exception e)
        {
            Log.Error(e, "No se pudo lanzar el instalador de la actualización");
            _vm.MostrarMensaje("No se pudo iniciar la instalación. El archivo está en la carpeta Actualizaciones.", esError: true);
            return false;
        }
    }

    /// <summary>El operador decide saltarse esta versión; no se vuelve a ofrecer.</summary>
    public void Omitir()
    {
        if (Disponible is null) return;
        _vm.Ctx.Preferencias.Actualizaciones.VersionOmitida = Disponible.Etiqueta;
        _vm.Ctx.GuardarPreferencias();
        Disponible = null;
        _archivoListo = null;
    }

    /// <summary>
    /// Descarga a la carpeta de datos. Se comprueba el tamaño contra el que declara
    /// GitHub: una descarga cortada a medias produciría un instalador roto, y es mejor
    /// no ofrecer nada que ofrecer algo que falle al ejecutarse.
    /// </summary>
    private async Task<string?> DescargarAsync(HttpClient cliente, InfoActualizacion info)
    {
        var carpeta = Path.Combine(_vm.Ctx.Rutas.Raiz, "Actualizaciones");
        Directory.CreateDirectory(carpeta);
        var destino = Path.Combine(carpeta, $"CorazaInstalador-{Limpiar(info.Etiqueta)}.exe");

        // Si ya se descargó entera en un arranque anterior, no se repite.
        if (File.Exists(destino) && (info.Tamano == 0 || new FileInfo(destino).Length == info.Tamano))
            return destino;

        var temporal = destino + ".parcial";
        try
        {
            await using (var origen = await cliente.GetStreamAsync(info.Url).ConfigureAwait(false))
            await using (var archivo = File.Create(temporal))
                await origen.CopyToAsync(archivo).ConfigureAwait(false);

            if (info.Tamano > 0 && new FileInfo(temporal).Length != info.Tamano)
            {
                File.Delete(temporal);
                Log.Warning("La descarga de la actualización quedó incompleta");
                return null;
            }

            if (File.Exists(destino)) File.Delete(destino);
            File.Move(temporal, destino);
            return destino;
        }
        catch (Exception e)
        {
            Log.Debug(e, "No se pudo descargar la actualización");
            try { if (File.Exists(temporal)) File.Delete(temporal); } catch (IOException) { }
            return null;
        }
    }

    private static string Limpiar(string etiqueta) =>
        string.Concat(etiqueta.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));
}
