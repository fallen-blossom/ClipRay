using System.Drawing;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Ocr;

internal sealed class ScriptSupplementalOcrRuntime(
    IOcrRuntime primary,
    IOcrRuntime supplemental,
    Func<string, bool> belongsToSupplementalScript) : IOcrRuntime, IOcrWarmup, IDisposable, IAsyncDisposable
{
    public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        IReadOnlyList<OcrWord> primaryWords;
        Exception? primaryFailure = null;
        try
        {
            primaryWords = await primary.RecognizeAsync(bitmap, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            primaryWords = [];
            primaryFailure = exception;
        }

        try
        {
            var supplementalWords = await supplemental.RecognizeAsync(bitmap, cancellationToken).ConfigureAwait(false);
            return ScriptSupplementalOcr.Merge(primaryWords, supplementalWords, belongsToSupplementalScript);
        }
        catch (OperationCanceledException) { throw; }
        catch when (primaryWords.Count > 0)
        {
            return primaryWords;
        }
        catch when (primaryFailure is not null)
        {
            throw new OcrUnavailableException("The local multilingual OCR runtimes are unavailable.");
        }
    }

    public async Task WarmupAsync(CancellationToken cancellationToken)
    {
        if (primary is IOcrWarmup primaryWarmup)
            await primaryWarmup.WarmupAsync(cancellationToken).ConfigureAwait(false);
        if (supplemental is IOcrWarmup supplementalWarmup)
            await supplementalWarmup.WarmupAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _ = DisposeAsync();
    }
    public async ValueTask DisposeAsync()
    {
        try { await OcrRuntimeRelease.ReleaseAsync(primary).ConfigureAwait(false); }
        finally { await OcrRuntimeRelease.ReleaseAsync(supplemental).ConfigureAwait(false); }
    }
}

public static class ScriptSupplementalOcr
{
    public static IReadOnlyList<OcrWord> Merge(
        IReadOnlyList<OcrWord> primary,
        IReadOnlyList<OcrWord> supplemental,
        Func<string, bool> belongsToSupplementalScript)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(supplemental);
        ArgumentNullException.ThrowIfNull(belongsToSupplementalScript);
        var scriptWords = supplemental
            .Where(word => !string.IsNullOrWhiteSpace(word.Text) && belongsToSupplementalScript(word.Text))
            .ToArray();
        if (scriptWords.Length == 0) return primary.ToArray();

        var retained = primary.ToList();
        var accepted = new List<OcrWord>();
        foreach (var script in scriptWords)
        {
            var overlaps = primary.Where(word => SubstantiallyOverlaps(word.Bounds, script.Bounds)).ToArray();
            if (overlaps.Length == 0) { accepted.Add(script); continue; }
            // A supplemental detector can see just a syllable inside an entire main
            // line. Neither delete that line nor add duplicate text over its pixels.
            if (overlaps.Any(word => !CanReplaceCompleteLine(word, script))) continue;
            foreach (var word in overlaps) retained.Remove(word);
            var bounds = overlaps.Select(word => word.Bounds).Append(script.Bounds).Aggregate(Union);
            accepted.Add(script with { Bounds = bounds });
        }
        return retained.Concat(accepted)
            .OrderBy(static word => word.Bounds.Top)
            .ThenBy(static word => word.Bounds.Left)
            .ToArray();
    }

    public static bool ContainsHangul(string text) => text.Any(static character =>
        character is >= '\u1100' and <= '\u11FF'
            or >= '\u3130' and <= '\u318F'
            or >= '\uA960' and <= '\uA97F'
            or >= '\uAC00' and <= '\uD7AF'
            or >= '\uD7B0' and <= '\uD7FF');

    private static bool SubstantiallyOverlaps(PixelRect first, PixelRect second)
    {
        var intersectionWidth = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        var intersectionHeight = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        var intersectionArea = intersectionWidth * intersectionHeight;
        var smallerArea = Math.Min(first.Width * first.Height, second.Width * second.Height);
        return smallerArea > 0 && intersectionArea / smallerArea >= 0.25;
    }

    private static bool CanReplaceCompleteLine(OcrWord primary, OcrWord supplemental)
    {
        var intersectionWidth = Math.Max(0, Math.Min(primary.Bounds.Right, supplemental.Bounds.Right)
            - Math.Max(primary.Bounds.Left, supplemental.Bounds.Left));
        var intersectionHeight = Math.Max(0, Math.Min(primary.Bounds.Bottom, supplemental.Bounds.Bottom)
            - Math.Max(primary.Bounds.Top, supplemental.Bounds.Top));
        var intersection = intersectionWidth * intersectionHeight;
        if (intersection / Math.Max(1, primary.Bounds.Width * primary.Bounds.Height) < .8
            || intersection / Math.Max(1, supplemental.Bounds.Width * supplemental.Bounds.Height) < .6) return false;
        var originalLength = primary.Text.Count(c => !char.IsWhiteSpace(c));
        var replacementLength = supplemental.Text.Count(c => !char.IsWhiteSpace(c));
        if (replacementLength < originalLength * .4) return false;
        // Reject known-poor supplemental output; model scores are not comparable
        // correctness probabilities and do not override the completeness checks.
        return supplemental.Evidence?.RawScore is not { } score || double.IsFinite(score) && score >= .65;
    }

    private static PixelRect Union(PixelRect first, PixelRect second) => new(
        Math.Min(first.Left, second.Left), Math.Min(first.Top, second.Top),
        Math.Max(first.Right, second.Right) - Math.Min(first.Left, second.Left),
        Math.Max(first.Bottom, second.Bottom) - Math.Min(first.Top, second.Top));
}
