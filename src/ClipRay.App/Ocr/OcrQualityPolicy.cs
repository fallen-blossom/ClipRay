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
        // Independent geometry must account for every existing line. More text alone never wins.
        var covers = primary.All(p => alternative.Any(a => Overlap(p.Bounds,a.Bounds) >= .6));
        var known = primary.Where(w => w.Evidence?.RawScore is { } v && double.IsFinite(v)).ToArray();
        var next = alternative.Where(w => w.Evidence?.RawScore is { } v && double.IsFinite(v)).ToArray();
        return covers && next.Length == alternative.Count && next.Average(w => w.Evidence!.RawScore!.Value) >= .8
            && (known.Length == 0 || next.Average(w => w.Evidence!.RawScore!.Value) > known.Average(w => w.Evidence!.RawScore!.Value)+.1)
            ? alternative : primary;
    }
    private static double Overlap(PixelRect a, PixelRect b) => Math.Max(0,Math.Min(a.Right,b.Right)-Math.Max(a.Left,b.Left))
        * Math.Max(0,Math.Min(a.Bottom,b.Bottom)-Math.Max(a.Top,b.Top)) / Math.Max(1,a.Width*a.Height);
}

public sealed record OcrRecognitionTrace(int InputWidth,int InputHeight,double Scale,int PreparedWidth,int PreparedHeight,
    IReadOnlyList<OcrLineTrace> RawLines,int RestoredBlockCount,long ElapsedMilliseconds);
public sealed record OcrLineTrace(PixelRect Bounds,bool IsDetectedLine,string? Engine,string? Model,double? RawScore,int TextLength);
