using System.Drawing;
using System.IO;
using ClipRay.App.Ocr;
using ClipRay.App.Security;
using ClipRay.App.Settings;

namespace ClipRay.App.Translation;

public sealed class TranslationApplicationRuntime : IDisposable
{
    private readonly AppSettings _settings;
    private readonly ICredentialVault _vault;
    private readonly OcrService _ocr;
    private readonly OcrService _machineOcr;
    private readonly LocalTranslationProvider _local;
    private readonly IDeepSeekTranslationProvider _deepSeek;
    private readonly IMachineTranslationProvider _machine;
    private readonly TranslationApplicationOrchestrator _orchestrator;
    private readonly TranslationApplicationOrchestrator _machineOrchestrator;

    public TranslationApplicationRuntime(
        AppSettings settings,
        ICredentialVault vault,
        string? installRoot = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _ocr = new OcrService();
        _machineOcr = OcrService.CreateForCommonMachine();
        _local = new LocalTranslationProvider(LocalLmtOptions.Discover(settings.LocalLmtModelPath, installRoot));
        _deepSeek = new DeepSeekTranslationProvider(vault);
        var root = installRoot ?? AppContext.BaseDirectory;
        _machine = new LocalMachineTranslationProvider(new BundledMachineTranslationProcess(
            Path.Combine(root, "machine-translation", "runtime", "python.exe"),
            Path.Combine(root, "machine-translation", "machine_translation_worker.py"),
            MachineTranslationModelPaths.Resolve(root), workerTimeout: TimeSpan.FromSeconds(10), keepAlive: true),
            supportsCommonLanguages: MachineTranslationModelPaths.IsCommonLanguageModel(MachineTranslationModelPaths.Resolve(root)));
        _orchestrator = new TranslationApplicationOrchestrator(
            (bitmap, token) => _ocr.RecognizeAsync(bitmap, token),
            TranslateAsync);
        _machineOrchestrator = new TranslationApplicationOrchestrator(
            (bitmap, token) => _machineOcr.RecognizeAsync(bitmap, token),
            TranslateAsync);
    }

    public Task<ApplicationTranslationOutcome> ExecuteAsync(
        Bitmap source,
        TranslationRouteKind route,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        _orchestrator.ExecuteAsync(
            source,
            route,
            targetLanguage,
            static blocks => blocks,
            cancellationToken);

    public Task<ApplicationTranslationOutcome> ExecuteAsync(
        Bitmap source,
        TranslationRouteKind route,
        CancellationToken cancellationToken) =>
        ExecuteAsync(source, route, _settings.TranslationTargetLanguage, cancellationToken);

    /// <summary>TestLab diagnostic for the common-script OCR used by live translation.</summary>
    public Task<ApplicationTranslationOutcome> ExecuteMachineWithCommonOcrProbeAsync(
        Bitmap source, string targetLanguage, CancellationToken cancellationToken) =>
        _machineOrchestrator.ExecuteAsync(source, TranslationRouteKind.Machine, targetLanguage,
            static blocks => blocks, cancellationToken);

    public TranslationRouteKind ConfiguredDeepSeekRoute =>
        TranslationRouteKind.DeepSeekFlash;

    private async Task<TranslationBatchOutcome> TranslateAsync(
        TranslationRouteKind route,
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken)
    {
        if (route == TranslationRouteKind.Machine)
        {
            var machine = TranslationPipelineTrace.ObserveMachineProvider(_machine, "direct-machine");
            var machineResults = await machine.TranslateManyAsync(requests, cancellationToken).ConfigureAwait(false);
            return new(machineResults, new TranslationExecutionTrace(route, 0, 0, 0));
        }

        Task<IReadOnlyList<TranslationResult>> Primary(
            TranslationRouteKind primaryRoute,
            IReadOnlyList<TranslationRequest> primaryRequests,
            CancellationToken token) => TranslationPipelineTrace.ObserveProviderAsync(
                "primary", primaryRoute, primaryRequests,
                () => primaryRoute == TranslationRouteKind.LocalLmt
                    ? _local.TranslateManyAsync(primaryRequests, token)
                    : VisualTranslationExecutionPolicy.ExecuteCloudAsync(
                        primaryRequests, _vault, _deepSeek, null, token));

        if (_settings.MachineTranslationFallbackEnabled)
        {
            var machine = TranslationPipelineTrace.ObserveMachineProvider(_machine, "machine-fallback");
            var fallback = await new TranslationFallbackCoordinator(Primary, machine)
                .TranslateAsync(route, requests, cancellationToken).ConfigureAwait(false);
            var attempted = fallback.Diagnostics
                .Where(static diagnostic => diagnostic.Code.StartsWith("machine-fallback", StringComparison.Ordinal))
                .Sum(static diagnostic => diagnostic.Count);
            return new(fallback.Results, new TranslationExecutionTrace(
                route,
                attempted,
                fallback.RecoveredCount,
                fallback.FailedCount));
        }

        var results = await Primary(route, requests, cancellationToken).ConfigureAwait(false);
        return new(results, new TranslationExecutionTrace(route, 0, 0, 0));
    }

    public void Dispose()
    {
        (_machine as IDisposable)?.Dispose();
        _local.Dispose();
        _ocr.Dispose();
        _machineOcr.Dispose();
    }
}
