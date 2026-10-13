using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ClipRay.App.Infrastructure;

public interface IGlobalHotkeyHost : IDisposable
{
    bool TryRegister(GlobalHotkey hotkey, Action action);
    bool TryUnregister(GlobalHotkey hotkey);
}

public sealed class HotkeyHost : IGlobalHotkeyHost
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWindows = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private readonly Dictionary<GlobalHotkey, int> _bindings = [];
    private int _nextId = 1;

    public HotkeyHost()
    {
        var parameters = new HwndSourceParameters("ClipRay.Hotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3),
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WindowHook);
    }

    public bool TryRegister(GlobalHotkey hotkey, Action action)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(action);
        var modifiers = ModNoRepeat;
        if (hotkey.Control) modifiers |= ModControl;
        if (hotkey.Shift) modifiers |= ModShift;
        if (hotkey.Alt) modifiers |= ModAlt;
        if (hotkey.Windows) modifiers |= ModWindows;

        var id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, modifiers, checked((uint)hotkey.ToVirtualKey())))
        {
            return false;
        }

        _actions[id] = action;
        _bindings[hotkey] = id;
        return true;
    }

    public bool TryUnregister(GlobalHotkey hotkey)
    {
        if (!_bindings.TryGetValue(hotkey, out var id)) return true;
        if (!UnregisterHotKey(_source.Handle, id)) return false;
        _actions.Remove(id); _bindings.Remove(hotkey); return true;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _actions.Clear();
        _bindings.Clear();
        _source.RemoveHook(WindowHook);
        _source.Dispose();
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
