using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using ClipRay.App.Diagnostics;

namespace ClipRay.App.LiveTranslation;

// Local frame data with optional image artifacts; capture affinity stays enabled.
internal sealed class LiveTranslationAcceptanceTrace : IDisposable
{
    internal const string SchemaVersion = "clipray.live-evidence.v1";
    internal const string DataSchemaVersion = "clipray.live-evidence.v2-data";
    private readonly bool _dataOnly;
    private readonly bool _includeContent;
    private readonly LocalDiagnosticSession? _store;
    private string ActiveSchema => _dataOnly ? DataSchemaVersion : SchemaVersion;
    private static readonly object FailureGate = new();
    private static readonly Dictionary<string,LocalDiagnosticSession> FailureStores=new(StringComparer.OrdinalIgnoreCase);
    public static void RecordIntegrityFailure(string source, string restored,
        ClipRay.App.Translation.TranslationInvariantMismatch mismatch)
        => RecordMachineFailure(source, restored, "machine-content-integrity-failed", mismatch.ToString());

    public static void RecordProtectedFailure(string source, string restored)
        => RecordMachineFailure(source, restored, "machine-protected-content-failed", "ProtectedContentChanged");

    private static void RecordMachineFailure(string source, string restored, string reason, string mismatch)
    {
        var root = Environment.GetEnvironmentVariable("CLIPRAY_LIVE_ACCEPTANCE_ROOT");
        var identity = LiveTranslationWorkContext.Current;
        var evidence = new {
            utc = DateTimeOffset.UtcNow,
            capture_id = identity?.CaptureId,
            pipeline_id = identity?.PipelineId,
            viewport_generation = identity?.ViewportGeneration,
            source,
            source_hash = Hash(source),
            source_number_count = ClipRay.App.Translation.TranslationContentIntegrity.CountNumbers(source),
            restored,
            restored_hash = Hash(restored),
            restored_number_count = ClipRay.App.Translation.TranslationContentIntegrity.CountNumbers(restored),
            reason,
            mismatch
        };
        ScreenshotTranslationEvidence.Current?.Record("machine_failure", evidence);
        if (!ScreenshotTranslationEvidence.TryResolveLocalRoot(root, out var localRoot)) return;
        try
        {
            lock (FailureGate)
            {
                if(!FailureStores.TryGetValue(localRoot,out var store)) FailureStores[localRoot]=store=new(localRoot);
                store.Json(Path.Combine(localRoot, "integrity-failures.jsonl"),evidence,
                    includeContent: string.Equals(Environment.GetEnvironmentVariable("CLIPRAY_LIVE_EVIDENCE_MODE"),"content",StringComparison.OrdinalIgnoreCase));
            }
        }
        catch { }

        static string Hash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private readonly string? _root;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private int _imageIndex;
    private int _sourceIndex;
    private int _sequence;
    private int _writeFailures;
    private int _contextCount;
    private int _missingIdentityEvents;
    private bool _disposed;
    private bool _contextRecorded;
    private bool _terminalRecorded;
    private readonly HashSet<FrameKey> _appliedFrames = [];
    private readonly HashSet<FrameKey> _renderedFrames = [];
    private readonly HashSet<FrameKey> _observedFrames = [];
    private readonly HashSet<FrameKey> _unobservedFrames = [];
    private SourceKey? _firstSource;
    private SourceKey? _firstObservedSource;

    private readonly record struct SourceKey(long CaptureId, long PipelineId,
        long ViewportGeneration);
    private readonly record struct FrameKey(long CaptureId, long PipelineId,
        long ViewportGeneration, long Generation);

    public bool Enabled => _root is not null;
    internal string? SessionDirectory => _root;
    internal int WriteFailures => _writeFailures;

    internal static LiveTranslationAcceptanceTrace ForRuntime(string diagnosticsRoot)
    {
        var root = Environment.GetEnvironmentVariable("CLIPRAY_LIVE_ACCEPTANCE_ROOT");
        var images = string.Equals(Environment.GetEnvironmentVariable("CLIPRAY_LIVE_EVIDENCE_MODE"),
            "images", StringComparison.OrdinalIgnoreCase);
        return new(root ?? Path.Combine(diagnosticsRoot, "live"), dataOnlyOverride: !images);
    }

    public void SaveSource(System.Drawing.Bitmap source, LiveTranslationWorkIdentity? identity = null)
    {
        if(_root is null) return;
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                Directory.CreateDirectory(_root);
                identity ??= LiveTranslationWorkContext.Current;
                if (_dataOnly)
                {
                    _sourceIndex++;
                    WriteEvent("source_frame_fingerprint", null, identity, PixelFingerprint.Create(source));
                }
                else
                {
                var suffix = identity is null ? string.Empty
                    : $"-c{identity.CaptureId}-p{identity.PipelineId}-v{identity.ViewportGeneration}";
                var name=$"source-{++_sourceIndex}{suffix}.png";
                var path = Path.Combine(_root,name);
                if (!_store!.Image(path,stream=>source.Save(stream,System.Drawing.Imaging.ImageFormat.Png))) throw new IOException("Diagnostic image budget.");
                WriteEvent("source_image_saved", null, identity, new {
                    name,
                    sha256 = HashFile(path),
                    width = source.Width,
                    height = source.Height
                });
                }
                if (identity is null) _missingIdentityEvents++;
                else _firstSource ??= new SourceKey(identity.CaptureId,
                    identity.PipelineId, identity.ViewportGeneration);
            }
            catch { _writeFailures++; }
        }
    }

    public LiveTranslationAcceptanceTrace(string? rootOverride = null, bool? dataOnlyOverride = null)
    {
        _dataOnly = dataOnlyOverride ?? !string.Equals(
            Environment.GetEnvironmentVariable("CLIPRAY_LIVE_EVIDENCE_MODE"), "images", StringComparison.OrdinalIgnoreCase);
        _includeContent = !_dataOnly || string.Equals(Environment.GetEnvironmentVariable("CLIPRAY_LIVE_EVIDENCE_MODE"),"content",StringComparison.OrdinalIgnoreCase);
        var root = rootOverride ?? Environment.GetEnvironmentVariable("CLIPRAY_LIVE_ACCEPTANCE_ROOT");
        if (ScreenshotTranslationEvidence.TryResolveLocalRoot(root, out var localRoot))
        { _root = Path.Combine(localRoot, Guid.NewGuid().ToString("N")); _store = new(localRoot); }
        Record("started");
    }

    public void Record(string kind, long? generation = null,
        LiveTranslationWorkIdentity? identity = null, object? data = null)
    {
        if (_root is null) return;
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                WriteEvent(kind, generation, identity, data);
                if (kind == "session_context")
                    _contextRecorded = ++_contextCount == 1 && ValidSessionContext(data);
                if (kind is "ended" or "capture_start_failed" or "capture_start_cancelled")
                    _terminalRecorded = true;
                if (kind is "applied" or "render_cycle_observed" or "render_cycle_not_observed")
                {
                    if (TryFrameKey(identity, generation, out var key))
                    {
                        switch (kind)
                        {
                            case "applied": _appliedFrames.Add(key); break;
                            case "render_cycle_observed":
                                if (HasVisibleLayout(data) && _appliedFrames.Contains(key)
                                    && _renderedFrames.Contains(key))
                                {
                                    _observedFrames.Add(key);
                                    _firstObservedSource ??= new SourceKey(key.CaptureId,
                                        key.PipelineId, key.ViewportGeneration);
                                }
                                else _unobservedFrames.Add(key);
                                break;
                            case "render_cycle_not_observed": _unobservedFrames.Add(key); break;
                        }
                    }
                    else _missingIdentityEvents++;
                }
            }
            catch { _writeFailures++; }
        }
    }

    private static bool ValidSessionContext(object? data)
    {
        if (data is null) return false;
        try
        {
            var context = JsonSerializer.SerializeToElement(data);
            if (context.ValueKind != JsonValueKind.Object
                || !Nonempty("AppVersion") || !Nonempty("Route") || !Nonempty("TargetLanguage")
                || !context.TryGetProperty("ExecutableSha256", out var hash)
                || hash.ValueKind != JsonValueKind.String
                || hash.GetString() is not { Length: 64 } digest
                || !digest.All(Uri.IsHexDigit)
                || !context.TryGetProperty("ShowInRecordings", out var recording)
                || recording.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !Rectangle("Region", out var region)
                || !Rectangle("Monitor", out var monitor)) return false;
            return region.X >= monitor.X && region.Y >= monitor.Y
                && region.Right <= monitor.Right && region.Bottom <= monitor.Bottom;

            bool Nonempty(string field) => context.TryGetProperty(field, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString());
            bool Rectangle(string field, out System.Drawing.Rectangle bounds)
            {
                bounds = default;
                if (!context.TryGetProperty(field, out var value)
                    || value.ValueKind != JsonValueKind.Object
                    || !Number(value, "X", out var x) || !Number(value, "Y", out var y)
                    || !Number(value, "Width", out var width)
                    || !Number(value, "Height", out var height)
                    || width <= 0 || height <= 0) return false;
                bounds = new(x, y, width, height);
                return true;
            }
            static bool Number(JsonElement value, string field, out int number)
            {
                number = 0;
                return value.TryGetProperty(field, out var part)
                    && part.ValueKind == JsonValueKind.Number && part.TryGetInt32(out number);
            }
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    public void SaveRendered(BitmapSource? image, long generation,
        LiveTranslationWorkIdentity? identity = null)
    {
        if (_root is null || image is null) return;
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                if (_dataOnly)
                {
                    _imageIndex++;
                    WriteEvent("rendered_frame_fingerprint", generation, identity, PixelFingerprint.Create(image));
                }
                else
                {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                var name = $"rendered-{++_imageIndex}-{generation}.png";
                Directory.CreateDirectory(_root);
                var path = Path.Combine(_root, name);
                if (!_store!.Image(path,encoder.Save)) throw new IOException("Diagnostic image budget.");
                WriteEvent("rendered_image_saved", generation, identity, new {
                    name,
                    sha256 = HashFile(path),
                    width = image.PixelWidth,
                    height = image.PixelHeight
                });
                }
                if (TryFrameKey(identity, generation, out var key)) _renderedFrames.Add(key);
                else _missingIdentityEvents++;
            }
            catch { _writeFailures++; }
        }
    }

    private void WriteEvent(string kind, long? generation,
        LiveTranslationWorkIdentity? identity, object? data)
    {
        if (_root is null || _disposed) return;
        Directory.CreateDirectory(_root);
        var nextSequence = _sequence + 1;
        if (!_store!.Json(Path.Combine(_root, "events.jsonl"), new {
                schema_version = ActiveSchema,
                sequence = nextSequence,
                utc = DateTimeOffset.UtcNow,
                kind,
                generation,
                capture_id = identity?.CaptureId,
                pipeline_id = identity?.PipelineId,
                viewport_generation = identity?.ViewportGeneration,
                elapsed_ms = _clock.ElapsedMilliseconds,
                data
            },includeContent:_includeContent)) throw new IOException("Diagnostic event budget.");
        _sequence = nextSequence;
    }

    public void Dispose()
    {
        if (_root is null) return;
        lock (_gate)
        {
            if (_disposed) return;
            try { WriteEvent("trace_closed", null, null, new { write_failures = _writeFailures }); }
            catch { _writeFailures++; }
            _disposed = true;
            try
            {
                var missingRendered = _appliedFrames.Count(key => !_renderedFrames.Contains(key));
                var missingRenderCycle = _appliedFrames.Count(key => !_observedFrames.Contains(key));
                var unpairedRendered = _renderedFrames.Count(key => !_appliedFrames.Contains(key));
                var unpairedRenderCycle = _observedFrames.Count(key => !_appliedFrames.Contains(key));
                var firstSourceRendered = _firstSource is { } firstSource
                    && _appliedFrames.Any(key => key.CaptureId == firstSource.CaptureId
                        && key.PipelineId == firstSource.PipelineId
                        && key.ViewportGeneration == firstSource.ViewportGeneration
                        && _renderedFrames.Contains(key) && _observedFrames.Contains(key));
                var firstSourceWasFirstRender = firstSourceRendered
                    && _firstObservedSource == _firstSource;
                Directory.CreateDirectory(_root);
                if (!_store!.Json(Path.Combine(_root, "manifest.json"), new {
                        schema_version = ActiveSchema,
                        evidence_mode = _dataOnly ? "data" : "images",
                        diagnostic_truncated = _store.Truncated,
                        recording_complete = !_store.Truncated && _writeFailures == 0 && _missingIdentityEvents == 0
                            && _contextRecorded && _terminalRecorded,
                        complete = !_store.Truncated && _writeFailures == 0 && _missingIdentityEvents == 0
                            && _contextRecorded && _terminalRecorded && firstSourceWasFirstRender
                            && missingRendered == 0 && missingRenderCycle == 0
                            && unpairedRendered == 0 && unpairedRenderCycle == 0
                            && _unobservedFrames.Count == 0,
                        event_count = _sequence,
                        write_failures = _writeFailures,
                        missing_identity_event_count = _missingIdentityEvents,
                        has_context = _contextRecorded,
                        has_terminal = _terminalRecorded,
                        source_frame_count = _sourceIndex,
                        first_source_rendered = firstSourceRendered,
                        first_source_was_first_render = firstSourceWasFirstRender,
                        applied_frame_count = _appliedFrames.Count,
                        rendered_frame_count = _renderedFrames.Count,
                        observed_frame_count = _observedFrames.Count,
                        missing_rendered_frame_count = missingRendered,
                        missing_render_cycle_count = missingRenderCycle,
                        unpaired_rendered_frame_count = unpairedRendered,
                        unpaired_render_cycle_count = unpairedRenderCycle,
                        failed_render_cycle_count = _unobservedFrames.Count,
                        ended_utc = DateTimeOffset.UtcNow,
                        events_sha256 = HashFile(Path.Combine(_root, "events.jsonl"))
                    },append:false)) throw new IOException("Diagnostic manifest budget.");
            }
            catch { _writeFailures++; }
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool TryFrameKey(LiveTranslationWorkIdentity? identity,
        long? generation, out FrameKey key)
    {
        if (identity is not null && generation is not null)
        {
            key = new FrameKey(identity.CaptureId, identity.PipelineId,
                identity.ViewportGeneration, generation.Value);
            return true;
        }
        key = default;
        return false;
    }

    private static bool HasVisibleLayout(object? data)
    {
        if (data is null) return false;
        var value = JsonSerializer.SerializeToElement(data);
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("Visible", out var visible)
            && visible.ValueKind == JsonValueKind.True
            && value.TryGetProperty("Layouts", out var layouts)
            && layouts.ValueKind == JsonValueKind.Number
            && layouts.TryGetInt32(out var count) && count > 0;
    }
}
