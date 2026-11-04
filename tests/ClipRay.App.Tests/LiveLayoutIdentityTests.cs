using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Translation;
using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;

public static class LiveLayoutIdentityTests
{
    public static void Run()
    {
        var viewport=new Rectangle(0,0,1000,800);
        var style=new SourceVisualStyle(Color.White,15,24,SourceFontWeight.Normal,
            SourceFontCategory.SansSerif,false,SourceTextAlignment.Left,0,VisualStyleConfidence.High,20);
        InPlaceLayoutItem Item(PixelRect bounds,string source="same source",string text="当前译文")
        {
            var block=new OcrTextBlock(source,bounds,[]);
            return new(block,text,bounds,InPlaceBackgroundMode.FlatSampled,Color.Black,Color.White,
                BackgroundPatchPng:[1],SourceStyle:style,
                TextLayout:new(true,20,24,style.FontWeight,style.FontCategory,false,style.Alignment,0,"Microsoft YaHei UI"),
                SourceRegion:new("region",source,[block],bounds,VisualTextRole.Body,VisualStructureConfidence.High));
        }
        LiveTranslationLayoutCache Cache(params InPlaceLayoutItem[] items)
        {
            var cache=new LiveTranslationLayoutCache();
            cache.Update(new("Machine","completed",items),[viewport]);
            return cache;
        }
        var original=Item(new(10,10,400,60));
        var noisy=Item(new(11,11,398,59)) with { BackgroundPatchPng=[2],
            SourceStyle=style with {EstimatedFontSizePixels=19.5},
            TextLayout=original.TextLayout! with {FontSizePixels=19.5}};
        var stable=Cache(original).Update(new("Machine","completed",[noisy]),[viewport],[original]).LayoutItems.Single();
        Assert(ReferenceEquals(stable,original),"Verified same pixels must retain complete anchor/backing together.");
        var moved=Cache(original).Update(new("Machine","completed",[noisy]),[viewport]).LayoutItems.Single();
        Assert(moved.Bounds==noisy.Bounds && ReferenceEquals(moved.BackgroundPatchPng,noisy.BackgroundPatchPng),
            "Unverified movement must retain current geometry and backing.");
        Assert(moved.TextLayout!.FontSizePixels==20,"Measurement jitter must retain a fitting font.");
        var changed=noisy with {Text="版本二",TextAlignments=[new(0,4,0,3)]};
        var current=Cache(original).Update(new("Machine","completed",[changed]),[viewport],[original]).LayoutItems.Single();
        Assert(current.Text==changed.Text && current.TextAlignments==changed.TextAlignments
            && current.BackgroundPatchPng==changed.BackgroundPatchPng,"Current target offsets and backing must stay together.");
        var zoomed=noisy with {Bounds=new(10,10,640,96),
            SourceRegion=noisy.SourceRegion! with {Bounds=new(10,10,640,96)},
            SourceStyle=style with {EstimatedFontSizePixels=32,GlyphHeightPixels=24,LineHeightPixels=38},
            TextLayout=original.TextLayout! with {FontSizePixels=32,LineHeightPixels=38}};
        var zoom=Cache(original).Update(new("Machine","completed",[zoomed]),[viewport]).LayoutItems.Single();
        Assert(zoom.TextLayout!.FontSizePixels==32,"A real source zoom must not reuse the old smaller font.");
        var unknown=noisy with {SourceStyle=noisy.SourceStyle! with {EstimatedFontSizePixels=null}};
        var uncertain=Cache(original).Update(new("Machine","completed",[unknown]),[viewport]).LayoutItems.Single();
        Assert(uncertain.TextLayout!.FontSizePixels==unknown.TextLayout!.FontSizePixels,
            "Unknown source scale cannot justify reusing an older font.");
        var scrolled=Item(new(10,200,400,60));
        var scroll=Cache(original).Update(new("Machine","completed",[scrolled]),[viewport],[]).LayoutItems.Single();
        Assert(scroll.Bounds==scrolled.Bounds,"True scrolling must not freeze the anchor.");
        var duplicate=Item(new(10,30,400,60));
        var ambiguous=Cache(original,duplicate).Update(new("Machine","completed",[noisy]),[viewport],[original,duplicate]).LayoutItems.Single();
        Assert(ambiguous.Bounds==noisy.Bounds,"Repeated overlapping source text must not borrow an ambiguous anchor.");
        var color=noisy with {ForegroundColor=Color.Red,SourceStyle=style with {ForegroundColor=Color.Red}};
        var recolored=Cache(original).Update(new("Machine","completed",[color]),[viewport],[original]).LayoutItems.Single();
        Assert(recolored.SourceStyle!.ForegroundColor==Color.Red,"Current color must survive font stabilization.");
        Console.WriteLine("LIVE_LAYOUT_IDENTITY_TESTS_PASSED");
    }
    private static void Assert(bool condition,string message) {if(!condition) throw new Exception(message);}
}
