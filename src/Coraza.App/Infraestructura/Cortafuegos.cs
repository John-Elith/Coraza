using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace Coraza.App.Infraestructura;

/// <summary>Qué dice el Firewall de Windows sobre la regla del control remoto.</summary>
public enum EstadoRegla
{
    /// <summary>No se pudo averiguar. Nunca se trata como error: se ofrece crear la regla igual.</summary>
    Desconocido,
    NoExiste,
    Permitida,
    Bloqueada,
}

/// <summary>
/// Ayudante del Firewall de Windows para el control remoto.
///
/// Este es el punto donde más instalaciones se tuercen, y tiene un modo de fallo
/// cruel: al escuchar por primera vez en una interfaz que no es loopback, Windows
/// muestra su aviso con escudo de UAC. Coraza corre sin elevación, así que un usuario
/// estándar no puede aceptarlo; y si pulsa «Cancelar» —lo más probable tres minutos
/// antes del culto— <b>Windows escribe reglas de BLOQUEO y no vuelve a preguntar</b>.
/// Por eso se ofrece crear la regla a propósito, antes de que aparezca el aviso.
///
/// Dos asimetrías importantes:
/// - <b>Leer</b> el estado NO requiere administrador.
/// - <b>Crear</b> la regla sí, y al elevar con «runas» hay que usar UseShellExecute,
///   lo que impide capturar la salida: por eso después se verifica releyendo.
/// </summary>
public static class Cortafuegos
{
    public const string NombreRegla = "Coraza · Control remoto";

    /// <summary>
    /// Lee el estado de la regla. La salida de netsh está traducida, así que se aceptan
    /// las palabras en inglés y en español, y ante cualquier duda se prioriza que la
    /// regla EXISTA sobre el texto concreto.
    /// </summary>
    public static EstadoRegla LeerEstado()
    {
        var salida = Ejecutar("advfirewall firewall show rule name=\"" + NombreRegla + "\"");
        if (salida is null) return EstadoRegla.Desconocido;

        // netsh dice «No rules match the specified criteria» / «Ninguna regla coincide…».
        if (salida.Contains("No rules match", StringComparison.OrdinalIgnoreCase) ||
            salida.Contains("Ninguna regla", StringComparison.OrdinalIgnoreCase))
            return EstadoRegla.NoExiste;

        if (salida.Contains("Block", StringComparison.OrdinalIgnoreCase) ||
            salida.Contains("Bloquear", StringComparison.OrdinalIgnoreCase))
            return EstadoRegla.Bloqueada;

        if (salida.Contains("Allow", StringComparison.OrdinalIgnoreCase) ||
            salida.Contains("Permitir", StringComparison.OrdinalIgnoreCase))
            return EstadoRegla.Permitida;

        return EstadoRegla.Desconocido;
    }

    /// <summary>
    /// Perfil de la red activa. Importa porque la regla se crea para redes privadas y
    /// de dominio: si el equipo está en perfil «Público» no se aplicará, y eso hay que
    /// decírselo al operador en vez de dejarlo adivinando.
    /// </summary>
    public static string PerfilActivo()
    {
        var salida = Ejecutar("advfirewall show currentprofile");
        if (salida is null) return "";
        if (salida.Contains("Public", StringComparison.OrdinalIgnoreCase) ||
            salida.Contains("Público", StringComparison.OrdinalIgnoreCase)) return "Público";
        if (salida.Contains("Domain", StringComparison.OrdinalIgnoreCase) ||
            salida.Contains("Dominio", StringComparison.OrdinalIgnoreCase)) return "Dominio";
        if (salida.Contains("Private", StringComparison.OrdinalIgnoreCase) ||
            salida.Contains("Privado", StringComparison.OrdinalIgnoreCase)) return "Privado";
        return "";
    }

    /// <summary>
    /// Crea la regla pidiendo permisos de administrador UNA sola vez. Borra antes la
    /// regla existente para ser idempotente y, sobre todo, para pisar una regla de
    /// bloqueo que Windows haya escrito por un «Cancelar» anterior.
    /// </summary>
    /// <returns><c>false</c> si el usuario canceló el aviso de UAC.</returns>
    public static bool Permitir(int puerto, bool incluirPublicas, out bool cancelado)
    {
        cancelado = false;
        var perfiles = incluirPublicas ? "private,domain,public" : "private,domain";
        var argumentos =
            $"/c netsh advfirewall firewall delete rule name=\"{NombreRegla}\" >nul 2>&1 & " +
            $"netsh advfirewall firewall add rule name=\"{NombreRegla}\" dir=in action=allow " +
            $"program=\"{Environment.ProcessPath}\" protocol=TCP localport={puerto} " +
            $"profile={perfiles} enable=yes";

        try
        {
            var proceso = Process.Start(new ProcessStartInfo("cmd.exe", argumentos)
            {
                UseShellExecute = true,   // obligatorio para «runas»; impide capturar la salida
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            });
            proceso?.WaitForExit(15000);
            return true;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: el usuario cerró el aviso de UAC. No es un fallo del
            // programa y no debe verse como error.
            cancelado = true;
            return false;
        }
        catch (Exception e)
        {
            Log.Warning(e, "No se pudo crear la regla del firewall para el control remoto");
            return false;
        }
    }

    /// <summary>
    /// La línea exacta para que un voluntario de sistemas la ejecute a mano desde
    /// PowerShell como administrador. Se muestra tal cual, para copiar y pegar.
    /// </summary>
    public static string ComandoManual(int puerto, bool incluirPublicas)
    {
        var perfiles = incluirPublicas ? "private,domain,public" : "private,domain";
        return $"netsh advfirewall firewall add rule name=\"{NombreRegla}\" dir=in action=allow " +
               $"program=\"{Environment.ProcessPath}\" protocol=TCP localport={puerto} " +
               $"profile={perfiles} enable=yes";
    }

    private static string? Ejecutar(string argumentos)
    {
        try
        {
            using var proceso = Process.Start(new ProcessStartInfo("netsh", argumentos)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (proceso is null) return null;
            var salida = proceso.StandardOutput.ReadToEnd();
            proceso.WaitForExit(5000);
            return salida;
        }
        catch (Exception e)
        {
            Log.Debug(e, "No se pudo consultar el firewall");
            return null;   // Desconocido: nunca un error visible
        }
    }
}
