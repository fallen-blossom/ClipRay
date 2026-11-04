using ClipRay.Core.Geometry;

namespace ClipRay.App.Ocr;

internal static class OcrExecutionPolicy
{
    internal static int SelectThreads(int processorCount) => Math.Clamp(processorCount / 2, 1, 4);
    internal static readonly KeyValuePair<string,string>[] SessionEntries = [
        new("session.intra_op.allow_spinning", "0"), new("session.inter_op.allow_spinning", "0")];
}

internal static class OcrQualityPolicy
{
    // Scores are recognizer output, not calibrated correctness probabilities.
    internal static double? RawConfidence(float[]? scores) => scores is { Length: > 0 }
        && scores.All(float.IsFinite) ? scores.Average(x => (double)x) : null;
    internal static bool NeedsReview(IReadOnlyList<OcrWord> words, int width, int height) => words.Count == 0
        || words.Any(w => w.Evidence?.RawScore is { } score && double.IsFinite(score) && score < .65
            || w.Bounds.Height <= 12 && (w.Bounds.Left <= 2 || w.Bounds.Top <= 2
                || w.Bounds.Right >= width-2 || w.Bounds.Bottom >= height-2));
    internal static IReadOnlyList<OcrWord> Choose(IReadOnlyList<OcrWord> primary, IReadOnlyList<OcrWord> alternative)
    {
        if (primary.Count == 0) return alternative;
        if (alternative.Count == 0) return primary;
        if (alternative.All(word => word.Evidence is { EngineId: "windows-ocr", RawScore: null }))
            return ChooseUnscoredWindows(primary, alternative);
        // Independent geometry must account for every existing line. More text alone never wins.
        var covers = primary.All(p => alternative.Any(a => Overlap(p.Bounds,a.Bounds) >= .6));
        var known = primary.Where(w => w.Evidence?.RawScore is { } v && double.IsFinite(v)).ToArray();
        var next = alternative.Where(w => w.Evidence?.RawScore is { } v && double.IsFinite(v)).ToArray();
        return covers && next.Length == alternative.Count && next.Average(w => w.Evidence!.RawScore!.Value) >= .8
            && (known.Length == 0 || next.Average(w => w.Evidence!.RawScore!.Value) > known.Average(w => w.Evidence!.RawScore!.Value)+.1)
            ? alternative : primary;
    }

    private static IReadOnlyList<OcrWord> ChooseUnscoredWindows(IReadOnlyList<OcrWord> primary, IReadOnlyList<OcrWord> alternative)
    {
        // Windows reports words rather than scored detector lines. Account for a
        // complete line with the envelope of its aligned words, including spaces.
        var matches = primary.Select(line => alternative.Where(word => SameLine(line.Bounds, word.Bounds)).ToArray()).ToArray();
        for (var i = 0; i < primary.Count; i++)
        {
            if (matches[i].Length == 0 || Overlap(primary[i].Bounds, Envelope(matches[i])) < .75
                || matches[i].Sum(word => word.Text.Count(char.IsLetterOrDigit))
                    < primary[i].Text.Count(char.IsLetterOrDigit) * .5) return primary;
        }
        var result = new List<OcrWord>();
        var used = new HashSet<OcrWord>();
        var agreesWithRetained = true;
        for (var i = 0; i < primary.Count; i++)
        {
            foreach (var word in matches[i]) used.Add(word);
            if (primary[i].Evidence?.RawScore is { } score && double.IsFinite(score) && score < .65)
                result.AddRange(matches[i]);
            else
            {
                result.Add(primary[i]);
                agreesWithRetained &= Normalize(primary[i].Text) == Normalize(string.Concat(matches[i].OrderBy(w => w.Bounds.Left).Select(w => w.Text)));
            }
        }
        // Independently detected extra lines are useful only after Windows agrees
        // with every retained main line. Unknown confidence is never invented.
        if (agreesWithRetained)
            result.AddRange(alternative.Where(word => !used.Contains(word)
                && !primary.Any(line => Overlap(word.Bounds, line.Bounds) > .1)));
        return result.OrderBy(word => word.Bounds.Top).ThenBy(word => word.Bounds.Left).ToArray();
    }

    private static bool SameLine(PixelRect line, PixelRect word) =>
        Math.Max(0, Math.Min(line.Bottom, word.Bottom) - Math.Max(line.Top, word.Top))
            / Math.Max(1, Math.Min(line.Height, word.Height)) >= .6
        && word.Left >= line.Left - 4 && word.Right <= line.Right + 4;
    private static PixelRect Envelope(IEnumerable<OcrWord> words)
    {
        var rectangles = words.Select(word => word.Bounds).ToArray();
        var left = rectangles.Min(rect => rect.Left); var top = rectangles.Min(rect => rect.Top);
        return new(left, top, rectangles.Max(rect => rect.Right) - left, rectangles.Max(rect => rect.Bottom) - top);
    }
    private static string Normalize(string text) => string.Concat(text.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    private static double Overlap(PixelRect a, PixelRect b) => Math.Max(0,Math.Min(a.Right,b.Right)-Math.Max(a.Left,b.Left))
        * Math.Max(0,Math.Min(a.Bottom,b.Bottom)-Math.Max(a.Top,b.Top)) / Math.Max(1,a.Width*a.Height);
}

public sealed record OcrRecognitionTrace(int InputWidth,int InputHeight,double Scale,int PreparedWidth,int PreparedHeight,
    IReadOnlyList<OcrLineTrace> RawLines,int RestoredBlockCount,long ElapsedMilliseconds)
{
    public double ServiceWaitMilliseconds { get; init; }
    public double CopyMilliseconds { get; init; }
    public double PrepareMilliseconds { get; init; }
    public double RuntimeMilliseconds { get; init; }
    public double GroupMilliseconds { get; init; }
}
public sealed record OcrLineTrace(PixelRect Bounds,bool IsDetectedLine,string? Engine,string? Model,double? RawScore,int TextLength);
internal sealed record OcrRuntimeStageTrace(string Name, double Milliseconds, int Count = 0);
