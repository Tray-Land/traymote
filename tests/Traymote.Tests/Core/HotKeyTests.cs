using System.Globalization;
using Traymote.Core;

namespace Traymote.Tests.Core;

[TestClass]
public sealed class HotKeyTests
{
    [TestMethod]
    [DataRow(HotKey.Control | HotKey.Alt, (uint)'R')]
    [DataRow(HotKey.Win | HotKey.Shift, 0x70u)]
    [DataRow(HotKey.Alt, 0x20u)]
    public void TryParse_SerializedShortcut_RoundTrips(uint modifiers, uint key)
    {
        HotKey original = new(modifiers, key);

        bool parsed = HotKey.TryParse(original.Serialize(), out HotKey actual);

        Assert.IsTrue(parsed);
        Assert.AreEqual(original, actual);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("3")]
    [DataRow("Ctrl+Alt+R")]
    [DataRow("3,82,1")]
    [DataRow("16,82")]
    [DataRow("0,82")]
    [DataRow("3,0")]
    [DataRow("3,256")]
    [DataRow("3,4294967296")]
    [DataRow("3,17")]
    [DataRow("3,160")]
    [DataRow("3,27")]
    [DataRow("3,123")]
    public void TryParse_InvalidShortcut_RejectsInput(string? text)
    {
        bool parsed = HotKey.TryParse(text, out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void Serialize_NonDefaultCulture_UsesInvariantStorage()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            HotKey hotKey = new(HotKey.Control | HotKey.Alt, 'R');

            string serialized = hotKey.Serialize();

            Assert.AreEqual("3,82", serialized);
            Assert.IsTrue(HotKey.TryParse(serialized, out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
