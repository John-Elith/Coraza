using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Coraza.Remoto.Red;

/// <summary>Una dirección IPv4 de este equipo por la que el teléfono podría entrar.</summary>
/// <param name="Inalambrica">Se ofrece primero: el teléfono casi siempre entra por wifi.</param>
public sealed record InterfazLocal(
    string Nombre,
    string Descripcion,
    string Direccion,
    bool Inalambrica,
    bool Activa);

/// <summary>
/// Decide qué dirección poner en el código QR.
///
/// Importa más de lo que parece: en un equipo con Hyper-V, WSL o VirtualBox hay
/// varias IPv4 y solo una es la del wifi de la iglesia. Si el QR apunta a otra, el
/// teléfono no conecta y no hay ningún mensaje que lo explique. Por eso el filtro
/// es explícito y se puede probar: <see cref="Filtrar"/> recibe la lista, no la
/// consulta, de modo que las pruebas no dependen de las tarjetas de red reales.
/// </summary>
public static class DireccionesLocales
{
    /// <summary>Adaptadores que nunca llevan al teléfono, por mucho que tengan IPv4.</summary>
    private static readonly string[] Virtuales =
    {
        "hyper-v", "vmware", "virtualbox", "vethernet", "wsl", "bluetooth",
        "loopback", "tap-", "tunnel", "docker", "npcap", "zerotier", "tailscale",
    };

    /// <summary>
    /// Filtro puro: descarta lo inalcanzable y ordena poniendo el wifi primero.
    /// </summary>
    public static IReadOnlyList<InterfazLocal> Filtrar(IEnumerable<InterfazLocal> candidatas) =>
        candidatas
            .Where(i => i.Activa)
            .Where(i => !EsVirtual(i))
            .Where(i => EsUtilizable(i.Direccion))
            .GroupBy(i => i.Direccion)
            .Select(g => g.First())
            .OrderByDescending(i => i.Inalambrica)
            .ThenBy(i => i.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Direcciones reales de este equipo, ya filtradas.</summary>
    public static IReadOnlyList<InterfazLocal> Detectar() => Filtrar(Enumerar());

    private static bool EsVirtual(InterfazLocal i)
    {
        var texto = (i.Nombre + " " + i.Descripcion).ToLowerInvariant();
        return Virtuales.Any(v => texto.Contains(v, StringComparison.Ordinal));
    }

    /// <summary>
    /// Descarta loopback y las de «autoconfiguración» 169.254.x.x, que aparecen cuando
    /// no hubo DHCP: tienen pinta de dirección válida y no llevan a ninguna parte.
    /// </summary>
    private static bool EsUtilizable(string direccion)
    {
        if (!IPAddress.TryParse(direccion, out var ip)) return false;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        if (IPAddress.IsLoopback(ip)) return false;
        var octetos = ip.GetAddressBytes();
        return !(octetos[0] == 169 && octetos[1] == 254);
    }

    private static IEnumerable<InterfazLocal> Enumerar()
    {
        NetworkInterface[] tarjetas;
        try { tarjetas = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { yield break; }

        foreach (var tarjeta in tarjetas)
        {
            if (tarjeta.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            IPInterfaceProperties propiedades;
            try { propiedades = tarjeta.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }

            var inalambrica = tarjeta.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            foreach (var unicast in propiedades.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                yield return new InterfazLocal(
                    tarjeta.Name,
                    tarjeta.Description,
                    unicast.Address.ToString(),
                    inalambrica,
                    tarjeta.OperationalStatus == OperationalStatus.Up);
            }
        }
    }
}
