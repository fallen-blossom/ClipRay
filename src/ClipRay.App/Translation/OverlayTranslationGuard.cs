using ClipRay.App.Ocr;

namespace ClipRay.App.Translation;

public static class OverlayTranslationGuard
{
    public static string? GetFailure(OcrRecognitionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsSuccess) return result.Status;
        return result.Blocks.Count == 0
            ? "未识别到可翻译文字；请缩小选区，或在 Windows 中安装对应的 OCR 语言。"
            : null;
    }
}
