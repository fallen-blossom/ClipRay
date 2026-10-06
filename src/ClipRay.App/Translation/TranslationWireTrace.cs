using System.Threading;

namespace ClipRay.App.Translation;

// The opt-in local acceptance trace may inspect provider payloads without
// making production translation depend on evidence storage.
internal static class TranslationWireTrace
{
    private static readonly AsyncLocal<Action<string, object?>?> Sink = new();

    public static IDisposable Push(Action<string, object?> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var previous = Sink.Value;
        Sink.Value = write;
        return new Restore(previous);
    }

    public static void Record(string kind, object? data)
    {
        try { Sink.Value?.Invoke(kind, data); }
        catch { /* Evidence must not affect provider calls. */ }
    }

    private sealed class Restore(Action<string, object?>? previous) : IDisposable
    {
        public void Dispose() => Sink.Value = previous;
    }
}
