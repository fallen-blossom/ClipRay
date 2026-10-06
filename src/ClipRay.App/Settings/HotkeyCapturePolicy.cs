using System.Runtime.InteropServices;
using System.Windows.Input;
using ClipRay.App.Infrastructure;

namespace ClipRay.App.Settings;

public static class HotkeyCapturePolicy
{
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;

    public static ModifierKeys ReadCurrentModifiers() => ResolveModifiers(
        Keyboard.Modifiers,
        IsKeyDown(VkLeftWindows),
        IsKeyDown(VkRightWindows));

    public static ModifierKeys ResolveModifiers(
        ModifierKeys observedModifiers,
        bool leftWindowsDown,
        bool rightWindowsDown) =>
        leftWindowsDown || rightWindowsDown
            ? observedModifiers | ModifierKeys.Windows
            : observedModifiers;

    public static Key ResolveEventKey(
        Key reportedKey,
        Key systemKey,
        Key imeProcessedKey,
        Key deadCharProcessedKey) => reportedKey switch
    {
        Key.System => systemKey,
        Key.ImeProcessed => imeProcessedKey,
        Key.DeadCharProcessed => deadCharProcessedKey,
        _ => reportedKey,
    };

    public static GlobalHotkey Capture(Key key, ModifierKeys modifiers)
    {
        if (!IsRegisterableKey(key))
        {
            throw new ArgumentException("请同时按下一个非修饰键。");
        }

        var hotkey = new GlobalHotkey
        {
            Control = modifiers.HasFlag(ModifierKeys.Control),
            Shift = modifiers.HasFlag(ModifierKeys.Shift),
            Alt = modifiers.HasFlag(ModifierKeys.Alt),
            Windows = modifiers.HasFlag(ModifierKeys.Windows),
            Key = key == System.Windows.Input.Key.Return ? "Enter" : key.ToString(),
        };
        if (IsReservedForEditing(hotkey))
        {
            throw new ArgumentException("Ctrl+Z 和 Ctrl+Shift+Z 已固定用于覆盖层撤回与重做，请改用其他快捷键。");
        }
        return hotkey;
    }

    public static bool IsReservedForEditing(GlobalHotkey hotkey) =>
        hotkey.Control && hotkey.Key.Equals("Z", StringComparison.OrdinalIgnoreCase)
        && !hotkey.Alt && !hotkey.Windows;

    public static bool Matches(GlobalHotkey hotkey, Key key, ModifierKeys modifiers)
    {
        if (!Enum.TryParse<Key>(hotkey.Key, ignoreCase: true, out var configured)) return false;
        return configured == key
            && hotkey.Control == modifiers.HasFlag(ModifierKeys.Control)
            && hotkey.Shift == modifiers.HasFlag(ModifierKeys.Shift)
            && hotkey.Alt == modifiers.HasFlag(ModifierKeys.Alt)
            && hotkey.Windows == modifiers.HasFlag(ModifierKeys.Windows);
    }

    public static bool MatchesAny(
        GlobalHotkey primary,
        GlobalHotkey? secondary,
        Key key,
        ModifierKeys modifiers) =>
        Matches(primary, key, modifiers)
        || secondary is not null && Matches(secondary, key, modifiers);

    private static bool IsRegisterableKey(Key key) =>
        key is not (Key.None
            or Key.System
            or Key.ImeProcessed
            or Key.DeadCharProcessed
            or Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftShift
            or Key.RightShift
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LWin
            or Key.RWin)
        && KeyInterop.VirtualKeyFromKey(key) != 0;

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
