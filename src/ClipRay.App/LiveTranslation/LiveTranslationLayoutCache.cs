using System.Drawing;
using System.Text.RegularExpressions;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

namespace ClipRay.App.LiveTranslation;

public sealed class LiveTranslationLayoutCache
{
    private readonly List<InPlaceLayoutItem> _items=[];
    private readonly List<FontTier> _fontTiers=[];

    internal LiveTranslationLayoutCache Fork()
    {
        var copy = new LiveTranslationLayoutCache();
        copy._items.AddRange(_items); copy._fontTiers.AddRange(_fontTiers);
        return copy;
    }

    public LiveTranslationFrame Update(LiveTranslationFrame candidate,IReadOnlyList<Rectangle> windows)
    {
        var layouts=candidate.LayoutItems.Select(item=>
        {
            if(item.SourceRegion is null) return item;
            var prior=_items.FirstOrDefault(old=>old.SourceRegion is not null
                && old.Bounds==item.Bounds && old.SourceRegion.Bounds==item.SourceRegion.Bounds
                && Normalize(old.SourceRegion.SourceText)==Normalize(item.SourceRegion.SourceText));
            // Background evidence belongs to this new source version. Only reuse
            // the established text and measured typography at identical geometry.
            var sameStyle = prior is not null && SameStyleVersion(prior,item);
            if(prior is not null && sameStyle
                && (prior.TextLayout is null
                || prior.TextLayout.ShouldRender && prior.TextLayout.FontSizePixels
                    >= TranslationTextLayoutPolicy.MinimumReadableFontSizePixels))
                return item with {Text=prior.Text,TextLayout=prior.TextLayout,
                    TextAlignments=prior.TextAlignments,UnresolvedSourceStyleSpanCount=prior.UnresolvedSourceStyleSpanCount};
            // Source span styles are a separate version. Keep this candidate's
            // text, alignments and runs together; substituting an older wording
            // would make current target UTF-16 offsets refer to different words.
            if(prior is not null && !sameStyle)
                return item;
            if(item.SourceStyle is null || item.TextLayout is null || item.SourceRegion.Role != VisualTextRole.Body)
                return item;
            var sourceSize=item.SourceStyle.EstimatedFontSizePixels
                ?? item.SourceStyle.GlyphHeightPixels / TranslationTextLayoutPolicy.VisibleGlyphRatio;
            var tier=_fontTiers.Where(t => t.Category==item.SourceStyle.FontCategory
                && t.Weight==item.SourceStyle.FontWeight && t.Alignment==item.SourceStyle.Alignment
                && t.Italic==item.SourceStyle.IsItalic && t.Rotation==item.SourceStyle.RotationDegrees
                && Math.Min(t.SourceSize,sourceSize)/Math.Max(t.SourceSize,sourceSize)>=.82)
                .OrderBy(t => Math.Abs(t.SourceSize-sourceSize)).FirstOrDefault();
            if(tier is null)
            {
                // A font reduced to fit this rectangle is local to that text.
                // Learning it as a session-wide tier shrinks unrelated body
                // paragraphs, including paragraphs on later viewports.
                if(item.TextLayout.ShouldRender && item.TextLayout.FontSizePixels
                    >= TranslationTextLayoutPolicy.MinimumReadableFontSizePixels
                    && (item.TextLayout.FontSizePixels >= sourceSize
                        || TranslationTextLayoutPolicy.FitAtSize(item.Text,item.Bounds,item.SourceStyle,
                            sourceSize,item.SourceStyle.LineHeightPixels).ShouldRender))
                    _fontTiers.Add(new(sourceSize,item.SourceStyle.FontCategory,item.TextLayout.FontSizePixels,
                        item.TextLayout.LineHeightPixels,item.SourceStyle.FontWeight,item.SourceStyle.Alignment,
                        item.SourceStyle.IsItalic,item.SourceStyle.RotationDegrees));
                return item;
            }
            // OCR can tighten a line's box after a small text change. A line
            // height borrowed from an older, taller box must not discard a
            // translation that the current frame has already fitted.
            var lineHeight=Math.Min(tier.LineHeight,item.TextLayout.LineHeightPixels);
            var stableStyle=item.SourceStyle with {EstimatedFontSizePixels=tier.FontSize,LineHeightPixels=lineHeight};
            var stableLayout=TranslationTextLayoutPolicy.FitAtSize(item.Text,item.Bounds,stableStyle,
                tier.FontSize,lineHeight) with { SourceFontSizePixels=item.TextLayout.SourceFontSizePixels };
            stableLayout=TranslationTextLayoutPolicy.WithStyleRuns(item.Text,item.Bounds,item.SourceStyle,stableLayout,
                item.TextLayout.StyleRuns);
            return item with {TextLayout=stableLayout.ShouldRender ? stableLayout : item.TextLayout};
        }).ToArray();
        _items.RemoveAll(item=>windows.Any(window=>window.IntersectsWith(Rectangle.FromLTRB(
            (int)Math.Floor(item.Bounds.Left),(int)Math.Floor(item.Bounds.Top),(int)Math.Ceiling(item.Bounds.Right),(int)Math.Ceiling(item.Bounds.Bottom)))));
        var visible=layouts.Where(item => item.TextLayout is null || item.TextLayout.ShouldRender
            && item.TextLayout.FontSizePixels >= TranslationTextLayoutPolicy.MinimumReadableFontSizePixels).ToArray();
        foreach(var item in visible)
        {
            // A regional frame includes unchanged siblings for atomic display.
            // Replacing those entries prevents the cache accumulating a second
            // copy on every crop and retaining an older style version first.
            _items.RemoveAll(old=>old.Bounds==item.Bounds
                && old.SourceRegion?.Bounds==item.SourceRegion?.Bounds
                && Normalize(old.SourceRegion?.SourceText ?? old.SourceBlock.Text)
                    == Normalize(item.SourceRegion?.SourceText ?? item.SourceBlock.Text));
            _items.Add(item);
        }
        return candidate with {LayoutItems=visible,
            Status=visible.Length<layouts.Length ? "translation-partial" : candidate.Status,
            ErrorCategory=visible.Length<layouts.Length ? "stable-font-does-not-fit" : candidate.ErrorCategory};
    }

    private static string Normalize(string text)=>Regex.Replace(text,@"\s+"," ").Trim();
    private static bool SameStyleVersion(InPlaceLayoutItem first,InPlaceLayoutItem second)=>
        first.SourceStyle == second.SourceStyle
        && (first.SourceRegion?.SourceStyleSpans ?? []).SequenceEqual(second.SourceRegion?.SourceStyleSpans ?? []);
    private sealed record FontTier(double SourceSize,SourceFontCategory Category,double FontSize,double LineHeight,
        SourceFontWeight Weight,SourceTextAlignment Alignment,bool Italic,double Rotation);
}
