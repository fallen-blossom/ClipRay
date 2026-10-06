using ClipRay.App.Translation;

internal static class CommonLanguageTests
{
    public static void ExplicitJapaneseHanReachesWorker()
    {
        using var provider = new LocalMachineTranslationProvider(new ExplicitJapaneseProcess(), supportsCommonLanguages: true);
        foreach (var kind in new[] { TranslationTextKind.Body, TranslationTextKind.General })
        {
            var result = provider.TranslateManyAsync([new TranslationRequest("Japanese", "zh", "手紙", textKind: kind)], default).GetAwaiter().GetResult()[0];
            if (!result.IsSuccess || result.Text != "信件") throw new Exception("Explicit Japanese Han was preserved as Chinese.");
        }
        if (LocalLmtLanguage.Normalize("es") != "Spanish" || LocalLmtLanguage.Normalize("ja") != "Japanese")
            throw new Exception("Language codes do not normalize to model prompt language names.");
    }
    private sealed class ExplicitJapaneseProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            if (request.SourceLanguage != "ja" || request.Texts.Count != 1 || request.Texts[0] != "手紙")
                throw new Exception("Explicit Japanese did not reach worker intact.");
            return Task.FromResult<IReadOnlyList<string>>(["信件"]);
        }
    }
    public static void MixedJapaneseKeepsFirstCharacterAndChineseLiteral()
    {
        const string source = "これは日本語です。English should also be translated. 中文保持原样。";
        var regions = VisualTextRegionBuilder.Build([new ClipRay.App.Ocr.OcrTextBlock(source,new(0,0,800,24),[])]);
        if(regions[0].SourceText != source) throw new Exception("Mixed Japanese lost its first character as icon noise.");
        using var provider = new LocalMachineTranslationProvider(new MixedJapaneseProcess(), supportsCommonLanguages: true);
        var result = provider.TranslateManyAsync([new TranslationRequest("auto","zh",source,textKind:TranslationTextKind.Body)],default).GetAwaiter().GetResult()[0];
        if(result.IsSuccess) throw new Exception("A translated Chinese literal was accepted in a mixed Japanese paragraph.");
    }
    private sealed class MixedJapaneseProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<string>>(["这是日语。英语也应该翻译。中语保持原始。"]);
    }
    public static void ParagraphCannotJumpOverAnotherDetectedLine()
    {
        var blocks = new[] {
            new ClipRay.App.Ocr.OcrTextBlock("First independent sentence is here.",new(30,0,400,38),[]),
            new ClipRay.App.Ocr.OcrTextBlock("Middle sentence must keep reading order.",new(30,23,500,41),[]),
            new ClipRay.App.Ocr.OcrTextBlock("Last sentence must not join first alone.",new(30,52,500,39),[])
        };
        var regions = VisualTextRegionBuilder.Build(blocks);
        if (regions.Any(region => region.SourceText.Contains("First") && region.SourceText.Contains("Last")
            && !region.SourceText.Contains("Middle"))) throw new Exception("Paragraph grouping skipped an intervening line.");
    }
    public static void SupplementalFragmentCannotReplaceCompleteLine()
    {
        var selected = ClipRay.App.Ocr.ScriptOcrSelection.Select([
            new("I can use this app today, but I cannot use it tomorrow.", .95, "latin"),
            new("丨", .999, "cjk")]);
        if(selected?.Script != "latin") throw new Exception("A tiny script fragment replaced a complete English line.");
        var ordered = ClipRay.App.Ocr.ScriptOcrSelection.RestoreReadingOrder(".ةعاس 24 ةدمل 18", "arabic");
        if(ordered != "18 لمدة 24 ساعة.") throw new Exception("Arabic reading order reversed numeric identifiers.");
    }
    public static void JapaneseKanjiAreNotProtectedChinese()
    {
        using var provider = new LocalMachineTranslationProvider(new JapaneseProcess(), supportsCommonLanguages: true);
        foreach (var language in new[] { "auto", "ja" })
        {
            var result = provider.TranslateManyAsync([new TranslationRequest(language, "zh",
                "今日は使えますが、明日は使えません。", textKind: TranslationTextKind.Body)], default).GetAwaiter().GetResult()[0];
            if (!result.IsSuccess || result.Text != "今天可以使用，但明天不能使用。")
                throw new Exception("Japanese kanji were preserved or rejected as Chinese.");
        }
    }
    private sealed class JapaneseProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            if (request.Texts.Count != 1 || request.Texts[0] != "今日は使えますが、明日は使えません。")
                throw new Exception("Japanese sentence was fragmented.");
            return Task.FromResult<IReadOnlyList<string>>(["今天可以使用，但明天不能使用。"]);
        }
    }
}
