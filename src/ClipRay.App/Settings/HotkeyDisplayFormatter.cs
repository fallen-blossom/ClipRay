using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using ClipRay.App.Infrastructure;

namespace ClipRay.App.Settings;

public interface IHotkeyKeyLabelResolver
{
    string? Resolve(Key key, bool shift);
}

public static class HotkeyDisplayFormatter
{
    private static readonly IHotkeyKeyLabelResolver WindowsResolver = new WindowsHotkeyKeyLabelResolver();

    public static string Format(GlobalHotkey hotkey) => Format(hotkey, WindowsResolver);

    public static string Format(GlobalHotkey hotkey, IHotkeyKeyLabelResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(resolver);
        if (!Enum.TryParse<Key>(hotkey.Key, ignoreCase: true, out var key) || key == Key.None)
        {
            throw new FormatException($"无法识别的按键: {hotkey.Key}");
        }

        var parts = new List<string>(5);
        if (hotkey.Control) parts.Add("Ctrl");
        if (hotkey.Shift) parts.Add("Shift");
        if (hotkey.Alt) parts.Add("Alt");
        if (hotkey.Windows) parts.Add("Win");
        parts.Add(FormatActionKey(key, hotkey.Shift, resolver));
        return string.Join('+', parts);
    }

    private static string FormatActionKey(Key key, bool shift, IHotkeyKeyLabelResolver resolver)
    {
        if (key is >= Key.A and <= Key.Z) return key.ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return $"Num {(int)key - (int)Key.NumPad0}";
        }

        var fixedLabel = key switch
        {
            Key.Add => "Num +",
            Key.Subtract => "Num -",
            Key.Multiply => "Num *",
            Key.Divide => "Num /",
            Key.Decimal => $"Num {resolver.Resolve(key, shift: false) ?? "."}",
            Key.Separator => $"Num {resolver.Resolve(key, shift: false) ?? ","}",
            Key.Back => "Backspace",
            Key.Tab => "Tab",
            Key.Enter => "Enter",
            Key.Escape => "Esc",
            Key.Space => "Space",
            Key.PageUp => "Page Up",
            Key.PageDown => "Page Down",
            Key.End => "End",
            Key.Home => "Home",
            Key.Left => "←",
            Key.Up => "↑",
            Key.Right => "→",
            Key.Down => "↓",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.CapsLock => "Caps Lock",
            Key.NumLock => "Num Lock",
            Key.Scroll => "Scroll Lock",
            Key.PrintScreen => "Print Screen",
            Key.Pause => "Pause",
            Key.Apps => "Menu",
            _ => null,
        };
        if (fixedLabel is not null) return fixedLabel;

        var resolved = resolver.Resolve(key, shift);
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString(CultureInfo.InvariantCulture);
        }

        return key.ToString().StartsWith("Oem", StringComparison.OrdinalIgnoreCase)
            ? $"键 0x{KeyInterop.VirtualKeyFromKey(key):X2}"
            : key.ToString();
    }
}

public sealed class WindowsHotkeyKeyLabelResolver : IHotkeyKeyLabelResolver
{
    private const uint MapVirtualKeyToScanCodeEx = 4;
    private const uint PreserveKeyboardState = 4;
    private const int VirtualKeyShift = 0x10;

    public string? Resolve(Key key, bool shift)
    {
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0) return null;

        var layout = GetKeyboardLayout(0);
        var scanCode = MapVirtualKeyEx(virtualKey, MapVirtualKeyToScanCodeEx, layout);
        var keyboardState = new byte[256];
        if (shift) keyboardState[VirtualKeyShift] = 0x80;
        var buffer = new StringBuilder(8);

        // 查询显示文字不能消耗 dead key；Windows 10 1607+ 的 bit 2 保持内核键盘状态不变。
        var count = ToUnicodeEx(
            virtualKey,
            scanCode,
            keyboardState,
            buffer,
            buffer.Capacity,
            PreserveKeyboardState,
            layout);
        if (count != 0)
        {
            var length = Math.Min(Math.Abs(count), buffer.Capacity);
            var value = buffer.ToString(0, length);
            if (value.Length > 0 && value.All(static character =>
                    !char.IsControl(character) && !char.IsWhiteSpace(character)))
            {
                return value;
            }
        }

        if (scanCode == 0) return null;
        var keyData = (int)(scanCode & 0xFF) << 16;
        if ((scanCode & 0xFF00) != 0) keyData |= 1 << 24;
        buffer.Clear();
        var nameLength = GetKeyNameText(keyData, buffer, buffer.Capacity);
        if (nameLength <= 0) return null;
        var name = buffer.ToString(0, nameLength);
        return name.Contains("OEM", StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint keyboardLayout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(
        uint virtualKey,
        uint scanCode,
        [In] byte[] keyboardState,
        [Out] StringBuilder buffer,
        int bufferSize,
        uint flags,
        nint keyboardLayout);

    [DllImport("user32.dll", EntryPoint = "GetKeyNameTextW", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int keyData, [Out] StringBuilder buffer, int bufferSize);
}
