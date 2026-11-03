using System.Text.Json;
using System.Text.Json.Serialization;
using Traymote.Core;

namespace Traymote.Services;

internal sealed class AppSettings
{
    public string? DeviceHost { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceModel { get; set; }
    public string? DeviceSerial { get; set; }
    public bool HotKeyEnabled { get; set; } = true;
    public string? HotKey { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>
/// Persists settings as JSON under LocalAppData, which works both packaged and unpackaged.
/// </summary>
internal static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Traymote",
        "settings.json");

    // Device reconnects and shortcut changes share one instance, so neither save overwrites the other.
    private static readonly AppSettings Settings = ReadSettings();

    public static event EventHandler? HotKeyChanged;

    public static bool HotKeyEnabled => Settings.HotKeyEnabled;

    public static HotKey HotKey => Core.HotKey.TryParse(Settings.HotKey, out HotKey hotKey) ? hotKey : Core.HotKey.Default;

    public static AppSettings Load() => Settings;

    public static bool TrySaveHotKey(bool enabled, HotKey hotKey)
    {
        if (!hotKey.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(hotKey));
        }

        bool previousEnabled = Settings.HotKeyEnabled;
        string? previousHotKey = Settings.HotKey;
        Settings.HotKeyEnabled = enabled;
        Settings.HotKey = hotKey.Serialize();
        if (!Save(Settings))
        {
            Settings.HotKeyEnabled = previousEnabled;
            Settings.HotKey = previousHotKey;
            return false;
        }

        HotKeyChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

    private static AppSettings ReadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using FileStream stream = File.OpenRead(SettingsPath);
                AppSettings settings = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new();
                if (settings.HotKey is not null && !Core.HotKey.TryParse(settings.HotKey, out _))
                {
                    System.Diagnostics.Debug.WriteLine("[Settings] Invalid hotkey; using the default shortcut.");
                    settings.HotKey = null;
                }

                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] Load failed: {ex.Message}");
        }

        return new();
    }

    public static bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] Save failed: {ex.Message}");
            return false;
        }
    }
}
