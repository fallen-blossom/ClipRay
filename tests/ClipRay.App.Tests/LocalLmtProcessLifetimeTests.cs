using System.Diagnostics;
using System.Globalization;
using System.IO;
using ClipRay.App.Translation;

internal static class LocalLmtProcessLifetimeTests
{
    public static int SleepAsChild(string[] args)
    {
        File.WriteAllText(args[1], Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Thread.Sleep(TimeSpan.FromSeconds(30));
        return 0;
    }

    public static async Task CancellingOneShotStopsItsChild()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"clipray-lmt-child-{Guid.NewGuid():N}.pid");
        var executable = Environment.ProcessPath ?? throw new Exception("Test process path unavailable.");
        var start = new LocalLmtSessionStart(executable, $"--local-lmt-sleep-child \"{pidFile}\"");
        using var cancellation = new CancellationTokenSource();
        var run = new LocalLmtOneShotRunner().TranslateAsync(start, "fixture prompt", cancellation.Token);
        Process? child = null;
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(pidFile) && deadline.Elapsed < TimeSpan.FromSeconds(8))
                await Task.Delay(40);
            if (!File.Exists(pidFile)) throw new Exception("One-shot child did not start.");
            child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture));
            cancellation.Cancel();
            try { await run; throw new Exception("One-shot cancellation did not propagate."); }
            catch (OperationCanceledException) { }
            if (!child.WaitForExit(3000)) throw new Exception("Cancelled one-shot child is still running.");
        }
        finally
        {
            cancellation.Cancel();
            if (child is { HasExited: false })
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit(3000);
            }
            child?.Dispose();
            if (File.Exists(pidFile)) File.Delete(pidFile);
        }
    }
}
