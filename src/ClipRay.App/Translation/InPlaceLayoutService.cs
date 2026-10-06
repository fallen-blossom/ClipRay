using System.Drawing;
using System.IO;
using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

public enum InPlaceBackgroundMode
{
    FlatSampled,
    ComplexBacking,
}

public sealed record InPlaceLayoutItem(
    OcrTextBlock SourceBlock,
    string Text,
    PixelRect Bounds,
    InPlaceBackgroundMode BackgroundMode,
    Color BackgroundColor,
    Color ForegroundColor,
    byte[]? BackgroundPatchPng = null,
    SourceVisualStyle? SourceStyle = null,
    TranslationTextLayout? TextLayout = null,
    byte[]? MaskedPatchPng = null,
    VisualTextRegion? SourceRegion = null);

public sealed record InPlaceLayoutRejection(int Region, string Reason);

public sealed record InPlaceLayoutPlan(
    IReadOnlyList<InPlaceLayoutItem> Items,
    IReadOnlyList<InPlaceLayoutRejection> Rejections);

public static class InPlaceLayoutService
{
    public static int CountRequiredOverlays(
        IReadOnlyList<VisualTextRegion> regions,
        IReadOnlyList<TranslationResult> results)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(results);
        if (regions.Count != results.Count)
            throw new ArgumentException("Each visual region must retain one translation result.", nameof(results));
        var kinds = VisualTranslationRequestFactory.ClassifyRegions(regions);
        return regions.Select((region, index) => (region, index)).Count(item =>
            kinds[item.index] != TranslationTextKind.IdentityMetadata
            && results[item.index].IsSuccess
            && results[item.index].Status != "machine-preserved"
            && !string.IsNullOrWhiteSpace(results[item.index].Text)
            && !string.Equals(item.region.SourceText.Trim(), results[item.index].Text.Trim(),
                StringComparison.Ordinal));
    }

    public static bool HasCompleteCoverage(
        IReadOnlyList<VisualTextRegion> regions,
        IReadOnlyList<TranslationResult> results,
        IReadOnlyList<InPlaceLayoutItem> items,
        IReadOnlyList<InPlaceLayoutRejection>? rejections)
    {
        if (regions.Count != results.Count) return false;
        var kinds = VisualTranslationRequestFactory.ClassifyRegions(regions);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        var intentional = new HashSet<int>();
        for (var index = 0; index < regions.Count; index++)
        {
            var source = regions[index].SourceText.Trim();
            var result = results[index];
            var changed = !string.IsNullOrWhiteSpace(result.Text)
                && !string.Equals(source, result.Text.Trim(), StringComparison.Ordinal);
            if (changed && result.Status is ("machine-preserved" or "source-preserved"))
                return false;
            if (!result.IsSuccess || !changed) continue;
            if (kinds[index] == TranslationTextKind.IdentityMetadata)
                intentional.Add(index + 1);
            else
            {
                if (!expected.TryAdd(regions[index].Id, result.Text)) return false;
            }
        }

        var observedIntentional = new HashSet<int>();
        foreach (var rejection in rejections ?? [])
        {
            if (rejection.Reason != "identity-pixels-preserved"
                || !intentional.Contains(rejection.Region)
                || !observedIntentional.Add(rejection.Region))
                return false;
        }
        if (!observedIntentional.SetEquals(intentional)) return false;

        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = item.SourceRegion?.Id;
            if (id is null || !expected.TryGetValue(id, out var text)
                || !observed.Add(id)
                || !string.Equals(item.Text.Trim(), text.Trim(), StringComparison.Ordinal))
                return false;
        }
        return observed.Count == expected.Count;
    }

    public static IReadOnlyList<InPlaceLayoutItem> BuildRegions(
        Bitmap source,
        IReadOnlyList<VisualTextRegion> regions,
        IReadOnlyList<TranslationResult> results) =>
        BuildRegionPlan(source, regions, results).Items;

    public static InPlaceLayoutPlan BuildRegionPlan(
        Bitmap source,
        IReadOnlyList<VisualTextRegion> regions,
        IReadOnlyList<TranslationResult> results)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(results);
        if (regions.Count != results.Count)
        {
            throw new ArgumentException("Each visual region must retain one translation result.", nameof(results));
        }

        var siblingLines = regions.SelectMany(static region => region.Lines).ToArray();
        var kinds = VisualTranslationRequestFactory.ClassifyRegions(regions);
        var candidates = new List<RegionLayoutCandidate>();
        var rejections = new List<InPlaceLayoutRejection>();
        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            var result = results[index];
            if (!result.IsSuccess || result.Status == "machine-preserved" || string.IsNullOrWhiteSpace(result.Text)
                || string.Equals(region.SourceText.Trim(), result.Text.Trim(), StringComparison.Ordinal))
            {
                continue;
            }

            if (kinds[index] == TranslationTextKind.IdentityMetadata)
            {
                rejections.Add(new InPlaceLayoutRejection(index + 1, "identity-pixels-preserved"));
                continue;
            }
            var backing = SampleAverage(source, region.Bounds);
            var style = SourceVisualStyleEstimator.EstimateRegion(source, region, siblingLines, backing);
            // OCR boxes at the edge of adjacent fragments can overlap by a few pixels.
            // A fragment that remains in the source must retain its original ink and color.
            var protectedNeighbors = regions
                .Where((other, otherIndex) => otherIndex != index
                    && (kinds[otherIndex] == TranslationTextKind.IdentityMetadata
                        || !results[otherIndex].IsSuccess
                        || results[otherIndex].Status is "machine-preserved" or "source-preserved"
                        || string.IsNullOrWhiteSpace(results[otherIndex].Text)
                        || string.Equals(other.SourceText.Trim(), results[otherIndex].Text.Trim(),
                            StringComparison.Ordinal)))
                .Select(other => ProtectedNeighborBounds(source, region, other, style, backing))
                .Where(other => other.Left < region.Bounds.Right && other.Right > region.Bounds.Left
                    && other.Top < region.Bounds.Bottom && other.Bottom > region.Bounds.Top)
                .ToArray();
            using var mask = TextReplacementMask.Build(source, region, protectedNeighbors);
            var repaired = MaskedBackgroundRepair.Repair(source, mask);
            if (repaired.Confidence != VisualSafetyConfidence.High || repaired.PatchPng is null)
            {
                rejections.Add(new InPlaceLayoutRejection(index + 1, "unsafe-background"));
                continue;
            }

            if (region.Role == VisualTextRole.Control
                && style.Confidence == VisualStyleConfidence.Low)
            {
                // Compact controls can have sparse pixels whether OCR supplies word boxes or not.
                // The full control mask was already accepted above, so retain a conservative
                // layout instead of silently discarding a deterministic local translation.
                style = style with { Confidence = VisualStyleConfidence.Medium };
            }
            var foreground = ResolveSourceForeground(style, backing);
            style = style with { ForegroundColor = foreground };
            candidates.Add(new RegionLayoutCandidate(index + 1, region, result.Text, repaired, backing, style));
        }

        NormalizePeerTypography(candidates);
        var layouts = candidates
            .Select(static candidate => TranslationTextLayoutPolicy.Fit(
                candidate.Text,
                candidate.Region.Bounds,
                candidate.Style))
            .ToArray();
        for (var index = 0; index < candidates.Count; index++)
        {
            if (layouts[index].ShouldRender
                || candidates[index].Region.Role is not (VisualTextRole.Control or VisualTextRole.Metadata))
            {
                continue;
            }
            layouts[index] = TranslationTextLayoutPolicy.FitCompact(
                candidates[index].Text,
                candidates[index].Region.Bounds,
                candidates[index].Style);
        }
        foreach (var cohort in candidates
            .Select((candidate, index) => (candidate, index))
            .Where(item => item.candidate.CohortId >= 0
                && item.candidate.Region.Role != VisualTextRole.Body
                && layouts[item.index].ShouldRender)
            .GroupBy(static item => item.candidate.CohortId))
        {
            var sharedFontSize = cohort.Min(item => layouts[item.index].FontSizePixels);
            foreach (var item in cohort)
            {
                layouts[item.index] = TranslationTextLayoutPolicy.FitAtMost(
                    item.candidate.Text,
                    item.candidate.Region.Bounds,
                    item.candidate.Style,
                    sharedFontSize);
            }
        }

        var items = new List<InPlaceLayoutItem>();
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var region = candidate.Region;
            var style = candidate.Style;
            var layout = layouts[index];
            if (!layout.ShouldRender || layout.FontSizePixels < TranslationTextLayoutPolicy.MinimumReadableFontSizePixels)
            {
                rejections.Add(new InPlaceLayoutRejection(candidate.RegionIndex,
                    layout.ShouldRender ? "text-too-small" : "text-does-not-fit"));
                continue;
            }

            items.Add(new InPlaceLayoutItem(
                region.Lines[0], candidate.Text, region.Bounds, candidate.Repair.Mode,
                candidate.Backing, style.ForegroundColor,
                null, style, layout, candidate.Repair.PatchPng, region));
        }
        return new InPlaceLayoutPlan(items, rejections);
    }

    private static PixelRect ProtectedNeighborBounds(Bitmap source, VisualTextRegion translated,
        VisualTextRegion preserved, SourceVisualStyle translatedStyle, Color backing)
    {
        // Keep the preserved OCR box intact. Its halo protects antialiasing that
        // OCR missed, except where those pixels still match the translated text's
        // own ink. A wide emoji box must not spare the last glyph of its neighbor.
        var margin = Math.Clamp(Math.Ceiling(preserved.Bounds.Height * 0.12), 3, 6);
        var left = preserved.Bounds.Left - margin;
        var right = preserved.Bounds.Right + margin;
        var top = preserved.Bounds.Top - margin;
        var bottom = preserved.Bounds.Bottom + margin;
        var verticalOverlap = Math.Min(translated.Bounds.Bottom, bottom)
            - Math.Max(translated.Bounds.Top, top);
        if (translatedStyle.Confidence == VisualStyleConfidence.Low || verticalOverlap <= 0)
            return new PixelRect(left, top, right - left, bottom - top);
        var preservedStyle = SourceVisualStyleEstimator.EstimateRegion(source, preserved,
            preserved.Lines, SampleAverage(source, preserved.Bounds));
        // Same-colored ink just outside the neighbor's OCR box has no reliable
        // ownership. Keep its halo; erasing a translated glyph is preferable to
        // deleting a retained name or label without evidence.
        if (preservedStyle.Confidence == VisualStyleConfidence.Low
            || ColorDistance(preservedStyle.ForegroundColor, translatedStyle.ForegroundColor) < 72)
            return new PixelRect(left, top, right - left, bottom - top);

        var yStart = Math.Max(0, (int)Math.Floor(Math.Max(translated.Bounds.Top, top)));
        var yEnd = Math.Min(source.Height, (int)Math.Ceiling(Math.Min(translated.Bounds.Bottom, bottom)));
        var preservedCenter = (preserved.Bounds.Left + preserved.Bounds.Right) / 2;
        var translatedCenter = (translated.Bounds.Left + translated.Bounds.Right) / 2;
        if (preservedCenter >= translatedCenter)
        {
            var xStart = Math.Max(0, (int)Math.Floor(Math.Max(translated.Bounds.Left, left)));
            var xEnd = Math.Min(source.Width, (int)Math.Floor(Math.Min(translated.Bounds.Right, preserved.Bounds.Left)));
            for (var x = xStart; x < xEnd; x++)
            for (var y = yStart; y < yEnd; y++)
                if (IsOwnInk(source.GetPixel(x, y), translatedStyle.ForegroundColor, backing))
                    left = Math.Max(left, x + 1);
            left = Math.Min(left, preserved.Bounds.Left);
        }
        else
        {
            var xStart = Math.Max(0, (int)Math.Ceiling(Math.Max(translated.Bounds.Left, preserved.Bounds.Right)));
            var xEnd = Math.Min(source.Width, (int)Math.Ceiling(Math.Min(translated.Bounds.Right, right)));
            for (var x = xStart; x < xEnd; x++)
            for (var y = yStart; y < yEnd; y++)
                if (IsOwnInk(source.GetPixel(x, y), translatedStyle.ForegroundColor, backing))
                    right = Math.Min(right, x);
            right = Math.Max(right, preserved.Bounds.Right);
        }
        return new PixelRect(left, top, right - left, bottom - top);

        static bool IsOwnInk(Color pixel, Color foreground, Color backing)
        {
            static int DistanceSquared(Color a, Color b)
            {
                var red = a.R - b.R;
                var green = a.G - b.G;
                var blue = a.B - b.B;
                return red * red + green * green + blue * blue;
            }
            return DistanceSquared(pixel, foreground) <= 48 * 48
                && DistanceSquared(pixel, backing) >= 30 * 30;
        }
    }

    private static Color ResolveSourceForeground(SourceVisualStyle style, Color backing)
    {
        // Muted UI text is intentional source evidence. Preserve it whenever the
        // sampled foreground is stable and still visibly distinct from its backing.
        if (style.Confidence != VisualStyleConfidence.Low
            && ContrastRatio(style.ForegroundColor, backing) >= 1.35)
        {
            return style.ForegroundColor;
        }
        return ChooseContrastForeground(backing);
    }

    private static void NormalizePeerTypography(List<RegionLayoutCandidate> candidates)
    {
        var inlineFlowMembers = new HashSet<int>();
        for (var first = 0; first < candidates.Count; first++)
        for (var second = first + 1; second < candidates.Count; second++)
        {
            if (!AreAdjacentInlineFragments(candidates[first], candidates[second])) continue;
            inlineFlowMembers.Add(first);
            inlineFlowMembers.Add(second);
        }

        var visited = new bool[candidates.Count];
        var nextCohortId = 0;
        for (var start = 0; start < candidates.Count; start++)
        {
            if (visited[start]) continue;
            var group = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                group.Add(current);
                for (var candidate = 0; candidate < candidates.Count; candidate++)
                {
                    if (visited[candidate]
                        || !AreSameScalePeers(candidates[current], candidates[candidate]))
                    {
                        continue;
                    }
                    visited[candidate] = true;
                    queue.Enqueue(candidate);
                }
            }
            if (group.Count < 2) continue;
            var glyphHeight = Median(group.Select(index => candidates[index].Style.GlyphHeightPixels));
            var lineHeight = Median(group.Select(index => candidates[index].Style.LineHeightPixels));
            var fontSize = Median(group.Select(index => candidates[index].Style.EstimatedFontSizePixels
                ?? candidates[index].Style.GlyphHeightPixels / TranslationTextLayoutPolicy.VisibleGlyphRatio));
            foreach (var index in group)
            {
                // A peer's paragraph line spacing is not evidence that one
                // OCR line occupies more height than its own ink box. Borrow
                // the peer font size, but fit a single line within its local
                // height instead of shrinking the font to fit borrowed spacing.
                var localLineHeight = candidates[index].Region.Lines.Count == 1
                    ? Math.Min(lineHeight, candidates[index].Region.Bounds.Height)
                    : lineHeight;
                candidates[index] = candidates[index] with
                {
                    CohortId = nextCohortId,
                    Style = candidates[index].Style with
                    {
                        GlyphHeightPixels = glyphHeight,
                        EstimatedFontSizePixels = Math.Round(fontSize * 2) / 2,
                        LineHeightPixels = Math.Max(glyphHeight, localLineHeight),
                        Alignment = inlineFlowMembers.Contains(index)
                            ? SourceTextAlignment.Left
                            : candidates[index].Style.Alignment,
                    },
                };
            }
            nextCohortId++;
        }
    }

    private static bool AreAdjacentInlineFragments(RegionLayoutCandidate first, RegionLayoutCandidate second)
    {
        if (first.Region.Lines.Count != 1 || second.Region.Lines.Count != 1) return false;
        var firstBounds = first.Region.Bounds;
        var secondBounds = second.Region.Bounds;
        var maximumHeight = Math.Max(firstBounds.Height, secondBounds.Height);
        var heightRatio = Math.Min(firstBounds.Height, secondBounds.Height) / Math.Max(1, maximumHeight);
        if (heightRatio < 0.82) return false;

        var firstCenterY = firstBounds.Top + firstBounds.Height / 2;
        var secondCenterY = secondBounds.Top + secondBounds.Height / 2;
        if (Math.Abs(firstCenterY - secondCenterY) > maximumHeight * 0.35) return false;

        var left = firstBounds.Left <= secondBounds.Left ? firstBounds : secondBounds;
        var right = firstBounds.Left <= secondBounds.Left ? secondBounds : firstBounds;
        var gap = right.Left - left.Right;
        return gap >= -maximumHeight * 0.2 && gap <= maximumHeight * 0.5;
    }

    private static bool AreSameScalePeers(RegionLayoutCandidate first, RegionLayoutCandidate second)
    {
        if (first.Region.Role == VisualTextRole.Body && second.Region.Role == VisualTextRole.Body
            && first.Style.FontCategory == second.Style.FontCategory)
        {
            var firstBodyBounds = first.Region.Bounds;
            var secondBodyBounds = second.Region.Bounds;
            var horizontalOverlap = Math.Max(0,
                Math.Min(firstBodyBounds.Right, secondBodyBounds.Right)
                - Math.Max(firstBodyBounds.Left, secondBodyBounds.Left));
            var sameTextColumn = horizontalOverlap >= Math.Min(firstBodyBounds.Width, secondBodyBounds.Width) * .25
                || Math.Abs(firstBodyBounds.Left - secondBodyBounds.Left)
                    <= Math.Max(firstBodyBounds.Height, secondBodyBounds.Height) * 2;
            if (!sameTextColumn) return false;
            var firstSize = first.Style.EstimatedFontSizePixels ?? first.Style.GlyphHeightPixels / .72;
            var secondSize = second.Style.EstimatedFontSizePixels ?? second.Style.GlyphHeightPixels / .72;
            return Math.Min(firstSize,secondSize) / Math.Max(firstSize,secondSize) >= .82;
        }
        // A card title and its account rows can share a left edge and similar
        // OCR box heights while using visibly different type weights. They must
        // not share the smaller row's fitted font size.
        if (first.Style.FontWeight != second.Style.FontWeight
            && !AreAdjacentInlineFragments(first, second))
            return false;
        if(first.Region.Role!=second.Region.Role && !AreAdjacentInlineFragments(first,second)) return false;
        if (first.Region.Lines.Count != 1 || second.Region.Lines.Count != 1) return false;
        var firstBounds = first.Region.Bounds;
        var secondBounds = second.Region.Bounds;
        var boundsRatio = Math.Min(firstBounds.Height, secondBounds.Height)
            / Math.Max(1, Math.Max(firstBounds.Height, secondBounds.Height));
        if (boundsRatio < 0.82) return false;
        var glyphRatio = Math.Min(first.Style.GlyphHeightPixels, second.Style.GlyphHeightPixels)
            / Math.Max(1, Math.Max(first.Style.GlyphHeightPixels, second.Style.GlyphHeightPixels));

        var maximumHeight = Math.Max(firstBounds.Height, secondBounds.Height);
        var firstCenterY = firstBounds.Top + firstBounds.Height / 2;
        var secondCenterY = secondBounds.Top + secondBounds.Height / 2;
        var upper = firstBounds.Top <= secondBounds.Top ? firstBounds : secondBounds;
        var lower = firstBounds.Top <= secondBounds.Top ? secondBounds : firstBounds;
        var verticalGap = lower.Top - upper.Bottom;
        // OCR ink height varies with the letters in a label. Repeated controls
        // in one column are stronger scale evidence when their OCR boxes match.
        var sameColumnStack = first.Region.Role == second.Region.Role
            && boundsRatio >= 0.88
            && glyphRatio >= 0.48
            && Math.Abs(firstBounds.Left - secondBounds.Left) <= maximumHeight * 0.6
            && verticalGap >= -maximumHeight * 0.2
            && verticalGap <= maximumHeight * 6;
        if (sameColumnStack) return true;
        if (glyphRatio < 0.65) return false;

        var centerAligned = Math.Abs(firstCenterY - secondCenterY) <= maximumHeight * 0.35;
        var horizontalGap = Math.Max(firstBounds.Left, secondBounds.Left)
            - Math.Min(firstBounds.Right, secondBounds.Right);
        var sameLineFragments = centerAligned
            && horizontalGap >= -maximumHeight * 0.2
            && horizontalGap <= maximumHeight * 1.5;
        if (sameLineFragments) return true;

        return Math.Abs(firstBounds.Left - secondBounds.Left) <= maximumHeight * 1.2
            && verticalGap >= -maximumHeight * 0.2
            && verticalGap <= maximumHeight * 1.25;
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }

    private sealed record RegionLayoutCandidate(
        int RegionIndex,
        VisualTextRegion Region,
        string Text,
        BackgroundRepairResult Repair,
        Color Backing,
        SourceVisualStyle Style,
        int CohortId = -1);

    public static IReadOnlyList<InPlaceLayoutItem> Build(
        Bitmap source,
        IReadOnlyList<TranslationOverlayBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(blocks);
        var sourceBlocks = blocks.Select(static block => block.SourceBlock).ToArray();
        var accepted = new List<PixelRect>();
        var items = new List<InPlaceLayoutItem>();
        foreach (var block in blocks.Where(static block => block.State == TranslationBlockState.Changed))
        {
            var sampled = SampleAverage(source, block.SourceBlock.Bounds);
            var mode = ChooseBackgroundMode(source, block.SourceBlock.Bounds);
            var style = SourceVisualStyleEstimator.Estimate(source, block.SourceBlock, sourceBlocks, sampled);
            var foreground = ContrastRatio(style.ForegroundColor, sampled) >= 4.5
                ? style.ForegroundColor
                : ChooseContrastForeground(sampled);
            style = style with { ForegroundColor = foreground };
            var layout = TranslationTextLayoutPolicy.Fit(block.Text, block.SourceBlock.Bounds, style);
            if (!layout.ShouldRender || accepted.Any(bounds => HasMaterialOverlap(bounds, block.SourceBlock.Bounds)))
            {
                continue;
            }
            var patch = mode == InPlaceBackgroundMode.ComplexBacking
                ? BuildReconstructedPatch(source, block.SourceBlock.Bounds)
                : null;
            items.Add(new InPlaceLayoutItem(
                block.SourceBlock,
                block.Text,
                block.SourceBlock.Bounds,
                mode,
                sampled,
                foreground,
                patch,
                style,
                layout));
            accepted.Add(block.SourceBlock.Bounds);
        }
        return items;
    }

    public static IReadOnlyList<InPlaceLayoutItem> Build(
        Bitmap source,
        IReadOnlyList<OcrTextBlock> blocks,
        IReadOnlyList<string> translatedTexts)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(translatedTexts);
        if (blocks.Count != translatedTexts.Count)
        {
            throw new ArgumentException("Each OCR block must retain one translated mapping.", nameof(translatedTexts));
        }

        return blocks.Select((block, index) =>
        {
            var sampled = SampleAverage(source, block.Bounds);
            var mode = ChooseBackgroundMode(source, block.Bounds);
            var background = mode == InPlaceBackgroundMode.FlatSampled
                ? sampled
                : sampled;
            var actualBacking = Composite(background, sampled);
            var foreground = TryEstimateForegroundColor(source, block.Bounds, sampled, out var estimated)
                && ContrastRatio(estimated, actualBacking) >= 4.5
                    ? estimated
                    : ChooseContrastForeground(actualBacking);
            var patch = mode == InPlaceBackgroundMode.ComplexBacking
                ? BuildReconstructedPatch(source, block.Bounds)
                : null;
            return new InPlaceLayoutItem(block, translatedTexts[index], block.Bounds, mode, background, foreground, patch);
        }).ToArray();
    }

    private static byte[]? BuildReconstructedPatch(Bitmap source, PixelRect bounds)
    {
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, source.Width);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, source.Height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, source.Height);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0) return null;

        using var patch = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var globalX = left + x;
            var globalY = top + y;
            var vertical = Interpolate(
                SampleVerticalEdge(source, globalX, top - 1, -1),
                SampleVerticalEdge(source, globalX, bottom, 1),
                (y + 0.5) / height);
            var horizontal = Interpolate(
                SampleHorizontalEdge(source, left - 1, globalY, -1),
                SampleHorizontalEdge(source, right, globalY, 1),
                (x + 0.5) / width);
            patch.SetPixel(x, y, Blend(vertical, horizontal, 0.75));
        }

        using var stream = new MemoryStream();
        patch.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    private static Color SampleVerticalEdge(Bitmap source, int x, int startY, int direction)
    {
        var samples = new List<Color>(3);
        for (var index = 0; index < 3; index++)
        {
            var y = startY + index * direction;
            if (x >= 0 && x < source.Width && y >= 0 && y < source.Height) samples.Add(source.GetPixel(x, y));
        }
        return samples.Count == 0 ? Color.Empty : Average(samples);
    }

    private static Color SampleHorizontalEdge(Bitmap source, int startX, int y, int direction)
    {
        var samples = new List<Color>(3);
        for (var index = 0; index < 3; index++)
        {
            var x = startX + index * direction;
            if (x >= 0 && x < source.Width && y >= 0 && y < source.Height) samples.Add(source.GetPixel(x, y));
        }
        return samples.Count == 0 ? Color.Empty : Average(samples);
    }

    private static Color Interpolate(Color first, Color second, double amount)
    {
        if (first.IsEmpty) return second.IsEmpty ? Color.Black : second;
        if (second.IsEmpty) return first;
        return Color.FromArgb(
            255,
            (int)Math.Round(first.R + (second.R - first.R) * amount),
            (int)Math.Round(first.G + (second.G - first.G) * amount),
            (int)Math.Round(first.B + (second.B - first.B) * amount));
    }

    private static Color Blend(Color primary, Color secondary, double primaryWeight) => Color.FromArgb(
        255,
        (int)Math.Round(primary.R * primaryWeight + secondary.R * (1 - primaryWeight)),
        (int)Math.Round(primary.G * primaryWeight + secondary.G * (1 - primaryWeight)),
        (int)Math.Round(primary.B * primaryWeight + secondary.B * (1 - primaryWeight)));

    private static Color Average(IReadOnlyList<Color> colors) => Color.FromArgb(
        255,
        (int)Math.Round(colors.Average(static color => color.R)),
        (int)Math.Round(colors.Average(static color => color.G)),
        (int)Math.Round(colors.Average(static color => color.B)));

    public static InPlaceBackgroundMode ChooseBackgroundMode(Bitmap source, PixelRect bounds)
    {
        var samples = Sample(source, bounds).ToArray();
        if (samples.Length == 0) return InPlaceBackgroundMode.ComplexBacking;
        var dominant = DominantColor(samples);
        var backingSamples = samples.Where(color => ColorDistance(color, dominant) <= 28).ToArray();
        if (backingSamples.Length < samples.Length * 0.6) return InPlaceBackgroundMode.ComplexBacking;
        var mean = backingSamples.Average(Luminance);
        var variance = backingSamples.Average(color => Math.Pow(Luminance(color) - mean, 2));
        return variance <= 144 ? InPlaceBackgroundMode.FlatSampled : InPlaceBackgroundMode.ComplexBacking;
    }

    private static Color SampleAverage(Bitmap source, PixelRect bounds)
    {
        var samples = Sample(source, bounds).ToArray();
        if (samples.Length == 0) return Color.Black;
        return DominantColor(samples);
    }

    private static Color DominantColor(IReadOnlyList<Color> samples)
    {
        var cluster = samples
            .GroupBy(static color => (color.R / 16, color.G / 16, color.B / 16))
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key.Item1)
            .ThenBy(static group => group.Key.Item2)
            .ThenBy(static group => group.Key.Item3)
            .First()
            .ToArray();
        return Color.FromArgb(
            255,
            (int)Math.Round(cluster.Average(static color => color.R)),
            (int)Math.Round(cluster.Average(static color => color.G)),
            (int)Math.Round(cluster.Average(static color => color.B)));
    }

    private static bool TryEstimateForegroundColor(
        Bitmap source,
        PixelRect bounds,
        Color sampledBacking,
        out Color foreground)
    {
        foreground = Color.Empty;
        var pixels = SampleInterior(source, bounds).ToArray();
        if (pixels.Length == 0) return false;

        // OCR bounds contain both ink and anti-aliased backing pixels. The ink
        // is the cluster furthest from the nearby backing color, so use only
        // the strongest outliers instead of averaging the whole rectangle.
        var candidates = pixels
            .Select(color => (Color: color, Distance: ColorDistance(color, sampledBacking)))
            .Where(sample => sample.Distance >= 24)
            .OrderByDescending(sample => sample.Distance)
            .ToArray();
        if (candidates.Length == 0) return false;

        var take = Math.Clamp(candidates.Length / 5, 6, 32);
        var selected = candidates.Take(Math.Min(take, candidates.Length)).ToArray();
        var estimated = Color.FromArgb(
            255,
            (int)Math.Round(selected.Average(static sample => sample.Color.R)),
            (int)Math.Round(selected.Average(static sample => sample.Color.G)),
            (int)Math.Round(selected.Average(static sample => sample.Color.B)));
        // A complex photo can also contain high-contrast pixels. Only carry a
        // color into the replacement when the strongest ink pixels form one
        // stable cluster; otherwise the black/white contrast fallback is safer.
        var spread = selected.Average(sample => ColorDistance(sample.Color, estimated));
        if (spread > 48) return false;
        foreground = estimated;
        return true;
    }

    private static Color ChooseContrastForeground(Color actualBacking) =>
        ContrastRatio(Color.White, actualBacking) >= ContrastRatio(Color.Black, actualBacking)
            ? Color.White
            : Color.Black;

    private static IEnumerable<Color> SampleInterior(Bitmap source, PixelRect bounds)
    {
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, source.Width);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, source.Height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, source.Height);
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
            yield return source.GetPixel(x, y);
    }

    private static double ColorDistance(Color first, Color second)
    {
        var red = first.R - second.R;
        var green = first.G - second.G;
        var blue = first.B - second.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private static IEnumerable<Color> Sample(Bitmap source, PixelRect bounds)
    {
        var innerLeft = Math.Clamp((int)Math.Floor(bounds.Left), 0, source.Width);
        var innerTop = Math.Clamp((int)Math.Floor(bounds.Top), 0, source.Height);
        var innerRight = Math.Clamp((int)Math.Ceiling(bounds.Right), innerLeft, source.Width);
        var innerBottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), innerTop, source.Height);
        var left = Math.Clamp(innerLeft - 3, 0, source.Width - 1);
        var top = Math.Clamp(innerTop - 3, 0, source.Height - 1);
        var right = Math.Clamp(innerRight + 3, left + 1, source.Width);
        var bottom = Math.Clamp(innerBottom + 3, top + 1, source.Height);
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            if (x < innerLeft || x >= innerRight || y < innerTop || y >= innerBottom)
            {
                yield return source.GetPixel(x, y);
            }
        }
    }

    private static double Luminance(Color color) => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;

    private static bool HasMaterialOverlap(PixelRect first, PixelRect second)
    {
        var width = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        var height = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        var overlap = width * height;
        var smaller = Math.Min(first.Width * first.Height, second.Width * second.Height);
        return smaller > 0 && overlap / smaller > 0.2;
    }

    public static Color Composite(Color foreground, Color background)
    {
        var alpha = foreground.A / 255d;
        return Color.FromArgb(
            255,
            (int)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (int)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
            (int)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }

    public static double ContrastRatio(Color first, Color second)
    {
        var lighter = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        var darker = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
