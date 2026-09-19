using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Coraza.Rendering;
using Serilog;

namespace Coraza.App.Infraestructura;

public sealed record InfoPantalla(string Dispositivo, string Nombre, Int32Rect Limites, bool Principal, int Numero)
{
    public double Proporcion => Limites.Height == 0 ? 16.0 / 9 : (double)Limites.Width / Limites.Height;

    public string Descripcion =>
        $"Pantalla {Numero} · {Nombre} · {Limites.Width}×{Limites.Height} ({PatronPrueba.Proporcion(Limites.Width, Limites.Height)}){(Principal ? " · principal" : "")}";
}

/// <summary>Detecta monitores y proyectores conectados, su resolución y si Windows está duplicando la pantalla.</summary>
public static class GestorPantallas
{
    public static List<InfoPantalla> Enumerar()
    {
        var nombres = NombresAmigables();
        var encontradas = new List<(string Dispositivo, Int32Rect Limites, bool Principal)>();

        Nativo.MonitorEnumProc proc = (IntPtr monitor, IntPtr _, ref Nativo.RECT _, IntPtr _) =>
        {
            var info = new Nativo.MONITORINFOEX { cbSize = Marshal.SizeOf<Nativo.MONITORINFOEX>() };
            if (Nativo.GetMonitorInfo(monitor, ref info))
                encontradas.Add((info.szDevice, info.rcMonitor.ARect(), (info.dwFlags & 1) != 0));
            return true;
        };
        Nativo.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);

        return encontradas
            .OrderByDescending(p => p.Principal)
            .ThenBy(p => p.Limites.X)
            .Select((p, i) => new InfoPantalla(
                p.Dispositivo,
                nombres.TryGetValue(p.Dispositivo, out var n) ? n : "Monitor",
                p.Limites,
                p.Principal,
                NumeroDe(p.Dispositivo, i + 1)))
            .ToList();
    }

    /// <summary>¿Windows está en modo «Duplicar»? En ese caso el proyector no aparece como segunda pantalla.</summary>
    public static bool ModoDuplicar()
    {
        try
        {
            if (Nativo.GetDisplayConfigBufferSizes(Nativo.QDC_DATABASE_CURRENT, out var caminos, out var modos) != 0) return false;
            var arregloCaminos = new Nativo.DISPLAYCONFIG_PATH_INFO[caminos];
            var arregloModos = new Nativo.DISPLAYCONFIG_MODE_INFO[modos];
            if (Nativo.QueryDisplayConfigTopologia(Nativo.QDC_DATABASE_CURRENT, ref caminos, arregloCaminos, ref modos, arregloModos, out var topologia) != 0)
                return false;
            return topologia == Nativo.DISPLAYCONFIG_TOPOLOGY_CLONE;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo consultar la topología de pantallas");
            return false;
        }
    }

    /// <summary>Equivale a Win + P → «Extender».</summary>
    public static bool CambiarAExtender()
    {
        try
        {
            return Nativo.SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, Nativo.SDC_APPLY | Nativo.SDC_TOPOLOGY_EXTEND) == 0;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo cambiar a modo extender");
            return false;
        }
    }

    /// <summary>Nombre del dispositivo (\\.\DISPLAY1…) de la pantalla donde está la ventana.</summary>
    public static string? DispositivoDe(Window ventana)
    {
        var hwnd = new WindowInteropHelper(ventana).Handle;
        if (hwnd == IntPtr.Zero) return null;
        var monitor = Nativo.MonitorFromWindow(hwnd, 2);
        var info = new Nativo.MONITORINFOEX { cbSize = Marshal.SizeOf<Nativo.MONITORINFOEX>() };
        return Nativo.GetMonitorInfo(monitor, ref info) ? info.szDevice : null;
    }

    private static Dictionary<string, string> NombresAmigables()
    {
        var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Nativo.GetDisplayConfigBufferSizes(Nativo.QDC_ONLY_ACTIVE_PATHS, out var caminos, out var modos) != 0) return resultado;
            var arregloCaminos = new Nativo.DISPLAYCONFIG_PATH_INFO[caminos];
            var arregloModos = new Nativo.DISPLAYCONFIG_MODE_INFO[modos];
            if (Nativo.QueryDisplayConfig(Nativo.QDC_ONLY_ACTIVE_PATHS, ref caminos, arregloCaminos, ref modos, arregloModos, IntPtr.Zero) != 0)
                return resultado;

            foreach (var camino in arregloCaminos.Take((int)caminos))
            {
                var origen = new Nativo.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header =
                    {
                        type = 1,
                        size = (uint)Marshal.SizeOf<Nativo.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = camino.sourceInfo.adapterId,
                        id = camino.sourceInfo.id,
                    },
                };
                if (Nativo.DisplayConfigGetDeviceInfo(ref origen) != 0) continue;

                var destino = new Nativo.DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header =
                    {
                        type = 2,
                        size = (uint)Marshal.SizeOf<Nativo.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = camino.targetInfo.adapterId,
                        id = camino.targetInfo.id,
                    },
                };
                var nombre = "Monitor";
                if (Nativo.DisplayConfigGetDeviceInfo(ref destino) == 0)
                {
                    if (!string.IsNullOrWhiteSpace(destino.monitorFriendlyDeviceName)) nombre = destino.monitorFriendlyDeviceName.Trim();
                    else if (destino.outputTechnology == Nativo.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL) nombre = "Pantalla integrada";
                }
                resultado.TryAdd(origen.viewGdiDeviceName, nombre);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudieron leer los nombres de los monitores");
        }
        return resultado;
    }

    private static int NumeroDe(string dispositivo, int respaldo)
    {
        var digitos = new string(dispositivo.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digitos, out var n) ? n : respaldo;
    }
}

/// <summary>Evita que Windows suspenda el equipo o apague la pantalla mientras se proyecta.</summary>
public static class EvitarSuspension
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    public static void Activar() => Nativo.SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);

    public static void Desactivar() => Nativo.SetThreadExecutionState(ES_CONTINUOUS);
}
