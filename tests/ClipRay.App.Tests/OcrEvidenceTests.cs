using System.Drawing;
using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;

internal static class OcrEvidenceTests
{
    public static void PreservesRawScoreAndModelThroughGrouping()
    {
        var evidence = OcrWordEvidence.FromScriptCandidate(
            new ScriptOcrCandidate("News", 0.73, "latin"), "latin_PP-OCRv5_rec_mobile_infer.onnx")
            with { ModelSha256 = new string('a', 64) };
        using var bitmap = new Bitmap(120, 60);
        using var service = new OcrService(() => new FixedRuntime(
            new OcrWord("News", new PixelRect(12, 9, 70, 20), evidence)));
        var result = service.RecognizeAsync(bitmap, CancellationToken.None).GetAwaiter().GetResult();
        var actual = result.Blocks.Single().Words.Single().Evidence;
        if (actual is null || actual.EngineId != "rapidocr-common-script"
            || actual.ModelId != "latin_PP-OCRv5_rec_mobile_infer.onnx"
            || actual.Script != "latin" || actual.RawScore != 0.73
            || actual.ModelSha256 != new string('a', 64))
            throw new InvalidOperationException("OCR provenance was lost or rewritten.");
    }

    private sealed class FixedRuntime(OcrWord word) : IOcrRuntime
    {
        public Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<OcrWord>>([word]);
    }
}
