using System.Text.RegularExpressions;

namespace ClipRay.App.Translation;

public sealed record MachineTranslationProcessRequest(
    string SourceLanguage,
    string TargetLanguage,
    IReadOnlyList<string> Texts);

public sealed record MachineTranslationProcessResult(IReadOnlyList<string> Texts,
    TranslationExecutionEvidence? Evidence = null,
    IReadOnlyList<TranslationExecutionEvidence?>? TextEvidence = null);

public sealed class MachineTranslationRuntimeException(string status) : InvalidOperationException(status)
{
    public string Status { get; } = status;
}

public interface IMachineTranslationProcess
{
    Task<IReadOnlyList<string>> TranslateAsync(
        MachineTranslationProcessRequest request,
        CancellationToken cancellationToken);

    async Task<MachineTranslationProcessResult> TranslateWithEvidenceAsync(
        MachineTranslationProcessRequest request, CancellationToken cancellationToken) =>
        new(await TranslateAsync(request, cancellationToken).ConfigureAwait(false));
}

public sealed class LocalMachineTranslationProvider(IMachineTranslationProcess process,
    Action<string, string, TranslationInvariantMismatch>? onIntegrityFailure = null,
    bool supportsCommonLanguages = false,
    Action<string, string>? onProtectedFailure = null)
    : IMachineTranslationProvider, IDisposable
{
    private readonly IMachineTranslationProcess _process = process ?? throw new ArgumentNullException(nameof(process));
    private static readonly Dictionary<string,string> ControlTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Select effort"] = "选择推理强度",
        ["Less usage"] = "更低用量",
        ["High"] = "高",
    };
    private static readonly Regex CommitIdentifierPattern = new(
        @"^[0-9a-f]{7,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SocialCounterPattern = new(
        @"^(?:[OQIil|uUtThH]+\s*|\d+\s+)?\d+(?:[.,]\d+)?[KkMmBb]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return [];

        if (requests.Any(request => !(IsEnglish(request.SourceLanguage) || IsSimplifiedChinese(request.SourceLanguage)
            || supportsCommonLanguages && new[] { "ja", "ko", "fr", "de", "es", "ru", "pt", "it", "ar" }.Contains(WorkbenchLanguageOptions.Code(request.SourceLanguage)))
            || !IsSimplifiedChinese(request.TargetLanguage)))
        {
            return requests.Select(static _ =>
                new TranslationResult(false, string.Empty, "machine-language-pair-unavailable")).ToArray();
        }

        string ProcessLanguage(TranslationRequest request) => supportsCommonLanguages
            ? WorkbenchLanguageOptions.Code(request.SourceLanguage).ToLowerInvariant() : "en";
        var languageGroups = requests.Select((request, index) => (request, index)).GroupBy(item => ProcessLanguage(item.request)).ToArray();
        if (languageGroups.Length > 1)
        {
            var ordered = new TranslationResult[requests.Count];
            foreach (var group in languageGroups)
            {
                var members = group.ToArray();
                var translatedGroup = await TranslateManyAsync(members.Select(item => item.request).ToArray(), cancellationToken).ConfigureAwait(false);
                for (var index = 0; index < members.Length; index++) ordered[members[index].index] = translatedGroup[index];
            }
            return ordered;
        }
        var processLanguage = ProcessLanguage(requests[0]);

        string? ControlTranslation(TranslationRequest request)
        {
            if (request.TextKind != TranslationTextKind.UiControl
                || IsSimplifiedChinese(request.SourceLanguage)) return null;
            if (ControlTerms.TryGetValue(request.OcrText.Trim(), out var localTerm)) return localTerm;
            return request.Scene == "social_media"
                && LocalDeterministicTranslation.TryTranslateKnownUiTerm(request.OcrText, out var socialTerm)
                ? socialTerm : null;
        }
        var controlTranslations = requests.Select(ControlTranslation).ToArray();
        var controlIndices = Enumerable.Range(0, requests.Count)
            .Where(index => controlTranslations[index] is not null).ToHashSet();
        if (controlIndices.Count > 0)
        {
            var remaining = await TranslateManyAsync(requests.Where((_,index) => !controlIndices.Contains(index)).ToArray(),
                cancellationToken).ConfigureAwait(false);
            var next = 0;
            return requests.Select((request,index) => controlIndices.Contains(index)
                ? new TranslationResult(true,controlTranslations[index]!,"machine-completed")
                : remaining[next++]).ToArray();
        }

        var protectedTexts = requests
            .Select(static request => MachineProtectedTranslationText.Create(request.OcrText,
                request.TextKind is TranslationTextKind.Body or TranslationTextKind.Heading,
                LocalLmtLanguage.Normalize(request.SourceLanguage) == "Japanese"))
            .ToArray();
        var translatedSegments = protectedTexts
            .Select(static item => item.Segments.Select(static segment => segment.Text).ToArray())
            .ToArray();
        var workItems = protectedTexts
            .SelectMany((item, requestIndex) => item.Segments
                .Select((segment, segmentIndex) => new MachineTranslationWorkItem(
                    requestIndex,
                    segmentIndex,
                    segment.Text,
                    segment.ShouldTranslate && !ShouldPreserveWithoutModel(requests[requestIndex]))))
            .Where(static item => item.ShouldTranslate)
            .ToArray();

        if (workItems.Length == 0)
        {
            return requests.Select(static request =>
                new TranslationResult(
                    true,
                    request.OcrText,
                    ShouldPreserveWithoutModel(request) ? "machine-preserved" : "machine-completed")).ToArray();
        }

        IReadOnlyList<string> translated;
        var executionEvidence = new TranslationExecutionEvidence?[workItems.Length];
        try
        {
            var response = await _process.TranslateWithEvidenceAsync(
                new MachineTranslationProcessRequest(
                    processLanguage,
                    "zh",
                    workItems.Select(static item => item.Text).ToArray()),
                cancellationToken).ConfigureAwait(false);
            translated = response.Texts;
            if (response.TextEvidence is { } perText && perText.Count == workItems.Length)
                executionEvidence = perText.ToArray();
            else if (response.TextEvidence is null) Array.Fill(executionEvidence, response.Evidence);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or System.IO.FileNotFoundException or MachineTranslationRuntimeException)
        {
            var status = exception is MachineTranslationRuntimeException runtime ? runtime.Status
                : exception is TimeoutException ? "machine-worker-timeout" : "machine-component-unavailable";
            return CreateResults(requests, protectedTexts, translatedSegments, workItems,
                Enumerable.Repeat<string?>(status, workItems.Length).ToArray());
        }
        catch
        {
            var failures = new string?[workItems.Length];
            for (var index = 0; index < workItems.Length; index++)
            {
                try
                {
                    var singleResponse = await _process.TranslateWithEvidenceAsync(
                        new MachineTranslationProcessRequest(processLanguage, "zh", [workItems[index].Text]),
                        cancellationToken).ConfigureAwait(false);
                    var single = singleResponse.Texts;
                    executionEvidence[index] = singleResponse.TextEvidence is { Count: 1 } perText
                        ? perText[0] : singleResponse.TextEvidence is null ? singleResponse.Evidence : null;
                    if (single.Count == 1)
                        translatedSegments[workItems[index].RequestIndex][workItems[index].SegmentIndex] = single[0];
                    else
                        failures[index] = "machine-result-count-mismatch";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is MachineTranslationRuntimeException or System.IO.FileNotFoundException or TimeoutException)
                {
                    var status = exception is MachineTranslationRuntimeException runtime ? runtime.Status
                        : exception is TimeoutException ? "machine-worker-timeout" : "machine-component-unavailable";
                    for (var remaining = index; remaining < workItems.Length; remaining++) failures[remaining] = status;
                    break;
                }
                catch
                {
                    failures[index] = "machine-provider-failed";
                }
            }
            return CreateResults(requests, protectedTexts, translatedSegments, workItems, failures, executionEvidence);
        }

        if (translated.Count != workItems.Length)
        {
            return CreateResults(
                requests,
                protectedTexts,
                translatedSegments,
                workItems,
                Enumerable.Repeat<string?>("machine-result-count-mismatch", workItems.Length).ToArray(), executionEvidence);
        }

        for (var index = 0; index < workItems.Length; index++)
            translatedSegments[workItems[index].RequestIndex][workItems[index].SegmentIndex] = translated[index];
        return CreateResults(requests, protectedTexts, translatedSegments, workItems, new string?[workItems.Length], executionEvidence);
    }

    private IReadOnlyList<TranslationResult> CreateResults(
        IReadOnlyList<TranslationRequest> requests,
        IReadOnlyList<MachineProtectedTranslationText> protectedTexts,
        IReadOnlyList<string[]> translatedSegments,
        IReadOnlyList<MachineTranslationWorkItem> workItems,
        IReadOnlyList<string?> failures,
        IReadOnlyList<TranslationExecutionEvidence?>? executionEvidence = null)
    {
        var results = new TranslationResult[requests.Count];
        for (var requestIndex = 0; requestIndex < requests.Count; requestIndex++)
        {
            var failure = workItems
                .Select((item, index) => item.RequestIndex == requestIndex ? failures[index] : null)
                .FirstOrDefault(static status => status is not null);
            if (failure is not null)
            {
                results[requestIndex] = new TranslationResult(false, string.Empty, failure);
                continue;
            }

            if (ShouldPreserveWithoutModel(requests[requestIndex]))
            {
                results[requestIndex] = new TranslationResult(
                    true,
                    requests[requestIndex].OcrText,
                    "machine-preserved");
                continue;
            }

            if(!protectedTexts[requestIndex].TryReassemble(translatedSegments[requestIndex],out var restored))
            {
                try { onProtectedFailure?.Invoke(requests[requestIndex].OcrText, restored); }
                catch { } // Optional local evidence must not affect translation results.
                results[requestIndex]=new TranslationResult(false,string.Empty,"machine-protected-content-failed",
                    Diagnostic:new TranslationDiagnostic(TranslationDiagnosticKind.ProtectedContentChanged,TranslationDiagnosticStage.Initial));
                continue;
            }
            var mismatch = TranslationContentIntegrity.FindMismatch(requests[requestIndex].OcrText, restored);
            if (mismatch is null)
            {
                if (workItems.Any(item => item.RequestIndex == requestIndex)
                    && requests[requestIndex].OcrText.Any(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
                    && string.Equals(restored.Trim(), requests[requestIndex].OcrText.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    results[requestIndex] = new TranslationResult(false, string.Empty, "machine-output-unchanged");
                    continue;
                }
                results[requestIndex] = new TranslationResult(true, restored, "machine-completed");
                continue;
            }
            try { onIntegrityFailure?.Invoke(requests[requestIndex].OcrText, restored, mismatch.Value); }
            catch { } // Optional local evidence must not affect translation results.
            if (requests[requestIndex].TextKind == TranslationTextKind.Metadata)
            {
                results[requestIndex] = new TranslationResult(
                    true,
                    requests[requestIndex].OcrText,
                    "machine-preserved");
                continue;
            }

            results[requestIndex] = new TranslationResult(
                    false,
                    string.Empty,
                    "machine-content-integrity-failed",
                    Diagnostic: new TranslationDiagnostic(
                        mismatch switch
                        {
                            TranslationInvariantMismatch.Number => TranslationDiagnosticKind.NumericInvariantChanged,
                            TranslationInvariantMismatch.ModelToken => TranslationDiagnosticKind.ModelTokenInvariantChanged,
                            TranslationInvariantMismatch.Hotkey => TranslationDiagnosticKind.HotkeyInvariantChanged,
                            _ => TranslationDiagnosticKind.ProtectedContentChanged,
                        },
                        TranslationDiagnosticStage.Initial));
        }
        if (executionEvidence is not null)
            for (var index = 0; index < results.Length; index++)
            {
                // Only attribute a region when all model segments agree. A bypass
                // or mixed retry result must not inherit another region's engine.
                var evidence = workItems.Select((item, position) => (item, position))
                    .Where(pair => pair.item.RequestIndex == index)
                    .Select(pair => executionEvidence[pair.position]).Distinct().ToArray();
                if (evidence.Length == 1 && evidence[0] is { } matching)
                    results[index] = results[index] with { ExecutionEvidence = matching };
            }
        return results;
    }

    public void Dispose() => (_process as IDisposable)?.Dispose();

    private static bool ShouldPreserveWithoutModel(TranslationRequest request) =>
        IsSimplifiedChinese(request.SourceLanguage)
        || LocalLmtLanguage.Normalize(request.SourceLanguage) != "Japanese" && IsPredominantlyChinese(request.OcrText)
        || CommitIdentifierPattern.IsMatch(request.OcrText.Trim())
        || request.TextKind is TranslationTextKind.IdentityMetadata
            or TranslationTextKind.Hashtag
            or TranslationTextKind.BrandOrIdentifier
        || request.TextKind == TranslationTextKind.UiControl
            && SocialCounterPattern.IsMatch(request.OcrText.Trim());

    private static bool IsPredominantlyChinese(string text)
    {
        var hanCount = text.Count(static character => character is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF');
        if (hanCount == 0 || text.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9d')) return false;
        // Count English words rather than letters: technical names should not
        // turn a Chinese sentence into English source just because they are long.
        var englishWords = Regex.Matches(text, @"[A-Za-z]+(?:['-][A-Za-z]+)*").Count;
        return hanCount >= englishWords;
    }

    private static bool IsEnglish(string value) =>
        value.Equals("auto", StringComparison.OrdinalIgnoreCase)
        || value.Equals("en", StringComparison.OrdinalIgnoreCase)
        || value.Equals("English", StringComparison.OrdinalIgnoreCase);

    private static bool IsSimplifiedChinese(string value) =>
        value.Equals("zh", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Chinese", StringComparison.OrdinalIgnoreCase)
        || value.Equals("简体中文", StringComparison.OrdinalIgnoreCase);

    private sealed record MachineTranslationWorkItem(
        int RequestIndex,
        int SegmentIndex,
        string Text,
        bool ShouldTranslate);
}
