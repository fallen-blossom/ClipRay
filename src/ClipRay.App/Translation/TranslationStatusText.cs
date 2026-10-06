using System.Globalization;

namespace ClipRay.App.Translation;

public static class TranslationStatusText
{
    public const string DeepSeekCompleted = "翻译完成 · DeepSeek";
    public const string DeepSeekPrivacyNotice = "云端仅发送 OCR 文本，不上传截图。";
    public const string LocalCompleted = "本地 LMT 翻译完成；未使用网络。";

    public static string FormatOverlayCompletion(
        bool isDeepSeek,
        int changedCount,
        int retainedCount,
        DeepSeekCostEstimate? costEstimate = null)
    {
        var provider = isDeepSeek ? "DeepSeek" : "本地 LMT";
        var cost = FormatCompactCostEstimate(costEstimate);
        return retainedCount == 0
            ? $"翻译完成 · {provider}{cost} · 已替换 {changedCount} 处"
            : $"翻译部分完成 · {provider}{cost} · 已替换 {changedCount} 处，保留原文 {retainedCount} 处";
    }

    public static string FormatOverlayFailure(
        int retainedCount,
        string providerStatus,
        DeepSeekCostEstimate? costEstimate = null) =>
        $"翻译未完成{FormatCompactCostEstimate(costEstimate)} · 保留原文 {retainedCount} 处。{LocalizeProviderStatus(providerStatus)}";

    public static string FormatOverlayStatus(
        bool isDeepSeek,
        int changedCount,
        int retainedCount,
        IReadOnlyList<TranslationResult> results,
        DeepSeekCostEstimate? costEstimate = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        var failure = results.FirstOrDefault(static result => !result.IsSuccess);
        if (failure is null) return FormatOverlayCompletion(isDeepSeek, changedCount, retainedCount, costEstimate);

        var allFailed = results.All(static result => !result.IsSuccess);
        var status = allFailed
            ? FormatOverlayFailure(retainedCount, failure.Status, costEstimate)
            : FormatOverlayCompletion(isDeepSeek, changedCount, retainedCount, costEstimate);
        if (isDeepSeek || failure.Diagnostic is null) return status;

        return allFailed
            ? $"{status}。原因：整段未通过{FormatLocalDiagnostic(failure.Diagnostic)}"
            : $"{status}。另有 {results.Count(static result => !result.IsSuccess)} 段未译：{FormatLocalDiagnostic(failure.Diagnostic)}";
    }

    public static string FormatOverlayStatus(
        TranslationRouteKind route,
        TranslationExecutionTrace? trace,
        int changedCount,
        int retainedCount,
        IReadOnlyList<TranslationResult> results,
        DeepSeekCostEstimate? costEstimate = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        var isDeepSeek = route is TranslationRouteKind.DeepSeekFlash or TranslationRouteKind.DeepSeekPro;
        var provider = route == TranslationRouteKind.Machine
            ? "本地机翻"
            : isDeepSeek ? "DeepSeek" : "本地 LMT";
        var failure = results.FirstOrDefault(static result => !result.IsSuccess);
        var cost = isDeepSeek ? FormatCompactCostEstimate(costEstimate) : string.Empty;
        var status = failure is null
            ? retainedCount == 0
                ? $"翻译完成 · {provider}{cost} · 已替换 {changedCount} 处"
                : $"翻译部分完成 · {provider}{cost} · 已替换 {changedCount} 处，保留原文 {retainedCount} 处"
            : results.All(static result => !result.IsSuccess)
                ? $"翻译未完成{cost} · {provider} · 保留原文 {retainedCount} 处。{LocalizeProviderStatus(failure.Status)}"
                : $"翻译部分完成 · {provider}{cost} · 已替换 {changedCount} 处，保留原文 {retainedCount} 处";

        if (trace is { FallbackAttemptedCount: > 0 })
        {
            if (trace.RecoveredCount > 0) status += $" · 机翻兜底恢复 {trace.RecoveredCount} 处";
            if (trace.FailedCount > 0) status += $"，仍失败 {trace.FailedCount} 处";
        }
        return status;
    }

    public static string FormatCostEstimate(DeepSeekCostEstimate? estimate)
    {
        if (estimate is null) return string.Empty;
        return $"本次预估费用：约 ¥{FormatCny(estimate.EstimatedCny)}（{FormatPriceWindow(estimate.PriceSchedule)}）";
    }

    public static string FormatDeepSeekTooltip(DeepSeekCostEstimate? estimate)
    {
        if (estimate is null) return DeepSeekPrivacyNotice;
        return $"{DeepSeekPrivacyNotice} 费用基于 API 返回的 token usage 与 DeepSeek 官方公开单价估算；实际账单以 DeepSeek 为准。"
            + $"价格时段：{estimate.PriceSchedule}；缓存命中 {estimate.PromptCacheHitTokens}，未命中 {estimate.PromptCacheMissTokens}，输出 {estimate.CompletionTokens}；共 {estimate.RequestCount} 次请求。";
    }

    private static string FormatCompactCostEstimate(DeepSeekCostEstimate? estimate) =>
        estimate is null ? string.Empty : $" · 约 ¥{FormatCny(estimate.EstimatedCny)}（{FormatPriceWindow(estimate.PriceSchedule)}）";

    private static string FormatPriceWindow(string schedule) =>
        schedule.Contains("每次请求", StringComparison.Ordinal)
            ? "按请求时段计价"
            : schedule.Contains("高峰", StringComparison.Ordinal)
            ? "高峰价"
            : schedule.Contains("低谷", StringComparison.Ordinal)
                ? "低谷价"
                : "基准价";

    private static string FormatLocalDiagnostic(TranslationDiagnostic diagnostic)
    {
        var detail = diagnostic.Kind switch
        {
            TranslationDiagnosticKind.ModelUnavailable => "本地模型不可用或完整性验证失败",
            TranslationDiagnosticKind.ProcessFailure => "本地推理进程异常",
            TranslationDiagnosticKind.ProcessStartupFailed => "本地推理进程启动失败",
            TranslationDiagnosticKind.ProcessSessionExited => "本地推理会话已退出",
            TranslationDiagnosticKind.ResponseBoundaryLost => "本地推理响应边界丢失",
            TranslationDiagnosticKind.ResetBoundaryLost => "本地推理清理后边界丢失",
            TranslationDiagnosticKind.EmptyTranslation => "本地模型未返回译文",
            TranslationDiagnosticKind.OutputTooLong => "本地模型返回内容超出安全长度",
            TranslationDiagnosticKind.PromptEcho => "本地模型返回了提示内容",
            TranslationDiagnosticKind.SourceUnchanged => "返回未翻译原文",
            TranslationDiagnosticKind.SourceLanguageOnly => "仍主要为源语言",
            TranslationDiagnosticKind.MissingTargetLanguage => "缺少目标语言内容",
            TranslationDiagnosticKind.ProtectedContentChanged => "未通过受保护内容校验",
            TranslationDiagnosticKind.ProtectedAccountChanged => "未通过受保护账号校验",
            TranslationDiagnosticKind.ProtectedUrlChanged => "未通过受保护链接校验",
            TranslationDiagnosticKind.ProtectedNumericChanged => "未通过受保护数字校验",
            TranslationDiagnosticKind.ProtectedOtherChanged => "未通过受保护标识符校验",
            TranslationDiagnosticKind.NumericInvariantChanged => "未通过数字校验",
            TranslationDiagnosticKind.ModelTokenInvariantChanged => "未通过模型或版本标识符校验",
            TranslationDiagnosticKind.HotkeyInvariantChanged => "未通过快捷键校验",
            _ => "本地翻译失败",
        };
        return diagnostic.Stage is TranslationDiagnosticStage.RetryChunk && diagnostic.RetryChunkIndex is int index
            ? $"第 {index} 个分块{detail}"
            : $"整段{detail}";
    }

    private static string FormatCny(decimal value) =>
        value > 0m && value < 0.000001m
            ? "< 0.000001"
            : value.ToString("0.######", CultureInfo.InvariantCulture);

    public static string LocalizeProviderStatus(string status)
    {
        if (status == "machine-output-unchanged") return "机翻返回了未翻译的原文；已保留原文，可切换模型重试。";
        if (status == "machine-language-pair-unavailable") return "本地机翻仅支持英语到简体中文，请切换目标语言或模型。";
        if (status == "machine-provider-failed") return "本地机翻进程未完成，请重试或切换模型。";
        if (status == "primary-provider-failed") return "主翻译路线异常，机翻兜底也未恢复；原文已保留。";
        if (string.IsNullOrWhiteSpace(status)) return "翻译状态不可用。";
        if (status.StartsWith("Local LMT completed (", StringComparison.Ordinal))
            return "本地 LMT 翻译完成；模型将在本机保留 15 分钟。";
        return status switch
        {
            "Translation completed." => "翻译完成。",
            "Translation timed out." => "翻译请求超时。",
            "credential-missing" => "未找到本机 DeepSeek 凭据。",
            "network-failed" => "DeepSeek 网络连接失败。",
            "DeepSeek returned an invalid translation block response." => "DeepSeek 返回的译文格式无效。",
            "DeepSeek changed protected translation content." => "DeepSeek 改动了受保护内容，已拒绝该译文。",
            "DeepSeek added text around protected-only content." => "DeepSeek 在只应保留的内容周围添加了文字，已拒绝该译文。",
            "DeepSeek returned an unreadable display name." => "DeepSeek 未将显示名称转换为可读形式。",
            "DeepSeek returned the source text unchanged." => "DeepSeek 返回了未翻译的原文。",
            "DeepSeek returned text without target-language content." => "DeepSeek 返回的译文缺少目标语言内容。",
            "DeepSeek returned no translation blocks." => "DeepSeek 未返回可用译文。",
            "DeepSeek response was truncated at the output limit." => "DeepSeek 输出达到长度上限，译文不完整。",
            "DeepSeek batch request failed without exposing private details." => "DeepSeek 批量翻译失败；未显示任何私密信息。",
            "DeepSeek batch recovery reached its bounded attempt limit." => "DeepSeek 翻译恢复次数已达上限。",
            "DeepSeek connection succeeded." => "DeepSeek 连接成功。",
            "DeepSeek connection failed without exposing private details." => "DeepSeek 连接失败；未显示任何私密信息。",
            "Local LMT model is unavailable or failed verification." => "本地模型不可用或完整性验证失败。",
            "Local LMT failed without exposing private text." => "本地模型翻译失败；未显示任何私密文本。",
            "Local LMT bypassed equivalent languages." => "源语言与目标语言相同，已保留原文。",
            "Local deterministic translation completed." => "本地确定性翻译完成。",
            "Local protected text preserved without model inference." => "内容无需模型推理，已在本地保留。",
            "Local LMT returned empty translated text." => "本地模型未返回译文。",
            "Local LMT changed a numeric, model, or hotkey invariant." => "本地模型改变了数字、模型名或快捷键，已拒绝结果。",
            "Local LMT changed a protected local value." => "本地模型改变了受保护内容，已拒绝结果。",
            "Visual translation timed out; original text was retained." => "画面翻译超时，已保留原文。",
            _ => status,
        };
    }

    public static string EnglishProviderStatus(string status) => status switch
    {
        "machine-output-unchanged" => "Machine translation returned the source text unchanged. You can retry with another model.",
        "machine-language-pair-unavailable" => "Local machine translation supports English to Simplified Chinese only.",
        "machine-provider-failed" => "Local machine translation did not complete. Retry or choose another model.",
        "primary-provider-failed" => "The primary provider failed and machine fallback did not recover. Source text was kept.",
        "credential-missing" => "No local DeepSeek credential was found.",
        "network-failed" => "The DeepSeek network connection failed.",
        "" => "Translation status is unavailable.",
        _ when status.StartsWith("Local LMT completed (", StringComparison.Ordinal) =>
            "Local LMT completed. The model stays loaded on this device for 15 minutes.",
        _ => status,
    };
}
