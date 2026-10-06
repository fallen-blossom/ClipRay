using System.Drawing;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class WorkbenchGroupingTests
{
    public static async Task RunAsync()
    {
        await VerifySplitAsync();
        using var image = new Bitmap(400, 240);
        VisualTextRegion Region(string text, double y) => new(text, text, [], new PixelRect(10, y, 200, 25),
            VisualTextRole.Body, VisualStructureConfidence.High);
        using var session = new TranslationWorkbenchSession(image, new(0, 0, 400, 240), new(0, 0, 400, 240),
            [Region("First source", 10), Region("Second source", 45), Region("Untouched", 100)], TranslationRouteKind.Machine, "zh");
        var first = session.Regions[0].Id; var second = session.Regions[1].Id; var other = session.Regions[2].Id;
        session.Edit(first, "Corrected first");
        var late = new TaskCompletionSource<WorkbenchTranslationOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = session.TranslateAsync(first, (_, _, _) => late.Task);
        var merged = session.Merge([second, first]);
        if (session.Regions.Count != 2 || session.Regions[0].Draft != "Corrected first\nSecond source"
            || session.Regions[0].Original.SourceText != "First source\nSecond source") throw new Exception("Merge lost order or source provenance.");
        session.Edit(merged, "Edited after merge");
        session.Edit(other, "Unrelated edit");
        session.UndoGrouping();
        late.SetResult(new(first, new(true, "Stale", "completed")));
        await pending;
        if (session.Regions[0].Draft != "Corrected first" || session.Regions[0].Translation != ""
            || session.Regions[0].State == WorkbenchRegionState.Translating) throw new Exception("Undo resurrected an in-flight result.");
        session.Edit(first, "Edited after undo");
        session.RedoGrouping();
        if (session.Regions[0].Draft != "Edited after merge" || session.Regions[1].Draft != "Unrelated edit")
            throw new Exception("Redo lost merged edits or changed unrelated content.");
        session.UndoGrouping();
        if (session.Regions[0].Draft != "Edited after undo") throw new Exception("Repeated undo lost restored edits.");
        session.Configure(second, "ja", "zh", TranslationRouteKind.Machine);
        try { session.Merge([first, second]); throw new Exception("Configuration conflict accepted."); }
        catch (InvalidOperationException) { }
        if (session.Regions.Count != 3) throw new Exception("Rejected merge mutated session.");
        session.Configure(second, "auto", "zh", TranslationRouteKind.Machine);
        var inner = session.Merge([first, second]);
        var outer = session.Merge([inner, other]);
        session.Edit(outer, "Nested edit");
        session.UndoGrouping(); session.UndoGrouping();
        if (session.Regions.Count != 3) throw new Exception("Nested undo did not recover leaf regions.");
        session.RedoGrouping(); session.RedoGrouping();
        if (session.Regions.Single().Draft != "Nested edit") throw new Exception("Nested redo lost merged edit.");
        await session.TranslateAsync(outer, (_, input, _) =>
        {
            var block = new ClipRay.App.Ocr.OcrTextBlock(input.Region.SourceText, input.Region.Bounds, []);
            var layout = new InPlaceLayoutItem(block, "合并译文", input.Region.Bounds,
                InPlaceBackgroundMode.FlatSampled, Color.White, Color.Black, SourceRegion: input.Region);
            return Task.FromResult(new WorkbenchTranslationOutput(outer,
                new(true, "合并译文", "completed", ExecutionEvidence: new("fixture", "model", null, "cpu", "cpu", null)), layout));
        });
        var translated = session.Regions.Single();
        session.UndoGrouping(); session.RedoGrouping();
        var restored = session.Regions.Single();
        if (restored.State != WorkbenchRegionState.Translated || restored.Translation != translated.Translation
            || !ReferenceEquals(restored.Layout, translated.Layout)
            || !session.Attempts.Any(attempt => attempt.RegionId == outer && attempt.ExecutionEvidence?.EngineId == "fixture"))
            throw new Exception("Grouping restoration lost translation, layout or execution history.");
        Console.WriteLine("Grouping history, edits and stale-result checks passed without foreground.");
    }

    private static async Task VerifySplitAsync()
    {
        using var image = new Bitmap(400, 160);
        ClipRay.App.Ocr.OcrTextBlock[] lines = [new("First original", new(10, 10, 150, 25), []),
            new("Second original", new(10, 50, 190, 25), [])];
        var original = new VisualTextRegion("original", "First original\nSecond original", lines,
            new(10, 10, 190, 65), VisualTextRole.Body, VisualStructureConfidence.High);
        using var session = new TranslationWorkbenchSession(image, new(0, 0, 400, 160), new(0, 0, 400, 160),
            [original], TranslationRouteKind.Machine, "zh");
        var id = session.Regions.Single().Id;
        session.Edit(id, "Corrected first\nCorrected second");
        var late = new TaskCompletionSource<WorkbenchTranslationOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = session.TranslateAsync(id, (_, _, _) => late.Task);
        var ids = session.Split(id, 1, 16);
        var parts = session.Regions;
        if (parts.Any(part => !part.Original.SharesParentOcrEvidence)
            || !WorkbenchOcrEvidence.Describe(parts[0].Original, "en").Contains("parent OCR record"))
            throw new Exception("Split evidence was presented as a fresh independent recognition.");
        if (string.Concat(parts.Select(part => part.Draft)) != "Corrected first\nCorrected second"
            || parts[0].Original.Bounds != lines[0].Bounds || parts[1].Original.SourceText != "Second original")
            throw new Exception("Split lost draft text or fabricated geometry.");
        session.Edit(ids[1], "Edited split part");
        session.UndoGrouping();
        late.SetResult(new(id, new(true, "stale", "completed"))); await pending;
        if (session.Regions.Single().Translation != "") throw new Exception("Split undo accepted stale result.");
        session.RedoGrouping();
        if (session.Regions[1].Draft != "Edited split part") throw new Exception("Split redo lost edits.");
        session.UndoGrouping();
        session.Edit(id, "a😀b");
        try { session.Split(id, 1, 2); throw new Exception("Split accepted half a Unicode character."); }
        catch (ArgumentOutOfRangeException) { }
        if (session.Regions.Count != 1) throw new Exception("Rejected split mutated session.");
    }
}
