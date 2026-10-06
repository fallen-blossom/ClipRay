using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LiveTranslationViewportCommitPolicyTests
{
    public static void SafePartialViewportKeepsSuccessfulSiblings()
    {
        var nearEmpty = new LiveTranslationFrame("Machine", "translation-partial", [],
            "Layout:partial-or-failed")
        {
            ExpectedLayoutCount = 8,
            ResultState = LiveTranslationViewportResultState.AcceptablePartial,
        };
        var mostlyRejected = nearEmpty with { RejectedLayoutCount = 5 };
        if (LiveTranslationViewportResultPolicy.Classify(nearEmpty, 1)
                != LiveTranslationViewportResultState.AcceptablePartial
            || LiveTranslationViewportResultPolicy.Classify(nearEmpty, 4)
                != LiveTranslationViewportResultState.AcceptablePartial
            || LiveTranslationViewportResultPolicy.Classify(mostlyRejected, 4)
                != LiveTranslationViewportResultState.AcceptablePartial
            || LiveTranslationViewportResultPolicy.Classify(nearEmpty, 0)
                != LiveTranslationViewportResultState.Failed
            || LiveTranslationViewportResultPolicy.CanPresent(LiveTranslationViewportResultState.Failed))
            throw new Exception("A safe partial viewport hid successful siblings or treated zero output as usable.");
    }

    public static void UnsafeViewportGeometryCannotBePresented()
    {
        using var bitmap = new Bitmap(100, 100);
        var snapshot = new LiveTranslationPixelSnapshot(bitmap);
        var first = Item(new PixelRect(10, 10, 50, 25), "一");
        var second = Item(new PixelRect(20, 12, 50, 25), "二");
        var frame = new LiveTranslationFrame("Machine", "completed", [first, second])
        {
            ExpectedLayoutCount = 2,
        };
        var overlap = LiveTranslationViewportCommitPolicy.Measure(snapshot, frame, 2, [first, second]);
        if (overlap.OverlappingLayouts == 0
            || LiveTranslationViewportResultPolicy.Classify(frame, overlap)
                != LiveTranslationViewportResultState.Failed)
            throw new Exception("A materially overlapping viewport passed the quality gate.");

        var unreadable = first with { TextLayout = new(false, 3, 4,
            SourceFontWeight.Normal, SourceFontCategory.SansSerif, false,
            SourceTextAlignment.Left, 0, "Segoe UI") };
        var outside = second with { Bounds = new PixelRect(90, 90, 50, 25) };
        var invalid = LiveTranslationViewportCommitPolicy.Measure(snapshot, frame, 2, [unreadable, outside]);
        if (invalid.UnreadableLayouts == 0 || invalid.OutOfBoundsLayouts == 0
            || LiveTranslationViewportResultPolicy.Classify(frame, invalid)
                != LiveTranslationViewportResultState.Failed)
            throw new Exception("An unreadable or out-of-bounds viewport passed the quality gate.");

        var bodyBounds = new PixelRect(5, 5, 90, 70);
        var bodyBlock = new OcrTextBlock("important body", bodyBounds, []);
        var body = new VisualTextRegion("body", bodyBlock.Text, [bodyBlock], bodyBounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var controlBounds = new PixelRect(5, 82, 20, 10);
        var controlBlock = new OcrTextBlock("button", controlBounds, []);
        var control = new VisualTextRegion("control", controlBlock.Text, [controlBlock], controlBounds,
            VisualTextRole.Control, VisualStructureConfidence.High);
        var retainedControl = Item(controlBounds, "按钮") with { SourceRegion = control };
        var missingBody = new LiveTranslationFrame("Machine", "partial", [retainedControl])
        {
            SourceRegions = [body, control],
            ExpectedLayoutCount = 2,
            ExpectedPrimaryLayoutCount = 1,
            ExpectedPrimaryLayoutArea = bodyBounds.Width * bodyBounds.Height,
            ResultState = LiveTranslationViewportResultState.AcceptablePartial,
        };
        var primaryMetrics = LiveTranslationViewportCommitPolicy.Measure(
            snapshot, missingBody, 1, [retainedControl]);
        if (!primaryMetrics.HasMissingPrimaryContent
            || LiveTranslationViewportResultPolicy.Classify(missingBody, primaryMetrics)
                != LiveTranslationViewportResultState.AcceptablePartial)
            throw new Exception("A safe translated control was hidden because another region kept its source text.");
    }

    public static void OneUnsafeLayoutDoesNotBlankSafeTranslations()
    {
        using var bitmap = new Bitmap(100, 100);
        var snapshot = new LiveTranslationPixelSnapshot(bitmap);
        var safe = Item(new PixelRect(5, 5, 20, 10), "安全译文");
        var unreadable = Item(new PixelRect(40, 5, 20, 10), "过小译文") with
        {
            TextLayout = new(true, 2.15, 4, SourceFontWeight.Normal,
                SourceFontCategory.SansSerif, false, SourceTextAlignment.Left, 0, "Segoe UI")
        };
        var outside = Item(new PixelRect(95, 5, 20, 10), "越界译文");
        var overlappingFirst = Item(new PixelRect(5, 40, 40, 15), "重叠一");
        var overlappingSecond = Item(new PixelRect(15, 42, 40, 15), "重叠二");
        var items = new[] { safe, unreadable, outside, overlappingFirst, overlappingSecond };
        var frame = new LiveTranslationFrame("Machine", "completed", items)
        {
            ExpectedLayoutCount = items.Length,
        };
        var commit = LiveTranslationViewportCommitPolicy.Commit(
            snapshot, snapshot, [], LiveTranslationRegionWitness.Create(snapshot, items), frame);

        if (commit.ResultState != LiveTranslationViewportResultState.AcceptablePartial
            || commit.Witness.Count != 1 || !ReferenceEquals(commit.Witness.Items[0], safe)
            || commit.Quarantined.Count != 4
            || commit.Quarantined.Count(item => item.Reason == "unreadable-text") != 1
            || commit.Quarantined.Count(item => item.Reason == "out-of-bounds") != 1
            || commit.Quarantined.Count(item => item.Reason == "material-overlap") != 2
            || commit.Metrics.HasUnsafeLayout || commit.Metrics.RejectedLayouts != 4)
            throw new Exception("A bad layout hid safe siblings or entered the displayed viewport.");
    }

    public static void AdjacentBodyParagraphMarginsAreSafeViewportGeometry()
    {
        using var bitmap = new Bitmap(200, 120);
        var snapshot = new LiveTranslationPixelSnapshot(bitmap);
        var firstLine = new OcrTextBlock("first", new PixelRect(0, 10, 160, 20), []);
        var secondLine = new OcrTextBlock("second", new PixelRect(0, 55, 160, 20), []);
        var firstRegion = new VisualTextRegion("first", firstLine.Text, [firstLine],
            new PixelRect(0, 10, 160, 40), VisualTextRole.Body, VisualStructureConfidence.High);
        var secondRegion = new VisualTextRegion("second", secondLine.Text, [secondLine],
            new PixelRect(0, 42, 160, 40), VisualTextRole.Body, VisualStructureConfidence.High);
        var first = Item(firstRegion.Bounds, "第一段") with { SourceRegion = firstRegion };
        var second = Item(secondRegion.Bounds, "第二段") with { SourceRegion = secondRegion };
        var frame = new LiveTranslationFrame("Machine", "completed", [first, second])
        {
            SourceRegions = [firstRegion, secondRegion],
            ExpectedLayoutCount = 2,
            ExpectedPrimaryLayoutCount = 2,
            ExpectedPrimaryLayoutArea = first.Bounds.Width * first.Bounds.Height
                + second.Bounds.Width * second.Bounds.Height,
        };

        var metrics = LiveTranslationViewportCommitPolicy.Measure(snapshot, frame, 2, [first, second]);
        if (metrics.OverlappingLayouts != 0
            || LiveTranslationViewportResultPolicy.Classify(frame, metrics)
                != LiveTranslationViewportResultState.Ready)
            throw new Exception("Adjacent body paragraph margins were rejected as overlapping text.");
    }

    public static void AdjacentDifferentRoleTextRowsAreSafeViewportGeometry()
    {
        using var bitmap = new Bitmap(720, 1360);
        var snapshot = new LiveTranslationPixelSnapshot(bitmap);
        var upperLine = new OcrTextBlock("Line 55", new PixelRect(0, 1242, 702, 28), []);
        var lowerLine = new OcrTextBlock("Line 56", new PixelRect(2, 1324, 669, 19), []);
        var upperRegion = new VisualTextRegion("upper", upperLine.Text, [upperLine],
            new PixelRect(0, 1242, 702, 87), VisualTextRole.Body, VisualStructureConfidence.High);
        var lowerRegion = new VisualTextRegion("lower", lowerLine.Text, [lowerLine],
            new PixelRect(2, 1324, 669, 19), VisualTextRole.Metadata, VisualStructureConfidence.High);
        var upper = Item(upperRegion.Bounds, "第55行") with { SourceRegion = upperRegion };
        var lower = Item(lowerRegion.Bounds, "第56行") with { SourceRegion = lowerRegion };
        var frame = new LiveTranslationFrame("Machine", "translation-partial", [upper, lower])
        {
            SourceRegions = [upperRegion, lowerRegion],
            ExpectedLayoutCount = 2,
            ResultState = LiveTranslationViewportResultState.AcceptablePartial,
        };

        var metrics = LiveTranslationViewportCommitPolicy.Measure(snapshot, frame, 2, [upper, lower]);
        if (metrics.OverlappingLayouts != 0
            || LiveTranslationViewportResultPolicy.Classify(frame, metrics)
                != LiveTranslationViewportResultState.Ready)
            throw new Exception("Adjacent OCR rows with different roles were rejected for a five-pixel margin overlap.");
    }

    public static void BodyParagraphsSharingATextRowRemainUnsafe()
    {
        using var bitmap = new Bitmap(200, 120);
        var snapshot = new LiveTranslationPixelSnapshot(bitmap);
        var firstLine = new OcrTextBlock("first", new PixelRect(0, 10, 160, 20), []);
        var overlappingLine = new OcrTextBlock("overlap", new PixelRect(0, 18, 160, 20), []);
        var firstRegion = new VisualTextRegion("first", firstLine.Text, [firstLine],
            new PixelRect(0, 10, 160, 40), VisualTextRole.Body, VisualStructureConfidence.High);
        var overlappingRegion = new VisualTextRegion("overlap", overlappingLine.Text, [overlappingLine],
            new PixelRect(0, 18, 160, 40), VisualTextRole.Body, VisualStructureConfidence.High);
        var first = Item(firstRegion.Bounds, "第一段") with { SourceRegion = firstRegion };
        var second = Item(overlappingRegion.Bounds, "重叠段") with { SourceRegion = overlappingRegion };
        var frame = new LiveTranslationFrame("Machine", "completed", [first, second])
        {
            SourceRegions = [firstRegion, overlappingRegion],
            ExpectedLayoutCount = 2,
        };

        var metrics = LiveTranslationViewportCommitPolicy.Measure(snapshot, frame, 2, [first, second]);
        if (metrics.OverlappingLayouts == 0
            || LiveTranslationViewportResultPolicy.Classify(frame, metrics)
                != LiveTranslationViewportResultState.Failed)
            throw new Exception("Body paragraphs sharing a text row were accepted as margin-only overlap.");
    }

    public static void OneFailedPrimaryRegionDoesNotBlankAnOtherwiseUsefulViewport()
    {
        var metrics = new LiveTranslationViewportMetrics(
            ExpectedLayouts: 12,
            CandidateLayouts: 11,
            RetainedLayouts: 11,
            RejectedLayouts: 0,
            ExpectedPrimaryLayouts: 5,
            RetainedPrimaryLayouts: 4,
            ExpectedPrimaryArea: 92_085,
            RetainedPrimaryArea: 64_485);
        var frame = new LiveTranslationFrame("Machine", "translation-failed", [],
            "Translation:partial-or-failed")
        {
            ExpectedLayoutCount = 12,
            ExpectedPrimaryLayoutCount = 5,
            ExpectedPrimaryLayoutArea = 92_085,
            ResultState = LiveTranslationViewportResultState.AcceptablePartial,
        };

        if (metrics.HasMissingPrimaryContent
            || LiveTranslationViewportResultPolicy.Classify(frame, metrics)
                != LiveTranslationViewportResultState.AcceptablePartial)
            throw new Exception("One failed primary region blanked an otherwise useful viewport.");

        var scrolledMetrics = metrics with
        {
            ExpectedLayouts = 21,
            CandidateLayouts = 14,
            RetainedLayouts = 14,
            RejectedLayouts = 1,
            ExpectedPrimaryLayouts = 6,
            RetainedPrimaryLayouts = 3,
            ExpectedPrimaryArea = 159_849,
            RetainedPrimaryArea = 72_138,
        };
        var scrolledFrame = frame with
        {
            ExpectedLayoutCount = 21,
            RejectedLayoutCount = 1,
            ExpectedPrimaryLayoutCount = 6,
            ExpectedPrimaryLayoutArea = 159_849,
            ErrorCategory = "stable-font-does-not-fit",
        };
        if (scrolledMetrics.HasMissingPrimaryContent
            || LiveTranslationViewportResultPolicy.Classify(scrolledFrame, scrolledMetrics)
                != LiveTranslationViewportResultState.AcceptablePartial)
            throw new Exception("A readable half of the primary regions was blanked after scrolling.");
    }

    private static InPlaceLayoutItem Item(PixelRect bounds, string text) => new(
        new OcrTextBlock("source", bounds, []), text, bounds,
        InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White);

    public static void FullViewportCommitIsAtomicAndRejectsOnlyNewerViewport()
    {
        using var sourceBitmap = new Bitmap(600, 900);
        using (var graphics = Graphics.FromImage(sourceBitmap))
        using (var brush = new SolidBrush(Color.White))
            foreach (var y in new[] { 60, 240, 420, 600, 780 })
                graphics.FillRectangle(brush, 40, y, 500, 42);

        var block = new OcrTextBlock("new viewport", new PixelRect(40, 240, 500, 42), []);
        var region = new VisualTextRegion("new", block.Text, [block], block.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var item = new InPlaceLayoutItem(block, "新视口", block.Bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: region);
        var source = new LiveTranslationPixelSnapshot(sourceBitmap);
        var candidate = LiveTranslationRegionWitness.Create(source, [item]);
        var frame = new LiveTranslationFrame("Machine", "completed", [item])
        {
            SourceRegions = [region],
            ExpectedLayoutCount = 1,
        };

        using var incidentalBitmap = new Bitmap(sourceBitmap);
        // Outside the translated source region: this really is incidental
        // to the item, unlike a pixel inside an OCR region with no word boxes.
        incidentalBitmap.SetPixel(550, 250, Color.Red);
        var ready = LiveTranslationViewportCommitPolicy.Commit(
            source, new LiveTranslationPixelSnapshot(incidentalBitmap), [region], candidate, frame);

        if (ready.State != LiveTranslationViewportCommitState.Ready
            || ready.Witness.Count != 1
            || ready.Witness.Items[0].Text != "新视口")
            throw new Exception("An incidental change reduced a complete full-viewport result.");

        using var tinySourceBitmap = new Bitmap(40, 40);
        using (var graphics = Graphics.FromImage(tinySourceBitmap))
        using (var brush = new SolidBrush(Color.White))
            graphics.FillRectangle(brush, 4, 24, 12, 8);
        using var tinyMediaBitmap = new Bitmap(tinySourceBitmap);
        using (var graphics = Graphics.FromImage(tinyMediaBitmap))
        using (var brush = new SolidBrush(Color.Red))
            graphics.FillRectangle(brush, 4, 24, 12, 8);
        var tinyPlan = LiveTranslationChangePlanner.Analyze(
            new LiveTranslationPixelSnapshot(tinySourceBitmap),
            new LiveTranslationPixelSnapshot(tinyMediaBitmap), []);
        if (tinyPlan.IsViewportReplacement
            || !LiveTranslationViewportCommitPolicy.IsFullViewport(tinyPlan.Windows, 40, 40))
            throw new Exception("A local crop filling a small canvas was mistaken for viewport replacement evidence.");

        using var dynamicTextBitmap = new Bitmap(sourceBitmap);
        using (var graphics = Graphics.FromImage(dynamicTextBitmap))
        using (var brush = new SolidBrush(Color.Red))
            graphics.FillRectangle(brush, 40, 240, 500, 42);
        var dynamicCommit = LiveTranslationViewportCommitPolicy.Commit(
            source, new LiveTranslationPixelSnapshot(dynamicTextBitmap), [region], candidate, frame);
        if (dynamicCommit.State != LiveTranslationViewportCommitState.Ready
            || dynamicCommit.Witness.Count != 0
            || dynamicCommit.ResultState != LiveTranslationViewportResultState.Failed)
            throw new Exception("Substantially changed media text was accepted as a displayable viewport.");

        using var newerBitmap = new Bitmap(600, 900);
        using (var graphics = Graphics.FromImage(newerBitmap))
        using (var brush = new SolidBrush(Color.White))
            foreach (var y in new[] { 20, 200, 380, 560, 740 })
                graphics.FillRectangle(brush, 40, y, 500, 42);

        var stale = LiveTranslationViewportCommitPolicy.Commit(
            source, new LiveTranslationPixelSnapshot(newerBitmap), [region], candidate, frame);
        if (stale.State != LiveTranslationViewportCommitState.Stale || stale.Witness.Count != 0
            || stale.ResultState != LiveTranslationViewportResultState.Stale)
            throw new Exception("A newer viewport accepted a stale full-viewport result.");
    }
}
