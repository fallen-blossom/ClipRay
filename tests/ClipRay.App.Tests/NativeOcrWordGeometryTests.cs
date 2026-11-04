using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;
using RapidOcrNet;
using SkiaSharp;

internal static class NativeOcrWordGeometryTests
{
    internal static void Run()
    {
        NativeWordResultsPreserveLineAndEvidence();
        NativeWordConfidenceRequiresOriginalCharacterAlignment();
        CtcColumnsMapNonuniformWordsAndRealScores();
        BlankColumnsCanRecoverBoundariesWithoutInventingText();
        InvalidAlignmentKeepsOnlyTheCompleteLine();
        MultiscriptGeometryKeepsEveryCharacter();
    }

    private static readonly OcrWordEvidence Evidence = new("rapidocr-common-script", "synthetic-recognizer", "latin", .9, "synthetic-sha");

    private static void NativeWordResultsPreserveLineAndEvidence()
    {
        var result = new OcrResult { StrRes = "I can read.", TextBlocks = [new TextBlock
        {
            Text = "I can read.", BoxPoints = Points(10, 20, 220, 30),
            Chars = ["I", " ", "c", "a", "n", " ", "r", "e", "a", "d", "."],
            CharScores = [.8f, .01f, .9f, .9f, .9f, .02f, .95f, .95f, .95f, .95f, .95f],
            WordResults = [Native("I", 10, 20, 12, 30, .999f), Native("can", 35, 20, 70, 30, (.01f + .9f + .9f) / 3),
                Native("read.", 120, 20, 110, 30, .123f)]
        }] };
        var actual = RapidOcrRuntime.ConvertResult(result, "synthetic-model", "latin", "actual-sha").Single();
        Check(actual.Text == "I can read." && actual.IsDetectedLine && actual.Bounds == new PixelRect(10, 20, 220, 30),
            "Native word geometry changed the complete detected line.");
        Check(actual.RecognitionWords.Select(word => word.Text).SequenceEqual(["I", "can", "read."])
            && actual.RecognitionWords.All(word => !word.IsDetectedLine && word.Evidence is { EngineId: "rapidocr", ModelId: "synthetic-model", ModelSha256: "actual-sha" }),
            "Native child geometry lost text order or model provenance.");
        Check(Math.Abs(actual.RecognitionWords[0].Evidence!.RawScore!.Value - .8) < .0001,
            "The leading word did not retain its original recognized-character score.");
        Check(Math.Abs(actual.RecognitionWords[1].Evidence!.RawScore!.Value - .9) < .0001
            && Math.Abs(actual.RecognitionWords[2].Evidence!.RawScore!.Value - .95) < .0001,
            "Native word confidence reused upstream scores shifted by whitespace instead of original character ranges.");
        Check(actual.RecognitionWords[0].Bounds.Width != actual.RecognitionWords[1].Bounds.Width,
            "Native word rectangles were replaced by equal character/word widths.");
    }

    private static void NativeWordConfidenceRequiresOriginalCharacterAlignment()
    {
        var boxes = new[] { Native("I", 0, 0, 20, 30, .99f), Native("can", 30, 0, 100, 30, .99f) };
        foreach (var (chars, scores) in new (string[]? Chars, float[]? Scores)[] {
            (null, null), (["Other"], [.95f]), (["I", " ", "c", "a", "n"], [.8f]),
            (["Ican"], [.9f]) })
        {
            var words = NativeOcrWordGeometry.FromNativeWords(boxes, "I can", new(0, 0, 130, 30), Evidence, chars, scores);
            Check(words.Count == 2 && words.All(word => word.Evidence?.RawScore is null),
                "Missing, conflicting, truncated or shared-token confidence was replaced by an invented native word score.");
        }
        var partial = NativeOcrWordGeometry.FromNativeWords(boxes, "I can", new(0, 0, 130, 30), Evidence,
            ["I", " ", "c", "a", "n"], [.8f, .01f, .9f, float.NaN, .9f]);
        Check(Math.Abs(partial[0].Evidence!.RawScore!.Value - .8) < .0001 && partial[1].Evidence?.RawScore is null,
            "Nonfinite confidence either poisoned an unrelated word or became a guessed word score.");
    }

    private static void CtcColumnsMapNonuniformWordsAndRealScores()
    {
        var line = new TextLine
        {
            Chars = "I can read".Select(character => character.ToString()).ToArray(),
            CharCols = [3, 5, 9, 11, 13, 16, 20, 22, 24, 26], ColCount = 32,
            CharScores = [.8f, .01f, .9f, .9f, .9f, .01f, .95f, .95f, .95f, .95f]
        };
        var words = NativeOcrWordGeometry.FromCtcLine(line, new(100, 50, 320, 30), "I can read", Evidence);
        Check(words.Select(word => word.Text).SequenceEqual(["I", "can", "read"]), "CTC word boundaries lost recognized text.");
        Check(words[0].Bounds.Left == 125 && words[0].Bounds.Right == 145
            && words[1].Bounds.Left == 185 && words[1].Bounds.Right == 245
            && words[2].Bounds.Left == 295 && words[2].Bounds.Right == 375,
            "CTC geometry ignored nonuniform model time columns or the crop origin.");
        Check(words.All(word => word.Bounds.Top == 50 && word.Bounds.Height == 30 && word.Evidence?.ModelSha256 == "synthetic-sha")
            && Math.Abs(words[1].Evidence!.RawScore!.Value - .9) < .0001,
            "CTC word confidence included skipped whitespace or lost provenance.");
    }

    private static void BlankColumnsCanRecoverBoundariesWithoutInventingText()
    {
        var line = Line("Icanread", [2, 10, 12, 14, 22, 24, 26, 28]);
        var words = NativeOcrWordGeometry.FromCtcLine(line, new(0, 0, 320, 30), "Icanread", Evidence);
        Check(words.Select(word => word.Text).SequenceEqual(["I", "can", "read"])
            && string.Concat(words.Select(word => word.Text)) == "Icanread",
            "Large CTC blank intervals could not expose boundaries without changing semantic characters.");
        Check(words.Zip(words.Skip(1)).All(pair => pair.First.Bounds.Right <= pair.Second.Bounds.Left),
            "Neighboring CTC word cells overlap.");
    }

    private static void InvalidAlignmentKeepsOnlyTheCompleteLine()
    {
        foreach (var line in new[] {
            new TextLine { Chars = ["I"], CharScores = [.9f], CharCols = null, ColCount = 32 },
            new TextLine { Chars = ["I", "c"], CharScores = [.9f], CharCols = [2, 4], ColCount = 32 },
            Line("Ican", [2, 4, 3, 8]), Line("Ican", [2, 4, 8, 32]) })
            Check(NativeOcrWordGeometry.FromCtcLine(line, new(0, 0, 200, 30), "Ican", Evidence).Count == 0,
                "Malformed CTC alignment invented usable word geometry.");
        Check(NativeOcrWordGeometry.FromNativeWords([Native("can", 0, 0, 200, 30, .99f)], "I can", new(0, 0, 200, 30), Evidence).Count == 0,
            "A native geometry subset silently removed the leading I.");
        Check(NativeOcrWordGeometry.FromCtcLine(Line("Ican", [2, 4, 6, 8]), new(0, 0, 200, 30), "Other text", Evidence).Count == 0,
            "Geometry from a different script candidate was attached to the chosen line.");
    }

    private static void MultiscriptGeometryKeepsEveryCharacter()
    {
        const string text = "中日한글 I";
        var words = NativeOcrWordGeometry.FromCtcLine(Line(text, [2, 6, 10, 14, 17, 22]), new(5, 8, 320, 35), text, Evidence);
        Check(string.Concat(words.Select(word => word.Text)) == "中日한글I"
            && words.All(word => word.Bounds.Left >= 5 && word.Bounds.Right <= 325 && word.Bounds.Height == 35),
            "Multiscript CTC projection lost characters or escaped its crop.");
    }

    private static TextLine Line(string text, int[] columns) => new()
    { Chars = text.Select(character => character.ToString()).ToArray(), CharScores = Enumerable.Repeat(.9f, text.Length).ToArray(), CharCols = columns, ColCount = 32 };
    private static WordBox Native(string text, int left, int top, int width, int height, float score) =>
        new() { Text = text, BoxPoints = Points(left, top, width, height), Score = score };
    private static SKPointI[] Points(int left, int top, int width, int height) =>
        [new(left, top), new(left + width, top), new(left + width, top + height), new(left, top + height)];
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
