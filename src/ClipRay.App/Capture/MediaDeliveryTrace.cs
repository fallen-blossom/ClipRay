using System.IO;
using System.Text.Json;
using ClipRay.App.Infrastructure;
using ClipRay.App.Diagnostics;

namespace ClipRay.App.Capture;

/// <summary>Local delivery metadata only; never reads clipboard history or media contents.</summary>
internal static class MediaDeliveryTrace
{
    private static readonly object Gate = new();
    private static int _writeFailures;
    internal static int WriteFailures => Volatile.Read(ref _writeFailures);
    internal static Action<string,string?,object?>? SinkOverride { get; set; }
    internal static string? RootOverride { get; set; }
    private static LocalDiagnosticSession? _session;
    private static string? _sessionRoot;

    internal static void Record(string operation, string? artifactId, object? detail)
    {
        try
        {
            if (SinkOverride is { } sink) { sink(operation,artifactId,detail); return; }
            var paths = ApplicationPaths.Create(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            lock (Gate)
            {
                var root=RootOverride??paths.DiagnosticsRoot;
                if (_sessionRoot!=root) { _sessionRoot=root;_session=new(root); }
                var version = typeof(MediaDeliveryTrace).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
                if (!_session!.Json(Path.Combine(root, $"media-delivery-{DateTime.UtcNow:yyyy-MM-dd}.jsonl"),new { SchemaVersion = 1, Utc = DateTimeOffset.UtcNow,
                    Pid = Environment.ProcessId, BinaryVersion = version, Operation = operation, ArtifactId = artifactId, Detail = detail }))
                    Interlocked.Increment(ref _writeFailures);
            }
        }
        catch { Interlocked.Increment(ref _writeFailures); }
    }
}
