using ClipRay.App.Translation;
using ClipRay.App.Ocr;

internal static class MachineChineseSourceTests
{
    public static void MachineOcrLoadsOnlySupportedSourceScripts()
    {
        if (!CommonScriptOcrRuntime.RecognitionScripts.SequenceEqual(["latin", "cjk"]))
            throw new Exception("Machine OCR loaded recognizers outside its English and Chinese language contract.");
    }

    public static void MachineOcrRetainsChineseAndEnglish()
    {
        using var bitmap = new System.Drawing.Bitmap(1000, 400);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        using (var font = new System.Drawing.Font("Microsoft YaHei", 24))
        {
            graphics.Clear(System.Drawing.Color.Black);
            graphics.DrawString("中文直接不翻译，保留原文。", font, System.Drawing.Brushes.White, 20, 40);
            graphics.DrawString("This English sentence should still translate quickly.", font, System.Drawing.Brushes.White, 20, 140);
        }
        using var service = ClipRay.App.Ocr.OcrService.CreateForEnglishMachine();
        var result = service.RecognizeAsync(bitmap, default).GetAwaiter().GetResult();
        var text = string.Join("\n", result.Blocks.Select(block => block.Text));
        if (!result.IsSuccess || !text.Contains("中文") || !text.Contains("English sentence"))
            throw new Exception("Machine OCR lost Chinese or English source on the bilingual fixture: " + text);
    }

    public static void ChineseSourceDoesNotStartEnglishTranslation()
    {
        using var provider = new LocalMachineTranslationProvider(new RejectProcess());
        foreach (var source in new[] { "中文直接不翻译。", "我在 Codex 界面看到的中文翻译十分怪异。", "停止翻译", "显示实时翻译区域边框（光圈）" })
        foreach (var language in new[] { "auto", "zh" })
        {
            var result = provider.TranslateManyAsync([new TranslationRequest(language, "zh", source)], default)
                .GetAwaiter().GetResult()[0];
            if (!result.IsSuccess || result.Text != source || result.Status != "machine-preserved")
                throw new Exception("Chinese source was not retained exactly: " + source);
        }
    }

    public static void MixedEnglishTranslatesWithoutSendingChineseToModel()
    {
        using var provider = new LocalMachineTranslationProvider(new MixedProcess());
        var result = provider.TranslateManyAsync([new TranslationRequest("auto", "zh",
            "Please keep the Chinese greeting 你好 unchanged while translating this English sentence.")], default)
            .GetAwaiter().GetResult()[0];
        if (!result.IsSuccess || !result.Text.Contains("你好") || !result.Text.Contains("请保留"))
            throw new Exception("Mixed English source lost its unchanged Chinese span or English translation.");
    }

    private sealed class RejectProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            throw new Exception("An already Chinese region must not start the English model.");
    }
    private sealed class MixedProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            if (request.Texts.Any(text => text.Contains("你好"))) throw new Exception("Chinese span reached English model input.");
            return Task.FromResult<IReadOnlyList<string>>(request.Texts.Select(text => text.Contains("Please") ? "请保留中文问候" : "原样，并翻译这个英文句子。").ToArray());
        }
    }
}
