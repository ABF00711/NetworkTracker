using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkTracker;

internal sealed record AdapterEntry(string Id, string Name, string Description, bool IsUp, bool HasGateway)
{
    public string MenuLabel { get; init; } = "";
}

internal static class AdapterCatalog
{
    private static readonly string[] NoiseMarkers =
    [
        "Loopback",
        "Bluetooth",
        "vEthernet",
        "Hyper-V",
        "VMware",
        "VirtualBox",
        "Teredo",
        "isatap",
        "Wi-Fi Direct",
        "WSL",
        "Npcap",
        "Pseudo-Interface",
        "Kernel Debug"
    ];

    public static IReadOnlyList<AdapterEntry> List()
    {
        NetworkInterface[] nics = ReadAll();
        List<AdapterEntry> candidates = nics.Where(IsListed).Select(ToEntry).ToList();
        if (candidates.Count == 0)
            candidates = nics.Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback).Select(ToEntry).ToList();

        HashSet<string> duplicateNames = candidates
            .GroupBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        return candidates
            .Select(entry => entry with { MenuLabel = Label(entry, duplicateNames.Contains(entry.Name)) })
            .OrderByDescending(entry => entry.IsUp && entry.HasGateway)
            .ThenByDescending(entry => entry.IsUp)
            .ThenBy(entry => entry.MenuLabel, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static NetworkInterface? Resolve(string? preferredId)
    {
        NetworkInterface[] nics = ReadAll();
        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            NetworkInterface? chosen = nics.FirstOrDefault(nic =>
                string.Equals(nic.Id, preferredId, StringComparison.OrdinalIgnoreCase));
            if (chosen != null)
                return chosen;
        }

        return nics.Where(IsListed).OrderByDescending(Score).FirstOrDefault()
            ?? nics.FirstOrDefault(nic =>
                nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                nic.OperationalStatus == OperationalStatus.Up);
    }

    private static NetworkInterface[] ReadAll()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException ex)
        {
            AppLog.Write(ex);
            return [];
        }
    }

    private static bool IsListed(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            return false;

        string text = nic.Name + " " + nic.Description;
        return !NoiseMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static int Score(NetworkInterface nic)
    {
        int score = 0;
        if (nic.OperationalStatus == OperationalStatus.Up)
            score += 50;
        if (HasGateway(nic))
            score += 100;
        if (IsWired(nic.NetworkInterfaceType))
            score += 10;
        else if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            score += 8;
        return score;
    }

    private static bool IsWired(NetworkInterfaceType type) =>
        type is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx;

    private static bool HasGateway(NetworkInterface nic)
    {
        try
        {
            return nic.GetIPProperties().GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                !gateway.Address.Equals(IPAddress.Any));
        }
        catch (Exception ex) when (ex is NetworkInformationException or SocketException)
        {
            return false;
        }
    }

    private static AdapterEntry ToEntry(NetworkInterface nic) =>
        new(nic.Id, nic.Name, nic.Description, nic.OperationalStatus == OperationalStatus.Up, HasGateway(nic));

    private static string Label(AdapterEntry entry, bool qualify)
    {
        string name = qualify && !string.IsNullOrWhiteSpace(entry.Description)
            ? $"{entry.Name} — {entry.Description}"
            : entry.Name;
        return entry.IsUp ? name : $"{name} (disconnected)";
    }
}
