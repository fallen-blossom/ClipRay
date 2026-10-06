using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class KoreanRegionTests
{
    public static void Run()
    {
        OcrTextBlock Row(string text, int x, int y, int width, int height) => new(text, new PixelRect(x, y, width, height), []);
        var heading = VisualTextRegionBuilder.Build([Row("Who to follow", 0, 0, 214, 36)])[0];
        var account = VisualTextRegionBuilder.Build([Row("@example", 0, 80, 200, 28)])[0];
        var identityWithAction = VisualTextRegionBuilder.Build([Row("Sample Name Follow", 0, 160, 240, 28)])[0];
        var link = VisualTextRegionBuilder.Build([Row("Show translation", 0, 200, 240, 28)])[0];
        var kinds = VisualTranslationRequestFactory.ClassifyRegions([heading, account, identityWithAction, link]);
        if (kinds[0] == TranslationTextKind.IdentityMetadata || kinds[1] != TranslationTextKind.IdentityMetadata
            || kinds[2] != TranslationTextKind.IdentityMetadata)
            throw new Exception("Follow heading must remain translatable without disabling account protection.");
        var first = Row("원인은서버가", 96, 320, 174, 36);
        var second = Row("요청을 처리하는 동안", 97, 351, 273, 34);
        var third = Row("다른 작업을 기다리고 있기", 97, 380, 424, 37);
        var regions = VisualTextRegionBuilder.Build([first, second, third]);
        if (regions.Count != 1 || !regions[0].SourceText.Contains(first.Text)
            || !regions[0].SourceText.Contains(second.Text) || !regions[0].SourceText.Contains(third.Text))
            throw new Exception("Wrapped Hangul paragraph lost its complete semantic unit.");
        var separated = VisualTextRegionBuilder.Build([first, Row(second.Text, 97, 410, 273, 34)]);
        if (separated.Count != 2) throw new Exception("Paragraph gap incorrectly merged.");
        var terminal = VisualTextRegionBuilder.Build([Row("작업이끝납니다.", 96, 320, 174, 36), second]);
        if (terminal.Count != 2) throw new Exception("Completed short Hangul sentence merged with next row.");
        var mixed = VisualTextRegionBuilder.Build([Row("Show translation", 96, 320, 174, 36), second]);
        if (mixed.Count != 2) throw new Exception("UI control merged into Hangul prose.");
        var varied = VisualTextRegionBuilder.Build([
            Row("우리 팀은 새로운 서비스를 사용하고 있는데 문제가 발생합니다", 129, 143, 556, 31),
            Row("서버가 다른 작업을 처리하고 있을 수도 있습니다.", 97, 171, 540, 36)]);
        if (varied.Count != 1) throw new Exception("Moderate Hangul OCR height variation split body prose.");
    }
}
