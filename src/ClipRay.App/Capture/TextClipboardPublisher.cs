using System.Runtime.InteropServices;

namespace ClipRay.App.Capture;

public enum TextCopyResult { Copied, Busy, Failed, Empty }

public static class TextClipboardPublisher
{
    public static TextCopyResult TryCopy(string text, Action<string>? publish = null, Action<TimeSpan>? delay = null)
    {
        if (string.IsNullOrEmpty(text)) return TextCopyResult.Empty;
        // Plain text keeps the user's Windows clipboard history/sync preferences unchanged.
        publish ??= System.Windows.Clipboard.SetText;
        delay ??= Thread.Sleep;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { publish(text); return TextCopyResult.Copied; }
            catch (ExternalException)
            {
                if (attempt == 2) return TextCopyResult.Busy;
                delay(TimeSpan.FromMilliseconds(25));
            }
            catch { return TextCopyResult.Failed; }
        }
        return TextCopyResult.Busy;
    }
}
