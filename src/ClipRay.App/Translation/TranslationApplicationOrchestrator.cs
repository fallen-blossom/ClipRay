using System.Drawing;
using System.Diagnostics;
using ClipRay.App.Ocr;

namespace ClipRay.App.Translation;

public enum TranslationPipelineStage
{
    None,
    Ocr,
    Scope,
    Credential,
    Translation,
    Layout,
}

public sealed record ApplicationTranslationOutcome(
    bool IsSuccess,
    TranslationPipelineStage FailureStage,
    string Status,
    IReadOnlyList<OcrTextBlock> OcrBlocks,
    IReadOnlyList<VisualTextRegion> Regions,
    IReadOnlyList<TranslationResult> Results,
    IReadOnlyList<InPlaceLayoutItem> LayoutItems,
    IReadOnlyList<InPlaceLayoutRejection>? LayoutRejections = null,
    TranslationExecutionTrace? ExecutionTrace = null);

public sealed record TranslationExecutionTrace(
    TranslationRouteKind RequestedRoute,
    int FallbackAttemptedCount,
    int RecoveredCount,
    int FailedCount);

public sealed record TranslationBatchOutcome(
    IReadOnlyList<TranslationResult> Results,
    TranslationExecutionTrace Trace);

public sealed record TranslationPipelineTelemetry(
    long OcrMilliseconds,
    long TranslationMilliseconds,
    long LayoutMilliseconds,
    long TotalMilliseconds);

public sealed record WorkbenchRecognitionOutcome(bool IsSuccess, string Status,
    IReadOnlyList<OcrTextBlock> Blocks, IReadOnlyList<VisualTextRegion> Regions);

public sealed class TranslationApplicationOrchestrator
{
    private readonly Func<Bitmap, CancellationToken, Task<OcrRecognitionResult>> _recognize;
    private readonly Func<TranslationRouteKind, IReadOnlyList<TranslationRequest>, CancellationToken,
        Task<TranslationBatchOutcome>> _translate;
    private readonly Func<Bitmap, IReadOnlyList<VisualTextRegion>, IReadOnlyList<TranslationResult>,
        IReadOnlyList<InPlaceLayoutItem>>? _buildLayout;

    public TranslationApplicationOrchestrator(
        Func<Bitmap, CancellationToken, Task<OcrRecognitionResult>> recognize,
        Func<TranslationRouteKind, IReadOnlyList<TranslationRequest>, CancellationToken,
            Task<TranslationBatchOutcome>> translate,
        Func<Bitmap, IReadOnlyList<VisualTextRegion>, IReadOnlyList<TranslationResult>,
            IReadOnlyList<InPlaceLayoutItem>>? buildLayout = null)
    {
        _recognize = recognize ?? throw new ArgumentNullException(nameof(recognize));
        _translate = translate ?? throw new ArgumentNullException(nameof(translate));
        _buildLayout = buildLayout;
    }

    public TranslationApplicationOrchestrator(
        Func<Bitmap, CancellationToken, Task<OcrRecognitionResult>> recognize,
        Func<TranslationRouteKind, IReadOnlyList<TranslationRequest>, CancellationToken,
            Task<IReadOnlyList<TranslationResult>>> translate,
        Func<Bitmap, IReadOnlyList<VisualTextRegion>, IReadOnlyList<TranslationResult>,
            IReadOnlyList<InPlaceLayoutItem>>? buildLayout = null)
        : this(
            recognize,
            async (route, requests, cancellationToken) => new TranslationBatchOutcome(
                await translate(route, requests, cancellationToken).ConfigureAwait(false),
                new TranslationExecutionTrace(route, 0, 0, 0)),
            buildLayout)
    {
    }

    public async Task<WorkbenchRecognitionOutcome> RecognizeWorkbenchAsync(Bitmap source,
        Func<IReadOnlyList<OcrTextBlock>, IReadOnlyList<OcrTextBlock>> scope,
        CancellationToken cancellationToken, string? windowsLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(scope);
        using var explicitOcr = windowsLanguage is null ? null : OcrService.CreateForWindowsLanguage(windowsLanguage);
        var ocr = explicitOcr is null
            ? await _recognize(source, cancellationToken).ConfigureAwait(false)
            : await explicitOcr.RecognizeAsync(source, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ocr.IsSuccess) return new(false, ocr.Status, ocr.Blocks, []);
        var blocks = scope(ocr.Blocks);
        if (blocks.Count == 0) return new(false, "ocr-empty", blocks, []);
        return new(true, "completed", blocks, VisualTextRegionBuilder.Build(blocks, source));
    }

    // One request binds the provider's positional result to one stable region identity.
    // Siblings remain in the layout input to preserve surrounding geometry and scene hints.
    public async Task<WorkbenchTranslationOutput> TranslateWorkbenchRegionAsync(Bitmap source,
        WorkbenchTranslationInput input, IReadOnlyList<VisualTextRegion> siblings,
        CancellationToken cancellationToken)
    {
        var regions = siblings.Where(region => region.Id != input.Region.Id).Prepend(input.Region).ToArray();
        if (regions.Select(region => region.Id).Distinct(StringComparer.Ordinal).Count() != regions.Length)
            throw new ArgumentException("Region identities must be unique.", nameof(siblings));
        var template = VisualTranslationRequestFactory.Create(regions, input.TargetLanguage, DeepSeekModels.Flash)[0];
        var request = new TranslationRequest(input.SourceLanguage, input.TargetLanguage, template.OcrText,
            template.Model, template.TextKind, template.Scene, template.TranslationHint);
        cancellationToken.ThrowIfCancellationRequested();
        var batch = await _translate(input.Route, [request], cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (batch.Results.Count != 1)
            return new(input.RegionId, new(false, "", "translation-count-mismatch"));
        var result = batch.Results[0];
        if (!result.IsSuccess || result.Status == "machine-preserved"
            || string.IsNullOrWhiteSpace(result.Text)
            || string.Equals(result.Text.Trim(), input.Region.SourceText.Trim(), StringComparison.Ordinal))
            return new(input.RegionId, result);
        // Account/identity rows can contain badges that OCR geometry does not describe.
        // Keep their pixels intact while still exposing the requested translation as text.
        if (request.TextKind == TranslationTextKind.IdentityMetadata)
            return new(input.RegionId, result, LayoutFailure: "identity-pixels-preserved");
        var results = regions.Select((region, index) => index == 0 ? result
            : new TranslationResult(true, region.SourceText, "machine-preserved")).ToArray();
        try
        {
            var plan = _buildLayout is null ? InPlaceLayoutService.BuildRegionPlan(source, regions, results)
                : new InPlaceLayoutPlan(_buildLayout(source, regions, results), []);
            cancellationToken.ThrowIfCancellationRequested();
            var matching = plan.Items.Where(item => item.SourceRegion?.Id == input.Region.Id).ToArray();
            return matching.Length == 1
                ? new(input.RegionId, result, matching[0])
                : new(input.RegionId, result, LayoutFailure: plan.Rejections.FirstOrDefault(item => item.Region == 1)?.Reason ?? "layout-unavailable");
        }
        catch (OperationCanceledException) { throw; }
        catch { return new(input.RegionId, result, LayoutFailure: "layout-failed"); }
    }

    public Task<ApplicationTranslationOutcome> ExecuteAsync(
        Bitmap source,
        TranslationRouteKind route,
        string targetLanguage,
        Func<IReadOnlyList<OcrTextBlock>, IReadOnlyList<OcrTextBlock>> scope,
        CancellationToken cancellationToken) =>
        ExecuteAsync(source, route, targetLanguage, scope, progress: null, telemetry: null, cancellationToken);

    public Task<ApplicationTranslationOutcome> ExecuteAsync(
        Bitmap source,
        TranslationRouteKind route,
        string targetLanguage,
        Func<IReadOnlyList<OcrTextBlock>, IReadOnlyList<OcrTextBlock>> scope,
        Action<TranslationPipelineStage>? progress,
        CancellationToken cancellationToken)
        => ExecuteAsync(source, route, targetLanguage, scope, progress, telemetry: null, cancellationToken);

    public async Task<ApplicationTranslationOutcome> ExecuteAsync(
        Bitmap source,
        TranslationRouteKind route,
        string targetLanguage,
        Func<IReadOnlyList<OcrTextBlock>, IReadOnlyList<OcrTextBlock>> scope,
        Action<TranslationPipelineStage>? progress,
        Action<TranslationPipelineTelemetry>? telemetry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(scope);
        var total = Stopwatch.StartNew();
        long ocrMilliseconds = 0;
        long translationMilliseconds = 0;
        long layoutMilliseconds = 0;

        ApplicationTranslationOutcome Complete(ApplicationTranslationOutcome outcome)
        {
            total.Stop();
            try
            {
                telemetry?.Invoke(new TranslationPipelineTelemetry(
                    ocrMilliseconds,
                    translationMilliseconds,
                    layoutMilliseconds,
                    total.ElapsedMilliseconds));
            }
            catch
            {
                // Telemetry must never affect the translation result.
            }
            return outcome;
        }

        progress?.Invoke(TranslationPipelineStage.Ocr);
        var stage = Stopwatch.StartNew();
        var ocr = await _recognize(source, cancellationToken).ConfigureAwait(false);
        ocrMilliseconds = stage.ElapsedMilliseconds;
        TranslationPipelineTrace.RecordProjected("ocr_completed", () => new
        {
            ocr.IsSuccess, ocr.Status,
            blocks = ocr.Blocks.Select((block, index) => new
            {
                index, block.Text, block.Bounds, block.Words
            }).ToArray()
        });
        if (!ocr.IsSuccess)
            return Complete(Failure(TranslationPipelineStage.Ocr, ocr.Status, ocr.Blocks));

        var blocks = scope(ocr.Blocks);
        TranslationPipelineTrace.RecordProjected("scope_completed", () => new
        {
            blocks = blocks.Select((block, index) => new
            {
                index, block.Text, block.Bounds, block.Words
            }).ToArray()
        });
        if (blocks.Count == 0)
            return Complete(Failure(TranslationPipelineStage.Scope, "ocr-empty", blocks));

        var regions = VisualTextRegionBuilder.Build(blocks, source);
        TranslationPipelineTrace.RecordProjected("regions_grouped", () => regions.Select((region, index) => new
        {
            index, region.Id, region.SourceText, region.Bounds,
            role = region.Role.ToString(), confidence = region.Confidence.ToString(),
            lines = region.Lines.Select(line => new { line.Text, line.Bounds, line.Words }).ToArray(),
            region.OriginalOcrWords, region.SharesParentOcrEvidence
        }).ToArray());
        var model = DeepSeekModels.Flash;
        var requests = VisualTranslationRequestFactory.Create(regions, targetLanguage, model);
        TranslationPipelineTrace.RecordProjected("translation_requests", () => new
        {
            route = route.ToString(), target_language = targetLanguage,
            region_ids = regions.Select(static region => region.Id).ToArray(),
            requests = TranslationPipelineTrace.ProjectRequests(requests)
        });
        progress?.Invoke(TranslationPipelineStage.Translation);
        stage.Restart();
        var translation = await _translate(route, requests, cancellationToken).ConfigureAwait(false);
        var results = translation.Results;
        TranslationPipelineTrace.RecordProjected("translation_outputs", () => new
        {
            results = TranslationPipelineTrace.ProjectResults(results), translation.Trace
        });
        translationMilliseconds = stage.ElapsedMilliseconds;
        if (results.Count != regions.Count)
            return Complete(new(false, TranslationPipelineStage.Translation, "translation-count-mismatch", blocks, regions, results, [], ExecutionTrace: translation.Trace));

        IReadOnlyList<InPlaceLayoutItem> layout;
        IReadOnlyList<InPlaceLayoutRejection> layoutRejections = [];
        try
        {
            stage.Restart();
            if (_buildLayout is null)
            {
                var plan = InPlaceLayoutService.BuildRegionPlan(source, regions, results);
                layout = plan.Items;
                layoutRejections = plan.Rejections;
            }
            else
            {
                layout = _buildLayout(source, regions, results);
            }
            layoutMilliseconds = stage.ElapsedMilliseconds;
        }
        catch (Exception)
        {
            layoutMilliseconds = stage.ElapsedMilliseconds;
            return Complete(new(false, TranslationPipelineStage.Layout, "layout-failed", blocks, regions, results, [], ExecutionTrace: translation.Trace));
        }
        var success = results.All(static result => result.IsSuccess);
        var requiredLayoutCount = InPlaceLayoutService.CountRequiredOverlays(regions, results);
        var layoutComplete = _buildLayout is null
            ? InPlaceLayoutService.HasCompleteCoverage(regions, results, layout, layoutRejections)
            : layout.Count == requiredLayoutCount;
        if (success && !layoutComplete)
        {
            return Complete(new(false, TranslationPipelineStage.Layout, "layout-incomplete", blocks, regions, results, layout, layoutRejections, translation.Trace));
        }
        return Complete(new(
            success,
            success ? TranslationPipelineStage.None : TranslationPipelineStage.Translation,
            success ? "completed" : "translation-failed",
            blocks,
            regions,
            results,
            layout,
            layoutRejections,
            translation.Trace));
    }

    private static ApplicationTranslationOutcome Failure(
        TranslationPipelineStage stage,
        string status,
        IReadOnlyList<OcrTextBlock> blocks) =>
        new(false, stage, status, blocks, [], [], []);
}
