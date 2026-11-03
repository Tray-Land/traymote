using System.Globalization;

namespace Traymote.Core;

internal readonly record struct HotKey(uint Modifiers, uint VirtualKey)
{
    public const uint Alt = 0x1;
    public const uint Control = 0x2;
    public const uint Shift = 0x4;
    public const uint Win = 0x8;

    public static HotKey Default { get; } = new(Control | Alt, 'R');

    public bool IsValid =>
        Modifiers != 0 && (Modifiers & ~(Alt | Control | Shift | Win)) == 0 &&
        VirtualKey is > 0x07 and <= 0xFE and not 0x1B and not 0x7B &&
        !IsModifierKey(VirtualKey);

    public string Serialize() => string.Create(CultureInfo.InvariantCulture, $"{Modifiers},{VirtualKey}");

    public static bool TryParse(string? text, out HotKey hotKey)
    {
        hotKey = default;
        string[] parts = (text ?? string.Empty).Split(',');
        if (parts.Length != 2 ||
            !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint modifiers) ||
            !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint key))
        {
            return false;
        }

        HotKey candidate = new(modifiers, key);
        if (!candidate.IsValid)
        {
            return false;
        }

        hotKey = candidate;
        return true;
    }

    public static bool IsModifierKey(uint key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
}
