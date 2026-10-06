using System.Net.Http.Headers;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClipRay.App.Security;

namespace ClipRay.App.Translation;

public enum TranslationSemanticOutcome
{
    Failed,
    Translated,
    IntentionallyPreserved,
}

public sealed record TranslationExecutionEvidence(string EngineId, string ModelId,
    string? VerifiedModelSha256, string RequestedBackend,
    string? ActualBackend = null, string? BackendFallbackReason = null);

public sealed record TranslationResult(
    bool IsSuccess,
    string Text,
    string Status,
    DeepSeekCostEstimate? CostEstimate = null,
    TranslationDiagnostic? Diagnostic = null,
    IReadOnlyList<TranslationChunkDiagnostic>? ChunkDiagnostics = null,
    TranslationExecutionEvidence? ExecutionEvidence = null)
{
    public TranslationSemanticOutcome SemanticOutcome => !IsSuccess
        ? TranslationSemanticOutcome.Failed
        : Status is "machine-preserved" or "source-preserved"
            ? TranslationSemanticOutcome.IntentionallyPreserved
            : TranslationSemanticOutcome.Translated;
}

internal sealed record TranslationBlock(string Id, TranslationRequest Request, ProtectedTranslationText ProtectedText);

internal sealed record TranslationContractParseResult(
    bool IsStructuralSuccess,
    IReadOnlyList<TranslationResult> Results,
    TranslationFailureKind FailureKind);

internal static class TranslationBlockContract
{
    public static TranslationBlock[] Create(IReadOnlyList<TranslationRequest> requests) =>
        requests.Select((request, index) => new TranslationBlock($"b{index}", request, ProtectedTranslationText.Create(request.OcrText))).ToArray();

    public static bool TryParse(TranslationBlock[] blocks, string response, out string[] translations)
    {
        translations = [];
        if (!TryParseResults(blocks, response, out var results) || results.Any(static result => !result.IsSuccess))
        {
            return false;
        }
        translations = results.Select(static result => result.Text).ToArray();
        return true;
    }

    public static bool TryParseResults(TranslationBlock[] blocks, string response, out TranslationResult[] results)
    {
        results = [];
        if (!TryNormalizeJsonTransport(response, out var normalized)) return false;
        try
        {
            using var document = JsonDocument.Parse(normalized);
            var entries = document.RootElement;
            if (entries.ValueKind == JsonValueKind.Object
                && entries.TryGetProperty("items", out var wrappedItems))
            {
                if (entries.EnumerateObject().Count() != 1) return false;
                entries = wrappedItems;
            }
            var returned = new Dictionary<string, string>(StringComparer.Ordinal);
            if (entries.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in entries.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.String
                        || !returned.TryAdd(entry.Name, entry.Value.GetString()!)) return false;
                }
            }
            else if (entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object || entry.EnumerateObject().Count() != 2 ||
                        !entry.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                        !entry.TryGetProperty("translation", out var translation) || translation.ValueKind != JsonValueKind.String ||
                        !returned.TryAdd(id.GetString()!, translation.GetString()!)) return false;
                }
            }
            else return false;
            if (returned.Count != blocks.Length) return false;

            results = new TranslationResult[blocks.Length];
            for (var index = 0; index < blocks.Length; index++)
            {
                var block = blocks[index];
                if (!returned.TryGetValue(block.Id, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    results[index] = new TranslationResult(
                        false,
                        string.Empty,
                        "DeepSeek returned an invalid translation block response.");
                    continue;
                }
                if (!block.ProtectedText.TryRestore(value, out var restored))
                {
                    results[index] = new TranslationResult(
                        false,
                        string.Empty,
                        "DeepSeek changed protected translation content.",
                        Diagnostic: new TranslationDiagnostic(
                            TranslationDiagnosticKind.ProtectedContentChanged,
                            TranslationDiagnosticStage.Initial));
                    continue;
                }
                restored = LocalizeRelativeTime(block.Request, restored);
                var semanticFailure = GetSemanticFailure(block, restored);
                if (semanticFailure is not null)
                {
                    results[index] = new TranslationResult(
                        false,
                        string.Empty,
                        semanticFailure,
                        Diagnostic: new TranslationDiagnostic(
                            DiagnosticKindForSemanticFailure(semanticFailure),
                            TranslationDiagnosticStage.Initial));
                    continue;
                }
                results[index] = new TranslationResult(true, restored, "Translation completed.");
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static TranslationContractParseResult ParseResults(TranslationBlock[] blocks, string response)
    {
        if (TryParseResults(blocks, response, out var results))
        {
            return new TranslationContractParseResult(true, results, TranslationFailureKind.None);
        }

        return new TranslationContractParseResult(
            false,
            [],
            ClassifyStructuralFailure(blocks, response));
    }

    private static TranslationFailureKind ClassifyStructuralFailure(TranslationBlock[] blocks, string response)
    {
        if (!TryNormalizeJsonTransport(response, out var normalized)) return TranslationFailureKind.MalformedJson;
        try
        {
            using var document = JsonDocument.Parse(normalized);
            var entries = document.RootElement;
            if (entries.ValueKind == JsonValueKind.Object && entries.TryGetProperty("items", out var wrapped))
            {
                entries = wrapped;
            }

            var expected = blocks.Select(static block => block.Id).ToHashSet(StringComparer.Ordinal);
            var returned = new HashSet<string>(StringComparer.Ordinal);
            if (entries.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in entries.EnumerateObject())
                {
                    if (!expected.Contains(entry.Name) || !returned.Add(entry.Name))
                    {
                        return TranslationFailureKind.DuplicateOrUnknownIds;
                    }
                }
            }
            else if (entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object
                        || !entry.TryGetProperty("id", out var idElement)
                        || idElement.ValueKind != JsonValueKind.String)
                    {
                        return TranslationFailureKind.MalformedJson;
                    }
                    var id = idElement.GetString()!;
                    if (!expected.Contains(id) || !returned.Add(id))
                    {
                        return TranslationFailureKind.DuplicateOrUnknownIds;
                    }
                }
            }
            else
            {
                return TranslationFailureKind.MalformedJson;
            }

            return returned.Count < blocks.Length
                ? TranslationFailureKind.MissingIds
                : TranslationFailureKind.InvalidItem;
        }
        catch (JsonException)
        {
            return TranslationFailureKind.MalformedJson;
        }
    }

    private static bool TryNormalizeJsonTransport(string response, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = response.Trim();
        if (trimmed.Length == 0) return false;
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            normalized = trimmed;
            return true;
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstLineEnd < 0 || closingFence <= firstLineEnd) return false;
        var language = trimmed[3..firstLineEnd].Trim();
        if (language.Length > 0 && !string.Equals(language, "json", StringComparison.OrdinalIgnoreCase)) return false;
        normalized = trimmed[(firstLineEnd + 1)..closingFence].Trim();
        return normalized.Length > 0;
    }

    private static bool IsUnchangedNonTargetSource(TranslationBlock block, string translated)
    {
        if (!block.ProtectedText.HasUnprotectedLetters) return false;
        var request = block.Request;
        if (!string.Equals(request.OcrText.Trim(), translated.Trim(), StringComparison.Ordinal)) return false;
        if (IsPreservableUnchanged(request)) return false;
        if (!LocalLmtLanguage.AreEquivalent(request.SourceLanguage, request.TargetLanguage))
        {
            var source = request.SourceLanguage.Trim();
            if (!string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase) || ContainsNonTargetScript(translated, request.TargetLanguage))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsChangedProtectedOnlySource(TranslationBlock block, string translated) =>
        !block.ProtectedText.HasUnprotectedLetters
        && !string.Equals(block.Request.OcrText.Trim(), translated.Trim(), StringComparison.Ordinal);

    private static string? GetSemanticFailure(TranslationBlock block, string translated)
    {
        if (IsChangedProtectedOnlySource(block, translated))
            return "DeepSeek added text around protected-only content.";
        if (IsUnreadableIdentityMetadata(block.Request, translated))
            return "DeepSeek returned an unreadable display name.";
        if (IsUnchangedNonTargetSource(block, translated))
            return "DeepSeek returned the source text unchanged.";
        if (IsMissingTargetLanguageContent(block, translated))
            return "DeepSeek returned text without target-language content.";
        return null;
    }

    private static TranslationDiagnosticKind DiagnosticKindForSemanticFailure(string failure) => failure switch
    {
        "DeepSeek added text around protected-only content." => TranslationDiagnosticKind.ProtectedContentChanged,
        "DeepSeek returned an unreadable display name." => TranslationDiagnosticKind.SourceLanguageOnly,
        "DeepSeek returned the source text unchanged." => TranslationDiagnosticKind.SourceUnchanged,
        "DeepSeek returned text without target-language content." => TranslationDiagnosticKind.MissingTargetLanguage,
        _ => TranslationDiagnosticKind.SourceLanguageOnly,
    };

    private static bool IsOpaqueTechnicalText(string text)
    {
        var value = text.Trim();
        if (value.Length == 0 || value.IndexOfAny(['\r', '\n']) >= 0) return false;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "file") return true;
        if (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '/' or '\\') return true;
        return !value.Any(char.IsWhiteSpace) && (value.Contains('/') || value.Contains('\\'));
    }

    private static bool IsPreservableUnchanged(TranslationRequest request) =>
        request.TextKind == TranslationTextKind.BrandOrIdentifier
        || IsLatinIdentityWithoutTranslatableTail(request)
        || IsOpaqueTechnicalText(request.OcrText);

    private static bool IsLatinIdentityWithoutTranslatableTail(TranslationRequest request)
    {
        if (request.TextKind != TranslationTextKind.IdentityMetadata) return false;
        var source = request.OcrText.Trim();
        var handle = Regex.Match(source, @"@[A-Za-z0-9_]+", RegexOptions.CultureInvariant);
        if (!handle.Success) return IsLatinDisplayName(source);
        var name = source[..handle.Index].Trim();
        var tail = source[(handle.Index + handle.Length)..].Trim(' ', '·', '•', '.');
        return IsLatinDisplayName(name) && tail.Length == 0;
    }

    private static bool IsLatinDisplayName(string name) =>
        name.Any(char.IsLetter) && name.Where(char.IsLetter).All(IsLatin);

    private static bool IsMissingTargetLanguageContent(TranslationBlock block, string translated)
    {
        if (!block.ProtectedText.HasUnprotectedLetters) return false;
        var request = block.Request;
        if (request.TextKind == TranslationTextKind.General || IsPreservableUnchanged(request)) return false;
        if (!ContainsNonTargetScript(request.OcrText, request.TargetLanguage)) return false;
        if (VisualTranslationRequestFactory.IsKnownOfficialLatinRendering(request.OcrText, translated)) return false;
        return !ContainsTargetScript(translated, request.TargetLanguage);
    }

    private static bool IsUnreadableIdentityMetadata(TranslationRequest request, string translated)
    {
        if (request.TextKind != TranslationTextKind.IdentityMetadata) return false;
        var sourceHandle = request.OcrText.IndexOf('@');
        var translatedHandle = translated.IndexOf('@');
        if (sourceHandle <= 0 || translatedHandle <= 0) return false;
        var sourceName = request.OcrText[..sourceHandle].Trim();
        var translatedName = translated[..translatedHandle].Trim();
        return string.Equals(sourceName, translatedName, StringComparison.Ordinal)
            && !IsLatinDisplayName(sourceName)
            && ContainsNonTargetScript(sourceName, request.TargetLanguage);
    }

    private static string LocalizeRelativeTime(TranslationRequest request, string translated)
    {
        if (request.TextKind != TranslationTextKind.IdentityMetadata) return translated;
        var target = request.TargetLanguage.Trim().ToLowerInvariant();
        var targetIsChinese = target.Contains("chinese", StringComparison.Ordinal)
            || target.Contains("中文", StringComparison.Ordinal)
            || target.Contains("zh", StringComparison.Ordinal);
        if (!targetIsChinese) return translated;
        var sourceAge = Regex.Match(
            request.OcrText,
            @"(?<![\p{L}\p{N}_])(\d+)(mo|[smhdwy])\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!sourceAge.Success) return translated;
        var amount = sourceAge.Groups[1].Value;
        var localized = sourceAge.Groups[2].Value.ToLowerInvariant() switch
        {
            "s" => amount + "秒前",
            "m" => amount + "分钟前",
            "h" => amount + "小时前",
            "d" => amount + "天前",
            "w" => amount + "周前",
            "mo" => amount + "个月前",
            "y" => amount + "年前",
            _ => string.Empty,
        };
        if (localized.Length == 0) return translated;
        var targetAgePattern = $@"(?<!\d){Regex.Escape(amount)}\s*(?:秒|分钟|分鐘|分|小时|小時|天|周|週|星期|个月|個月|月|年|mo|[smhdwy])(?:前)?";
        var matches = Regex.Matches(translated, targetAgePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (matches.Count == 0) return translated;
        var match = matches[^1];
        return translated[..match.Index] + localized + translated[(match.Index + match.Length)..];
    }

    private static bool ContainsTargetScript(string text, string targetLanguage)
    {
        var target = targetLanguage.Trim().ToLowerInvariant();
        if (target.Contains("chinese", StringComparison.Ordinal) || target.Contains("中文", StringComparison.Ordinal) || target.Contains("zh", StringComparison.Ordinal))
            return text.Any(IsCjk);
        if (target is "english" or "en" || target.StartsWith("en-", StringComparison.Ordinal))
            return text.Any(IsBasicLatin);
        if (target is "japanese" or "日本語" or "ja" || target.StartsWith("ja-", StringComparison.Ordinal))
            return text.Any(IsJapanese);
        if (target is "korean" or "한국어" or "ko" || target.StartsWith("ko-", StringComparison.Ordinal))
            return text.Any(IsKorean);
        if (target is "french" or "français" or "fr" || target.StartsWith("fr-", StringComparison.Ordinal))
            return text.Any(IsLatin);
        if (target is "russian" or "русский" or "ru" || target.StartsWith("ru-", StringComparison.Ordinal))
            return text.Any(IsCyrillic);
        return true;
    }

    private static bool ContainsNonTargetScript(string text, string targetLanguage)
    {
        var target = targetLanguage.Trim().ToLowerInvariant();
        var targetIsChinese = target.Contains("chinese", StringComparison.Ordinal) || target.Contains("中文", StringComparison.Ordinal) || target.Contains("zh", StringComparison.Ordinal);
        var targetIsEnglish = target is "english" or "en" || target.StartsWith("en-", StringComparison.Ordinal);
        var targetIsJapanese = target is "japanese" or "日本語" or "ja" || target.StartsWith("ja-", StringComparison.Ordinal);
        var targetIsKorean = target is "korean" or "한국어" or "ko" || target.StartsWith("ko-", StringComparison.Ordinal);
        var targetIsFrench = target is "french" or "français" or "fr" || target.StartsWith("fr-", StringComparison.Ordinal);
        var targetIsRussian = target is "russian" or "русский" or "ru" || target.StartsWith("ru-", StringComparison.Ordinal);
        foreach (var character in text)
        {
            if (!char.IsLetter(character)) continue;
            if (targetIsChinese && IsCjk(character)) continue;
            if (targetIsEnglish && IsBasicLatin(character)) continue;
            if (targetIsJapanese && IsJapanese(character)) continue;
            if (targetIsKorean && IsKorean(character)) continue;
            if (targetIsFrench && IsLatin(character)) continue;
            if (targetIsRussian && IsCyrillic(character)) continue;
            return true;
        }
        return false;
    }

    internal static bool RequiresTargetScriptTranslation(TranslationRequest request) =>
        !IsPreservableUnchanged(request)
        && ContainsNonTargetScript(request.OcrText, request.TargetLanguage);

    internal static bool CanBypassModel(TranslationBlock block) =>
        block.Request.TextKind == TranslationTextKind.BrandOrIdentifier
        || !block.ProtectedText.HasUnprotectedLetters;

    internal static TranslationResult BypassModel(TranslationBlock block) => new(
        true,
        LocalizeRelativeTime(block.Request, block.Request.OcrText),
        "source-preserved");

    private static bool IsBasicLatin(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    private static bool IsLatin(char value) => IsBasicLatin(value)
        || value is >= '\u00C0' and <= '\u024F'
        or >= '\u1E00' and <= '\u1EFF';
    private static bool IsCjk(char value) => value is >= '\u3400' and <= '\u9FFF';
    private static bool IsJapanese(char value) => value is >= '\u3040' and <= '\u30FF' or >= '\u3400' and <= '\u9FFF';
    private static bool IsKorean(char value) => value is >= '\uAC00' and <= '\uD7AF';
    private static bool IsCyrillic(char value) => value is >= '\u0400' and <= '\u052F';

    public static string SerializePrompt(TranslationBlock[] blocks) =>
        JsonSerializer.Serialize(
            blocks.ToDictionary(
                static block => block.Id,
                static block => new
            {
                source_language = block.Request.SourceLanguage,
                target_language = block.Request.TargetLanguage,
                scene = block.Request.Scene,
                text_kind = ToWireName(block.Request.TextKind),
                translation_hint = block.Request.TranslationHint,
                text = block.ProtectedText.Text,
            },
                StringComparer.Ordinal));

    private static string ToWireName(TranslationTextKind kind) => kind switch
    {
        TranslationTextKind.UiControl => "ui_control",
        TranslationTextKind.IdentityMetadata => "identity_metadata",
        TranslationTextKind.BrandOrIdentifier => "brand_or_identifier",
        _ => kind.ToString().ToLowerInvariant(),
    };
}

public interface IDeepSeekTranslationProvider
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, string credentialName, CancellationToken cancellationToken);
    Task<IReadOnlyList<TranslationResult>> TranslateBlocksAsync(IReadOnlyList<TranslationRequest> requests, string credentialName, CancellationToken cancellationToken);
    Task<TranslationResult> TestConnectionAsync(string credentialName, string model, CancellationToken cancellationToken);
    Task<TranslationResult> TestCandidateConnectionAsync(string apiKey, string model, CancellationToken cancellationToken);
}

public sealed class DeepSeekTranslationProvider : IDeepSeekTranslationProvider
{
    private static readonly Uri Endpoint = new("https://api.deepseek.com/chat/completions");
    private readonly ICredentialVault _vault;
    private readonly HttpMessageHandler _handler;
    private readonly TimeSpan _timeout;

    public DeepSeekTranslationProvider(
        ICredentialVault vault,
        HttpMessageHandler? handler = null,
        TimeSpan? timeout = null)
    {
        _vault = vault;
        _handler = handler ?? CreateProductionHandler();
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        string credentialName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var results = await TranslateBlocksAsync([request], credentialName, cancellationToken).ConfigureAwait(false);
        return results[0];
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateBlocksAsync(
        IReadOnlyList<TranslationRequest> requests,
        string credentialName,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0) return [];
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialName);
        var effectiveRequests = requests;
        var visualBatch = IsVisualBatch(effectiveRequests);
        var blocks = TranslationBlockContract.Create(effectiveRequests);
        var costAccumulator = new DeepSeekCostAccumulator();
        var modelIndices = blocks
            .Select(static (block, index) => (block, index))
            .Where(static item => !TranslationBlockContract.CanBypassModel(item.block))
            .Select(static item => item.index)
            .ToArray();
        var merged = blocks
            .Select(static block => TranslationBlockContract.CanBypassModel(block)
                ? TranslationBlockContract.BypassModel(block)
                : new TranslationResult(false, string.Empty, "translation-pending"))
            .ToArray();
        if (modelIndices.Length > 0)
        {
            var modelBlocks = modelIndices.Select(index => blocks[index]).ToArray();
            var modelResults = await TranslationBatchCoordinator.TranslateAsync(
                modelBlocks,
                (batch, token) => TranslateBatchAttemptAsync(batch, credentialName, costAccumulator, token),
                cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < modelIndices.Length; index++)
                merged[modelIndices[index]] = modelResults[index];
        }

        var failedIndices = merged
            .Select((result, index) => (result, index))
            .Where(static item => !item.result.IsSuccess)
            .Select(static item => item.index)
            .ToArray();
        if (ShouldRetryFailedSiblings(merged, failedIndices))
        {
            var failedBlocks = failedIndices.Select(index => blocks[index]).ToArray();
            var focusedRetry = await TranslationBatchCoordinator.TranslateAsync(
                failedBlocks,
                (batch, token) => TranslateBatchAttemptAsync(batch, credentialName, costAccumulator, token),
                cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < failedIndices.Length; index++)
            {
                merged[failedIndices[index]] = focusedRetry[index];
            }
        }
        if (visualBatch) return AttachCostEstimate(merged, costAccumulator);

        var bodyIndices = effectiveRequests
            .Select((request, index) => (request, index))
            .Where(item => item.request.TextKind == TranslationTextKind.Body
                && merged[item.index].IsSuccess
                && TranslationBlockContract.RequiresTargetScriptTranslation(item.request))
            .Select(static item => item.index)
            .ToArray();
        if (bodyIndices.Length == 0) return AttachCostEstimate(merged, costAccumulator);

        var bodyReviewRequests = bodyIndices
            .Select(index => CreateBodyReviewRequest(effectiveRequests[index]))
            .ToArray();
        var bodyReviewBlocks = TranslationBlockContract.Create(bodyReviewRequests);
        var bodyReview = await TranslationBatchCoordinator.TranslateAsync(
            bodyReviewBlocks,
            (batch, token) => TranslateBatchAttemptAsync(batch, credentialName, costAccumulator, token),
            cancellationToken).ConfigureAwait(false);
        var reviewed = bodyReview.ToArray();
        var invalidReviewIndices = reviewed
            .Select((result, index) => (result, index))
            .Where(static item => !item.result.IsSuccess
                && string.Equals(
                    item.result.Status,
                    "DeepSeek returned an invalid translation block response.",
                    StringComparison.Ordinal))
            .Select(static item => item.index)
            .ToArray();
        if (invalidReviewIndices.Length > 0)
        {
            var invalidReviewBlocks = invalidReviewIndices.Select(index => bodyReviewBlocks[index]).ToArray();
            var focusedReview = await TranslationBatchCoordinator.TranslateAsync(
                invalidReviewBlocks,
                (batch, token) => TranslateBatchAttemptAsync(batch, credentialName, costAccumulator, token),
                cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < invalidReviewIndices.Length; index++)
            {
                reviewed[invalidReviewIndices[index]] = focusedReview[index];
            }
        }
        for (var index = 0; index < bodyIndices.Length; index++)
        {
            merged[bodyIndices[index]] = reviewed[index];
        }
        return AttachCostEstimate(merged, costAccumulator);
    }

    private static IReadOnlyList<TranslationResult> AttachCostEstimate(
        IReadOnlyList<TranslationResult> results,
        DeepSeekCostAccumulator accumulator)
    {
        var estimate = accumulator.Snapshot();
        return estimate is null
            ? results
            : results.Select(result => result with { CostEstimate = estimate }).ToArray();
    }

    private static bool IsVisualBatch(IReadOnlyList<TranslationRequest> requests) =>
        requests.Count > 0 && requests.All(static request =>
            request.Scene is "screen_capture" or "social_media");

    private static bool ShouldRetryFailedSiblings(
        IReadOnlyList<TranslationResult> results,
        IReadOnlyList<int> failedIndices) =>
        failedIndices.Count > 0
        && failedIndices.Count < results.Count
        && results.Any(static result => result.IsSuccess);

    private static TranslationRequest CreateBodyReviewRequest(TranslationRequest request) => new(
        request.SourceLanguage,
        request.TargetLanguage,
        request.OcrText,
        request.Model,
        request.TextKind,
        request.Scene,
        request.TranslationHint);

    private async Task<TranslationBatchAttempt> TranslateBatchAttemptAsync(
        IReadOnlyList<TranslationBlock> blocks,
        string credentialName,
        DeepSeekCostAccumulator costAccumulator,
        CancellationToken cancellationToken)
    {
        var payload = SerializeBatchPayload(blocks.ToArray());
        TranslationWireTrace.Record("provider_request", new {
            Provider = "DeepSeek",
            BlockCount = blocks.Count,
            Payload = payload
        });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            var response = await SendAsync(payload, credentialName, timeout.Token).ConfigureAwait(false);
            TranslationWireTrace.Record("provider_response", new {
                Provider = "DeepSeek",
                Content = response.Content,
                response.FinishReason,
                response.Usage
            });
            costAccumulator.Add(blocks[0].Request.Model, response.Usage, DateTimeOffset.UtcNow);
            if (string.IsNullOrWhiteSpace(response.Content))
            {
                return TranslationBatchAttempt.Failed(
                    blocks.Count,
                    "DeepSeek returned no translation blocks.",
                    TranslationFailureKind.MalformedJson);
            }

            var parsed = TranslationBlockContract.ParseResults(blocks.ToArray(), response.Content);
            if (parsed.IsStructuralSuccess)
            {
                return TranslationBatchAttempt.Succeeded(parsed.Results);
            }

            var truncated = string.Equals(response.FinishReason, "length", StringComparison.OrdinalIgnoreCase);
            return TranslationBatchAttempt.Failed(
                blocks.Count,
                truncated
                    ? "DeepSeek response was truncated at the output limit."
                    : "DeepSeek returned an invalid translation block response.",
                truncated ? TranslationFailureKind.Truncated : parsed.FailureKind,
                recoverableStructural: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TranslationWireTrace.Record("provider_failure", new { Category = "cancelled" });
            throw;
        }
        catch (OperationCanceledException)
        {
            TranslationWireTrace.Record("provider_failure", new { Category = "timeout" });
            return TranslationBatchAttempt.Failed(blocks.Count, "Translation timed out.", TranslationFailureKind.Transport);
        }
        catch (DeepSeekCredentialMissingException)
        {
            TranslationWireTrace.Record("provider_failure", new { Category = "credential-missing" });
            return TranslationBatchAttempt.Failed(blocks.Count, "credential-missing", TranslationFailureKind.Transport);
        }
        catch (DeepSeekHttpException exception)
        {
            TranslationWireTrace.Record("provider_failure", new { Category = "http", exception.StatusCode });
            return TranslationBatchAttempt.Failed(blocks.Count, DescribeHttpFailure(exception.StatusCode), TranslationFailureKind.Transport);
        }
        catch (HttpRequestException)
        {
            TranslationWireTrace.Record("provider_failure", new { Category = "network" });
            return TranslationBatchAttempt.Failed(blocks.Count, "network-failed", TranslationFailureKind.Transport);
        }
        catch (JsonException)
        {
            TranslationWireTrace.Record("provider_failure", new { Category = "malformed-json" });
            return TranslationBatchAttempt.Failed(blocks.Count, "DeepSeek returned an invalid translation block response.", TranslationFailureKind.MalformedJson);
        }
        catch (IOException)
        {
            TranslationWireTrace.Record("provider_failure", new { Category = "io" });
            return TranslationBatchAttempt.Failed(blocks.Count, "network-failed", TranslationFailureKind.Transport);
        }
        catch
        {
            TranslationWireTrace.Record("provider_failure", new { Category = "internal" });
            return TranslationBatchAttempt.Failed(blocks.Count, "DeepSeek provider internal failure.", TranslationFailureKind.Transport);
        }
    }

    public async Task<TranslationResult> TestConnectionAsync(
        string credentialName,
        string model,
        CancellationToken cancellationToken)
    {
        DeepSeekModels.Validate(model);
        var result = await TranslateAsync(
            new TranslationRequest("English", "English", "connection test", model),
            credentialName,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? result with { Text = string.Empty, Status = "DeepSeek connection succeeded." }
            : result with { Text = string.Empty };
    }

    public async Task<TranslationResult> TestCandidateConnectionAsync(
        string apiKey,
        string model,
        CancellationToken cancellationToken)
    {
        CredentialSecretPolicy.ValidateCandidate("DeepSeek", apiKey);
        DeepSeekModels.Validate(model);
        var request = new TranslationRequest("English", "English", "connection test", model);
        var block = TranslationBlockContract.Create([request]).Single();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            var response = await SendWithKeyAsync(SerializeBatchPayload([block]), apiKey, timeout.Token).ConfigureAwait(false);
            var parsed = TranslationBlockContract.ParseResults([block], response.Content);
            return parsed.IsStructuralSuccess && parsed.Results.Single().IsSuccess
                ? new TranslationResult(true, string.Empty, "DeepSeek connection succeeded.")
                : new TranslationResult(false, string.Empty, "DeepSeek connection failed without exposing private details.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new TranslationResult(false, string.Empty, "Translation timed out."); }
        catch (DeepSeekHttpException exception) { return new TranslationResult(false, string.Empty, DescribeHttpFailure(exception.StatusCode)); }
        catch (HttpRequestException) { return new TranslationResult(false, string.Empty, "network-failed"); }
        catch { return new TranslationResult(false, string.Empty, "DeepSeek connection failed without exposing private details."); }
    }

    private async Task<DeepSeekAssistantResponse> SendAsync(string payload, string credentialName, CancellationToken cancellationToken)
    {
        // 凭据只在请求即将发出时读取，并仅存活于本次请求对象的生命周期。
        var key = _vault.GetForUse(credentialName);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new DeepSeekCredentialMissingException();
        }

        return await SendWithKeyAsync(payload, key, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DeepSeekAssistantResponse> SendWithKeyAsync(
        string payload,
        string key,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new HttpRequestException("Redirects are not allowed.");
        }
        if (!response.IsSuccessStatusCode) throw new DeepSeekHttpException((int)response.StatusCode);
        return await ReadAssistantContentAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DeepSeekAssistantResponse> ReadAssistantContentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(":", StringComparison.Ordinal)) continue;
            if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var raw = line + Environment.NewLine + await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                return ExtractNonStreamingContent(raw);
            }

            var content = new StringBuilder();
            string? finishReason = null;
            DeepSeekTokenUsage? usage = null;
            AppendStreamingChunk(trimmed[5..].Trim(), content, ref finishReason, ref usage);
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith(":", StringComparison.Ordinal)) continue;
                if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                var data = trimmed[5..].Trim();
                if (string.Equals(data, "[DONE]", StringComparison.Ordinal)) break;
                AppendStreamingChunk(data, content, ref finishReason, ref usage);
            }
            return new DeepSeekAssistantResponse(content.ToString(), finishReason, usage);
        }

        return new DeepSeekAssistantResponse(string.Empty, null, null);
    }

    private static DeepSeekAssistantResponse ExtractNonStreamingContent(string response)
    {
        using var envelope = JsonDocument.Parse(response);
        var choice = envelope.RootElement.GetProperty("choices")[0];
        var content = choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        var finishReason = choice.TryGetProperty("finish_reason", out var finish)
            && finish.ValueKind == JsonValueKind.String
            ? finish.GetString()
            : null;
        return new DeepSeekAssistantResponse(content, finishReason, ExtractUsage(envelope.RootElement));
    }

    private static void AppendStreamingChunk(
        string data,
        StringBuilder content,
        ref string? finishReason,
        ref DeepSeekTokenUsage? usage)
    {
        if (string.Equals(data, "[DONE]", StringComparison.Ordinal)) return;
        using var chunk = JsonDocument.Parse(data);
        usage = ExtractUsage(chunk.RootElement) ?? usage;
        if (!chunk.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return;
        }
        var choice = choices[0];
        if (choice.TryGetProperty("delta", out var delta)
            && delta.TryGetProperty("content", out var deltaContent)
            && deltaContent.ValueKind == JsonValueKind.String)
            content.Append(deltaContent.GetString());
        if (choice.TryGetProperty("finish_reason", out var reason)
            && reason.ValueKind == JsonValueKind.String)
            finishReason = reason.GetString();
    }

    private static DeepSeekTokenUsage? ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;
        var cacheHit = ReadTokenCount(usage, "prompt_cache_hit_tokens");
        var cacheMiss = ReadTokenCount(usage, "prompt_cache_miss_tokens");
        var prompt = ReadTokenCount(usage, "prompt_tokens");
        if (cacheHit == 0 && cacheMiss == 0 && prompt > 0) cacheMiss = prompt;
        return new DeepSeekTokenUsage(
            cacheHit,
            cacheMiss,
            ReadTokenCount(usage, "completion_tokens"));
    }

    private static long ReadTokenCount(JsonElement usage, string propertyName) =>
        usage.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var count)
        && count >= 0
            ? count
            : 0;

    private static string DescribeHttpFailure(int statusCode) => statusCode switch
    {
        401 => "DeepSeek 请求失败（401：凭据无效或未授权）。",
        402 => "DeepSeek 请求失败（402：账户余额不足）。",
        422 => "DeepSeek 请求失败（422：请求参数不被接受）。",
        429 => "DeepSeek 请求失败（429：请求频率受限，请稍后重试）。",
        500 => "DeepSeek 请求失败（500：服务端错误，请稍后重试）。",
        503 => "DeepSeek 请求失败（503：服务繁忙，请稍后重试）。",
        _ => $"DeepSeek 请求失败（HTTP {statusCode}）。",
    };

    private sealed class DeepSeekCredentialMissingException : Exception
    {
    }

    private static string SerializeBatchPayload(TranslationBlock[] blocks)
    {
        var request = blocks[0].Request;
        const string thinkingMode = "disabled";
        const string system = "Translate every input item's text into its target_language, following its scene, text_kind, and translation_hint. Translate every human-readable segment that is not already in the target language, including mixed-language text. Keep segments already in the target language unchanged. Preserve URLs, file paths, code, numbers, formatting, and every [[CLIPRAY_...]] token exactly. Preserve brand names, product names, and trademark names already written in Latin exactly. For non-target-script proper names, follow the item's translation_hint: use an established target-language or official Latin name when the hint supplies one; otherwise translate or transliterate into the target language instead of copying the source script. A leading hashtag marker is formatting: preserve the marker but translate its readable words. For identity_metadata, preserve the protected @handle, make the display name readable as instructed, and localize relative time; do not add labels or explanations. Return exactly one JSON object and nothing else. Its property names must be exactly the input IDs, and each property value must be that item's complete non-empty translated text. Example: {\"b0\":\"translated text\"}.";
        return JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = new[] { new { role = "system", content = system }, new { role = "user", content = TranslationBlockContract.SerializePrompt(blocks) } },
            thinking = new { type = thinkingMode },
            response_format = new { type = "json_object" },
            temperature = 0.2,
            stream = true,
            stream_options = new { include_usage = true },
        });
    }

    public static HttpClientHandler CreateProductionHandler() => new()
    {
        AllowAutoRedirect = false,
    };

    private sealed class DeepSeekHttpException(int statusCode) : Exception
    {
        public int StatusCode { get; } = statusCode;
    }

    private sealed record DeepSeekAssistantResponse(
        string Content,
        string? FinishReason,
        DeepSeekTokenUsage? Usage);

}

public sealed class TranslationRuntimeServices(
    Func<IDeepSeekTranslationProvider> providerFactory,
    Func<ClipRay.App.Ocr.OcrService> ocrFactory)
{
    private readonly Lazy<IDeepSeekTranslationProvider> _provider = new(providerFactory);
    private readonly Lazy<ClipRay.App.Ocr.OcrService> _ocr = new(ocrFactory);

    public IDeepSeekTranslationProvider Provider => _provider.Value;
    public ClipRay.App.Ocr.OcrService Ocr => _ocr.Value;

    public void Dispose()
    {
        if (_ocr.IsValueCreated) _ocr.Value.Dispose();
    }
}
