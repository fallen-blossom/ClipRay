using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

namespace ClipRay.App.LiveTranslation;

public static class LiveTranslationFrameCoordinates
{
    public static LiveTranslationFrame Offset(LiveTranslationFrame frame,int x,int y)
    {
        PixelRect Bounds(PixelRect bounds)=>new(bounds.X+x,bounds.Y+y,bounds.Width,bounds.Height);
        OcrTextBlock Block(OcrTextBlock block)=>block with { Bounds=Bounds(block.Bounds),
            Words=block.Words.Select(word=>word with {Bounds=Bounds(word.Bounds)}).ToArray() };
        VisualTextRegion Region(VisualTextRegion region)=>region with { Bounds=Bounds(region.Bounds),
            Lines=region.Lines.Select(Block).ToArray() };
        return frame with {
            LayoutItems=frame.LayoutItems.Select(item=>item with { Bounds=Bounds(item.Bounds),
                SourceBlock=Block(item.SourceBlock),SourceRegion=item.SourceRegion is null?null:Region(item.SourceRegion) }).ToArray(),
            SourceRegions=frame.SourceRegions.Select(Region).ToArray()
        };
    }
}
