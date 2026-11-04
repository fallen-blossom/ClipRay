using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;

internal static class OcrReliabilityTests
{
    internal static void Run()
    {
        SupplementalFragmentKeepsCompleteLine();
        CompleteKoreanKeepsGeometry();
        UnscoredWindowsRescuesLowScoreLine();
        WindowsCannotDeleteOrReplaceConfidentContent();
    }

    private static OcrWord Word(string text, PixelRect bounds, double? score = .95, string engine = "rapidocr") =>
        new(text, bounds, new(engine, "synthetic-model", null, score)) { IsDetectedLine = engine != "windows-ocr" };

    private static void SupplementalFragmentKeepsCompleteLine()
    {
        var main = Word("This complete English sentence must stay visible.", new(10, 20, 600, 30));
        foreach (var fragment in new[] {
            Word("한", new(100, 22, 20, 26)),
            Word("한", main.Bounds), // Geometry alone cannot prove complete recognition.
            Word("한국어 문장입니다", main.Bounds, .4) })
        {
            var result = ScriptSupplementalOcr.Merge([main], [fragment], ScriptSupplementalOcr.ContainsHangul);
            Check(result.Count == 1 && result[0] == main, "Supplemental fragment removed or duplicated a complete line.");
        }
    }

    private static void CompleteKoreanKeepsGeometry()
    {
        var main = Word("0|0大44@HM44", new(10, 40, 180, 24));
        var korean = Word("한국어 본문입니다", new(8, 38, 190, 28));
        var separate = Word("안녕하세요", new(10, 90, 100, 24));
        var result = ScriptSupplementalOcr.Merge([main], [korean, separate], ScriptSupplementalOcr.ContainsHangul);
        Check(result.Count == 2 && result[0].Text == korean.Text && result[0].Bounds == korean.Bounds
            && result[1] == separate, "A complete or independent Korean line was lost.");
        var inset = korean with { Bounds = new(12, 41, 176, 22) };
        result = ScriptSupplementalOcr.Merge([main], [inset], ScriptSupplementalOcr.ContainsHangul);
        Check(result.Single().Bounds == main.Bounds, "Replacement shrank the original full-line geometry.");
    }

    private static void UnscoredWindowsRescuesLowScoreLine()
    {
        var main = Word("He11o wor1d", new(10, 10, 180, 24), .5);
        var windows = new[] { Word("Hello", new(10, 10, 80, 24), null, "windows-ocr"),
            Word("world", new(105, 10, 85, 24), null, "windows-ocr"),
            Word("Recovered line", new(10, 60, 180, 24), null, "windows-ocr") };
        var result = OcrQualityPolicy.Choose([main], windows);
        Check(result.Count == 3 && result.All(word => word.Evidence is { EngineId: "windows-ocr", RawScore: null }),
            "Unscored Windows words could not rescue a nonempty low-score primary result.");
        Check(OcrQualityPolicy.Choose([main], [windows[0]]).SequenceEqual([main]),
            "Partial Windows output deleted the rest of a primary line.");
    }

    private static void WindowsCannotDeleteOrReplaceConfidentContent()
    {
        var first = Word("Keep this line", new(10, 10, 180, 24));
        var second = Word("Keep the other line", new(10, 60, 180, 24));
        var mismatch = Word("Wrong replacement", first.Bounds, null, "windows-ocr");
        Check(OcrQualityPolicy.Choose([first, second], [mismatch]).SequenceEqual([first, second]),
            "Windows fallback discarded an uncovered primary line.");
        var extra = Word("New line", new(10, 110, 180, 24), null, "windows-ocr");
        Check(OcrQualityPolicy.Choose([first], [mismatch, extra]).SequenceEqual([first]),
            "Unscored disagreement replaced a confident line or appended unrelated output.");
        var agreement = Word(first.Text, first.Bounds, null, "windows-ocr");
        var recovered = OcrQualityPolicy.Choose([first], [agreement, extra]);
        Check(recovered.SequenceEqual([first, extra]), "Independent matching Windows result did not supplement a missing line.");
        var unknown = mismatch with { Evidence = null };
        Check(OcrQualityPolicy.Choose([first], [unknown]).SequenceEqual([first]), "Unknown confidence was treated as Windows evidence.");
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
