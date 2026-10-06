using ClipRay.Core.Geometry;
using ClipRay.Acceptance.Testing;

var tests = new (string Name, Action Run)[]
{
    ("Normalize drag in any direction", NormalizeDrag),
    ("Move clamps to screen bounds", MoveClampsToBounds),
    ("Top-left resize respects minimum size", ResizeTopLeftRespectsMinimum),
    ("Bottom-right resize clamps to bounds", ResizeBottomRightClamps),
    ("Toolbar prefers below then flips above", ToolbarPlacementFlips),
    ("Toolbar clamps a complete shell inside the frozen monitor", ToolbarPlacementClampsCompleteShell),
    ("Toolbar avoids a visible OCR panel obstacle", ToolbarAvoidsVisibleObstacle),
    ("Parses a UTF-8 TXT acceptance case", ParsesTxtAcceptanceCase),
    ("Rejects duplicate TXT acceptance case IDs", RejectsDuplicateTxtAcceptanceCaseIds),
    ("Requires common-language and forty-line scrolling catalog data", RequiresCommonLanguageAndFortyLineScrollingCatalogData),
    ("Requires real-image OCR bake-off cases without synthetic substitutes", RequiresRealImageOcrBakeOffCases),
    ("Formats an explicit acceptance catalog report", FormatsExplicitAcceptanceCatalogReport),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
return failures == 0 ? 0 : 1;

static void NormalizeDrag()
{
    var result = SelectionMath.FromPoints(new PixelPoint(80, 70), new PixelPoint(20, 10));
    Equal(new PixelRect(20, 10, 60, 60), result);
}

static void MoveClampsToBounds()
{
    var bounds = new PixelRect(0, 0, 100, 80);
    var result = SelectionMath.Move(new PixelRect(20, 10, 40, 30), -50, 100, bounds);
    Equal(new PixelRect(0, 50, 40, 30), result);
}

static void ResizeTopLeftRespectsMinimum()
{
    var bounds = new PixelRect(0, 0, 200, 160);
    var result = SelectionMath.Resize(
        new PixelRect(20, 20, 80, 60),
        ResizeHandle.TopLeft,
        new PixelPoint(98, 78),
        bounds,
        16);
    Equal(new PixelRect(84, 64, 16, 16), result);
}

static void ResizeBottomRightClamps()
{
    var bounds = new PixelRect(0, 0, 200, 160);
    var result = SelectionMath.Resize(
        new PixelRect(20, 20, 80, 60),
        ResizeHandle.BottomRight,
        new PixelPoint(250, 190),
        bounds,
        16);
    Equal(new PixelRect(20, 20, 180, 140), result);
}

static void ToolbarPlacementFlips()
{
    var bounds = new PixelRect(0, 0, 300, 200);
    var below = SelectionMath.PlaceToolbar(new PixelRect(50, 20, 100, 60), 120, 36, bounds, 8);
    Equal(new PixelPoint(40, 88), below);

    var above = SelectionMath.PlaceToolbar(new PixelRect(50, 160, 100, 30), 120, 36, bounds, 8);
    Equal(new PixelPoint(40, 116), above);
}

static void ToolbarPlacementClampsCompleteShell()
{
    var bounds = new PixelRect(0, 0, 416, 240);
    var result = SelectionMath.PlaceToolbar(new PixelRect(10, 180, 80, 30), 392, 44, bounds, 8);

    Equal(new PixelPoint(8, 128), result);
}

static void ToolbarAvoidsVisibleObstacle()
{
    var bounds = new PixelRect(0, 0, 500, 300);
    var result = SelectionMath.PlaceToolbar(
        new PixelRect(300, 80, 100, 40),
        380,
        60,
        bounds,
        8,
        [new PixelRect(400, 76, 100, 174)]);

    Equal(new PixelPoint(112, 12), result);
}

static void ParsesTxtAcceptanceCase()
{
    const string text = """
        [case]
        id=translation-en-zh-01
        kind=translation
        source_language=en
        target_language=zh-Hans
        source_text=The capture is ready to save.
        expected_text=截图已准备好保存。
        """;

    var catalog = AcceptanceCatalog.Parse(text, "memory.txt");

    Equal(1, catalog.Cases.Count);
    Equal("translation-en-zh-01", catalog.Cases[0].Id);
    Equal("截图已准备好保存。", catalog.Cases[0].GetRequired("expected_text"));
}

static void RejectsDuplicateTxtAcceptanceCaseIds()
{
    const string text = """
        [case]
        id=duplicate
        kind=translation
        source_language=en
        target_language=zh-Hans
        source_text=Save
        expected_text=保存

        [case]
        id=duplicate
        kind=translation
        source_language=zh-Hans
        target_language=en
        source_text=保存
        expected_text=Save
        """;

    ExpectThrows<InvalidDataException>(() => AcceptanceCatalog.Parse(text, "memory.txt"));
}

static void RequiresCommonLanguageAndFortyLineScrollingCatalogData()
{
    var casesRoot = Path.Combine(Directory.GetCurrentDirectory(), "tests", "ClipRay.Acceptance", "cases");
    var catalog = AcceptanceCatalog.Load(casesRoot);
    var languages = new[] { "zh-Hans", "en", "ja", "fr", "ru" };
    var translations = catalog.Cases.Where(static item => item.Kind == "translation").ToArray();
    foreach (var language in languages)
    {
        Equal(true, translations.Any(item => item.GetRequired("source_language") == language));
        Equal(true, translations.Any(item => item.GetRequired("target_language") == language));
    }

    Equal(true, catalog.Cases.Any(static item => item.Kind == "mixed_translation"));
    var scrolling = catalog.Cases.Single(static item => item.Kind == "scroll_capture");
    Equal("40", scrolling.GetRequired("minimum_content_lines"));
    var contentLineCount = 0;
    foreach (var key in scrolling.Fields.Keys)
    {
        if (key.StartsWith("content_line.", StringComparison.Ordinal)) contentLineCount++;
    }
    Equal(40, contentLineCount);
}

static void FormatsExplicitAcceptanceCatalogReport()
{
    var casesRoot = Path.Combine(Directory.GetCurrentDirectory(), "tests", "ClipRay.Acceptance", "cases");
    var report = AcceptanceCatalogReport.Format(AcceptanceCatalog.Load(casesRoot));

    Equal(true, report.Contains("CATALOG_VALID cases=31", StringComparison.Ordinal));
    Equal(true, report.Contains("translation_cases=20", StringComparison.Ordinal));
    Equal(true, report.Contains("mixed_translation_cases=5", StringComparison.Ordinal));
    Equal(true, report.Contains("scroll_cases=1", StringComparison.Ordinal));
    Equal(true, report.Contains("ocr_bake_off_cases=5", StringComparison.Ordinal));
    Equal(true, report.Contains("ocr_real_fixture_pending=5", StringComparison.Ordinal));
    Equal(true, report.Contains("automated_not_run=25", StringComparison.Ordinal));
    Equal(true, report.Contains("manual_pending=6", StringComparison.Ordinal));
    Equal(true, report.Contains("languages=zh-Hans,en,ja,fr,ru", StringComparison.Ordinal));
}

static void RequiresRealImageOcrBakeOffCases()
{
    var casesRoot = Path.Combine(Directory.GetCurrentDirectory(), "tests", "ClipRay.Acceptance", "cases");
    var ocrCases = AcceptanceCatalog.Load(casesRoot).Cases.Where(static item => item.Kind == "ocr_bake_off").ToArray();

    Equal(5, ocrCases.Length);
    foreach (var item in ocrCases)
    {
        Equal("real_ui_image_required", item.GetRequired("fixture_status"));
        Equal(false, item.Fields.ContainsKey("synthetic_image"));
        Equal(true, item.GetRequired("expected_text").Length > 0);
    }
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }
}

static void ExpectThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
