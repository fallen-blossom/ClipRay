using System.Drawing;
using System.Text.RegularExpressions;
using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

public enum VisualTextRole
{
    Body,
    Control,
    Heading,
    Metadata,
    Uncertain,
    IdentityMetadata,
}

public enum VisualStructureConfidence
{
    Low,
    Medium,
    High,
}

public sealed record VisualTextRegion(
    string Id,
    string SourceText,
    IReadOnlyList<OcrTextBlock> Lines,
    PixelRect Bounds,
    VisualTextRole Role,
    VisualStructureConfidence Confidence)
{
    public IReadOnlyList<OcrWord> OriginalOcrWords { get; init; } = Array.Empty<OcrWord>();
    public bool SharesParentOcrEvidence { get; init; }
}

public static class VisualTextRegionBuilder
{
    private const double MaterialColorDistance = 72;

    public static IReadOnlyList<VisualTextRegion> Build(IReadOnlyList<OcrTextBlock> blocks) =>
        BuildCore(blocks, null);

    public static IReadOnlyList<VisualTextRegion> Build(IReadOnlyList<OcrTextBlock> blocks, Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return BuildCore(blocks, source);
    }

    private static IReadOnlyList<VisualTextRegion> BuildCore(
        IReadOnlyList<OcrTextBlock> blocks,
        Bitmap? source)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var origins = new Dictionary<OcrTextBlock, IReadOnlyList<OcrWord>>(ReferenceEqualityComparer.Instance);
        foreach (var block in blocks) origins[block] = Array.AsReadOnly(block.Words.ToArray());
        IEnumerable<OcrTextBlock> Track(OcrTextBlock parent, IEnumerable<OcrTextBlock> children)
        {
            foreach (var child in children)
            {
                origins[child] = origins[parent];
                yield return child;
            }
        }
        var ordered = blocks
            .SelectMany(block => Track(block, RemoveLeadingDecorativeGlyph(block)))
            .SelectMany(block => Track(block, source is null
                ? [block]
                : [TrimLeadingSeparatedDecoration(source, block)]))
            .SelectMany(block => Track(block, SplitDistantControlLabels(block)))
            .SelectMany(block => Track(block, [source is null
                ? block
                : TrimTrailingColoredDecoration(source, block, blocks)]))
            .SelectMany(block => Track(block, SplitTranslationLinkPrefix(block)))
            .SelectMany(block => Track(block, source is null
                ? [block]
                : SplitStableColorSpans(source, block, blocks)))
            .SelectMany(block => Track(block, SplitInlineIdentityBadge(block)))
            .Where(static block => !string.IsNullOrWhiteSpace(block.Text) && !block.Bounds.IsEmpty)
            .OrderBy(static block => block.Bounds.Top)
            .ThenBy(static block => block.Bounds.Left)
            .ToArray();
        if (ordered.Length == 0) return [];

        var identityNames = ordered.Where(name => IsAdjacentDisplayName(name, ordered)).ToHashSet();

        var medianHeight = Median(ordered.Select(static block => block.Bounds.Height));
        var likelyBodyLeft = Median(ordered
            .Where(static block => block.Text.Length >= 20)
            .Select(static block => block.Bounds.Left)
            .DefaultIfEmpty(ordered.Min(static block => block.Bounds.Left)));
        var rowMembers = ordered
            .Where(block => ordered.Count(candidate => !ReferenceEquals(candidate, block) && IsHorizontalPeer(block, candidate)) >= 2)
            .ToHashSet();
        Dictionary<OcrTextBlock, SourceVisualStyle>? styles = null;
        if (source is not null)
        {
            styles = new Dictionary<OcrTextBlock, SourceVisualStyle>(ReferenceEqualityComparer.Instance);
            foreach (var block in ordered)
            {
                styles[block] = SourceVisualStyleEstimator.Estimate(
                    source,
                    block,
                    ordered,
                    SampleBacking(source, block.Bounds));
            }
        }
        var groups = new List<List<OcrTextBlock>>();
        foreach (var block in ordered)
        {
            var target = groups
                .Where(group => !identityNames.Contains(block) && !identityNames.Contains(group[^1]))
                .Where(group => CanJoin(
                    group[^1], block, medianHeight, rowMembers.Contains(block), styles))
                .Where(group => !ordered.Any(middle => !ReferenceEquals(middle, block)
                    && middle.Bounds.Top > group[^1].Bounds.Top && middle.Bounds.Top < block.Bounds.Top
                    && Math.Min(middle.Bounds.Right, block.Bounds.Right) > Math.Max(middle.Bounds.Left, block.Bounds.Left)))
                .OrderBy(group => block.Bounds.Top - group[^1].Bounds.Bottom)
                .ThenBy(group => Math.Abs(block.Bounds.Left - group[^1].Bounds.Left))
                .FirstOrDefault();
            if (target is null)
            {
                groups.Add([block]);
            }
            else
            {
                target.Add(block);
            }
        }

        return groups
            .OrderBy(static group => group.Min(static line => line.Bounds.Top))
            .ThenBy(static group => group.Min(static line => line.Bounds.Left))
            .Select((group, index) => (identityNames.Contains(group[0])
                ? CreateRegion(index, group, medianHeight, likelyBodyLeft) with { Role = VisualTextRole.IdentityMetadata }
                : CreateRegion(index, group, medianHeight, likelyBodyLeft)) with
                { OriginalOcrWords = Array.AsReadOnly(group.SelectMany(line => origins[line]).Distinct().ToArray()) })
            .ToArray();
    }

    private static bool IsAdjacentDisplayName(OcrTextBlock name, IReadOnlyList<OcrTextBlock> blocks)
    {
        var text = name.Text.Trim();
        if (text.Length is < 1 or > 64 || text.Contains('@')
            || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 6
            || text[^1] is '.' or '?' or '!' or '。' or '？' or '！') return false;
        return blocks.Any(account => !ReferenceEquals(account, name)
            && Regex.IsMatch(account.Text.TrimStart(), @"^@[\p{L}\p{N}_]+(?:\s|[·•。．.]|$)")
            && account.Bounds.Left >= name.Bounds.Right - name.Bounds.Height * 0.2
            && account.Bounds.Left - name.Bounds.Right <= Math.Max(name.Bounds.Height, account.Bounds.Height) * 2.5
            && Math.Abs((name.Bounds.Top + name.Bounds.Height / 2)
                - (account.Bounds.Top + account.Bounds.Height / 2)) <= Math.Max(name.Bounds.Height, account.Bounds.Height) * 0.35);
    }

    private static IEnumerable<OcrTextBlock> SplitStableColorSpans(
        Bitmap source,
        OcrTextBlock block,
        IReadOnlyList<OcrTextBlock> siblings)
    {
        if (IsTranslationLink(block.Text)) return [block];
        var words = ResolveWords(block)
            .Where(static word => !string.IsNullOrWhiteSpace(word.Text) && !word.Bounds.IsEmpty)
            .OrderBy(static word => word.Bounds.Left)
            .ToArray();
        if (words.Length < 2) return [block];

        var styled = words.Select(word =>
        {
            var wordBlock = new OcrTextBlock(word.Text, word.Bounds, [word]);
            var wordBacking = SampleBacking(source, word.Bounds);
            return (Word: word, Style: SourceVisualStyleEstimator.Estimate(source, wordBlock, siblings, wordBacking));
        }).ToArray();
        var groups = new List<List<OcrWord>>();
        var currentGroup = new List<OcrWord> { styled[0].Word };
        var pendingUncertain = new List<OcrWord>();
        SourceVisualStyle? stableAnchor = styled[0].Style.Confidence == VisualStyleConfidence.Low
            ? null
            : styled[0].Style;
        for (var index = 1; index < styled.Length; index++)
        {
            var current = styled[index].Style;
            if (current.Confidence == VisualStyleConfidence.Low)
            {
                pendingUncertain.Add(styled[index].Word);
                continue;
            }

            var stableChange = stableAnchor is not null
                && HasMaterialColorChange(stableAnchor.ForegroundColor, current.ForegroundColor);
            if (stableChange)
            {
                groups.Add(currentGroup);
                currentGroup = [.. pendingUncertain, styled[index].Word];
            }
            else
            {
                currentGroup.AddRange(pendingUncertain);
                currentGroup.Add(styled[index].Word);
            }
            pendingUncertain.Clear();
            stableAnchor = current;
        }
        currentGroup.AddRange(pendingUncertain);
        groups.Add(currentGroup);
        if (groups.Count == 1) return [block];
        var split = groups.Select(CreateBlockFromWords).ToArray();
        var backing = SampleBacking(source, block.Bounds);
        return block.Words.Count < 2
            ? RefineSyntheticSpanBounds(source, block, split, siblings, backing)
            : split;
    }

    private static IEnumerable<OcrTextBlock> RemoveLeadingDecorativeGlyph(OcrTextBlock block)
    {
        if (block.Words.Count < 2)
        {
            var attachedToken = block.Text.Trim();
            if (attachedToken.Length >= 4 && attachedToken[0] is '问' or '回'
                && !attachedToken.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9d')
                && attachedToken[1..].Count(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z') >= 3)
            {
                var text = attachedToken[1..];
                return [new OcrTextBlock(text, block.Bounds, [new OcrWord(text, block.Bounds)])];
            }
            return [block];
        }
        var words = block.Words.OrderBy(static word => word.Bounds.Left).ToArray();
        var first = words[0];
        var next = words[1];
        var token = first.Text.Trim();
        var nextText = next.Text.Trim();
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(nextText)) return [block];

        var compact = first.Bounds.Width <= first.Bounds.Height * 2.2;
        var gap = next.Bounds.Left - first.Bounds.Right;
        var nearNext = gap >= -first.Bounds.Height * 0.2
            && gap <= Math.Max(first.Bounds.Height, next.Bounds.Height) * 1.6;
        var nextLooksLikeText = nextText.Count(char.IsLetterOrDigit) >= 3
            || nextText.Count(IsCjk) >= 2;
        var symbolLike = token.Length <= 3
            && token.Any(static value => !char.IsLetterOrDigit(value) && value is not '_' and not '#' and not '@');
        var looksIconSized = first.Bounds.Width >= first.Bounds.Height * 0.9;
        var isolatedSingleGlyph = token.Length == 1
            && !char.IsDigit(token[0])
            && looksIconSized
            && gap >= first.Bounds.Height * 0.35;
        if (!compact || !nearNext || !nextLooksLikeText || (!symbolLike && !isolatedSingleGlyph))
        {
            return [block];
        }

        return [CreateBlockFromWords(words[1..])];
    }

    private static OcrTextBlock TrimLeadingSeparatedDecoration(Bitmap source, OcrTextBlock block)
    {
        // Some recognizers return one word whose rectangle includes a separate
        // pictogram. The word may be "Search" or even "Q Search" while the
        // first ink island is the icon. Retain only the text island for masking
        // and placement; the original OCR word remains available as evidence.
        if (block.Words.Count != 1 || block.Bounds.IsEmpty) return block;
        var text = block.Text.Trim();
        var iconPrefix = Regex.Match(text, @"^(?<icon>\S)\s+(?<label>\S.{2,})$");
        if (text.Any(char.IsWhiteSpace)
            && (!iconPrefix.Success || !IsPictogramOcrGlyph(iconPrefix.Groups["icon"].Value[0])))
            return block;
        var label = iconPrefix.Success ? iconPrefix.Groups["label"].Value.Trim() : text;
        if (label.Count(char.IsLetterOrDigit) < 3) return block;

        var left = Math.Clamp((int)Math.Floor(block.Bounds.Left), 0, source.Width);
        var top = Math.Clamp((int)Math.Floor(block.Bounds.Top), 0, source.Height);
        var right = Math.Clamp((int)Math.Ceiling(block.Bounds.Right), left, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(block.Bounds.Bottom), top, source.Height);
        if (right - left < block.Bounds.Height * 2 || bottom <= top) return block;
        var backing = SampleBacking(source, block.Bounds);
        var inkColumns = new bool[right - left];
        for (var x = left; x < right; x++)
        for (var y = top; y < bottom; y++)
        {
            if (ColorDistance(source.GetPixel(x, y), backing) >= 30)
            {
                inkColumns[x - left] = true;
                break;
            }
        }

        var first = Array.FindIndex(inkColumns, static occupied => occupied);
        if (first < 0 || first > inkColumns.Length * 0.35) return block;
        var firstEnd = first;
        while (firstEnd + 1 < inkColumns.Length && inkColumns[firstEnd + 1]) firstEnd++;
        var next = firstEnd + 1;
        while (next < inkColumns.Length && !inkColumns[next]) next++;
        if (next == inkColumns.Length) return block;
        var iconWidth = firstEnd - first + 1;
        var gap = next - firstEnd - 1;
        var last = Array.FindLastIndex(inkColumns, static occupied => occupied);
        var height = block.Bounds.Height;
        if (iconWidth < height * 0.35 || iconWidth > height * 0.95
            || gap < Math.Max(7, height * 0.2)
            || gap < iconWidth * 0.4
            || last - next + 1 < height * 1.35)
            return block;

        var textLeft = Math.Max(block.Bounds.Left, left + next - 2);
        var bounds = new PixelRect(textLeft, block.Bounds.Top,
            Math.Max(1, block.Bounds.Right - textLeft), block.Bounds.Height);
        return new OcrTextBlock(label, bounds, [block.Words[0] with { Text = label, Bounds = bounds }]);
    }

    private static bool IsPictogramOcrGlyph(char value) =>
        value is 'Q' or 'O' or '0' or '口' || !char.IsLetterOrDigit(value);

    private static IEnumerable<OcrTextBlock> SplitDistantControlLabels(OcrTextBlock block)
    {
        var labels = block.Words
            .OrderBy(static word => word.Bounds.Left)
            .Where(static word => IsSubstantialLabel(word.Text))
            .Select(static word => word with { Text = word.Text.Trim() })
            .ToArray();
        if (labels.Length < 2) return [block];

        var minimumGap = Math.Max(36d, labels.Max(static word => word.Bounds.Height) * 1.4);
        var hasControlGap = labels
            .Zip(labels.Skip(1))
            .Any(pair => pair.Second.Bounds.Left - pair.First.Bounds.Right >= minimumGap);
        if (!hasControlGap) return [block];

        // OCR may merge a row of separate icon-and-label chips into one block.
        // Translate only the independently bounded label words, never icon noise or the row.
        return labels.Select(static word => new OcrTextBlock(word.Text, word.Bounds, [word]));
    }

    private static bool IsSubstantialLabel(string value) =>
        value.Trim().Count(char.IsLetterOrDigit) >= 2;

    private static OcrTextBlock TrimTrailingColoredDecoration(
        Bitmap source,
        OcrTextBlock block,
        IReadOnlyList<OcrTextBlock> siblings)
    {
        if (block.Words.Count != 1
            || block.Text.Any(char.IsWhiteSpace)
            || block.Text.Trim().Length < 2)
        {
            return block;
        }

        var backing = SampleBacking(source, block.Bounds);
        var style = SourceVisualStyleEstimator.Estimate(source, block, siblings, backing);

        var left = Math.Clamp((int)Math.Floor(block.Bounds.Left), 0, source.Width);
        var top = Math.Clamp((int)Math.Floor(block.Bounds.Top), 0, source.Height);
        var right = Math.Clamp((int)Math.Ceiling(block.Bounds.Right), left, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(block.Bounds.Bottom), top, source.Height);
        var stable = new List<(int X, int Y)>();
        var alternateTrailingInk = 0;
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var pixel = source.GetPixel(x, y);
            if (ColorDistance(pixel, backing) < 30) continue;
            if (ColorDistance(pixel, style.ForegroundColor) <= 56) stable.Add((x, y));
        }
        if (stable.Count < 6) return block;

        var stableLeft = stable.Min(static pixel => pixel.X);
        var stableRight = stable.Max(static pixel => pixel.X) + 1;
        var minimumTrim = Math.Max(4, block.Bounds.Height * 0.18);
        var stableWidth = stableRight - stableLeft;
        if (stableLeft > block.Bounds.Left + block.Bounds.Height * 0.4
            || stableWidth < block.Bounds.Width * 0.35
            || block.Bounds.Right - stableRight < minimumTrim)
        {
            return block;
        }

        var stablePixels = stable.ToHashSet();
        var alternatePixels = new List<(int X, int Y)>();
        for (var y = top; y < bottom; y++)
        for (var x = stableRight; x < right; x++)
        {
            var pixel = source.GetPixel(x, y);
            if (ColorDistance(pixel, backing) >= 30
                && ColorDistance(pixel, style.ForegroundColor) >= MaterialColorDistance)
            {
                alternateTrailingInk++;
                alternatePixels.Add((x, y));
            }
        }
        if (alternateTrailingInk < 4) return block;
        var touchingPixels = alternatePixels
            .Where(pixel => Enumerable.Range(-1, 3).Any(dx =>
                Enumerable.Range(-1, 3).Any(dy => stablePixels.Contains((pixel.X + dx, pixel.Y + dy)))))
            .ToArray();
        var touchingRows = touchingPixels.Select(static pixel => pixel.Y).Distinct().Count();
        var minimumBroadConnection = Math.Max(3, (int)Math.Ceiling(block.Bounds.Height * 0.12));
        var isBroadlyConnectedText = touchingPixels.Length >= minimumBroadConnection
            && touchingRows >= minimumBroadConnection;
        if (isBroadlyConnectedText) return block;

        var trimmedRight = Math.Min(block.Bounds.Right, stableRight + 2);
        var bounds = new PixelRect(
            block.Bounds.Left,
            block.Bounds.Top,
            Math.Max(1, trimmedRight - block.Bounds.Left),
            block.Bounds.Height);
        return new OcrTextBlock(block.Text, bounds, [new OcrWord(block.Text, bounds)]);
    }

    private static OcrTextBlock[] RefineSyntheticSpanBounds(
        Bitmap source,
        OcrTextBlock original,
        OcrTextBlock[] spans,
        IReadOnlyList<OcrTextBlock> siblings,
        Color backing)
    {
        if (spans.Length < 2) return spans;
        var styles = spans
            .Select(span => SourceVisualStyleEstimator.Estimate(source, span, siblings, backing))
            .ToArray();
        var boundaries = new double[spans.Length - 1];
        for (var index = 0; index < boundaries.Length; index++)
        {
            var expected = (spans[index].Bounds.Right + spans[index + 1].Bounds.Left) / 2;
            boundaries[index] = FindColorTransition(
                source,
                original.Bounds,
                styles[index].ForegroundColor,
                styles[index + 1].ForegroundColor,
                backing,
                expected);
        }

        var refined = new OcrTextBlock[spans.Length];
        for (var index = 0; index < spans.Length; index++)
        {
            var left = index == 0 ? spans[index].Bounds.Left : boundaries[index - 1];
            var right = index == spans.Length - 1 ? spans[index].Bounds.Right : boundaries[index];
            var bounds = new PixelRect(
                left,
                spans[index].Bounds.Top,
                Math.Max(1, right - left),
                spans[index].Bounds.Height);
            refined[index] = new OcrTextBlock(
                spans[index].Text,
                bounds,
                [new OcrWord(spans[index].Text, bounds)]);
        }
        return refined;
    }

    private static double FindColorTransition(
        Bitmap source,
        PixelRect originalBounds,
        Color leftColor,
        Color rightColor,
        Color backing,
        double expected)
    {
        var radius = Math.Max(8, originalBounds.Height * 2);
        var searchLeft = Math.Clamp((int)Math.Floor(expected - radius), (int)originalBounds.Left, source.Width - 1);
        var searchRight = Math.Clamp((int)Math.Ceiling(expected + radius), searchLeft + 1, Math.Min(source.Width, (int)originalBounds.Right));
        var top = Math.Clamp((int)Math.Floor(originalBounds.Top), 0, source.Height);
        var bottom = Math.Clamp((int)Math.Ceiling(originalBounds.Bottom), top, source.Height);
        var columns = new List<(int X, bool IsRight)>();
        for (var x = searchLeft; x < searchRight; x++)
        {
            var leftDistance = 0d;
            var rightDistance = 0d;
            var ink = 0;
            for (var y = top; y < bottom; y++)
            {
                var pixel = source.GetPixel(x, y);
                if (ColorDistance(pixel, backing) < 24) continue;
                leftDistance += ColorDistance(pixel, leftColor);
                rightDistance += ColorDistance(pixel, rightColor);
                ink++;
            }
            if (ink > 0) columns.Add((x, rightDistance < leftDistance));
        }

        var candidates = new List<double>();
        for (var index = 1; index < columns.Count; index++)
        {
            if (columns[index - 1].IsRight || !columns[index].IsRight) continue;
            if (columns[index].X - columns[index - 1].X > originalBounds.Height) continue;
            candidates.Add((columns[index - 1].X + columns[index].X + 1) / 2d);
        }
        var resolved = candidates.Count == 0
            ? expected
            : candidates.OrderBy(candidate => Math.Abs(candidate - expected)).First();
        return Math.Clamp(resolved, originalBounds.Left + 1, originalBounds.Right - 1);
    }

    private static IEnumerable<OcrWord> ResolveWords(OcrTextBlock block)
    {
        if (block.Words.Count >= 2) return block.Words;
        var matches = Regex.Matches(block.Text, @"\S+")
            .Cast<Match>()
            .ToArray();
        if (matches.Length < 2) return block.Words;

        var totalUnits = Math.Max(1, TextWidthUnits(block.Text));
        return matches.Select(match =>
        {
            var startUnits = TextWidthUnits(block.Text.AsSpan(0, match.Index));
            var endUnits = TextWidthUnits(block.Text.AsSpan(0, match.Index + match.Length));
            var left = block.Bounds.Left + block.Bounds.Width * startUnits / totalUnits;
            var right = block.Bounds.Left + block.Bounds.Width * endUnits / totalUnits;
            return new OcrWord(
                match.Value,
                new PixelRect(left, block.Bounds.Top, Math.Max(1, right - left), block.Bounds.Height));
        }).ToArray();
    }

    private static double TextWidthUnits(ReadOnlySpan<char> text)
    {
        var units = 0d;
        foreach (var value in text)
        {
            units += value switch
            {
                _ when IsCjk(value) => 1,
                _ when char.IsWhiteSpace(value) => 0.35,
                _ when char.IsUpper(value) => 0.72,
                _ when char.IsLetterOrDigit(value) => 0.56,
                _ => 0.35,
            };
        }
        return units;
    }

    private static OcrTextBlock CreateBlockFromWords(IReadOnlyList<OcrWord> words)
    {
        var left = words.Min(static word => word.Bounds.Left);
        var top = words.Min(static word => word.Bounds.Top);
        var right = words.Max(static word => word.Bounds.Right);
        var bottom = words.Max(static word => word.Bounds.Bottom);
        var text = JoinWords(words);
        var bounds = new PixelRect(left, top, right - left, bottom - top);
        return new OcrTextBlock(text, bounds, [new OcrWord(text, bounds)]);
    }

    private static string JoinWords(IReadOnlyList<OcrWord> words)
    {
        var result = words[0].Text;
        for (var index = 1; index < words.Count; index++)
        {
            var previous = result.Length == 0 ? '\0' : result[^1];
            var next = words[index].Text.Length == 0 ? '\0' : words[index].Text[0];
            if (!IsCjk(previous) || !IsCjk(next)) result += " ";
            result += words[index].Text;
        }
        return result.Trim();
    }

    private static IEnumerable<OcrTextBlock> SplitTranslationLinkPrefix(OcrTextBlock block)
    {
        const string prefix = "Show translation";
        var words = block.Words.OrderBy(static word => word.Bounds.Left).ToArray();
        if (words.Length >= 3
            && string.Equals(words[0].Text, "Show", StringComparison.OrdinalIgnoreCase)
            && string.Equals(words[1].Text, "translation", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateBlockFromWords(words[..2]);
            yield return CreateBlockFromWords(words[2..]);
            yield break;
        }

        var text = block.Text.Trim();
        if (text.Length <= prefix.Length
            || !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !char.IsWhiteSpace(text[prefix.Length]))
        {
            yield return block;
            yield break;
        }

        var suffix = text[prefix.Length..].TrimStart();
        if (suffix.Length == 0)
        {
            yield return block;
            yield break;
        }

        // PP-OCR may return a complete line as one word. Use a bounded character
        // proportion only for this exact link prefix; it keeps the link separate
        // without treating arbitrary body text as a UI control.
        var splitRatio = prefix.Length / (double)text.Length;
        var splitX = block.Bounds.Left + block.Bounds.Width * splitRatio;
        var prefixBounds = new PixelRect(
            block.Bounds.Left,
            block.Bounds.Top,
            Math.Max(1, splitX - block.Bounds.Left),
            block.Bounds.Height);
        var bodyBounds = new PixelRect(
            splitX,
            block.Bounds.Top,
            Math.Max(1, block.Bounds.Right - splitX),
            block.Bounds.Height);
        yield return new OcrTextBlock(prefix, prefixBounds, [new OcrWord(prefix, prefixBounds)]);
        yield return new OcrTextBlock(suffix, bodyBounds, [new OcrWord(suffix, bodyBounds)]);
    }

    private static IEnumerable<OcrTextBlock> SplitInlineIdentityBadge(OcrTextBlock block)
    {
        if (block.Words.Count < 2) return [block];
        var words = block.Words.OrderBy(static word => word.Bounds.Left).ToArray();
        var identity = words[0].Text.Trim();
        var remaining = words.Skip(1).ToArray();
        var isTechnicalIdentity = identity.Contains('@')
            || identity.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || identity.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("/", StringComparison.Ordinal)
            || identity.Contains("\\", StringComparison.Ordinal);
        if (!isTechnicalIdentity || remaining.Any(static word => word.Text.Trim().Length > 12)) return [block];

        return words.Select(static word => new OcrTextBlock(word.Text, word.Bounds, [word])).ToArray();
    }

    private static bool CanJoin(
        OcrTextBlock previous,
        OcrTextBlock current,
        double medianHeight,
        bool currentIsHorizontalRowMember,
        IReadOnlyDictionary<OcrTextBlock, SourceVisualStyle>? styles)
    {
        var clippedSentenceContinuation = IsClippedViewportSentenceContinuation(previous, current);
        // A social-media "Show translation" link is a separate interactive
        // control even when OCR gives it the same height, baseline, and
        // backing style as the first body line. Joining it would route the
        // link through the Local LMT body validator and can drop both items.
        if (IsTranslationLink(previous.Text) || IsTranslationLink(current.Text)) return false;
        if (IsChineseProse(previous.Text) != IsChineseProse(current.Text)) return false;
        if (ScriptFamily(previous.Text) != ScriptFamily(current.Text)) return false;
        if ((IsMetadata(previous, medianHeight) && !clippedSentenceContinuation)
            || IsMetadata(current, medianHeight)) return false;
        if (IsHashtag(previous.Text) || IsHashtag(current.Text)) return false;
        // Complete standalone rows retain their own geometry. A following
        // unfinished full-width row can continue the same wrapped paragraph.
        var previousText = previous.Text.TrimEnd();
        if (previousText.Length >= 12
            && previousText[^1] is '.' or '?' or '!' or '。' or '？' or '！'
            && !ContinuesWrappedParagraph(previous, current))
            return false;
        if (styles is not null
            && styles.TryGetValue(previous, out var previousStyle)
            && styles.TryGetValue(current, out var currentStyle)
            && previousStyle.Confidence != VisualStyleConfidence.Low
            && currentStyle.Confidence != VisualStyleConfidence.Low
            && HasMaterialColorChange(previousStyle.ForegroundColor, currentStyle.ForegroundColor))
        {
            return false;
        }
        if (currentIsHorizontalRowMember) return false;
        if (previous.Bounds.Height >= medianHeight * 1.12
            && current.Bounds.Height <= previous.Bounds.Height * 0.82)
        {
            return false;
        }
        // A compact UI link directly above body copy is common on social feeds.
        // Do not let it become the first line of the body merely because both share a left edge.
        if (previous.Bounds.Height < current.Bounds.Height * 0.88
            && !clippedSentenceContinuation
            && !LooksLikeWrappedEastAsianProse(previous, current))
        {
            return false;
        }
        if (previous.Text.Trim().Length <= 24
            && current.Text.Trim().Length <= 24
            && !LooksLikeWrappedEastAsianProse(previous, current))
        {
            return false;
        }
        var topDelta = current.Bounds.Top - previous.Bounds.Top;
        if (topDelta < Math.Min(previous.Bounds.Height, current.Bounds.Height) * 0.6) return false;
        var heightRatio = Math.Min(previous.Bounds.Height, current.Bounds.Height)
            / Math.Max(1, Math.Max(previous.Bounds.Height, current.Bounds.Height));
        if (heightRatio < 0.75 && !clippedSentenceContinuation) return false;

        var gap = current.Bounds.Top - previous.Bounds.Bottom;
        // A short label above a long sentence is often a card heading even
        // when OCR reports both rows at nearly the same glyph height.
        if (previous.Text.Trim().Length <= 24
            && current.Text.Trim().Length >= 32
            && gap > Math.Min(previous.Bounds.Height, current.Bounds.Height) * 0.45)
        {
            return false;
        }
        if (gap < -Math.Min(previous.Bounds.Height, current.Bounds.Height) * 0.25
            || gap > Math.Max(previous.Bounds.Height, current.Bounds.Height) * 0.55)
        {
            return false;
        }

        var overlap = Math.Max(0,
            Math.Min(previous.Bounds.Right, current.Bounds.Right)
            - Math.Max(previous.Bounds.Left, current.Bounds.Left));
        var overlapRatio = overlap / Math.Max(1, Math.Min(previous.Bounds.Width, current.Bounds.Width));
        var edgeTolerance = Math.Max(previous.Bounds.Height, current.Bounds.Height) * 1.5;
        var leftAligned = Math.Abs(previous.Bounds.Left - current.Bounds.Left) <= edgeTolerance;
        var rightAligned = Math.Abs(previous.Bounds.Right - current.Bounds.Right) <= edgeTolerance;
        var centerAligned = Math.Abs(
            previous.Bounds.Left + previous.Bounds.Width / 2
            - (current.Bounds.Left + current.Bounds.Width / 2)) <= edgeTolerance;
        return overlapRatio >= 0.35 && (leftAligned || rightAligned || centerAligned);
    }

    private static bool IsClippedViewportSentenceContinuation(OcrTextBlock previous, OcrTextBlock current)
    {
        // Scrolling can cut the first OCR row at y=0. Its shortened box then
        // looks like metadata even when the next short row finishes the sentence.
        var first = previous.Text.TrimEnd();
        var next = current.Text.TrimStart();
        if (previous.Bounds.Top > 1 || first.Length < 32 || next.Length == 0 || next.Length > 24
            || first[^1] is '.' or '?' or '!' or ':' or ';'
            || next[0] is not (>= 'a' and <= 'z')
            || next[^1] is not ('.' or '?' or '!')) return false;

        var height = Math.Min(previous.Bounds.Height, current.Bounds.Height);
        return previous.Bounds.Height < current.Bounds.Height * 0.75
            && previous.Bounds.Width >= current.Bounds.Width * 3
            && Math.Abs(previous.Bounds.Left - current.Bounds.Left) <= height * 0.35
            && current.Bounds.Top - previous.Bounds.Bottom is >= 0
            && current.Bounds.Top - previous.Bounds.Bottom <= current.Bounds.Height * 0.35;
    }

    private static bool ContinuesWrappedParagraph(OcrTextBlock previous, OcrTextBlock current)
    {
        var next = current.Text.TrimEnd();
        var height = Math.Min(previous.Bounds.Height, current.Bounds.Height);
        return next.Length >= 32 && next[^1] is not ('.' or '?' or '!' or '。' or '？' or '！' or ':' or '：')
            && previous.Text.Trim().Length >= 32
            && previous.Bounds.Width >= height * 10
            && current.Bounds.Width >= previous.Bounds.Width * 0.75
            && current.Bounds.Width <= previous.Bounds.Width * 1.25
            && Math.Abs(previous.Bounds.Left - current.Bounds.Left) <= height * 0.35;
    }

    private static bool LooksLikeWrappedEastAsianProse(OcrTextBlock previous, OcrTextBlock current)
    {
        var previousText = previous.Text.Trim();
        var currentText = current.Text.Trim();
        // Hangul uses spaces and shorter wrapped rows; do not add it to IsCjk,
        // which is also used to decide whether joining text should insert spaces.
        var hangul = previousText.Count(IsHangul) >= 4 && currentText.Count(IsHangul) >= 4;
        if (previousText.Length < (hangul ? 6 : 12) || currentText.Length < 4) return false;
        if (!hangul && (!previousText.Any(IsCjk) || !currentText.Any(IsCjk))) return false;
        if (previousText[^1] is '。' or '！' or '？' or '!' or '?' or '.') return false;
        return previous.Bounds.Width >= previous.Bounds.Height * (hangul ? 4 : 7)
            && current.Bounds.Width >= current.Bounds.Height * 4;
    }

    private static bool IsChineseProse(string text) =>
        !text.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9d'
            or >= '\uac00' and <= '\ud7af')
        && text.Count(c => c is >= '\u3400' and <= '\u9fff') > 0
        && text.Count(c => c is >= '\u3400' and <= '\u9fff') >= Regex.Matches(text,@"[A-Za-z]+(?:['-][A-Za-z]+)*").Count;

    private static string ScriptFamily(string text) =>
        text.Any(c=>c is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9d') ? "ja"
        : text.Any(c=>c is >= '\uac00' and <= '\ud7af') ? "ko"
        : text.Any(c=>c is >= '\u0400' and <= '\u052f') ? "cyrillic"
        : text.Any(c=>c is >= '\u0600' and <= '\u06ff') ? "arabic"
        : IsChineseProse(text) ? "zh" : "latin";

    private static bool HasMaterialColorChange(Color first, Color second)
    {
        // Antialiasing and inferred word boxes alter the sampled luminance of white/gray
        // copy. That is not an inline color boundary and must not destroy sentence context.
        static bool IsNeutral(Color color) =>
            Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B)) <= 24;
        return !(IsNeutral(first) && IsNeutral(second))
            && ColorDistance(first, second) >= MaterialColorDistance;
    }

    private static bool IsHorizontalPeer(OcrTextBlock first, OcrTextBlock second)
    {
        var firstIsCompact = first.Text.Trim().Length <= 30
            && first.Bounds.Width <= first.Bounds.Height * 10;
        var secondIsCompact = second.Text.Trim().Length <= 30
            && second.Bounds.Width <= second.Bounds.Height * 10;
        if (!firstIsCompact || !secondIsCompact) return false;
        var heightRatio = Math.Min(first.Bounds.Height, second.Bounds.Height)
            / Math.Max(1, Math.Max(first.Bounds.Height, second.Bounds.Height));
        if (heightRatio < 0.75) return false;
        var centerY = first.Bounds.Top + first.Bounds.Height / 2;
        var candidateCenterY = second.Bounds.Top + second.Bounds.Height / 2;
        if (Math.Abs(centerY - candidateCenterY) > Math.Max(first.Bounds.Height, second.Bounds.Height) * 0.35)
        {
            return false;
        }
        var gap = Math.Max(first.Bounds.Left, second.Bounds.Left) - Math.Min(first.Bounds.Right, second.Bounds.Right);
        // Baseline alignment alone must not turn controls in distant columns
        // into peers of a short wrapped body line.
        return gap >= Math.Min(first.Bounds.Height, second.Bounds.Height) * 2
            && gap <= Math.Max(first.Bounds.Width, second.Bounds.Width);
    }

    private static VisualTextRegion CreateRegion(
        int index,
        IReadOnlyList<OcrTextBlock> lines,
        double medianHeight,
        double likelyBodyLeft)
    {
        var ordered = lines.OrderBy(static line => line.Bounds.Top).ThenBy(static line => line.Bounds.Left).ToArray();
        var left = ordered.Min(static line => line.Bounds.Left);
        var top = ordered.Min(static line => line.Bounds.Top);
        var right = ordered.Max(static line => line.Bounds.Right);
        var bottom = ordered.Max(static line => line.Bounds.Bottom);
        var bounds = new PixelRect(left, top, right - left, bottom - top);
        var role = Classify(ordered, medianHeight, likelyBodyLeft);
        var confidence = ordered.Length >= 2
            ? VisualStructureConfidence.High
            : role == VisualTextRole.Uncertain
                ? VisualStructureConfidence.Low
                : VisualStructureConfidence.Medium;
        return new VisualTextRegion(
            $"r{index}",
            string.Join('\n', ordered.Select(static line => line.Text)),
            ordered,
            bounds,
            role,
            confidence);
    }

    private static VisualTextRole Classify(
        IReadOnlyList<OcrTextBlock> lines,
        double medianHeight,
        double likelyBodyLeft)
    {
        if (lines.All(line => IsMetadata(line, medianHeight))) return VisualTextRole.Metadata;
        if (lines.Count >= 2) return VisualTextRole.Body;
        var line = lines[0];
        if (IsMetadata(line, medianHeight)) return VisualTextRole.Metadata;
        if(line.Text.Trim().Length >= 12 && line.Text.Trim()[^1] is '.' or '?' or '!' or '。' or '？' or '！')
            return VisualTextRole.Body;
        if (line.Text.Length <= 20 || line.Bounds.Right <= likelyBodyLeft) return VisualTextRole.Control;
        if (line.Bounds.Height >= medianHeight * 1.15) return VisualTextRole.Heading;
        if (line.Text.Length >= 25) return VisualTextRole.Body;
        return VisualTextRole.Uncertain;
    }

    private static bool IsMetadata(OcrTextBlock block, double medianHeight)
    {
        if (block.Bounds.Height <= medianHeight * 0.7) return true;
        var text = block.Text.Trim();
        return IsSocialIdentityRow(text)
            || Regex.IsMatch(text, @"^(?:[OQIil|uUtThHC]+\s*)?\d+(?:[.,]\d+)?[KkMmBb]?$")
            || Regex.IsMatch(text, @"^@[\p{L}\p{N}_]+$")
            || Regex.IsMatch(text, @"^\d+\s+(?:hours?|minutes?)\s+ago$", RegexOptions.IgnoreCase)
            || (text.Contains("posts", StringComparison.OrdinalIgnoreCase) && text.Any(char.IsDigit))
            || text.Contains("trending", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Joined ", StringComparison.OrdinalIgnoreCase)
            || text.Contains(" Following", StringComparison.OrdinalIgnoreCase)
            || text.Contains(" Followers", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Not followed", StringComparison.OrdinalIgnoreCase)
            || (text.Contains('@') && (text.Contains("·") || text.EndsWith('h')
                || Regex.IsMatch(text, @"@[\p{L}\p{N}_]+\s+(?:(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2}|\d+\s*[smhd])$", RegexOptions.IgnoreCase)));
    }

    private static bool IsSocialIdentityRow(string text)
    {
        if(text.Length>96) return false;
        var account=Regex.Match(text,@"(?<![\p{L}\p{N}_])@[A-Za-z0-9_]+");
        if(!account.Success || account.Index>48) return false;
        var tail=Regex.Replace(text[(account.Index+account.Length)..],@"[\s·•。．.]",string.Empty);
        return Regex.IsMatch(tail,@"^(?:(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\d{1,2}|\d+[smhd])$",RegexOptions.IgnoreCase);
    }

    private static Color SampleBacking(Bitmap source, PixelRect bounds)
    {
        var innerLeft = Math.Clamp((int)Math.Floor(bounds.Left), 0, source.Width);
        var innerTop = Math.Clamp((int)Math.Floor(bounds.Top), 0, source.Height);
        var innerRight = Math.Clamp((int)Math.Ceiling(bounds.Right), innerLeft, source.Width);
        var innerBottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), innerTop, source.Height);
        var left = Math.Clamp(innerLeft - 2, 0, source.Width - 1);
        var top = Math.Clamp(innerTop - 2, 0, source.Height - 1);
        var right = Math.Clamp(innerRight + 2, left + 1, source.Width);
        var bottom = Math.Clamp(innerBottom + 2, top + 1, source.Height);
        var samples = new List<Color>();
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            if (x < innerLeft || x >= innerRight || y < innerTop || y >= innerBottom)
                samples.Add(source.GetPixel(x, y));
        }
        if (samples.Count == 0) return source.GetPixel(left, top);
        return Color.FromArgb(
            255,
            MedianChannel(samples.Select(static color => color.R)),
            MedianChannel(samples.Select(static color => color.G)),
            MedianChannel(samples.Select(static color => color.B)));
    }

    private static int MedianChannel(IEnumerable<byte> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (int)Math.Round((ordered[middle - 1] + ordered[middle]) / 2d)
            : ordered[middle];
    }

    private static bool IsHashtag(string text) => text.TrimStart().StartsWith('#');

    private static bool IsTranslationLink(string text)
    {
        const string label = "Show translation";
        var value = text.Trim();
        return value.StartsWith(label, StringComparison.OrdinalIgnoreCase)
            && value[label.Length..].All(static character => !char.IsLetterOrDigit(character));
    }

    private static bool IsHangul(char value) => value is >= '\uac00' and <= '\ud7af';

    private static bool IsCjk(char value) => value is >= '\u3040' and <= '\u30ff'
        or >= '\u3400' and <= '\u9fff'
        or >= '\uf900' and <= '\ufaff';

    private static double ColorDistance(Color first, Color second)
    {
        var red = first.R - second.R;
        var green = first.G - second.G;
        var blue = first.B - second.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }
}
