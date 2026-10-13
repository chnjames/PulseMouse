namespace PulseMouse.Core;

/// <summary>A keyboard shortcut such as "Ctrl+Shift+T": modifier virtual-key codes plus one main key.</summary>
public sealed record KeyChord(IReadOnlyList<ushort> Modifiers, ushort Key)
{
    public const ushort Ctrl = 0x11, Shift = 0x10, Alt = 0x12, Win = 0x5B;

    private static readonly (ushort Vk, string Name)[] ModifierNames = [(Ctrl, "Ctrl"), (Shift, "Shift"), (Alt, "Alt"), (Win, "Win")];

    private static readonly Dictionary<string, ushort> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Control"] = Ctrl, ["Windows"] = Win, ["Meta"] = Win, ["Escape"] = 0x1B, ["Return"] = 0x0D,
        ["Del"] = 0x2E, ["Ins"] = 0x2D, ["PgUp"] = 0x21, ["PgDn"] = 0x22, ["PrtSc"] = 0x2C,
    };

    /// <summary>Canonical names; also the parse table (Aliases add alternative spellings).</summary>
    private static readonly Dictionary<ushort, string> KeyNames = BuildKeyNames();

    private static Dictionary<ushort, string> BuildKeyNames()
    {
        var names = new Dictionary<ushort, string>
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x1B] = "Esc", [0x20] = "Space",
            [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete",
            [0xAD] = "VolumeMute", [0xAE] = "VolumeDown", [0xAF] = "VolumeUp",
            [0xB0] = "MediaNext", [0xB1] = "MediaPrev", [0xB2] = "MediaStop", [0xB3] = "MediaPlayPause",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
            [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
        };
        for (ushort c = 'A'; c <= 'Z'; c++) names[c] = ((char)c).ToString();
        for (ushort c = '0'; c <= '9'; c++) names[c] = ((char)c).ToString();
        for (int i = 1; i <= 24; i++) names[(ushort)(0x6F + i)] = "F" + i;
        return names;
    }

    public static bool IsModifier(ushort vk) => vk is Ctrl or Shift or Alt or Win or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5C;

    /// <summary>Main keys PulseMouse can name; others are rejected rather than sent blindly.</summary>
    public static bool IsNamedKey(ushort vk) => KeyNames.ContainsKey(vk);

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = new KeyChord([], 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        // "+" is both the separator and a key name ("Ctrl++"), so split by hand.
        var parts = new List<string>();
        var current = "";
        foreach (char ch in text.Trim())
        {
            if (ch == '+' && current.Length > 0) { parts.Add(current.Trim()); current = ""; }
            else current += ch;
        }
        if (current.Trim().Length > 0) parts.Add(current.Trim());
        if (parts.Count == 0) return false;

        var modifiers = new List<ushort>();
        for (int i = 0; i < parts.Count - 1; i++)
        {
            ushort vk = ModifierNames.FirstOrDefault(m => m.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase)).Vk;
            if (vk == 0 && Aliases.TryGetValue(parts[i], out var alias) && alias is Ctrl or Win) vk = alias;
            if (vk == 0 || modifiers.Contains(vk)) return false;
            modifiers.Add(vk);
        }
        string last = parts[^1] == "+" ? "=" : parts[^1];
        ushort key = KeyNames.FirstOrDefault(k => k.Value.Equals(last, StringComparison.OrdinalIgnoreCase)).Key;
        if (key == 0 && Aliases.TryGetValue(last, out var keyAlias) && !IsModifier(keyAlias)) key = keyAlias;
        if (key == 0) return false;
        chord = new KeyChord(ModifierNames.Select(m => m.Vk).Where(modifiers.Contains).ToList(), key);
        return true;
    }

    public static string Format(IEnumerable<ushort> modifiers, ushort key)
    {
        var set = modifiers.ToHashSet();
        var names = ModifierNames.Where(m => set.Contains(m.Vk)).Select(m => m.Name).ToList();
        names.Add(KeyNames.TryGetValue(key, out var name) ? name : $"0x{key:X2}");
        return string.Join("+", names);
    }

    public override string ToString() => Format(Modifiers, Key);
}
