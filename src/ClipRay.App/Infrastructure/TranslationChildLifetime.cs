using System.Diagnostics;

namespace ClipRay.App.Infrastructure;

internal sealed class TranslationChildRetirementException(TranslationChildLifetime owner, Exception inner)
    : Exception("translation-child-retirement-failed", inner)
{
    // A failed exit observation must keep the exact physical owner reachable.
    internal TranslationChildLifetime Owner { get; } = owner;
}

// This owns one exact child handle and its transport. A stop request is not exit.
// Caller timeouts never cancel physical retirement or release the handle early.
internal sealed class TranslationChildLifetime(Action requestStop, Func<Task> waitForExit,
    Task transportDrain, Action releaseHandle)
{
    private readonly object _gate = new();
    private Task? _retirement;
    internal Exception? StopFailure { get; private set; }

    internal static Process Start(ProcessStartInfo info, Func<Exception> startFailure)
    {
        var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw startFailure();
            return process;
        }
        catch { process.Dispose(); throw; }
    }

    internal static TranslationChildLifetime Own(Process process, Task transportDrain) => new(
        () =>
        {
            if (process.HasExited) return;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
        },
        () => process.WaitForExitAsync(CancellationToken.None), transportDrain, process.Dispose);

    internal Task RetireAsync()
    {
        lock (_gate) return _retirement ??= Task.Run(RetireCoreAsync);
    }

    private async Task RetireCoreAsync()
    {
        try { await ReleaseAsync().ConfigureAwait(false); }
        catch (Exception error) { throw new TranslationChildRetirementException(this, error); }
    }

    private async Task ReleaseAsync()
    {
        try { requestStop(); }
        catch (Exception error) { StopFailure = error; }
        // Even a failed stop request retains the original owner until exit is
        // confirmed. Its failure is retained and cannot become successful drain.
        await waitForExit().ConfigureAwait(false);
        var failures = new List<Exception>();
        if (StopFailure is { } stopFailure) failures.Add(stopFailure);
        try { await transportDrain.ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        try { releaseHandle(); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException("translation-child-retirement-failed", failures);
    }
}
