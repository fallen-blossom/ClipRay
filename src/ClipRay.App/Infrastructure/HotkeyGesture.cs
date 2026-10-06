using System.Windows.Input;

namespace ClipRay.App.Infrastructure;

public sealed record GlobalHotkey
{
    public bool Control { get; init; }

    public bool Shift { get; init; }

    public bool Alt { get; init; }

    public bool Windows { get; init; }

    public string Key { get; init; } = string.Empty;

    public static GlobalHotkey Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new FormatException("快捷键不能为空。");
        }

        var hotkey = new GlobalHotkey { Key = NormalizeKey(parts[^1]) };
        foreach (var modifier in parts[..^1])
        {
            hotkey = modifier.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" when !hotkey.Control => hotkey with { Control = true },
                "SHIFT" when !hotkey.Shift => hotkey with { Shift = true },
                "ALT" when !hotkey.Alt => hotkey with { Alt = true },
                "WIN" or "WINDOWS" when !hotkey.Windows => hotkey with { Windows = true },
                _ => throw new FormatException($"无法识别或重复的修饰键: {modifier}"),
            };
        }

        return hotkey;
    }

    public int ToVirtualKey()
    {
        if (!Enum.TryParse<Key>(Key, ignoreCase: true, out var key) || !IsRegisterableKey(key))
        {
            throw new FormatException($"无法识别的按键: {Key}");
        }

        return KeyInterop.VirtualKeyFromKey(key);
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Control) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Windows) parts.Add("Win");
        parts.Add(string.Equals(Key, "Return", StringComparison.OrdinalIgnoreCase) ? "Enter" : Key);
        return string.Join('+', parts);
    }

    private static string NormalizeKey(string text)
    {
        if (!Enum.TryParse<Key>(text, ignoreCase: true, out var key) || !IsRegisterableKey(key))
        {
            throw new FormatException($"无法识别的按键: {text}");
        }

        return key == System.Windows.Input.Key.Return ? "Enter" : key.ToString();
    }

    private static bool IsRegisterableKey(System.Windows.Input.Key key) =>
        key is not (System.Windows.Input.Key.None
            or System.Windows.Input.Key.System
            or System.Windows.Input.Key.ImeProcessed
            or System.Windows.Input.Key.DeadCharProcessed
            or System.Windows.Input.Key.LeftCtrl
            or System.Windows.Input.Key.RightCtrl
            or System.Windows.Input.Key.LeftShift
            or System.Windows.Input.Key.RightShift
            or System.Windows.Input.Key.LeftAlt
            or System.Windows.Input.Key.RightAlt
            or System.Windows.Input.Key.LWin
            or System.Windows.Input.Key.RWin)
        && KeyInterop.VirtualKeyFromKey(key) != 0;
}
