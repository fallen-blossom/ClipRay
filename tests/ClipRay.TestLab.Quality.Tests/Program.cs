using System.Drawing;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using ClipRay.TestLab;

const string CaseId = "synthetic-visual-check";
const string ImageSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
var oracle = TestLabQualityOracle.Load(AppContext.BaseDirectory)
    ?? throw new InvalidOperationException("The synthetic quality oracle is missing.");

var firstBounds = new PixelRect(10, 10, 130, 24);
var secondBounds = new PixelRect(10, 50, 130, 24);
var firstBlock = new OcrTextBlock("First", firstBounds, []);
var secondBlock = new OcrTextBlock("Second", secondBounds, []);
var firstRegion = new VisualTextRegion("first", "First", [firstBlock], firstBounds,
    VisualTextRole.Body, VisualStructureConfidence.High);
var secondRegion = new VisualTextRegion("second", "Second", [secondBlock], secondBounds,
    VisualTextRole.Body, VisualStructureConfidence.High);
var textLayout = new TranslationTextLayout(true, 12, 15, SourceFontWeight.Normal,
    SourceFontCategory.SansSerif, false, SourceTextAlignment.Left, 0, "Segoe UI");
var firstLayout = new InPlaceLayoutItem(firstBlock, "第一段译文", firstBounds,
    InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White,
    SourceRegion: firstRegion, TextLayout: textLayout);
var secondLayout = new InPlaceLayoutItem(secondBlock, "第二段译文", secondBounds,
    InPlaceBackgroundMode.FlatSampled, Color.Black, Color.FromArgb(0x39, 0x78, 0xDB),
    SourceRegion: secondRegion, TextLayout: textLayout);
var good = new ApplicationTranslationOutcome(true, TranslationPipelineStage.None, "completed",
    [firstBlock, secondBlock], [firstRegion, secondRegion],
    [new TranslationResult(true, "第一段译文", "ok"),
        new TranslationResult(true, "第二段译文", "ok")],
    [firstLayout, secondLayout]);

TestLabOracleAssessment Assess(ApplicationTranslationOutcome outcome) =>
    oracle.Assess(CaseId, ImageSha256, "简体中文", outcome, true, 300, 100)
    ?? throw new InvalidOperationException("The synthetic case was not assessed.");

void ExpectFinding(string code, ApplicationTranslationOutcome outcome)
{
    var assessment = Assess(outcome);
    if (assessment.LayoutQuality != "failed" || !assessment.Findings.Any(item => item.Code == code))
        throw new InvalidOperationException($"The intentionally broken {code} example escaped the oracle.");
}

var accepted = Assess(good);
if (accepted.Findings.Count != 0
    || accepted.LayoutQuality != "machine-checked-needs-visual-review")
    throw new InvalidOperationException("The intact plan was incorrectly judged or marked visually reviewed.");

ExpectFinding("font-tier-inconsistent", good with { LayoutItems = [firstLayout,
    secondLayout with { TextLayout = textLayout with { FontSizePixels = 25 } }] });
ExpectFinding("foreground-color-mismatch", good with { LayoutItems = [firstLayout,
    secondLayout with { ForegroundColor = Color.White }] });
ExpectFinding("alignment-mismatch", good with { LayoutItems = [firstLayout with {
    TextLayout = textLayout with { Alignment = SourceTextAlignment.Center } }, secondLayout] });
ExpectFinding("layout-overlap", good with { LayoutItems = [firstLayout,
    secondLayout with { Bounds = secondBounds with { Y = 25 } }] });
ExpectFinding("layout-outside-image", good with { LayoutItems = [firstLayout,
    secondLayout with { Bounds = secondBounds with { X = 250 } }] });
ExpectFinding("layout-outside-allowed-bounds", good with { LayoutItems = [firstLayout,
    secondLayout with { Bounds = secondBounds with { X = 160 } }] });
if (Assess(good with { LayoutItems = [firstLayout,
        secondLayout with { Bounds = secondBounds with { X = 4 } }] })
    .Findings.Any(item => item.Code == "layout-outside-allowed-bounds"))
    throw new InvalidOperationException("Minor annotated-box jitter should not fail layout quality.");
ExpectFinding("layout-text-mismatch", good with { LayoutItems = [firstLayout,
    secondLayout with { Text = "第二段" }] });
ExpectFinding("region-not-rendered", good with { LayoutItems = [firstLayout] });
ExpectFinding("layout-duplicated", good with { LayoutItems = [firstLayout, secondLayout, secondLayout] });

var brandBlock = new OcrTextBlock("Grok", firstBounds, []);
var brandRegion = new VisualTextRegion("brand", "Grok", [brandBlock], firstBounds,
    VisualTextRole.Body, VisualStructureConfidence.High);
using var protectedSourceImage = new Bitmap(300, 100);
using var protectedRenderedImage = new Bitmap(protectedSourceImage);
TestLabOracleAssessment AssessBrand(TranslationResult result, IReadOnlyList<InPlaceLayoutItem> layout) =>
    oracle.Assess("synthetic-source-preserve", ImageSha256, "简体中文",
        good with { OcrBlocks = [brandBlock], Regions = [brandRegion], Results = [result],
            LayoutItems = layout }, true, 300, 100,
        protectedSourceImage, protectedRenderedImage)
    ?? throw new InvalidOperationException("The protected brand case was not assessed.");
if (AssessBrand(new TranslationResult(false, "Grok", "source-preserved"), []).Findings.Count != 0)
    throw new InvalidOperationException("Preserving a brand should not be a translation failure.");
if (!AssessBrand(new TranslationResult(true, "格罗克", "ok"), []).Findings.Any(item =>
        item.Code == "protected-source-changed"))
    throw new InvalidOperationException("A changed protected brand escaped the oracle.");

var identityBlock = new OcrTextBlock("tamara @tamara · Sep 18", firstBounds, []);
var identityRegion = new VisualTextRegion("identity-row", identityBlock.Text,
    [identityBlock], firstBounds, VisualTextRole.Metadata, VisualStructureConfidence.High);
var identityPreserved = new ApplicationTranslationOutcome(true, TranslationPipelineStage.None,
    "completed", [identityBlock], [identityRegion],
    [new TranslationResult(true, "tamara @tamara · 9月18日", "ok")], [],
    [new InPlaceLayoutRejection(1, "identity-pixels-preserved")]);
if (!oracle.AllowsIntentionalPreservation("synthetic-identity-pixels", ImageSha256,
        "简体中文", identityPreserved)
    || oracle.AllowsIntentionalPreservation(CaseId, ImageSha256,
        "简体中文", identityPreserved)
    || oracle.AllowsIntentionalPreservation("synthetic-identity-pixels", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        "简体中文", identityPreserved))
    throw new InvalidOperationException("An unannotated or mismatched preserved identity bypassed the structural gate.");
TestLabOracleAssessment AssessIdentity(string translated, IReadOnlyList<InPlaceLayoutItem> layout,
    Bitmap? rendered = null) =>
    oracle.Assess("synthetic-identity-pixels", ImageSha256, "简体中文",
        good with { OcrBlocks = [identityBlock], Regions = [identityRegion],
            Results = [new TranslationResult(true, translated, "ok")], LayoutItems = layout },
        true, 300, 100, protectedSourceImage, rendered ?? protectedRenderedImage)
    ?? throw new InvalidOperationException("The identity pixel case was not assessed.");
if (AssessIdentity("tamara @tamara · 9月18日", []).Findings.Count != 0)
    throw new InvalidOperationException("A localized date with preserved identity pixels should pass.");
if (AssessIdentity("tamara @tamara · 9月18日", [secondLayout with {
        Bounds = firstBounds with { X = 15 } }]).Findings.Any(item =>
        item.Code == "protected-pixels-covered"))
    throw new InvalidOperationException("An overlapping layout box with unchanged protected pixels was treated as damage.");
using var changedProtectedPixels = new Bitmap(protectedSourceImage);
changedProtectedPixels.SetPixel((int)firstBounds.Left + 1, (int)firstBounds.Top + 1, Color.Magenta);
if (!AssessIdentity("tamara @tamara · 9月18日", [firstLayout with {
        SourceBlock = identityBlock, SourceRegion = identityRegion }], changedProtectedPixels).Findings.Any(item =>
        item.Code == "protected-pixels-covered"))
    throw new InvalidOperationException("An overlay over protected identity pixels escaped the oracle.");
if (!AssessIdentity("tamara @tamara · 9月18日", [secondLayout with {
        Bounds = firstBounds with { X = 15 } }], changedProtectedPixels).Findings.Any(item =>
        item.Code == "protected-pixels-covered"))
    throw new InvalidOperationException("An unrelated overlay over identity pixels escaped the oracle.");
if (!AssessIdentity("tamara @tamara · 9月18日", [], changedProtectedPixels).Findings.Any(item =>
        item.Code == "protected-pixels-changed"))
    throw new InvalidOperationException("A changed rendered identity pixel escaped the oracle.");
if (!oracle.Assess("synthetic-identity-pixels", ImageSha256, "简体中文",
        good with { OcrBlocks = [identityBlock], Regions = [identityRegion],
            Results = [new TranslationResult(true, "tamara @tamara · 9月18日", "ok")], LayoutItems = [] },
        true, 300, 100)!.Findings.Any(item => item.Code == "protected-pixels-unverified"))
    throw new InvalidOperationException("Missing rendered-pixel evidence escaped the oracle.");
if (!AssessIdentity("tamara @tamara · Sep 18", []).Findings.Any(item =>
        item.Code == "meaning-signal-missing"))
    throw new InvalidOperationException("An untranslated identity-row date escaped the oracle.");

VisualTextRegion ReadingRegion(string id, string text, PixelRect bounds)
{
    var block = new OcrTextBlock(text, bounds, []);
    return new VisualTextRegion(id, text, [block], bounds,
        VisualTextRole.Body, VisualStructureConfidence.High);
}

var sidebarFirst = ReadingRegion("sidebar-first", "Side one", new PixelRect(10, 10, 80, 20));
var mainFirst = ReadingRegion("main-first", "Main one", new PixelRect(150, 20, 100, 20));
var sidebarSecond = ReadingRegion("sidebar-second", "Side two", new PixelRect(10, 40, 80, 20));
var mainSecond = ReadingRegion("main-second", "Main two", new PixelRect(150, 50, 100, 20));

TestLabOracleAssessment AssessReading(string caseId, IReadOnlyList<VisualTextRegion> regions) =>
    oracle.Assess(caseId, ImageSha256, "简体中文",
        new ApplicationTranslationOutcome(true, TranslationPipelineStage.None, "completed",
            [], regions, [], []), true)
    ?? throw new InvalidOperationException("The synthetic reading case was not assessed.");

// The truth alternates columns by y, while this valid output reads each column in full.
var columnMajor = new[] { sidebarFirst, sidebarSecond, mainFirst, mainSecond };
var grouped = AssessReading("synthetic-reading-groups", columnMajor);
if (grouped.Findings.Count != 0 || grouped.ContentQuality != "screened-needs-human-review")
    throw new InvalidOperationException("Column-major reading was incorrectly rejected across groups.");

var legacy = AssessReading("synthetic-reading-legacy", columnMajor);
if (!legacy.Findings.Any(item => item.Code == "reading-order" && item.Region == "sidebar-second"))
    throw new InvalidOperationException("An ungrouped old oracle lost its global ordering rule.");

var sidebarReversed = AssessReading("synthetic-reading-groups",
    [sidebarSecond, sidebarFirst, mainFirst, mainSecond]);
if (!sidebarReversed.Findings.Any(item => item.Code == "reading-order" && item.Region == "sidebar-second"))
    throw new InvalidOperationException("A reversed sidebar sequence escaped the grouped oracle.");

var mainReversed = AssessReading("synthetic-reading-groups",
    [sidebarFirst, sidebarSecond, mainSecond, mainFirst]);
if (!mainReversed.Findings.Any(item => item.Code == "reading-order" && item.Region == "main-second"))
    throw new InvalidOperationException("A reversed main-column sequence escaped the grouped oracle.");

Console.WriteLine("QUALITY_VISUAL_ORACLE_TESTS_PASSED: eight visual defects detected; grouped and legacy reading order checked.");
