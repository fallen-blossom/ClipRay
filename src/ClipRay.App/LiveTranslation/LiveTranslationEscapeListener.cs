using System.Runtime.InteropServices;
using ClipRay.App.Overlay;

namespace ClipRay.App.LiveTranslation;

// Observe Escape only during a live session. Single Escape remains available to the target app.
public sealed class LiveTranslationEscapeListener : IDisposable
{
    private readonly HookProc _callback;
    private readonly DoubleEscapePolicy _policy = new();
    private readonly Action _stop;
    private readonly Action _hint;
    private IntPtr _hook;
    private bool _pressed;

    public LiveTranslationEscapeListener(Action stop, Action hint)
    {
        _stop = stop;
        _hint = hint;
        _callback = OnKeyboard;
        _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("live-escape-hook-unavailable");
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == 0x1B)
        {
            if (message.ToInt64() is 0x101 or 0x105) _pressed = false;
            else if (message.ToInt64() is 0x100 or 0x104 && !_pressed)
            {
                _pressed = true;
                if (_policy.Press(DateTimeOffset.UtcNow) == DoubleEscapeResult.Close)
                {
                    _stop();
                    return new IntPtr(1);
                }
                _hint();
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.KeepAlive(_callback);
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? module);
}
