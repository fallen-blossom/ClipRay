using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.IO;
using System.Text.Json;
using ClipRay.App.Infrastructure;
using ClipRay.App.Security;
using ClipRay.App.Settings;
using ClipRay.App.Translation;

namespace ClipRay.TestLab;

public static class TestLabRunner
{
    public static Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        return RunCoreAsync(args, cancellationToken);
    }

    private static async Task<int> RunCoreAsync(string[] args, CancellationToken cancellationToken)
    {
        var parsed = Parse(args);
        var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
        var startedUtc = DateTimeOffset.UtcNow;
        var manifest = TestLabManifest.Load(parsed.Root);
        var reportsRoot = Path.Combine(parsed.Root, "reports");
        if (manifest.Errors.Count > 0)
        {
            TestLabReportWriter.Write(reportsRoot, new TestLabRunReport(
                runId, startedUtc, parsed.Mode, "failed", manifest.Errors, []));
            return 2;
        }
        TestLabQualityOracle? oracle;
        try
        {
            oracle = TestLabQualityOracle.Load(parsed.Root);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException
            or IOException or UnauthorizedAccessException)
        {
            TestLabReportWriter.Write(reportsRoot, new TestLabRunReport(
                runId, startedUtc, parsed.Mode, "failed", ["quality-oracle-invalid"], []));
            return 2;
        }
        if (oracle is not null && !oracle.Covers(manifest.Cases))
        {
            TestLabReportWriter.Write(reportsRoot, new TestLabRunReport(
                runId, startedUtc, parsed.Mode, "failed", ["quality-oracle-cases-mismatch"], []));
            return 2;
        }

        var paths = ApplicationPaths.Create(parsed.AppRoot,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var settings = new SettingsStore(Path.Combine(paths.DataRoot, "settings.json"), paths.InstallRoot)
            .LoadRecoverable().Settings;
        var reports = new List<TestLabCaseReport>();
        using var runtime = new TranslationApplicationRuntime(settings, new CredentialVault(), parsed.AppRoot);
        foreach (var testCase in manifest.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                reports.Add(await RunCaseAsync(parsed, runId, testCase, runtime, oracle, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                reports.Add(CreateInputFailure(parsed, runId, testCase));
            }
        }
        var (status, errors) = ResolveRunOutcome(reports, parsed.DetailTrace);
        TestLabReportWriter.Write(reportsRoot, new TestLabRunReport(
            runId, startedUtc, parsed.Mode, status, errors, reports,
            AppAssemblySha256: AssemblySha256(typeof(TranslationApplicationRuntime).Assembly.Location),
            TestLabAssemblySha256: AssemblySha256(typeof(TestLabRunner).Assembly.Location),
            LocalModelSha256: parsed.Mode is "local" or "compare"
                ? LocalModelSha256(settings, parsed.AppRoot) : null));
        return status == "expectation-met" ? 0 : 1;
    }

    private static async Task<TestLabCaseReport> RunCaseAsync(
        ParsedArguments parsed,
        string runId,
        TestLabCase testCase,
        TranslationApplicationRuntime runtime,
        TestLabQualityOracle? oracle,
        CancellationToken cancellationToken)
    {
        var input = ResolveInput(parsed.Root, testCase.Image);
        using var source = new Bitmap(input);
        var artifacts = TestLabArtifactPaths.Create(parsed.Root, runId, testCase.Id);
        Directory.CreateDirectory(artifacts.CaseRoot);
        var before = artifacts.BeforePath;
        source.Save(before, ImageFormat.Png);
        var imageSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
        var routes = parsed.Mode switch
        {
            "local" => new[] { TranslationRouteKind.LocalLmt },
            "machine" => new[] { TranslationRouteKind.Machine },
            "machine-common-ocr-probe" => new[] { TranslationRouteKind.Machine },
            "deepseek" => new[] { runtime.ConfiguredDeepSeekRoute },
            _ => new[] { TranslationRouteKind.LocalLmt, runtime.ConfiguredDeepSeekRoute },
        };
        var routeReports = new List<TestLabRouteReport>();
        foreach (var route in routes)
        {
            var routeName = parsed.Mode == "machine-common-ocr-probe" ? "machine-common-ocr-probe" : route switch
            {
                TranslationRouteKind.LocalLmt => "local",
                TranslationRouteKind.Machine => "machine",
                _ => "deepseek",
            };
            using var detail = parsed.DetailTrace
                ? TestLabDetailTrace.Start(parsed.Root, runId, testCase.Id, routeName, DateTimeOffset.UtcNow)
                : null;
            using var pipelineScope = detail?.ObservePipeline();
            using var wireScope = detail?.ObserveWire();
            detail?.RecordContext(testCase.Image, imageSha256, before, source.Width, source.Height,
                testCase.TargetLanguage,
                AssemblySha256(typeof(TranslationApplicationRuntime).Assembly.Location),
                AssemblySha256(typeof(TestLabRunner).Assembly.Location));
            ApplicationTranslationOutcome outcome;
            try
            {
                outcome = parsed.Mode == "machine-common-ocr-probe"
                    ? await runtime.ExecuteMachineWithCommonOcrProbeAsync(source,
                        testCase.TargetLanguage, cancellationToken)
                    : await runtime.ExecuteAsync(source, route, testCase.TargetLanguage, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                detail?.RecordRuntimeFailure(exception);
                outcome = new ApplicationTranslationOutcome(
                    false, TranslationPipelineStage.Translation, "runtime-failed", [], [], [], []);
            }
            detail?.RecordOutcome(outcome);
            var category = TestLabReportWriter.Categorize(outcome);
            var after = artifacts.After(routeName);
            var afterPath = string.Empty;
            Bitmap? renderedForOracle = null;
            try
            {
                TestLabVisualArtifactWriter.Write(source, outcome.LayoutItems, outcome.IsSuccess, category, after);
                renderedForOracle = new Bitmap(after);
                if (renderedForOracle.Width != source.Width || renderedForOracle.Height != source.Height)
                    throw new InvalidDataException("rendered-artifact-size-mismatch");
                afterPath = Path.GetRelativePath(parsed.Root, after);
                detail?.RecordRenderedArtifact(after);
            }
            catch
            {
                renderedForOracle?.Dispose();
                renderedForOracle = null;
                category = "artifact-render-failed";
                detail?.RecordRenderFailure();
            }
            var artifactWritten = afterPath.Length > 0;
            var expectationMet = artifactWritten && TestLabExpectation.IsMet(testCase.Expectation, outcome,
                oracle?.AllowsIntentionalPreservation(testCase.Id, imageSha256,
                    testCase.TargetLanguage, outcome) ?? false);
            var evidence = TestLabEvidenceAssessment.Create(expectationMet, artifactWritten, outcome);
            var oracleAssessment = oracle?.Assess(testCase.Id, imageSha256,
                testCase.TargetLanguage, outcome, artifactWritten, source.Width, source.Height,
                source, renderedForOracle);
            renderedForOracle?.Dispose();
            if (oracleAssessment is not null)
                evidence = evidence with
                {
                    ContentQuality = oracleAssessment.ContentQuality,
                    VisualReadability = evidence.VisualReadability == "known-incomplete"
                        ? "known-incomplete" : oracleAssessment.LayoutQuality,
                };
            var ocrSources = outcome.OcrBlocks.SelectMany(static block => block.Words)
                .Select(static word => word.Evidence is null ? "unknown"
                    : $"{word.Evidence.EngineId}/{word.Evidence.ModelId}/{word.Evidence.Script ?? "unknown"}"
                        + (word.Evidence.ModelSha256 is null ? string.Empty : $"@{word.Evidence.ModelSha256}"))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            detail?.Dispose();
            var detailReport = detail is null ? null : AssessDetailTrace(
                parsed.Root, runId, testCase.Id, routeName, before, afterPath.Length > 0 ? after : null);
            routeReports.Add(new TestLabRouteReport(
                routeName,
                outcome.IsSuccess && artifactWritten,
                expectationMet,
                artifactWritten ? outcome.FailureStage.ToString().ToLowerInvariant() : "artifact",
                category,
                outcome.Regions.Count,
                outcome.LayoutItems.Count,
                outcome.Regions.Count - outcome.LayoutItems.Count,
                afterPath,
                CreateChunkReports(outcome),
                TestLabReportWriter.CreateFailureReports(outcome),
                outcome.LayoutRejections,
                evidence,
                oracleAssessment,
                ocrSources,
                detailReport));
        }
        return new TestLabCaseReport(
            testCase.Id,
            testCase.Image,
            imageSha256,
            Path.GetRelativePath(parsed.Root, before),
            testCase.Expectation,
            routeReports);
    }

    private static TestLabCaseReport CreateInputFailure(
        ParsedArguments parsed,
        string runId,
        TestLabCase testCase)
    {
        var routeNames = parsed.Mode switch
        {
            "local" => new[] { "local" },
            "machine" => new[] { "machine" },
            "machine-common-ocr-probe" => new[] { "machine-common-ocr-probe" },
            "deepseek" => new[] { "deepseek" },
            _ => new[] { "local", "deepseek" },
        };
        var routes = routeNames.Select(route => new TestLabRouteReport(
            route, false, false, "input", "artifact-input-failed", 0, 0, 0, string.Empty, [],
            Evidence: TestLabEvidenceAssessment.InputFailure(),
            DetailTrace: parsed.DetailTrace ? new TestLabDetailTraceReport(
                DetailTraceRelativePath(parsed.Root, runId, testCase.Id, route),
                false, "input-or-trace-unavailable") : null)).ToArray();
        var input = Path.GetFullPath(Path.Combine(parsed.Root, testCase.Image));
        string hash;
        try { hash = File.Exists(input) ? AssemblySha256(input) : string.Empty; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            hash = string.Empty;
        }
        return new TestLabCaseReport(
            testCase.Id, testCase.Image, hash, string.Empty, testCase.Expectation, routes);
    }

    private static IReadOnlyList<TestLabChunkReport> CreateChunkReports(ApplicationTranslationOutcome outcome) =>
        outcome.Results
            .SelectMany(static result => result.ChunkDiagnostics ?? [])
            .Select(static diagnostic => new TestLabChunkReport(
                diagnostic.ChunkIndex,
                diagnostic.CharacterCount,
                diagnostic.Stage,
                diagnostic.ResultCategory,
                diagnostic.ExpectedProtectedTokenCount,
                diagnostic.ReturnedProtectedTokenCount))
            .ToArray();

    internal static (string Status, IReadOnlyList<string> Errors) ResolveRunOutcome(
        IReadOnlyList<TestLabCaseReport> reports, bool detailTraceRequested)
    {
        var qualityStatus = TestLabRunStatus.From(reports);
        if (!detailTraceRequested) return (qualityStatus, []);
        var incomplete = reports.SelectMany(static item => item.Routes.Select(route => (item.Id, route)))
            .Where(static item => item.route.DetailTrace?.Complete != true)
            .Select(static item => $"detail-evidence-incomplete:{item.Id}/{item.route.Route}")
            .ToArray();
        return incomplete.Length == 0 ? (qualityStatus, []) : ("evidence-incomplete", incomplete);
    }

    internal static TestLabDetailTraceReport AssessDetailTrace(
        string root, string runId, string caseId, string route, string beforePng, string? afterPng)
    {
        var relativePath = DetailTraceRelativePath(root, runId, caseId, route);
        if (string.IsNullOrWhiteSpace(afterPng))
            return new TestLabDetailTraceReport(relativePath, false, "render-unavailable");
        try
        {
            var directory = Path.Combine(root, relativePath);
            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifestHashPath = Path.Combine(directory, "manifest.sha256");
            var eventsPath = Path.Combine(directory, "events.jsonl");
            if (!File.Exists(manifestPath) || !File.Exists(manifestHashPath)
                || !File.Exists(eventsPath) || !File.Exists(beforePng) || !File.Exists(afterPng))
                return new TestLabDetailTraceReport(relativePath, false, "evidence-file-missing");
            if (!string.Equals(File.ReadAllText(manifestHashPath).Trim(), AssemblySha256(manifestPath),
                    StringComparison.OrdinalIgnoreCase))
                return new TestLabDetailTraceReport(relativePath, false, "manifest-hash-mismatch");
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var value = manifest.RootElement;
            if (value.GetProperty("schema_version").GetString() != TestLabDetailTrace.SchemaVersion
                || value.GetProperty("run_id").GetString() != runId
                || value.GetProperty("case_id").GetString() != caseId
                || value.GetProperty("route").GetString() != route
                || value.GetProperty("visibility_claim").GetString() != "offscreen-only")
                return new TestLabDetailTraceReport(relativePath, false, "manifest-identity-mismatch");
            if (!value.GetProperty("complete").GetBoolean())
                return new TestLabDetailTraceReport(relativePath, false, "manifest-incomplete");
            if (value.GetProperty("write_failures").GetInt32() != 0
                || value.GetProperty("events_sha256").GetString() != AssemblySha256(eventsPath)
                || value.GetProperty("source_png_sha256").GetString() != AssemblySha256(beforePng)
                || value.GetProperty("rendered_png_sha256").GetString() != AssemblySha256(afterPng))
                return new TestLabDetailTraceReport(relativePath, false, "evidence-hash-mismatch");
            var sequence = 0;
            foreach (var line in File.ReadLines(eventsPath))
            {
                using var item = JsonDocument.Parse(line);
                if (item.RootElement.GetProperty("sequence").GetInt32() != ++sequence)
                    return new TestLabDetailTraceReport(relativePath, false, "event-sequence-invalid");
            }
            if (sequence == 0 || value.GetProperty("event_count").GetInt32() != sequence)
                return new TestLabDetailTraceReport(relativePath, false, "event-count-mismatch");
            return new TestLabDetailTraceReport(relativePath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            return new TestLabDetailTraceReport(relativePath, false, "evidence-read-invalid");
        }
    }

    private static string DetailTraceRelativePath(string root, string runId, string caseId, string route) =>
        Path.GetRelativePath(root, Path.Combine(TestLabArtifactPaths.Create(root, runId, caseId).CaseRoot, route))
            .Replace('\\', '/');

    private static string AssemblySha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string? LocalModelSha256(AppSettings settings, string appRoot)
    {
        try
        {
            var path = LocalLmtOptions.Discover(settings.LocalLmtModelPath, appRoot).ModelPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            using var input = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ResolveInput(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var input = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!input.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(input))
            throw new InvalidDataException("case-image-missing-or-outside-root");
        return input;
    }

    private static ParsedArguments Parse(string[] args)
    {
        if (args.Length < 3 || args[0] is not ("local" or "machine" or "machine-common-ocr-probe" or "deepseek" or "compare"))
            throw new ArgumentException("Usage: ClipRay.TestLab <local|machine|machine-common-ocr-probe|deepseek|compare> --root <path> [--app-root <path>] [--detail-trace]");
        var rootIndex = Array.IndexOf(args, "--root");
        if (rootIndex < 0 || rootIndex + 1 >= args.Length)
            throw new ArgumentException("TestLab requires --root <path>.");
        var appRootIndex = Array.IndexOf(args, "--app-root");
        if (appRootIndex >= 0 && appRootIndex + 1 >= args.Length)
            throw new ArgumentException("TestLab requires a path after --app-root.");
        var appRoot = appRootIndex < 0 ? AppContext.BaseDirectory : args[appRootIndex + 1];
        var detailTrace = args.Contains("--detail-trace", StringComparer.Ordinal);
        var root = detailTrace
            ? TestLabDetailTrace.RequireLocalRoot(args[rootIndex + 1])
            : Path.GetFullPath(args[rootIndex + 1]);
        return new ParsedArguments(args[0], root, Path.GetFullPath(appRoot), detailTrace);
    }

    private sealed record ParsedArguments(string Mode, string Root, string AppRoot, bool DetailTrace);
}
