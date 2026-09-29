using System.Net.Http;

namespace Traymote.Services;

internal enum ConnectionState
{
    NoDevice,
    Searching,
    Connected,
    Offline,
}

/// <summary>
/// Owns the remembered Roku: finds it on startup (following it if its IP changed),
/// and sends key presses to it in order.
/// </summary>
/// <remarks>
/// Every public member is called from the UI thread and awaits resume there, so
/// <see cref="Changed"/> is always raised on the UI thread.
/// </remarks>
internal sealed class RemoteService
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(3);

    private const string AccessDeniedMessage =
        "The Roku refused the command. On the Roku, open Settings › System › Advanced system settings › " +
        "Control by mobile apps and set Network access to Default or Permissive.";

    // Key presses must reach the Roku in the order they were pressed.
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly AppSettings _settings = SettingsService.Load();
    private Task? _reconnectTask;

    public RemoteService()
    {
        if (_settings.DeviceHost is { Length: > 0 } host)
        {
            Device = new RokuDevice(host, _settings.DeviceName ?? "Roku", _settings.DeviceModel ?? string.Empty, _settings.DeviceSerial ?? string.Empty);
            State = ConnectionState.Offline;
        }
    }

    public event EventHandler? Changed;

    public RokuDevice? Device { get; private set; }

    public ConnectionState State { get; private set; } = ConnectionState.NoDevice;

    /// <summary>Why the last command failed, if it did.</summary>
    public string? Error { get; private set; }

    public bool HasRememberedDevice => _settings.DeviceHost is { Length: > 0 };

    /// <summary>
    /// Checks the remembered device is still there, re-finding it by serial number if its
    /// IP address changed. With nothing remembered, adopts the device if exactly one is found.
    /// Concurrent calls share one attempt.
    /// </summary>
    public Task ReconnectAsync()
    {
        if (_reconnectTask is { IsCompleted: false })
            return _reconnectTask;

        return _reconnectTask = ReconnectCoreAsync();
    }

    private async Task ReconnectCoreAsync()
    {
        SetState(ConnectionState.Searching);

        if (Device is { } remembered)
        {
            RokuDevice? current = await RokuClient.TryGetDeviceInfoAsync(remembered.Host);
            if (current is not null && IsSameDevice(current, remembered))
            {
                Remember(current);
                return;
            }

            IReadOnlyList<RokuDevice> found = await RokuDiscovery.DiscoverAsync(DiscoveryTimeout);
            if (found.FirstOrDefault(d => IsSameDevice(d, remembered)) is { } moved)
            {
                Remember(moved);
                return;
            }

            SetState(ConnectionState.Offline, "Can't reach this Roku. Make sure it's on and on the same network.");
            return;
        }

        IReadOnlyList<RokuDevice> devices = await RokuDiscovery.DiscoverAsync(DiscoveryTimeout);
        if (devices.Count == 1)
            Remember(devices[0]);
        else
            SetState(ConnectionState.NoDevice);
    }

    public Task<IReadOnlyList<RokuDevice>> DiscoverAsync() => RokuDiscovery.DiscoverAsync(DiscoveryTimeout);

    public void Select(RokuDevice device) => Remember(device);

    /// <summary>Connects to a device the user typed the address of.</summary>
    public async Task<bool> ConnectToHostAsync(string input)
    {
        if (!RokuClient.TryNormalizeHost(input, out string host))
            return false;

        RokuDevice? device = await RokuClient.TryGetDeviceInfoAsync(host);
        if (device is null)
            return false;

        Remember(device);
        return true;
    }

    public Task SendKeyAsync(string key) =>
        SendAsync(host => RokuClient.SendKeyAsync(host, key));

    public Task SendTextAsync(string text) =>
        SendAsync(host => RokuClient.SendTextAsync(host, text));

    private async Task SendAsync(Func<string, Task> send)
    {
        if (Device is not { } device)
            return;

        await _sendLock.WaitAsync();
        try
        {
            await send(device.Host);
            if (State != ConnectionState.Connected || Error is not null)
                SetState(ConnectionState.Connected);
        }
        catch (HttpRequestException ex) when (RokuClient.IsAccessDenied(ex))
        {
            SetState(ConnectionState.Connected, AccessDeniedMessage);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Most likely the Roku got a new IP address; go look for it.
            _ = ReconnectAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static bool IsSameDevice(RokuDevice candidate, RokuDevice remembered) =>
        string.IsNullOrEmpty(remembered.Serial)
            ? string.Equals(candidate.Host, remembered.Host, StringComparison.OrdinalIgnoreCase)
            : string.Equals(candidate.Serial, remembered.Serial, StringComparison.OrdinalIgnoreCase);

    private void Remember(RokuDevice device)
    {
        Device = device;
        _settings.DeviceHost = device.Host;
        _settings.DeviceName = device.Name;
        _settings.DeviceModel = device.Model;
        _settings.DeviceSerial = device.Serial;
        SettingsService.Save(_settings);
        SetState(ConnectionState.Connected);
    }

    private void SetState(ConnectionState state, string? error = null)
    {
        State = state;
        Error = error;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
