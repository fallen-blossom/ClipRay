using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ClipRay.App.Diagnostics;
using ClipRay.App.Translation;

namespace ClipRay.TestLab;

/// <summary>
/// Private, opt-in evidence for one TestLab case and route. Its PNG is a TestLab
/// offscreen render, never evidence of pixels visible on the user's desktop.
/// </summary>
internal sealed class TestLabDetailTrace : IDisposable
{
    internal const string SchemaVersion = "clipray.testlab-detail.v1";
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string _directory;
    private readonly string _runId;
    private readonly string _caseId;
    private readonly string _route;
    private readonly DateTimeOffset _startedUtc;
    private int _sequence;
    private int _writeFailures;
    private int _providerCallsStarted;
    private int _providerCallsEnded;
    private int _wireRequests;
    private int _wireOutcomes;
    private bool _contextRecorded;
    private bool _sourceRecorded;
    private bool _ocrRecorded;
    private bool _scopeRecorded;
    private bool _groupsRecorded;
    private bool _requestsRecorded;
    private bool _resultsRecorded;
    private bool _outcomeRecorded;
    private bool _renderRecorded;
    private bool _requiresRequests;
    private bool _requiresScope;
    private bool _cloudDispatched;
    private bool _disposed;
    private string? _sourceSha256;
    private string? _renderSha256;
    private string? _appAssemblySha256;
    private string? _testLabAssemblySha256;

    internal string DirectoryPath => _directory;
    internal int WriteFailures => _writeFailures;

    private TestLabDetailTrace(string root, string runId, string caseId, string route,
        DateTimeOffset startedUtc)
    {
        if (route is not ("local" or "machine" or "machine-common-ocr-probe" or "deepseek"))
            throw new ArgumentException("detail-route-invalid", nameof(route));
        var artifacts = TestLabArtifactPaths.Create(root, runId, caseId);
        _directory = Path.Combine(artifacts.CaseRoot, route);
        _runId = runId;
        _caseId = caseId;
        _route = route;
        _startedUtc = startedUtc;
        TryRecord("trace_started", new { run_id = runId, case_id = caseId, route });
    }

    internal static TestLabDetailTrace Start(string explicitRoot, string runId, string caseId,
        string route, DateTimeOffset startedUtc) =>
        new(RequireLocalRoot(explicitRoot), runId, caseId, route, startedUtc);

    internal IDisposable ObservePipeline() => TranslationPipelineTrace.Push(RecordPipeline);
    internal IDisposable ObserveWire() => TranslationWireTrace.Push(RecordWire);

    internal static string RequireLocalRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)
            || root.StartsWith(@"\\", StringComparison.Ordinal)
            || root.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("detail-trace-requires-an-absolute-local-root", nameof(root));
        var fullRoot = Path.GetFullPath(root);
        var driveRoot = Path.GetPathRoot(fullRoot)
            ?? throw new ArgumentException("detail-trace-root-has-no-drive", nameof(root));
        if (new DriveInfo(driveRoot).DriveType == DriveType.Network)
            throw new ArgumentException("detail-trace-rejects-network-root", nameof(root));
        for (DirectoryInfo? parent = new(fullRoot); parent is not null; parent = parent.Parent)
        {
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("detail-trace-rejects-reparse-root", nameof(root));
        }
        return fullRoot;
    }

    internal void RecordContext(string image, string sourceFileSha256, string beforePng,
        int width, int height, string targetLanguage, string appAssemblySha256,
        string testLabAssemblySha256)
    {
        _appAssemblySha256 = appAssemblySha256;
        _testLabAssemblySha256 = testLabAssemblySha256;
        _contextRecorded = TryRecord("run_context", new
        {
            run_id = _runId, case_id = _caseId, route = _route,
            started_utc = _startedUtc, image, source_file_sha256 = sourceFileSha256,
            source_width = width, source_height = height, target_language = targetLanguage,
            app_assembly_sha256 = appAssemblySha256,
            testlab_assembly_sha256 = testLabAssemblySha256
        });
        try
        {
            var hash = HashFile(beforePng);
            if (TryRecord("source_png", new
            {
                path = "../before.png", sha256 = hash, width, height,
                source_file_sha256 = sourceFileSha256
            }))
            {
                _sourceSha256 = hash;
                _sourceRecorded = true;
            }
        }
        catch { CountFailure(); }
    }

    internal void RecordPipeline(string kind, object? data)
    {
        var saved = TryRecord("pipeline_" + kind, data);
        if (!saved) return;
        lock (_gate)
        {
            switch (kind)
            {
                case "ocr_completed": _ocrRecorded = true; break;
                case "scope_completed": _scopeRecorded = true; break;
                case "regions_grouped": _groupsRecorded = true; break;
                case "translation_requests": _requestsRecorded = true; break;
                case "translation_outputs": _resultsRecorded = true; break;
                case "provider_call_started": _providerCallsStarted++; break;
                case "provider_call_completed":
                case "provider_call_failed": _providerCallsEnded++; break;
                case "cloud_provider_dispatched": _cloudDispatched = true; break;
            }
        }
    }

    // Only these DeepSeek fields are persisted. In particular, arbitrary future
    // trace properties such as Authorization or API keys are never serialized.
    internal void RecordWire(string kind, object? data)
    {
        if (kind is not ("provider_request" or "provider_response" or "provider_failure")) return;
        var saved = TryRecordProjected("wire_" + kind, () =>
        {
            var value = JsonSerializer.SerializeToElement(data);
            return kind switch
            {
                "provider_request" => (object)new
                {
                    provider = ReadString(value, "Provider"),
                    block_count = ReadInt(value, "BlockCount"),
                    payload = ReadString(value, "Payload")
                },
                "provider_response" => new
                {
                    provider = ReadString(value, "Provider"),
                    content = ReadString(value, "Content"),
                    finish_reason = ReadString(value, "FinishReason"),
                    usage = ReadUsage(value)
                },
                _ => new
                {
                    category = ReadString(value, "Category"),
                    status_code = ReadElement(value, "StatusCode")
                }
            };
        });
        if (!saved) return;
        lock (_gate)
        {
            if (kind == "provider_request") _wireRequests++;
            else _wireOutcomes++;
        }
    }

    internal void RecordOutcome(ApplicationTranslationOutcome outcome)
    {
        _requiresScope = outcome.FailureStage != TranslationPipelineStage.Ocr;
        _requiresRequests = outcome.FailureStage is not (TranslationPipelineStage.Ocr or TranslationPipelineStage.Scope)
            || outcome.Regions.Count > 0;
        if (TryRecordProjected("pipeline_outcome", () => TranslationOutcomeEvidenceProjection.Create(outcome)))
            _outcomeRecorded = true;
    }

    internal void RecordRuntimeFailure(Exception exception) =>
        TryRecord("runtime_exception", new { category = exception.GetType().Name });

    internal void RecordRenderedArtifact(string afterPng)
    {
        try
        {
            var hash = HashFile(afterPng);
            if (TryRecord("rendered_artifact", new
            {
                path = "../" + Path.GetFileName(afterPng), sha256 = hash,
                evidence_kind = "testlab-offscreen-composite"
            }))
            {
                _renderSha256 = hash;
                _renderRecorded = true;
            }
        }
        catch { CountFailure(); }
    }

    internal void RecordRenderFailure() =>
        TryRecord("render_failed", new { category = "artifact-render-failed" });

    private bool TryRecordProjected(string kind, Func<object?> projection)
    {
        try { return TryRecord(kind, projection()); }
        catch { CountFailure(); return false; }
    }

    private bool TryRecord(string kind, object? data)
    {
        lock (_gate)
        {
            try { WriteEvent(kind, data); return true; }
            catch { _writeFailures++; return false; }
        }
    }

    private void CountFailure()
    {
        lock (_gate) _writeFailures++;
    }

    private void WriteEvent(string kind, object? data)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TestLabDetailTrace));
        Directory.CreateDirectory(_directory);
        var next = _sequence + 1;
        File.AppendAllText(Path.Combine(_directory, "events.jsonl"), JsonSerializer.Serialize(new
        {
            schema_version = SchemaVersion, sequence = next,
            utc = DateTimeOffset.UtcNow, elapsed_ms = _clock.ElapsedMilliseconds,
            kind, data
        }) + Environment.NewLine);
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
                var complete = _writeFailures == 0 && _contextRecorded && _sourceRecorded
                    && _ocrRecorded && _outcomeRecorded && _renderRecorded
                    && (!_requiresScope || _scopeRecorded)
                    && (!_requiresRequests || (_groupsRecorded && _requestsRecorded && _resultsRecorded))
                    && _providerCallsStarted == _providerCallsEnded
                    && (!_cloudDispatched || (_wireRequests > 0 && _wireOutcomes >= _wireRequests));
                Directory.CreateDirectory(_directory);
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
                {
                    schema_version = SchemaVersion, run_id = _runId, case_id = _caseId,
                    route = _route, started_utc = _startedUtc, ended_utc = DateTimeOffset.UtcNow,
                    complete, write_failures = _writeFailures, event_count = _sequence,
                    events_sha256 = HashFile(eventsPath),
                    source_png_sha256 = _sourceSha256,
                    rendered_png_sha256 = _renderSha256,
                    app_assembly_sha256 = _appAssemblySha256,
                    testlab_assembly_sha256 = _testLabAssemblySha256,
                    has_context = _contextRecorded, has_source = _sourceRecorded,
                    has_raw_ocr = _ocrRecorded, has_scope = _scopeRecorded,
                    has_groups = _groupsRecorded, has_requests = _requestsRecorded,
                    has_results = _resultsRecorded, has_outcome = _outcomeRecorded,
                    has_render = _renderRecorded, requires_scope = _requiresScope,
                    requires_requests = _requiresRequests, cloud_dispatched = _cloudDispatched,
                    provider_calls_started = _providerCallsStarted,
                    provider_calls_ended = _providerCallsEnded,
                    wire_requests = _wireRequests, wire_outcomes = _wireOutcomes,
                    visibility_claim = "offscreen-only"
                }));
                File.WriteAllText(Path.Combine(_directory, "manifest.sha256"),
                    HashFile(manifestPath) + Environment.NewLine);
            }
            catch { _writeFailures++; }
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string? ReadString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static int? ReadInt(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out var count)
            ? count : null;

    private static JsonElement? ReadElement(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
            ? field.Clone() : null;

    private static object? ReadUsage(JsonElement value)
    {
        var usage = ReadElement(value, "Usage");
        return usage is { ValueKind: JsonValueKind.Object } fields ? new
        {
            prompt_cache_hit_tokens = ReadLong(fields, "PromptCacheHitTokens"),
            prompt_cache_miss_tokens = ReadLong(fields, "PromptCacheMissTokens"),
            completion_tokens = ReadLong(fields, "CompletionTokens")
        } : null;
    }

    private static long? ReadLong(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out var count)
            ? count : null;
}
