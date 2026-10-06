using System.Drawing;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Ocr;

internal sealed class ScriptSupplementalOcrRuntime(
    IOcrRuntime primary,
    IOcrRuntime supplemental,
    Func<string, bool> belongsToSupplementalScript) : IOcrRuntime, IOcrWarmup, IDisposable
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
        if (primary is IDisposable primaryDisposable) primaryDisposable.Dispose();
        if (supplemental is IDisposable supplementalDisposable) supplementalDisposable.Dispose();
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

        return primary
            .Where(word => !scriptWords.Any(script => SubstantiallyOverlaps(word.Bounds, script.Bounds)))
            .Concat(scriptWords)
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
}
