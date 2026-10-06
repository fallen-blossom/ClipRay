using ClipRay.App.Infrastructure;

namespace ClipRay.App.Settings;

public static class HotkeyBindingPolicy
{
    public static IReadOnlyList<GlobalHotkey> Active(GlobalHotkey primary, GlobalHotkey? secondary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        return secondary is null ? [primary] : [primary, secondary];
    }

    public static string FormatForDisplay(GlobalHotkey primary, GlobalHotkey? secondary) => string.Join(
        " 或 ",
        Active(primary, secondary).Select(HotkeyDisplayFormatter.Format));
}
