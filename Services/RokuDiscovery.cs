using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Traymote.Services;

/// <summary>
/// Finds Rokus on the local network with an SSDP M-SEARCH for the "roku:ecp" service type.
/// </summary>
internal static class RokuDiscovery
{
    private static readonly IPEndPoint SsdpEndpoint = new(IPAddress.Parse("239.255.255.250"), 1900);

    private static readonly byte[] SearchRequest = Encoding.ASCII.GetBytes(
        "M-SEARCH * HTTP/1.1\r\n" +
        "HOST: 239.255.255.250:1900\r\n" +
        "MAN: \"ssdp:discover\"\r\n" +
        "MX: 2\r\n" +
        "ST: roku:ecp\r\n" +
        "\r\n");

    public static async Task<IReadOnlyList<RokuDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ConcurrentDictionary<string, byte> hosts = new(StringComparer.OrdinalIgnoreCase);

        // Multicast leaves through a single interface per socket, and on machines with
        // VPN or Hyper-V adapters the default one is often wrong. Search from each of them.
        await Task.WhenAll(GetLocalIPv4Addresses()
            .Select(address => SearchFromAsync(address, timeout, hosts, cancellationToken)));

        RokuDevice?[] devices = await Task.WhenAll(hosts.Keys
            .Select(host => RokuClient.TryGetDeviceInfoAsync(host, cancellationToken)));

        return devices
            .OfType<RokuDevice>()
            .DistinctBy(d => string.IsNullOrEmpty(d.Serial) ? d.Host : d.Serial)
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static async Task SearchFromAsync(
        IPAddress localAddress,
        TimeSpan timeout,
        ConcurrentDictionary<string, byte> hosts,
        CancellationToken cancellationToken)
    {
        try
        {
            using UdpClient udp = new(new IPEndPoint(localAddress, 0));
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localAddress.GetAddressBytes());
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            // UDP is lossy; a second request a moment later catches devices that missed the first.
            await udp.SendAsync(SearchRequest, SsdpEndpoint, cts.Token);
            _ = ResendAsync(udp, cts.Token);

            while (!cts.IsCancellationRequested)
            {
                UdpReceiveResult result = await udp.ReceiveAsync(cts.Token);
                if (TryParseLocation(Encoding.ASCII.GetString(result.Buffer), out string host))
                    hosts.TryAdd(host, 0);
            }
        }
        catch (OperationCanceledException)
        {
            // Search window elapsed.
        }
        catch (SocketException)
        {
            // Adapter can't do multicast (or went away mid-search); the others still can.
        }
    }

    private static async Task ResendAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
            await udp.SendAsync(SearchRequest, SsdpEndpoint, cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Pulls the device host out of an SSDP response such as
    /// <c>LOCATION: http://192.168.1.134:8060/</c>, ignoring anything that isn't a Roku.
    /// </summary>
    internal static bool TryParseLocation(string response, out string host)
    {
        host = string.Empty;
        if (!response.Contains("roku:ecp", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (string line in response.Split("\r\n"))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0 || !line.AsSpan(0, colon).Trim().Equals("LOCATION", StringComparison.OrdinalIgnoreCase))
                continue;

            if (Uri.TryCreate(line[(colon + 1)..].Trim(), UriKind.Absolute, out Uri? location)
                && !string.IsNullOrEmpty(location.Host))
            {
                host = location.Host;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IPAddress> GetLocalIPv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                && nic.SupportsMulticast
                && nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));
}
