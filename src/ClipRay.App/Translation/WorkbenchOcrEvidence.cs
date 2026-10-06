using System.Globalization;
using ClipRay.App.Ocr;

namespace ClipRay.App.Translation;

public static class WorkbenchOcrEvidence
{
    public static bool NeedsStructureReview(VisualTextRegion region) =>
        region.Confidence == VisualStructureConfidence.Low || region.Role == VisualTextRole.Uncertain
        || SourceWords(region).Any(word => word.Evidence?.AlternativeText is not null);

    private static IEnumerable<OcrWord> SourceWords(VisualTextRegion region) => region.OriginalOcrWords.Count > 0
        ? region.OriginalOcrWords : region.Lines.SelectMany(line => line.Words);

    public static string Describe(VisualTextRegion region, string language)
    {
        string T(string text) => WorkbenchLocalization.Text(text, language);
        var lines = new List<string> { T("本次 OCR 原文（不随修正改变）："), region.SourceText, T("识别来源：") };
        if (region.SharesParentOcrEvidence)
            lines.Add(T("此区域包含手动拆分内容，以下评分和候选沿用父区域的识别记录；未对本区域重新识别。"));
        var words = SourceWords(region).ToArray();
        foreach (var word in words.Where(word => word.Evidence?.AlternativeText is not null))
        {
            lines.Add(language == "en"
                ? "Mixed-script readings differ; check the image. Alternative: " + word.Evidence!.AlternativeText
                : "混合文字候选存在分歧，请核对原图。另一候选：" + word.Evidence!.AlternativeText);
        }
        if (words.Length == 0 || words.Any(word => word.Evidence is null)) lines.Add(T("未提供模型信息"));
        foreach (var group in words.Where(word => word.Evidence is not null)
            .GroupBy(word => (word.Evidence!.EngineId, word.Evidence.ModelId, word.Evidence.Script, word.Evidence.ModelSha256)))
        {
            lines.Add($"{group.Key.EngineId} / {group.Key.ModelId}");
            lines.Add(T("识别脚本：") + (group.Key.Script ?? T("未提供")));
            lines.Add(T("模型摘要（引擎报告，未独立核验）：") + (group.Key.ModelSha256 ?? T("未核验")));
            var scores = group.Select(word => word.Evidence!.RawScore)
                .Where(score => score.HasValue && double.IsFinite(score.Value)).Select(score => score!.Value).ToArray();
            lines.Add(T("原始评分（不是准确率）：") + (scores.Length == 0 ? T("未提供")
                : scores.Min().ToString("G", CultureInfo.InvariantCulture) + " – " + scores.Max().ToString("G", CultureInfo.InvariantCulture)));
            if (scores.Length > 0 && scores.Length < group.Count()) lines.Add(T("部分文字未提供原始评分。"));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
