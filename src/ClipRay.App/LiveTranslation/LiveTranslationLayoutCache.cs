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

    public LiveTranslationFrame Update(LiveTranslationFrame candidate,IReadOnlyList<Rectangle> windows,
        IReadOnlyList<InPlaceLayoutItem>? verifiedUnchangedItems=null)
    {
        var matches=Associate(candidate.LayoutItems);
        var verified=verifiedUnchangedItems is null ? null : new LiveTranslationLayoutCache();
        if(verified is not null) verified._items.AddRange(verifiedUnchangedItems!);
        var verifiedMatches=verified?.Associate(candidate.LayoutItems);
        var layouts=candidate.LayoutItems.Select(item=>
        {
            if(item.SourceRegion is null) return item;
            matches.TryGetValue(item, out var prior);
            if(verifiedMatches is not null && verifiedMatches.TryGetValue(item,out var anchored)
                && anchored.Text==item.Text && SameVisualContent(anchored,item))
                // These pixels are EXACTLY unchanged at the old location. Keep
                // the matching backing and text anchor together, never graft an
                // old patch onto a new rectangle or current-version target offsets.
                return anchored;
            // Background evidence belongs to this new source version. Only reuse
            // the established text and measured typography at identical geometry.
            var sameStyle = prior is not null && SameStyleVersion(prior,item);
            if(prior is not null && sameStyle
                && prior.Bounds==item.Bounds && prior.SourceRegion!.Bounds==item.SourceRegion.Bounds
                && (prior.SourceRegion.SourceStyleSpans.Count==0 && item.SourceRegion.SourceStyleSpans.Count==0
                    && (prior.TextLayout?.StyleRuns.Count ?? 0)==0 && (item.TextLayout?.StyleRuns.Count ?? 0)==0
                    || prior.Text==item.Text && SameVisualContent(prior,item))
                && (prior.TextLayout is null
                || prior.TextLayout.ShouldRender && prior.TextLayout.FontSizePixels
                    >= TranslationTextLayoutPolicy.MinimumReadableFontSizePixels))
                return item with {Text=prior.Text,TextLayout=prior.TextLayout,
                    TextAlignments=prior.TextAlignments,UnresolvedSourceStyleSpanCount=prior.UnresolvedSourceStyleSpanCount};
            // Source span styles are a separate version. Keep this candidate's
            // text, alignments and runs together; substituting an older wording
            // would make current target UTF-16 offsets refer to different words.
            if(prior is not null && (!sameStyle || prior.Bounds!=item.Bounds
                || prior.SourceRegion!.Bounds!=item.SourceRegion.Bounds))
            {
                // Geometry and estimated metrics are measurements, not source
                // versions. Refit the established typography to THIS translation
                // and THIS rectangle, then apply only this version's target runs.
                if (CompatibleTypography(prior, item)
                    && prior.TextLayout is {ShouldRender:true} previous
                    && item.TextLayout is {ShouldRender:true} current
                    && item.SourceStyle is {} style)
                {
                    var stable=TranslationTextLayoutPolicy.FitAtSize(item.Text,item.Bounds,style,
                        previous.FontSizePixels,previous.LineHeightPixels);
                    stable=TranslationTextLayoutPolicy.WithStyleRuns(item.Text,item.Bounds,style,stable,current.StyleRuns);
                    if(stable.ShouldRender && stable.FontSizePixels==previous.FontSizePixels)
                        return item with {TextLayout=stable};
                }
                return item;
            }
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

    // Only mutually unambiguous overlapping source regions are associated.
    // Text alone is not an identity: repeated buttons/paragraphs must not borrow
    // each other's typography. No pixel-distance cutoff guesses whether a
    // movement is OCR noise or real scrolling; all new geometry is preserved.
    private Dictionary<InPlaceLayoutItem,InPlaceLayoutItem> Associate(IReadOnlyList<InPlaceLayoutItem> incoming)
    {
        bool Related(InPlaceLayoutItem old,InPlaceLayoutItem next) =>
            old.SourceRegion is {} a && next.SourceRegion is {} b
            && a.Role==b.Role && Normalize(a.SourceText)==Normalize(b.SourceText)
            && Math.Min(a.Bounds.Right,b.Bounds.Right)>Math.Max(a.Bounds.Left,b.Bounds.Left)
            && Math.Min(a.Bounds.Bottom,b.Bounds.Bottom)>Math.Max(a.Bounds.Top,b.Bounds.Top);
        var result=new Dictionary<InPlaceLayoutItem,InPlaceLayoutItem>();
        foreach(var item in incoming)
        {
            var related=_items.Where(old=>Related(old,item)).ToArray();
            if(related.Length==1 && incoming.Count(next=>Related(related[0],next))==1)
                result[item]=related[0];
        }
        return result;
    }
    private static bool SameVisualContent(InPlaceLayoutItem first,InPlaceLayoutItem second)
    {
        bool Style(SourceVisualStyle? a,SourceVisualStyle? b) => a is null || b is null ? a==b
            : a.ForegroundColor==b.ForegroundColor && a.FontCategory==b.FontCategory
                && a.FontWeight==b.FontWeight && a.Alignment==b.Alignment
                && a.IsItalic==b.IsItalic && a.RotationDegrees==b.RotationDegrees;
        var a=first.SourceRegion?.SourceStyleSpans ?? [];
        var b=second.SourceRegion?.SourceStyleSpans ?? [];
        return Style(first.SourceStyle,second.SourceStyle) && a.Count==b.Count
            && a.Zip(b).All(pair=>pair.First.Start==pair.Second.Start
                && pair.First.Length==pair.Second.Length && pair.First.Text==pair.Second.Text
                && Style(pair.First.Style,pair.Second.Style))
            && first.TextAlignments.SequenceEqual(second.TextAlignments)
            && first.TextLayout?.StyleRuns.SequenceEqual(second.TextLayout?.StyleRuns ?? []) != false;
    }
    private static bool CompatibleTypography(InPlaceLayoutItem first,InPlaceLayoutItem second) =>
        first.SourceStyle is {} a && second.SourceStyle is {} b
        && a.FontCategory==b.FontCategory && a.FontWeight==b.FontWeight
        && a.Alignment==b.Alignment && a.IsItalic==b.IsItalic
        && a.RotationDegrees==b.RotationDegrees && SameMeasuredScale(a,b);

    private static bool SameMeasuredScale(SourceVisualStyle first,SourceVisualStyle second)
    {
        if(first.Confidence==VisualStyleConfidence.Low || second.Confidence==VisualStyleConfidence.Low
            || first.EstimatedFontSizePixels is not >0 || second.EstimatedFontSizePixels is not >0
            || first.GlyphHeightPixels<=0 || second.GlyphHeightPixels<=0) return false;
        var a=first.EstimatedFontSizePixels.Value;
        var b=second.EstimatedFontSizePixels.Value;
        if(!double.IsFinite(a+b+first.GlyphHeightPixels+second.GlyphHeightPixels)) return false;
        // Font size is inferred from integer ink-height pixels. One ink pixel
        // sets its local measurement resolution; it is not a global screen
        // distance threshold. Real source zoom must follow the new typography.
        var resolution=Math.Max(a/first.GlyphHeightPixels,b/second.GlyphHeightPixels);
        return Math.Abs(a-b)<=resolution;
    }

    private static string Normalize(string text)=>Regex.Replace(text,@"\s+"," ").Trim();
    private static bool SameStyleVersion(InPlaceLayoutItem first,InPlaceLayoutItem second)=>
        first.SourceStyle == second.SourceStyle
        && (first.SourceRegion?.SourceStyleSpans ?? []).SequenceEqual(second.SourceRegion?.SourceStyleSpans ?? []);
    private sealed record FontTier(double SourceSize,SourceFontCategory Category,double FontSize,double LineHeight,
        SourceFontWeight Weight,SourceTextAlignment Alignment,bool Italic,double Rotation);
}
