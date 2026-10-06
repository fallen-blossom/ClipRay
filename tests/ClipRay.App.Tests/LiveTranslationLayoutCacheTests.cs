using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Translation;
using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;

internal static class LiveTranslationLayoutCacheTests
{
    public static void ChangedTextWithTighterOcrBoundsKeepsItsTranslation()
    {
        var oldBounds = new PixelRect(45, 137, 857, 42);
        var newBounds = new PixelRect(45, 137, 831, 38);
        var style = new SourceVisualStyle(Color.FromArgb(23, 33, 46), 29, 39.5,
            SourceFontWeight.Normal, SourceFontCategory.SansSerif, false,
            SourceTextAlignment.Left, 0, VisualStyleConfidence.High, 29.5);
        var oldText = "The blue train leaves at eight. Keep ticket XR-17 until arrival.";
        var newText = "The red train leaves at nine. Keep ticket XR-17 until arrival.";
        var oldBlock = new OcrTextBlock(oldText, oldBounds, []);
        var newBlock = new OcrTextBlock(newText, newBounds, []);
        var oldRegion = new VisualTextRegion("train", oldText, [oldBlock], oldBounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var newRegion = new VisualTextRegion("train", newText, [newBlock], newBounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var oldLayout = new TranslationTextLayout(true, 29.5, 41.5,
            SourceFontWeight.Normal, SourceFontCategory.SansSerif, false,
            SourceTextAlignment.Left, 0, "Segoe UI, Microsoft YaHei UI");
        var translatedOld = new InPlaceLayoutItem(oldBlock,
            "蓝色列车8点出发,在到达前保留XR-17的车票.", oldBounds,
            InPlaceBackgroundMode.FlatSampled, Color.White, Color.Black,
            SourceStyle: style, TextLayout: oldLayout, SourceRegion: oldRegion);
        var translatedNew = new InPlaceLayoutItem(newBlock,
            "红色列车9点出发,在到达前保留XR-17号车票.", newBounds,
            InPlaceBackgroundMode.FlatSampled, Color.White, Color.Black,
            SourceStyle: style,
            TextLayout: oldLayout with { FontSizePixels = 28.5, LineHeightPixels = 38.16101694915254 },
            SourceRegion: newRegion);
        var cache = new LiveTranslationLayoutCache();
        cache.Update(new("Machine", "completed", [translatedOld]), [new Rectangle(0, 0, 1100, 745)]);

        var updated = cache.Update(new("Machine", "completed", [translatedNew]),
            [new Rectangle(0, 0, 1100, 745)]);
        if (updated.LayoutItems.Count != 1 || updated.LayoutItems[0].Text != translatedNew.Text
            || updated.Status != "completed")
            throw new Exception("A tighter OCR box discarded a successfully fitted changed translation.");
        if (updated.LayoutItems[0].TextLayout!.FontSizePixels > oldLayout.FontSizePixels)
            throw new Exception("A changed sentence grew beyond the established body font.");

        var fallbackBounds = Enumerable.Range(500, 330)
            .Select(width => new PixelRect(45, 137, width, 38))
            .First(bounds => !TranslationTextLayoutPolicy.FitAtSize(translatedNew.Text, bounds,
                    style, 29.5, 38.16101694915254).ShouldRender
                && TranslationTextLayoutPolicy.FitAtSize(translatedNew.Text, bounds,
                    style, 28.5, 38.16101694915254).ShouldRender);
        var narrower = translatedNew with
        {
            Bounds = fallbackBounds,
            SourceRegion = newRegion with { Bounds = fallbackBounds },
        };
        var fallback = cache.Update(new("Machine", "completed", [narrower]),
            [new Rectangle(0, 0, 1100, 745)]);
        if (fallback.LayoutItems.Count != 1 || fallback.LayoutItems[0].TextLayout!.FontSizePixels != 28.5)
            throw new Exception("A current-frame fitted font was discarded when the older font no longer fit.");
    }

    public static void FixedFontUsesRendererGeometryWithoutShrinking()
    {
        var style = new SourceVisualStyle(Color.White,24,30,SourceFontWeight.Normal,
            SourceFontCategory.SansSerif,false,SourceTextAlignment.Left,0,VisualStyleConfidence.High,24);
        var fits = TranslationTextLayoutPolicy.FitAtSize("完整句子。", new PixelRect(0,0,400,50), style,24,30);
        if (!fits.ShouldRender || fits.FontSizePixels != 24 || fits.LineHeightPixels != 30)
            throw new Exception("A fitting fixed font changed its typography.");
        var tooSmall = TranslationTextLayoutPolicy.FitAtSize("完整句子。", new PixelRect(0,0,20,10), style,24,30);
        if (tooSmall.ShouldRender || tooSmall.FontSizePixels != 24)
            throw new Exception("A tight crop silently shrank the established font.");
    }
    public static void AdjacentNewParagraphsAreNotDiscardedAsOldOverlays()
    {
        using var bitmap=new Bitmap(500,160);
        var snapshot=new LiveTranslationPixelSnapshot(bitmap);
        InPlaceLayoutItem Item(int y) {
            var bounds=new PixelRect(10,y,400,61);
            var block=new OcrTextBlock("A complete paragraph.",bounds,[]);
            return new(block,"完整段落。",bounds,InPlaceBackgroundMode.FlatSampled,Color.Black,Color.White);
        }
        var first=Item(7);
        var second=Item(64); // Native OCR boxes include small overlapping margins.
        var incoming=LiveTranslationRegionWitness.Create(snapshot,[first,second]);
        if(LiveTranslationRegionWitness.Empty.AddUncovered(incoming).Count != 2)
            throw new Exception("Adjacent paragraphs in the same new frame were silently discarded.");
        var retained=LiveTranslationRegionWitness.Create(snapshot,[first]);
        if(retained.AddUncovered(LiveTranslationRegionWitness.Create(snapshot,[second])).Count != 1)
            throw new Exception("A new crop overwrote an overlapping retained translation.");
        InPlaceLayoutItem Paragraph(InPlaceLayoutItem item) => item with {
            SourceRegion=new VisualTextRegion("p",item.SourceBlock.Text,[item.SourceBlock],item.Bounds,
                VisualTextRole.Body,VisualStructureConfidence.High)
        };
        retained=LiveTranslationRegionWitness.Create(snapshot,[Paragraph(first)]);
        if(retained.AddUncovered(LiveTranslationRegionWitness.Create(snapshot,[Paragraph(second)])).Count != 2)
            throw new Exception("OCR margins prevented an adjacent changed paragraph from returning.");
    }
    public static void NewParagraphKeepsEstablishedFontTier()
    {
        var bounds=new PixelRect(10,10,400,60);
        var block=new OcrTextBlock("First complete sentence.",bounds,[]);
        var region=new VisualTextRegion("first",block.Text,[block],bounds,VisualTextRole.Body,VisualStructureConfidence.High);
        var style=new SourceVisualStyle(Color.White,24,32,SourceFontWeight.Normal,SourceFontCategory.SansSerif,
            false,SourceTextAlignment.Left,0,VisualStyleConfidence.High,26);
        var layout=new TranslationTextLayout(true,24,30,SourceFontWeight.Normal,SourceFontCategory.SansSerif,
            false,SourceTextAlignment.Left,0,"Segoe UI, Microsoft YaHei UI");
        var first=new InPlaceLayoutItem(block,"第一句话。",bounds,InPlaceBackgroundMode.FlatSampled,Color.Black,Color.White,
            SourceStyle:style,TextLayout:layout,SourceRegion:region);
        var cache=new LiveTranslationLayoutCache();
        cache.Update(new("Machine","completed",[first]),[new(0,0,500,100)]);
        var next=first with {Text="新出现的段落。",Bounds=new(10,110,400,60),
            SourceRegion=region with {Id="next",SourceText="A newly visible paragraph.",Bounds=new(10,110,400,60)},
            TextLayout=layout with {FontSizePixels=28}};
        var stable=cache.Update(new("Machine","completed",[next]),[new(0,100,500,100)]);
        if(stable.LayoutItems.Single().TextLayout!.FontSizePixels != 24
            || stable.LayoutItems.Single().Text != next.Text)
            throw new Exception("A new paragraph changed an established font tier or reused old text.");
    }
    public static void SameSemanticSourceKeepsFirstTypography()
    {
        var bounds=new PixelRect(10,10,200,50);
        var block=new OcrTextBlock("The limit is 3.",bounds,[]);
        var region=new VisualTextRegion("one",block.Text,[block],bounds,VisualTextRole.Body,VisualStructureConfidence.High);
        var layout=new TranslationTextLayout(true,20,24,SourceFontWeight.Normal,SourceFontCategory.SansSerif,false,SourceTextAlignment.Left,0,"Microsoft YaHei UI");
        var first=new InPlaceLayoutItem(block,"限制是3。",bounds,InPlaceBackgroundMode.FlatSampled,Color.Black,Color.White,
            TextLayout:layout,SourceRegion:region);
        var cache=new LiveTranslationLayoutCache();
        cache.Update(new("Machine","completed",[first]),[new Rectangle(0,0,300,100)]);
        var candidate=first with { Text="另一种译文3。",TextLayout=layout with { FontSizePixels=12 } };
        var stable=cache.Update(new("Machine","completed",[candidate]),[new Rectangle(0,0,300,100)]);
        if(stable.LayoutItems[0].Text!=first.Text || stable.LayoutItems[0].TextLayout!=layout)
            throw new Exception("The same semantic source changed its established text or typography.");
        var changed=candidate with {SourceRegion=region with {SourceText="The limit is 8."},Text="限制是8。"};
        if(cache.Update(new("Machine","completed",[changed]),[new Rectangle(0,0,300,100)]).LayoutItems[0].Text!=changed.Text)
            throw new Exception("Typography cache hid a real numeric content change.");
    }
}
