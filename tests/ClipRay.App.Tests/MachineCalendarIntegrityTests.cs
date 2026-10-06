using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class MachineCalendarIntegrityTests
{
    public static void OcrHandleNoiseDoesNotEnterTheModel()
    {
        using var provider = new LocalMachineTranslationProvider(new NoisyHandleProcess());
        const string handle = "@mvpate120Ø0";
        var result = provider.TranslateManyAsync([new TranslationRequest("auto", "zh",
            handle + "\nViews do not represent my employer.", textKind: TranslationTextKind.Body)], default)
            .GetAwaiter().GetResult()[0];
        if (!result.IsSuccess || !result.Text.Contains(handle) || !result.Text.Contains("意见"))
            throw new Exception("An OCR-corrupted account was shortened or caused body translation to be rejected.");
        using var counters = new LocalMachineTranslationProvider(new RejectProcess());
        var identifiers = counters.TranslateManyAsync(new[]{ "8b87b26", "19d53f4" }.Select(text => new TranslationRequest("auto", "zh", text)).ToArray(), default).GetAwaiter().GetResult();
        if (identifiers.Any(result => !result.IsSuccess || result.Status != "machine-preserved"))
            throw new Exception("A commit identifier reached the model.");
        var counter = counters.TranslateManyAsync([new TranslationRequest("auto", "zh", "u24",
            textKind: TranslationTextKind.UiControl),new TranslationRequest("auto","zh","t13",textKind:TranslationTextKind.UiControl)], default).GetAwaiter().GetResult();
        if (counter.Any(result=>!result.IsSuccess || result.Status != "machine-preserved") || counter[0].Text!="u24" || counter[1].Text!="t13")
            throw new Exception("An OCR-prefixed numeric social counter reached the model.");
    }

    private sealed class NoisyHandleProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            if (request.Texts.Any(text => text.Contains('@') || text.Contains('Ø')))
                throw new Exception("Account noise reached model input.");
            return Task.FromResult<IReadOnlyList<string>>(request.Texts.Select(_ => "意见不代表我的雇主。").ToArray());
        }
    }

    public static void CalendarNamesCanBecomeNumericMonths()
    {
        foreach (var pair in new[] {
            ("At the end of May, sales reached 10.", "5月底，销量达到10。"),
            ("Since January, we have 20 users.", "自1月以来，我们有20名用户。"),
            ("Released on May 12.", "5月12日发布。"),
            ("Released on 12 May.", "5月12日发布。"),
            ("In December and May we shipped updates.", "12月和5月发布了更新。") })
            if (TranslationContentIntegrity.FindMismatch(pair.Item1, pair.Item2) is not null)
                throw new Exception("A correct named calendar month conversion was rejected: " + pair.Item1);
        foreach (var pair in new[] {
            ("At the end of May, sales reached 10.", "5月底，销量达到11。"),
            ("At the end of May.", "6月底。"),
            ("At the end of May.", "5月底，还有5。"),
            ("May I use it?", "我能使用5月吗？"),
            ("March toward home.", "3月走回家。"),
            ("It may work.", "它在5月可以工作。") })
            if (TranslationContentIntegrity.FindMismatch(pair.Item1, pair.Item2) is null)
                throw new Exception("An invented or changed number was accepted: " + pair.Item1);
    }

    public static void WrittenNumberConversionKeepsExplicitNumerals()
    {
        const string source = "The blue train leaves at eight. Keep ticket XR-17 until arrival.";
        const string correct = "蓝色列车在8点出发。请保留XR-17车票直到到达。";
        if (TranslationContentIntegrity.FindMismatch(source, correct) is not null)
            throw new Exception("Writing 'eight' as 8 rejected an otherwise intact ticket number.");
        if (TranslationContentIntegrity.FindMismatch(source,
                "蓝色列车在9点出发。请保留XR-17车票直到到达。") is null
            || TranslationContentIntegrity.FindMismatch(source,
                "蓝色列车在8点出发。请保留XR-18车票直到到达。") is null
            || TranslationContentIntegrity.FindMismatch("Hello.", "你好7。") is null)
            throw new Exception("A changed explicit number or invented number passed integrity validation.");

        using var provider = new LocalMachineTranslationProvider(new FixedTranslationProcess(correct));
        var result = provider.TranslateManyAsync([new TranslationRequest("en", "zh", source,
            textKind: TranslationTextKind.Body)], default).GetAwaiter().GetResult()[0];
        if (!result.IsSuccess || result.Text != correct)
            throw new Exception("The machine provider rejected a valid written-number conversion.");
    }

    public static void ConjoinedOcrDigitsRemainNumericInvariants()
    {
        const string source = "Keep ticketXR-17until arrival.";
        const string correct = "请保留 XR-17 车票直到到达。";
        if (TranslationContentIntegrity.FindMismatch(source, correct) is not null)
            throw new Exception("OCR-conjoined ticket digits were mistaken for an invented number.");
        if (TranslationContentIntegrity.FindMismatch(source, "请保留 XR-18 车票直到到达。")
                != TranslationInvariantMismatch.ModelToken
            || TranslationContentIntegrity.FindMismatch(source, "请保留 XR-17 车票直到 2020 年。")
                != TranslationInvariantMismatch.Number)
            throw new Exception("Changed or invented digits escaped the OCR-conjoined numeric guard.");
        if (TranslationContentIntegrity.FindMismatch(source, "请保留 AB-17 车票直到到达。")
            != TranslationInvariantMismatch.ModelToken)
            throw new Exception("A changed OCR-conjoined identifier escaped integrity validation.");
    }

    private sealed class FixedTranslationProcess(string translation) : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<string>>(request.Texts.Select(_ => translation).ToArray());
    }

    public static void WebAddressesAndAgesBypassTheModel()
    {
        using var provider = new LocalMachineTranslationProvider(new RejectProcess());
        foreach (var text in new[] { "donvitocodes.com/coworker", "https://github.com/donvito", "·2h", "3d",
            "donvitocodes.com/coworker\ngithub.com/donvito/codex-..." })
        {
            var region = new VisualTextRegion("sample", text, [], new PixelRect(0,0,400,30),
                VisualTextRole.Body, VisualStructureConfidence.High);
            var request = VisualTranslationRequestFactory.Create([region], "zh", DeepSeekModels.Flash)[0];
            var result = provider.TranslateManyAsync([request], default).GetAwaiter().GetResult()[0];
            if (!result.IsSuccess || result.Text != text || result.Status != "machine-preserved")
                throw new Exception("A web address or compact age was sent to the model: " + text);
        }
    }

    public static void InlineWebAddressesStayOutOfModelInput()
    {
        using var provider = new LocalMachineTranslationProvider(new InlineProcess());
        var result = provider.TranslateManyAsync([new TranslationRequest("en", "zh",
            "Visit donvitocodes.com/coworker for updates.")], default).GetAwaiter().GetResult()[0];
        if (!result.IsSuccess || !result.Text.Contains("donvitocodes.com/coworker", StringComparison.Ordinal)
            || !result.Text.Contains("访问", StringComparison.Ordinal))
            throw new Exception("Inline address protection lost the URL or surrounding translated prose.");
    }

    private sealed class InlineProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            if (request.Texts.Any(text => text.Contains("donvitocodes", StringComparison.Ordinal)))
                throw new Exception("A URL was included in machine model input.");
            return Task.FromResult<IReadOnlyList<string>>(request.Texts.Select(text => text.Contains("Visit") ? "访问" : "获取更新。").ToArray());
        }
    }

    public static void FailedTranslationEvidenceCannotChangeResults()
    {
        var calls = 0;
        using var provider = new LocalMachineTranslationProvider(new InventedNumberProcess(), (source, restored, mismatch) =>
        {
            if (source != "Hello." || restored != "你好2020。" || mismatch != TranslationInvariantMismatch.Number)
                throw new Exception("Unexpected evidence payload.");
            calls++;
            throw new System.IO.IOException("An unavailable evidence destination must not change the result.");
        });
        var result = provider.TranslateManyAsync([new TranslationRequest("en", "zh", "Hello.")], default)
            .GetAwaiter().GetResult()[0];
        if (calls != 1 || result.IsSuccess || result.Status != "machine-content-integrity-failed")
            throw new Exception("Optional failure evidence affected integrity enforcement.");
    }

    public static void ProtectedFailureRecordsRejectedCandidateWithoutChangingResult()
    {
        const string source = "The redtrain leavesat nine.Keepticket XR-17 until arrival.";
        const string candidate = "红色列车九点出发，请保留 XR-17 车票。";
        var calls = 0;
        using var provider = new LocalMachineTranslationProvider(new FixedTranslationProcess(candidate),
            onProtectedFailure: (original, restored) =>
            {
                if (original != source || restored != candidate)
                    throw new Exception("Protected failure evidence lost the rejected candidate.");
                calls++;
                throw new System.IO.IOException("Diagnostic writes cannot affect translation results.");
            });
        var result = provider.TranslateManyAsync([new TranslationRequest("en", "zh", source,
            textKind: TranslationTextKind.Body)], default).GetAwaiter().GetResult()[0];
        if (calls != 1 || result.IsSuccess || result.Status != "machine-protected-content-failed")
            throw new Exception("A sentence-shaped OCR domain did not produce protected failure evidence.");
    }

    private sealed class InventedNumberProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<string>>(request.Texts.Select(_ => "你好2020。").ToArray());
    }

    private sealed class RejectProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            throw new InvalidOperationException("Protected UI text must bypass the model.");
    }
}
