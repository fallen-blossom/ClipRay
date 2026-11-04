using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

namespace ClipRay.App.LiveTranslation;

public static class LiveTranslationFrameCoordinates
{
    public static LiveTranslationFrame Offset(LiveTranslationFrame frame,int x,int y)
    {
        PixelRect Bounds(PixelRect bounds)=>new(bounds.X+x,bounds.Y+y,bounds.Width,bounds.Height);
        OcrWord Word(OcrWord word)=>word with {Bounds=Bounds(word.Bounds),
            RecognitionWords=word.RecognitionWords.Select(Word).ToArray()};
        OcrTextBlock Block(OcrTextBlock block)=>block with { Bounds=Bounds(block.Bounds),
            SelectionBounds=block.SelectionBounds is { } clip ? Bounds(clip) : null,
            Words=block.Words.Select(Word).ToArray(),
            PrimaryRecognitionWords=block.PrimaryRecognitionWords.Select(Word).ToArray() };
        VisualTextRegion Region(VisualTextRegion region)
        {
            var absolute = Bounds(region.Bounds);
            return region with {
                Id=FormattableString.Invariant($"{region.Id}:crop:{absolute.X:R}:{absolute.Y:R}:{absolute.Width:R}:{absolute.Height:R}"),
                Bounds=absolute, Lines=region.Lines.Select(Block).ToArray(),
                SourceStyleSpans=region.SourceStyleSpans.Select(span=>span with {Bounds=Bounds(span.Bounds)}).ToArray(),
                OriginalOcrWords=region.OriginalOcrWords.Select(Word).ToArray() };
        }
        return frame with {
            LayoutItems=frame.LayoutItems.Select(item=>item with { Bounds=Bounds(item.Bounds),
                SourceBlock=Block(item.SourceBlock),SourceRegion=item.SourceRegion is null?null:Region(item.SourceRegion) }).ToArray(),
            SourceRegions=frame.SourceRegions.Select(Region).ToArray(),
            ExpectedLayoutRegions=frame.ExpectedLayoutRegions?.Select(Region).ToArray()
        };
    }
}
