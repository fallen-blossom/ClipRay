using ClipRay.App.Ocr;

internal static class MixedScriptOcrTests
{
    public static void Run()
    {
        var first = new OcrWord("S ...", new(1127, 495, 88, 51)) { IsDetectedLine = true };
        var second = new OcrWord("Jaden Hicks", new(1291, 508, 150, 40)) { IsDetectedLine = true };
        var lines = OcrBlockGrouping.GroupWords([first, second]);
        if (lines.Count != 2 || !ReferenceEquals(lines[0].Words[0], first) || !ReferenceEquals(lines[1].Words[0], second))
            throw new Exception("Independent detector lines were horizontally merged or lost provenance.");
        var words = OcrBlockGrouping.GroupWords([
            new("ordinary", new(20, 20, 80, 20)), new("sentence", new(110, 20, 80, 20))]);
        if (words.Count != 1 || words[0].Text != "ordinary sentence")
            throw new Exception("Word-level OCR no longer forms a sentence line.");
        Check("@mira · 2h", "ミナ @mira · 2h", .97, "cjk", true);
        Check("@mira · 2h", "민아 @mira · 2h", .96, "korean", true);
        Check("@mira · 2h", "ミナ @mira2 · 2h", .97, "cjk", false);
        Check("@mira · 2h", "ミナ @mira · 2h", .3, "cjk", false);
        Check("@mira · 2h", "口 @mira · 2h", .97, "cjk", false);
        Check("Open settings", "Open settings", .99, "cjk", false);
        Check("@mira · 2h", "Mina @mira · 2h", .97, "latin", false);
        Console.WriteLine("Mixed-script completeness and disagreement checks passed.");
    }

    private static void Check(string partial, string full, double confidence, string script, bool chooseFull)
    {
        var result = ScriptOcrSelection.SelectWithReview([
            new(partial, .985, "latin"), new(full, confidence, script)]);
        if (result.Candidate?.Text != (chooseFull ? full : partial)
            || result.AlternativeText != (chooseFull ? partial : null))
            throw new Exception("Mixed-script selection or retained alternative differed from the contract.");
    }
}
