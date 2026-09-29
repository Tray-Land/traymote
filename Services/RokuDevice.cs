namespace Traymote.Services;

/// <summary>
/// A Roku reachable over the External Control Protocol (ECP).
/// </summary>
/// <param name="Host">IP address or host name. ECP always listens on port 8060.</param>
/// <param name="Name">The name the user gave the device, or its model name.</param>
/// <param name="Model">Model name, e.g. "Roku Ultra".</param>
/// <param name="Serial">Serial number. Stable across IP changes, so it is what identifies a remembered device.</param>
public sealed record RokuDevice(string Host, string Name, string Model, string Serial)
{
    public string Details => string.IsNullOrEmpty(Model) ? Host : $"{Model} · {Host}";
}
