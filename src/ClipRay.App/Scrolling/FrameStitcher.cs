using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClipRay.App.Scrolling;

public enum FrameStitchDisposition
{
    Append,
    LowConfidence,
    Repeated,
    Incompatible,
}

public sealed record FrameStitchResult(FrameStitchDisposition Disposition, int OverlapRows, double Confidence)
{
    public static FrameStitchResult LowConfidence { get; } = new(FrameStitchDisposition.LowConfidence, 0, 0);
}

/// <summary>保持像素原样的确定性纵向重叠检测与拼接。</summary>
public sealed class FrameStitcher
{
    private const int MaximumCandidateDisplacement = 1024;
    private readonly int _maximumOverlapRows;
    private readonly int _minimumOverlapRows;

    public FrameStitcher(int maximumOverlapRows = int.MaxValue, int minimumOverlapRows = 8)
    {
        if (maximumOverlapRows < 1) throw new ArgumentOutOfRangeException(nameof(maximumOverlapRows));
        if (minimumOverlapRows < 1 || minimumOverlapRows > maximumOverlapRows) throw new ArgumentOutOfRangeException(nameof(minimumOverlapRows));
        _maximumOverlapRows = maximumOverlapRows;
        _minimumOverlapRows = minimumOverlapRows;
    }

    public FrameStitchResult Analyze(Bitmap earlier, Bitmap next, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(next);
        if (earlier.Width != next.Width || earlier.PixelFormat != next.PixelFormat)
        {
            return new FrameStitchResult(FrameStitchDisposition.Incompatible, 0, 0);
        }

        var earlierPixels = ReadPixels(earlier);
        var nextPixels = ReadPixels(next);
        if (IsExactMatch(earlierPixels, nextPixels, Math.Min(earlier.Height, next.Height), cancellationToken))
        {
            return new FrameStitchResult(FrameStitchDisposition.Repeated, next.Height, 1);
        }
        if (IsNearRepeated(earlierPixels, nextPixels, cancellationToken, out var changedNearFrame))
        {
            return new FrameStitchResult(FrameStitchDisposition.Repeated, next.Height, 0.99);
        }
        if (changedNearFrame) return FrameStitchResult.LowConfidence;

        var maximum = Math.Min(_maximumOverlapRows, Math.Min(earlier.Height, next.Height));
        var candidates = new List<int>();
        var minimumReliableOverlap = Math.Max(_minimumOverlapRows, maximum / 4);
        var lower = Math.Max(minimumReliableOverlap, maximum - MaximumCandidateDisplacement);
        for (var overlap = maximum; overlap >= lower; overlap--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MatchesAnchors(earlierPixels, nextPixels, overlap) && IsExactMatch(earlierPixels, nextPixels, overlap, cancellationToken))
            {
                candidates.Add(overlap);
            }
        }

        if (candidates.Count == 1)
        {
            return new FrameStitchResult(FrameStitchDisposition.Append, candidates[0], 1);
        }

        if (candidates.Count > 1) return FrameStitchResult.LowConfidence;
        var rowRuns = AnalyzeRowRuns(earlierPixels, nextPixels, cancellationToken);
        return rowRuns.Disposition == FrameStitchDisposition.Append
            ? rowRuns
            : AnalyzeSimilarity(earlierPixels, nextPixels, cancellationToken);
    }

    private static bool MatchesAnchors(Pixels earlier, Pixels next, int overlap)
    {
        var start = earlier.Height - overlap;
        foreach (var y in new[] { 0, overlap / 2, overlap - 1 })
        foreach (var x in new[] { 0, earlier.Width / 2, earlier.Width - 1 })
            if (!earlier.EqualsAt(next, x, start + y, y)) return false;
        return true;
    }

    public Bitmap Stitch(Bitmap earlier, Bitmap next, FrameStitchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Disposition != FrameStitchDisposition.Append)
        {
            throw new InvalidOperationException("Only a confirmed overlap can be stitched.");
        }
        if (earlier.Width != next.Width || earlier.PixelFormat != next.PixelFormat || result.OverlapRows <= 0 || result.OverlapRows > next.Height)
        {
            throw new ArgumentException("Frames or overlap are incompatible.");
        }

        var stitched = new Bitmap(earlier.Width, checked(earlier.Height + next.Height - result.OverlapRows), earlier.PixelFormat);
        try
        {
            CopyRows(earlier, 0, stitched, 0, earlier.Height);
            CopyRows(next, result.OverlapRows, stitched, earlier.Height, next.Height - result.OverlapRows);
            return stitched;
        }
        catch
        {
            stitched.Dispose();
            throw;
        }
    }

    private static bool IsExactMatch(Pixels earlier, Pixels next, int overlapRows, CancellationToken cancellationToken)
    {
        var start = earlier.Height - overlapRows;
        for (var y = 0; y < overlapRows; y++)
        for (var x = 0; x < earlier.Width; x++)
        {
            if ((y & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!earlier.EqualsAt(next, x, start + y, y)) return false;
        }
        return true;
    }

    private static bool IsNearRepeated(Pixels earlier, Pixels next, CancellationToken cancellationToken, out bool changedNearFrame)
    {
        changedNearFrame=false;
        if (earlier.Width != next.Width || earlier.Height != next.Height) return false;
        // A blank center cannot witness unchanged edge text. Confirm the whole
        // frame and local columns; slight dispersed noise remains tolerated.
        var bandWidth = Math.Max(8, earlier.Width / 32);
        var bandDifferences = new long[(earlier.Width + bandWidth - 1) / bandWidth];
        var bandSamples = new long[bandDifferences.Length];
        long difference = 0;
        long samples = 0;
        var distinctPixel=false;
        for (var y = 0; y < earlier.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < earlier.Width; x++)
            {
                var delta = earlier.DifferenceAt(next, x, y, y);
                if (delta > 12 || (earlier.BytesPerPixel == 4 && earlier.Bytes[y*earlier.Stride+x*4+3] != next.Bytes[y*next.Stride+x*4+3])) distinctPixel=true;
                difference += delta;
                samples += 3;
                bandDifferences[x/bandWidth] += delta;
                bandSamples[x/bandWidth] += 3;
            }
        }
        var near = difference / (double)Math.Max(1, samples) < 0.25;
        changedNearFrame = near && (distinctPixel || bandDifferences.Select((value,index) => value / (double)Math.Max(1,bandSamples[index])).Any(mean => mean >= 1));
        return near && !changedNearFrame;
    }

    private FrameStitchResult AnalyzeRowRuns(Pixels earlier, Pixels next, CancellationToken cancellationToken)
    {
        if (earlier.Height != next.Height) return FrameStitchResult.LowConfidence;

        var margin = Math.Min(earlier.Width / 3, Math.Max(50, earlier.Width / 20));
        var bandWidth = earlier.Width - margin * 2;
        if (bandWidth <= 0) return FrameStitchResult.LowConfidence;

        var minimumRun = Math.Max(8, _minimumOverlapRows);
        var minimumReliableOverlap = Math.Max(_minimumOverlapRows, earlier.Height / 4);
        var minimumDelta = Math.Max(1, earlier.Height - _maximumOverlapRows);
        var maximumDelta = Math.Min(MaximumCandidateDisplacement, earlier.Height - minimumReliableOverlap);
        var rowRunCandidates = new List<RowRunCandidate>();

        for (var delta = minimumDelta; delta <= maximumDelta; delta++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var overlap = earlier.Height - delta;
            var matchingRows = 0;
            var currentRun = 0;
            var longestRun = 0;
            for (var y = 0; y < overlap; y++)
            {
                if ((y & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (earlier.RowEquals(next, delta + y, y, margin, bandWidth))
                {
                    matchingRows++;
                    currentRun++;
                    longestRun = Math.Max(longestRun, currentRun);
                }
                else
                {
                    currentRun = 0;
                }
            }

            var minimumMatchingRows = Math.Max(minimumRun * 2, overlap / 10);
            if (longestRun >= minimumRun && matchingRows >= minimumMatchingRows)
            {
                rowRunCandidates.Add(new RowRunCandidate(delta, overlap, longestRun, matchingRows));
            }
        }

        if (rowRunCandidates.Count == 0) return FrameStitchResult.LowConfidence;

        rowRunCandidates.Sort(static (left, right) => right.LongestRun.CompareTo(left.LongestRun));
        var best = rowRunCandidates[0];
        if (rowRunCandidates.Count > 1 && best.LongestRun - rowRunCandidates[1].LongestRun < minimumRun)
        {
            return FrameStitchResult.LowConfidence;
        }

        return new FrameStitchResult(
            FrameStitchDisposition.Append,
            best.OverlapRows,
            best.MatchingRows / (double)best.OverlapRows);
    }

    private FrameStitchResult AnalyzeSimilarity(Pixels earlier, Pixels next, CancellationToken cancellationToken)
    {
        if (earlier.Height != next.Height) return FrameStitchResult.LowConfidence;
        var margin = Math.Min(earlier.Width / 3, Math.Max(24, earlier.Width / 10));
        var bandWidth = earlier.Width - margin * 2;
        if (bandWidth < 8) return FrameStitchResult.LowConfidence;

        var minimumReliableOverlap = Math.Max(_minimumOverlapRows, earlier.Height / 4);
        var minimumDelta = Math.Max(1, earlier.Height - _maximumOverlapRows);
        var maximumDelta = Math.Min(MaximumCandidateDisplacement, earlier.Height - minimumReliableOverlap);
        var scores = new List<SimilarityCandidate>(maximumDelta - minimumDelta + 1);
        for (var delta = minimumDelta; delta <= maximumDelta; delta++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var overlap = earlier.Height - delta;
            var yStep = Math.Max(1, overlap / 120);
            var xStep = Math.Max(1, bandWidth / 64);
            long difference = 0;
            long samples = 0;
            for (var y = 0; y < overlap; y += yStep)
            for (var x = margin; x < earlier.Width - margin; x += xStep)
            {
                difference += earlier.DifferenceAt(next, x, delta + y, y);
                samples += 3;
            }
            scores.Add(new SimilarityCandidate(delta, overlap, difference / (double)Math.Max(1, samples)));
        }

        if (scores.Count == 0) return FrameStitchResult.LowConfidence;
        scores.Sort(static (left, right) => left.MeanDifference.CompareTo(right.MeanDifference));
        var best = scores[0];
        var second = scores.Count > 1 ? scores[1] : null;
        if (best.MeanDifference > 10 || second is not null && second.MeanDifference - best.MeanDifference < 0.35)
        {
            return FrameStitchResult.LowConfidence;
        }
        return new FrameStitchResult(
            FrameStitchDisposition.Append,
            best.OverlapRows,
            Math.Clamp(1 - best.MeanDifference / 32, 0, 1));
    }

    private sealed record RowRunCandidate(int Delta, int OverlapRows, int LongestRun, int MatchingRows);
    private sealed record SimilarityCandidate(int Delta, int OverlapRows, double MeanDifference);

    private static Pixels ReadPixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return new Pixels(bitmap.Width, bitmap.Height, Math.Abs(data.Stride), Image.GetPixelFormatSize(bitmap.PixelFormat) / 8, bytes);
        }
        finally { bitmap.UnlockBits(data); }
    }

    private sealed record Pixels(int Width, int Height, int Stride, int BytesPerPixel, byte[] Bytes)
    {
        public bool EqualsAt(Pixels other, int x, int thisY, int otherY)
        {
            var left = thisY * Stride + x * BytesPerPixel;
            var right = otherY * other.Stride + x * other.BytesPerPixel;
            for (var i = 0; i < BytesPerPixel; i++) if (Bytes[left + i] != other.Bytes[right + i]) return false;
            return true;
        }

        public bool RowEquals(Pixels other, int thisY, int otherY, int startX, int width)
        {
            var byteWidth = checked(width * BytesPerPixel);
            var left = thisY * Stride + startX * BytesPerPixel;
            var right = otherY * other.Stride + startX * other.BytesPerPixel;
            return Bytes.AsSpan(left, byteWidth).SequenceEqual(other.Bytes.AsSpan(right, byteWidth));
        }

        public int DifferenceAt(Pixels other, int x, int thisY, int otherY)
        {
            var left = thisY * Stride + x * BytesPerPixel;
            var right = otherY * other.Stride + x * other.BytesPerPixel;
            var channels = Math.Min(3, BytesPerPixel);
            var difference = 0;
            for (var channel = 0; channel < channels; channel++) difference += Math.Abs(Bytes[left + channel] - other.Bytes[right + channel]);
            return difference;
        }
    }

    internal static void CopyRows(Bitmap source, int sourceY, Bitmap destination, int destinationY, int rows)
    {
        if (rows == 0) return;
        var sourceData = source.LockBits(new Rectangle(0, sourceY, source.Width, rows), ImageLockMode.ReadOnly, source.PixelFormat);
        var destinationData = destination.LockBits(new Rectangle(0, destinationY, destination.Width, rows), ImageLockMode.WriteOnly, destination.PixelFormat);
        try
        {
            var bytesPerRow = checked(source.Width * Image.GetPixelFormatSize(source.PixelFormat) / 8);
            var buffer = new byte[bytesPerRow];
            for (var row = 0; row < rows; row++)
            {
                Marshal.Copy(IntPtr.Add(sourceData.Scan0, row * sourceData.Stride), buffer, 0, bytesPerRow);
                Marshal.Copy(buffer, 0, IntPtr.Add(destinationData.Scan0, row * destinationData.Stride), bytesPerRow);
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
            destination.UnlockBits(destinationData);
        }
    }
}
