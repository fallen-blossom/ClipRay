using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class AccountNameTranslationTests
{
    public static void LatinIdentityNamesAllowLocalizedDatesButNotUnchangedDates()
    {
        foreach (var (source, englishDate, chineseDate) in new[]
        {
            ("tamara @tamarajtran· Sep 18", "Sep 18", "9月18日"),
            ("Motion @motion_so·Sep 15", "Sep 15", "9月15日"),
        })
        {
            var request = new TranslationRequest("auto", "简体中文", source,
                textKind: TranslationTextKind.IdentityMetadata, scene: "social_media");
            var block = TranslationBlockContract.Create([request]).Single();
            var localized = block.ProtectedText.Text.Replace(englishDate, chineseDate,
                StringComparison.Ordinal);
            var result = Parse(block, localized);
            if (!result.IsSuccess || result.Text != source.Replace(englishDate, chineseDate,
                    StringComparison.Ordinal))
                throw new Exception($"A Latin account name blocked a localized date: {source}: {result.Status}");

            var unchanged = Parse(block, block.ProtectedText.Text);
            if (unchanged.IsSuccess || unchanged.Diagnostic?.Kind != TranslationDiagnosticKind.SourceUnchanged)
                throw new Exception($"An untranslated identity date was accepted: {source}");
        }

        var nameOnly = TranslationBlockContract.Create([
            new TranslationRequest("auto", "简体中文", "Loic @LDT0545",
                textKind: TranslationTextKind.IdentityMetadata, scene: "social_media")
        ]).Single();
        if (!Parse(nameOnly, nameOnly.ProtectedText.Text).IsSuccess)
            throw new Exception("A Latin display name and protected handle were rejected unchanged.");

        var japanese = TranslationBlockContract.Create([
            new TranslationRequest("auto", "简体中文", "くまの_よなよな @kumano_yonayona·6h",
                textKind: TranslationTextKind.IdentityMetadata, scene: "social_media")
        ]).Single();
        var untranslatedName = Parse(japanese,
            japanese.ProtectedText.Text.Replace("6h", "6小时", StringComparison.Ordinal));
        if (untranslatedName.IsSuccess
            || untranslatedName.Diagnostic?.Kind != TranslationDiagnosticKind.SourceLanguageOnly)
            throw new Exception("A non-Latin display name passed by translating only its date.");
    }

    public static void RecommendedAccountNamesArePreservedWithoutExemptingControls()
    {
        var regions = new[]
        {
            Region("zcode", "ZCode", VisualTextRole.Control, 1358, 733, 78, 28),
            Region("zcode-handle", "@zcode_ai", VisualTextRole.Metadata, 1359, 762, 104, 27),
            Region("zcode-follow", "Follow", VisualTextRole.Control, 1643, 753, 60, 18),
            Region("nano", "Nano(ナノ)", VisualTextRole.Control, 1356, 906, 124, 32),
            Region("nano-handle", "@Dstudio_ai", VisualTextRole.Metadata, 1359, 941, 114, 27),
            Region("nano-follow", "Follow", VisualTextRole.Control, 1643, 928, 60, 18),
            Region("search", "Search", VisualTextRole.Control, 1297, 30, 110, 24),
        };
        var requests = VisualTranslationRequestFactory.Create(regions, "简体中文", DeepSeekModels.Flash);
        foreach (var index in new[] { 0, 3 })
        {
            if (requests[index].TextKind != TranslationTextKind.BrandOrIdentifier
                || !requests[index].TranslationHint.Contains("Preserve", StringComparison.Ordinal))
                throw new Exception($"Account display name {requests[index].OcrText} was classified as a UI control.");
            var block = TranslationBlockContract.Create([requests[index]]).Single();
            if (!Parse(block, block.ProtectedText.Text).IsSuccess)
                throw new Exception($"Unchanged account display name {requests[index].OcrText} was rejected.");
        }
        foreach (var index in new[] { 2, 5, 6 })
        {
            if (requests[index].TextKind != TranslationTextKind.UiControl)
                throw new Exception($"Ordinary control {requests[index].OcrText} was treated as an account name.");
            var block = TranslationBlockContract.Create([requests[index]]).Single();
            var result = Parse(block, block.ProtectedText.Text);
            if (result.IsSuccess || result.Diagnostic?.Kind != TranslationDiagnosticKind.SourceUnchanged)
                throw new Exception($"Untranslated control {requests[index].OcrText} was accepted.");
        }

        var isolated = VisualTranslationRequestFactory.Create(
            [Region("isolated", "ZCode", VisualTextRole.Control, 10, 10, 78, 28)],
            "简体中文", DeepSeekModels.Flash).Single();
        if (isolated.TextKind != TranslationTextKind.UiControl)
            throw new Exception("An isolated unknown label was assumed to be an account name.");

        SportsTrendsPreserveNamesAndEventIdentifiers();
    }

    private static void SportsTrendsPreserveNamesAndEventIdentifiers()
    {
        var regions = new[]
        {
            Region("identity", "tamara @tamarajtran", VisualTextRole.Metadata, 500, 100, 230, 25),
            Region("sports", "Sports · Trending", VisualTextRole.Metadata, 1291, 182, 149, 32),
            Region("person", "Dante Moore", VisualTextRole.Control, 1293, 211, 136, 28),
            Region("trending-with", "Trending with", VisualTextRole.Metadata, 1293, 240, 122, 29),
            Region("topics", "Oregon, Raiola", VisualTextRole.Control, 1415, 240, 121, 29),
            Region("event", "#UFCVegas121", VisualTextRole.Control, 1416, 351, 120, 29),
            Region("ordinary-topic", "AI tools", VisualTextRole.Control, 1415, 410, 110, 29),
            Region("ordinary-main", "Travel Notice", VisualTextRole.Control, 500, 211, 160, 28),
        };
        var requests = VisualTranslationRequestFactory.Create(regions, "简体中文", DeepSeekModels.Flash);
        foreach (var index in new[] { 2, 4, 5 })
        {
            if (requests[index].TextKind != TranslationTextKind.BrandOrIdentifier)
                throw new Exception($"A sports name or event identifier was translated: {requests[index].OcrText}.");
            if (!TranslationBlockContract.CanBypassModel(TranslationBlockContract.Create([requests[index]])[0]))
                throw new Exception($"A protected name was still sent to the model: {requests[index].OcrText}.");
        }
        foreach (var index in new[] { 6, 7 })
            if (requests[index].TextKind == TranslationTextKind.BrandOrIdentifier)
                throw new Exception($"Ordinary text was overprotected: {requests[index].OcrText}.");
    }

    private static TranslationResult Parse(TranslationBlock block, string translated)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { [block.Id] = translated });
        var parsed = TranslationBlockContract.ParseResults([block], json);
        if (!parsed.IsStructuralSuccess || parsed.Results.Count != 1)
            throw new Exception("The account policy fixture did not produce a valid block response.");
        return parsed.Results[0];
    }

    private static VisualTextRegion Region(string id, string text, VisualTextRole role,
        double x, double y, double width, double height)
    {
        var bounds = new PixelRect(x, y, width, height);
        var block = new OcrTextBlock(text, bounds, [new OcrWord(text, bounds)]);
        return new VisualTextRegion(id, text, [block], bounds, role, VisualStructureConfidence.High);
    }
}
