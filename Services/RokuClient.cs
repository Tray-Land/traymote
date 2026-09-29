using System.Net;
using System.Net.Http;
using System.Xml.Linq;

namespace Traymote.Services;

/// <summary>
/// Minimal Roku External Control Protocol (ECP) client.
/// See https://sdkdocs.roku.com/docs/developer-program/dev-tools/external-control-api.md
/// </summary>
internal static class RokuClient
{
    public const int EcpPort = 8060;

    // Roku ECP key names for the buttons Traymote exposes.
    public const string KeyUp = "Up";
    public const string KeyDown = "Down";
    public const string KeyLeft = "Left";
    public const string KeyRight = "Right";
    public const string KeySelect = "Select";
    public const string KeyBack = "Back";
    public const string KeyHome = "Home";
    public const string KeyPlay = "Play";
    public const string KeyRev = "Rev";
    public const string KeyFwd = "Fwd";
    public const string KeyInfo = "Info"; // The * (options) button
    public const string KeyBackspace = "Backspace";
    public const string KeyEnter = "Enter";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static Uri BaseUri(string host) => new UriBuilder(Uri.UriSchemeHttp, host, EcpPort).Uri;

    /// <summary>
    /// Accepts what a person might type for a device address ("192.168.1.20",
    /// "192.168.1.20:8060", "http://192.168.1.20:8060/") and returns just the host.
    /// </summary>
    public static bool TryNormalizeHost(string? input, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "http://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || string.IsNullOrEmpty(uri.Host))
            return false;

        host = uri.Host;
        return true;
    }

    /// <summary>Returns the device at <paramref name="host"/>, or null if it isn't a reachable Roku.</summary>
    public static async Task<RokuDevice?> TryGetDeviceInfoAsync(string host, CancellationToken cancellationToken = default)
    {
        try
        {
            string xml = await Http.GetStringAsync(new Uri(BaseUri(host), "query/device-info"), cancellationToken);
            return ParseDeviceInfo(host, xml);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return null;
        }
    }

    internal static RokuDevice ParseDeviceInfo(string host, string xml)
    {
        XElement root = XElement.Parse(xml);

        string? Get(string name) =>
            root.Element(name)?.Value.Trim() is { Length: > 0 } value ? value : null;

        string model = Get("model-name") ?? string.Empty;
        string name = Get("user-device-name")
            ?? Get("friendly-device-name")
            ?? Get("default-device-name")
            ?? (model.Length > 0 ? model : "Roku");

        return new RokuDevice(host, name, model, Get("serial-number") ?? string.Empty);
    }

    public static async Task SendKeyAsync(string host, string key, CancellationToken cancellationToken = default)
    {
        using ByteArrayContent empty = new([]);
        using HttpResponseMessage response = await Http.PostAsync(
            new Uri(BaseUri(host), "keypress/" + key), empty, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Types <paramref name="text"/> into whatever text field is focused on the Roku,
    /// one character at a time, the way the official mobile app does.
    /// </summary>
    public static async Task SendTextAsync(string host, string text, CancellationToken cancellationToken = default)
    {
        foreach (System.Text.Rune rune in text.EnumerateRunes())
            await SendKeyAsync(host, "Lit_" + Uri.EscapeDataString(rune.ToString()), cancellationToken);
    }

    public static bool IsAccessDenied(HttpRequestException ex) => ex.StatusCode == HttpStatusCode.Forbidden;
}
