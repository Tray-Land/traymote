using System.Text.Json;
using Traymote.Core;
using Traymote.Services;

namespace Traymote.Tests.Services;

[TestClass]
public sealed class SettingsServiceTests
{
    [TestMethod]
    public void Deserialize_ExistingDeviceSettings_EnablesDefaultShortcut()
    {
        const string json = """{"DeviceHost":"192.168.1.50","DeviceName":"Living room"}""";

        AppSettings settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;

        Assert.IsTrue(settings.HotKeyEnabled);
        Assert.IsNull(settings.HotKey);
        Assert.AreEqual("192.168.1.50", settings.DeviceHost);
    }

    [TestMethod]
    public void Serialize_CustomDisabledShortcut_PreservesDeviceAndShortcut()
    {
        HotKey hotKey = new(HotKey.Control | HotKey.Shift, 'T');
        AppSettings settings = new()
        {
            DeviceHost = "192.168.1.50",
            HotKeyEnabled = false,
            HotKey = hotKey.Serialize(),
        };

        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        AppSettings restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;

        Assert.AreEqual(settings.DeviceHost, restored.DeviceHost);
        Assert.IsFalse(restored.HotKeyEnabled);
        Assert.IsTrue(HotKey.TryParse(restored.HotKey, out HotKey actual));
        Assert.AreEqual(hotKey, actual);
    }
}
