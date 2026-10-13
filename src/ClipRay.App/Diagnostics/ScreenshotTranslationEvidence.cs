using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;
using ClipRay.App.Translation;

namespace ClipRay.App.Diagnostics;

/// <summary>
/// A local record of one screenshot translation. The rendered layer is an
/// offscreen reconstruction; only a separate desktop observation can prove visibility.
/// </summary>
internal sealed class ScreenshotTranslationEvidence : IDisposable
{
    internal const string SchemaVersion = "clipray.screenshot-evidence.v1";
    internal const string DataSchemaVersion = "clipray.screenshot-evidence.v2-data";
    private readonly bool _dataOnly;
    private readonly bool _includeContent;
    private readonly LocalDiagnosticSession _store;
    private string ActiveSchema => _dataOnly ? DataSchemaVersion : SchemaVersion;
    private static readonly AsyncLocal<ScreenshotTranslationEvidence?> Active = new();
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string _directory;
    private readonly string _runId;
    private readonly DateTimeOffset _startedUtc;
    private int _sequence;
    private int _providerCallId;
    private int _writeFailures;
    private bool _contextRecorded;
    private bool _sourceSaved;
    private bool _terminalRecorded;
    private bool _overlayDecisionRecorded;
    private bool _translationRequired;
    private bool _translationInputsRecorded;
    private bool _translationOutputsRecorded;
    private bool _renderExpected;
    private bool _renderSaved;
    private bool _disposed;
    private string? _sourceSha256;
    private string? _renderSha256;

    internal static ScreenshotTranslationEvidence? Current => Active.Value;
    internal string DirectoryPath => _directory;
    internal int WriteFailures => _writeFailures;

    private ScreenshotTranslationEvidence(string root, string runId, DateTimeOffset startedUtc, bool dataOnly = false)
    {
        _dataOnly = dataOnly;
        _includeContent = !dataOnly || string.Equals(Environment.GetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_MODE"),"content",StringComparison.OrdinalIgnoreCase);
        _store = new(root);
        if (!Guid.TryParseExact(runId, "N", out _))
            throw new ArgumentException("Evidence run ID must be a GUID in N format.", nameof(runId));
        _directory = Path.Combine(root, runId);
        _runId = runId;
        _startedUtc = startedUtc;
        Record("started", new { run_id = runId, started_utc = startedUtc });
    }

    internal static ScreenshotTranslationEvidence? StartFromEnvironment(
        string runId, DateTimeOffset startedUtc)
    {
        var root = Environment.GetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_ROOT");
        if (!TryResolveLocalRoot(root, out var fullRoot)) return null;
        try { return new ScreenshotTranslationEvidence(fullRoot, runId, startedUtc,
            dataOnly: !string.Equals(Environment.GetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_MODE"),"images",StringComparison.OrdinalIgnoreCase)); }
        catch { return null; }
    }

    internal static ScreenshotTranslationEvidence? StartForRuntime(string diagnosticsRoot,
        string runId, DateTimeOffset startedUtc)
    {
        var root = Environment.GetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_ROOT")
            ?? Path.Combine(diagnosticsRoot, "screenshot");
        if (!TryResolveLocalRoot(root, out var localRoot)) return null;
        var images = string.Equals(Environment.GetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_MODE"),
            "images", StringComparison.OrdinalIgnoreCase);
        try { return new ScreenshotTranslationEvidence(localRoot, runId, startedUtc, dataOnly: !images); }
        catch { return null; }
    }

    // Shared with live evidence so both opt-in traces reject network and redirected roots.
    internal static bool TryResolveLocalRoot(string? root, out string fullRoot)
    {
        fullRoot = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)
                || root.StartsWith(@"\\", StringComparison.Ordinal)
                || root.StartsWith("//", StringComparison.Ordinal))
                return false;
            var resolved = Path.GetFullPath(root);
            var driveRoot = Path.GetPathRoot(resolved);
            if (driveRoot is null || new DriveInfo(driveRoot).DriveType == DriveType.Network)
                return false;
            for (DirectoryInfo? parent = new(resolved); parent is not null; parent = parent.Parent)
            {
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            fullRoot = resolved;
            return true;
        }
        catch { return false; }
    }

    internal static ScreenshotTranslationEvidence StartForTest(
        string root, string runId, DateTimeOffset startedUtc) =>
        new(root, runId, startedUtc);

    internal IDisposable Push()
    {
        var previous = Active.Value;
        Active.Value = this;
        return new Scope(previous);
    }

    internal void RecordContext(object context)
    {
        if (TryRecord("run_context", context)) _contextRecorded = true;
    }

    internal void Record(string kind, object? data = null) => TryRecord(kind, data);

    internal void SaveSource(Bitmap bitmap)
    {
        lock (_gate)
        {
            try
            {
                if (_dataOnly)
                {
                    var fingerprint = PixelFingerprint.Create(bitmap);
                    WriteEvent("source_frame_fingerprint", fingerprint);
                    _sourceSha256 = fingerprint.sha256;
                    _sourceSaved = true;
                    return;
                }
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, "source.png");
                if (!_store.Image(path,stream=>bitmap.Save(stream,ImageFormat.Png))) throw new IOException("Diagnostic image budget.");
                var hash = HashFile(path);
                WriteEvent("source_image_saved", new
                {
                    name = "source.png", sha256 = hash, width = bitmap.Width, height = bitmap.Height
                });
                _sourceSha256 = hash;
                _sourceSaved = true;
            }
            catch { _writeFailures++; }
        }
    }

    internal void RecordOutcome(ApplicationTranslationOutcome outcome)
    {
        if (outcome.Regions.Count > 0) _translationRequired = true;
        if (TryRecordProjected("pipeline_outcome", () => TranslationOutcomeEvidenceProjection.Create(outcome)))
            _terminalRecorded = true;
    }

    internal void RecordTerminalFailure(string category, string? stage)
    {
        if (TryRecord("pipeline_failed", new { category, stage }))
            _terminalRecorded = true;
    }

    internal void RecordOverlayDecision(bool applied, string reason, int layoutCount)
    {
        if (TryRecord(applied ? "overlay_applied" : "overlay_rejected",
            new { reason, layout_count = layoutCount }))
        {
            _overlayDecisionRecorded = true;
            _renderExpected = applied && layoutCount > 0;
        }
    }

    internal void SaveRenderedLayer(Func<BitmapSource> render)
    {
        lock (_gate)
        {
            try
            {
                var image = render();
                if (_dataOnly)
                {
                    var fingerprint = PixelFingerprint.Create(image);
                    WriteEvent("rendered_layer_fingerprint", new { fingerprint.sha256, fingerprint.width,
                        fingerprint.height, fingerprint.pixel_format, fingerprint.byte_count,
                        evidence_kind = "offscreen-layer-preview" });
                    _renderSha256 = fingerprint.sha256;
                    _renderSaved = true;
                    return;
                }
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, "rendered-layer.png");
                if (!_store.Image(path,encoder.Save)) throw new IOException("Diagnostic image budget.");
                var hash = HashFile(path);
                WriteEvent("rendered_layer_saved", new
                {
                    name = "rendered-layer.png", sha256 = hash,
                    width = image.PixelWidth, height = image.PixelHeight,
                    evidence_kind = "offscreen-layer-preview"
                });
                _renderSha256 = hash;
                _renderSaved = true;
            }
            catch { _writeFailures++; }
        }
    }

    internal void RecordTranslationInputs(TranslationRouteKind route,
        IReadOnlyList<TranslationRequest> requests)
    {
        _translationRequired = true;
        if (TryRecordProjected("translation_inputs", () => new
        {
            route = route.ToString(), requests = ProjectRequests(requests)
        })) _translationInputsRecorded = true;
    }

    internal void RecordTranslationOutputs(TranslationBatchOutcome outcome)
    {
        if (TryRecordProjected("translation_outputs", () => new
        {
            trace = outcome.Trace,
            results = ProjectResults(outcome.Results)
        })) _translationOutputsRecorded = true;
    }

    internal async Task<IReadOnlyList<TranslationResult>> ObserveProviderAsync(
        string role, TranslationRouteKind route, IReadOnlyList<TranslationRequest> requests,
        Func<Task<IReadOnlyList<TranslationResult>>> invoke)
    {
        int callId;
        lock (_gate) callId = ++_providerCallId;
        TryRecordProjected("provider_call_started", () => new
        {
            call_id = callId, role, route = route.ToString(), requests = ProjectRequests(requests)
        });
        try
        {
            var results = await invoke().ConfigureAwait(false);
            TryRecordProjected("provider_call_completed", () => new
            {
                call_id = callId, role, results = ProjectResults(results)
            });
            return results;
        }
        catch (Exception exception)
        {
            Record("provider_call_failed", new
            {
                call_id = callId, role, category = exception.GetType().Name
            });
            throw;
        }
    }

    internal IMachineTranslationProvider ObserveMachineProvider(
        IMachineTranslationProvider provider, string role) =>
        new EvidenceMachineProvider(this, provider, role);

    internal void RecordWire(string kind, object? data) => Record("wire_" + kind, data);

    private static object[] ProjectRequests(IReadOnlyList<TranslationRequest> requests) =>
        requests.Select((request, index) => (object)new
        {
            index, request.SourceLanguage, request.TargetLanguage, request.OcrText,
            request.Model, text_kind = request.TextKind.ToString(),
            request.Scene, request.TranslationHint
        }).ToArray();

    private static object[] ProjectResults(IReadOnlyList<TranslationResult> results) =>
        results.Select((result, index) => (object)new
        {
            index, result.IsSuccess, result.Text, result.Status,
            semantic_outcome = result.SemanticOutcome.ToString(),
            result.Diagnostic, result.ChunkDiagnostics, result.ExecutionEvidence
        }).ToArray();

    private bool TryRecord(string kind, object? data)
    {
        lock (_gate)
        {
            try { WriteEvent(kind, data); return true; }
            catch { _writeFailures++; return false; }
        }
    }

    private bool TryRecordProjected(string kind, Func<object?> project)
    {
        try { return TryRecord(kind, project()); }
        catch
        {
            lock (_gate) _writeFailures++;
            return false;
        }
    }

    private void WriteEvent(string kind, object? data)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ScreenshotTranslationEvidence));
        Directory.CreateDirectory(_directory);
        var next = _sequence + 1;
        if (!_store.Json(Path.Combine(_directory, "events.jsonl"), new
            {
                schema_version = ActiveSchema,
                sequence = next,
                utc = DateTimeOffset.UtcNow,
                elapsed_ms = _clock.ElapsedMilliseconds,
                kind,
                data
            },includeContent:_includeContent)) throw new IOException("Diagnostic event budget.");
        _sequence = next;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { WriteEvent("trace_closed", new { write_failures = _writeFailures }); }
            catch { _writeFailures++; }
            _disposed = true;
            try
            {
                var eventsPath = Path.Combine(_directory, "events.jsonl");
                var manifestPath = Path.Combine(_directory, "manifest.json");
                var complete = !_store.Truncated && _writeFailures == 0 && _contextRecorded && _sourceSaved
                    && _terminalRecorded && _overlayDecisionRecorded
                    && (!_translationRequired || (_translationInputsRecorded && _translationOutputsRecorded))
                    && (!_renderExpected || _renderSaved);
                Directory.CreateDirectory(_directory);
                if (!_store.Json(manifestPath, new
                {
                    schema_version = ActiveSchema,
                    evidence_mode = _dataOnly ? "data" : "images",
                    run_id = _runId,
                    started_utc = _startedUtc,
                    ended_utc = DateTimeOffset.UtcNow,
                    complete,
                    diagnostic_truncated = _store.Truncated,
                    write_failures = _writeFailures,
                    event_count = _sequence,
                    events_sha256 = HashFile(eventsPath),
                    source_sha256 = _sourceSha256,
                    rendered_layer_sha256 = _renderSha256,
                    has_context = _contextRecorded,
                    has_source = _sourceSaved,
                    has_terminal = _terminalRecorded,
                    has_overlay_decision = _overlayDecisionRecorded,
                    translation_required = _translationRequired,
                    has_translation_inputs = _translationInputsRecorded,
                    has_translation_outputs = _translationOutputsRecorded,
                    rendered_layer_required = _renderExpected,
                    has_rendered_layer = _renderSaved,
                    visibility_claim = "offscreen-only"
                },append:false)) throw new IOException("Diagnostic manifest budget.");
                if (!_store.Text(Path.Combine(_directory, "manifest.sha256"), HashFile(manifestPath) + Environment.NewLine))
                    throw new IOException("Diagnostic hash budget.");
            }
            catch { _writeFailures++; }
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class Scope(ScreenshotTranslationEvidence? previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }

    private sealed class EvidenceMachineProvider(
        ScreenshotTranslationEvidence evidence, IMachineTranslationProvider provider, string role)
        : IMachineTranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
            IReadOnlyList<TranslationRequest> requests, CancellationToken cancellationToken) =>
            evidence.ObserveProviderAsync(role, TranslationRouteKind.Machine, requests,
                () => provider.TranslateManyAsync(requests, cancellationToken));
    }
}

/// <summary>Reusable, text-bearing projection for local private quality evidence.</summary>
public static class TranslationOutcomeEvidenceProjection
{
    public static object Create(ApplicationTranslationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new
        {
            outcome.IsSuccess,
            failure_stage = outcome.FailureStage.ToString(),
            outcome.Status,
            ocr_blocks = outcome.OcrBlocks.Select((block, index) => new
            {
                index, block.Text, block.Bounds, block.Words
            }).ToArray(),
            regions = outcome.Regions.Select((region, index) => new
            {
                index, region.Id, region.SourceText, region.Bounds,
                role = region.Role.ToString(),
                confidence = region.Confidence.ToString(),
                source_lines = region.Lines.Select(line => new { line.Text, line.Bounds }).ToArray(),
                region.OriginalOcrWords, region.SharesParentOcrEvidence,
                result = index < outcome.Results.Count ? new
                {
                    outcome.Results[index].IsSuccess,
                    outcome.Results[index].Text,
                    outcome.Results[index].Status,
                    semantic_outcome = outcome.Results[index].SemanticOutcome.ToString(),
                    outcome.Results[index].Diagnostic,
                    outcome.Results[index].ChunkDiagnostics,
                    outcome.Results[index].ExecutionEvidence
                } : null
            }).ToArray(),
            layout_rejections = outcome.LayoutRejections,
            layouts = outcome.LayoutItems.Select(item => new
            {
                region_id = item.SourceRegion?.Id,
                item.Text, item.Bounds,
                background_mode = item.BackgroundMode.ToString(),
                background_argb = item.BackgroundColor.ToArgb().ToString("X8"),
                foreground_argb = item.ForegroundColor.ToArgb().ToString("X8"),
                source_style = item.SourceStyle is null ? null : new
                {
                    foreground_argb = item.SourceStyle.ForegroundColor.ToArgb().ToString("X8"),
                    item.SourceStyle.GlyphHeightPixels,
                    item.SourceStyle.LineHeightPixels,
                    item.SourceStyle.EstimatedFontSizePixels,
                    weight = item.SourceStyle.FontWeight.ToString(),
                    category = item.SourceStyle.FontCategory.ToString(),
                    alignment = item.SourceStyle.Alignment.ToString(),
                    confidence = item.SourceStyle.Confidence.ToString()
                },
                item.TextLayout,
                masked_patch_sha256 = HashBytes(item.MaskedPatchPng),
                background_patch_sha256 = HashBytes(item.BackgroundPatchPng)
            }).ToArray(),
            outcome.ExecutionTrace
        };
    }

    private static string? HashBytes(byte[]? bytes) => bytes is { Length: > 0 }
        ? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() : null;
}
