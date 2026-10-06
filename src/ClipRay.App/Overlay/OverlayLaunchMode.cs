namespace ClipRay.App.Overlay;

using System.Windows.Input;
using ClipRay.App.Infrastructure;
using ClipRay.App.Settings;

public enum OverlayLaunchMode
{
    Capture,
    Recording,
}

public static class OverlayLaunchInputPolicy
{
    public static bool IsConfirm(
        OverlayLaunchMode launchMode,
        GlobalHotkey configuredPrimary,
        GlobalHotkey? configuredSecondary,
        Key key,
        ModifierKeys modifiers) =>
        launchMode == OverlayLaunchMode.Recording && key == Key.Enter && modifiers == ModifierKeys.None
        || HotkeyCapturePolicy.MatchesAny(configuredPrimary, configuredSecondary, key, modifiers);

    public static bool IsImmediateCancel(OverlayLaunchMode launchMode, Key key, ModifierKeys modifiers) =>
        launchMode == OverlayLaunchMode.Recording
        && key == Key.Escape
        && modifiers == ModifierKeys.None;
}
