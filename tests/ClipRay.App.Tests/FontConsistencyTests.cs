using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class FontConsistencyTests
{
    // Opt-in regression using the user's local X screenshot. The input image is
    // kept outside the repository; the test receives its exact path at runtime.
    public static void RealXSingleLineKeepsPeerFont(string imagePath)
    {
        const string expectedSha256 = "bf9b8123b434c0e98db2cd2a0641b36b0738e6e5ac45176d60500f9da3176c8a";
        var actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath)))
            .ToLowerInvariant();
        if (actualSha256 != expectedSha256)
            throw new Exception("The X typography input does not match the recorded source image.");
        using var source = new Bitmap(imagePath);
        var shortBounds = new PixelRect(679, 269, 443, 23);
        var firstBounds = new PixelRect(675, 373, 463, 30);
        var secondBounds = new PixelRect(675, 401, 91, 32);
        var paragraphBounds = new PixelRect(675, 373, 463, 60);
        var shortText = "found the perfect use case for @typesafeai Jev:";
        var firstText = "in 2026, why is compaction still a summarization";
        var secondText = "prompt?";
        var shortLine = new OcrTextBlock(shortText, shortBounds,
            [new(shortText, shortBounds)]);
        var firstLine = new OcrTextBlock(firstText, firstBounds,
            [new(firstText, firstBounds)]);
        var secondLine = new OcrTextBlock(secondText, secondBounds,
            [new(secondText, secondBounds)]);
        var regions = new[]
        {
            new VisualTextRegion("x-short", shortText, [shortLine], shortBounds,
                VisualTextRole.Body, VisualStructureConfidence.High),
            new VisualTextRegion("x-paragraph", firstText + "\n" + secondText,
                [firstLine, secondLine], paragraphBounds,
                VisualTextRole.Body, VisualStructureConfidence.High),
        };
        var plan = InPlaceLayoutService.BuildRegionPlan(source, regions,
            [new(true, "找到了 @typesafeai Jev 的完美用例：", "completed"),
                new(true, "都2026年了，为什么压缩还是一个摘要提示词？", "completed")]);
        if (plan.Items.Count != 2)
            throw new Exception("The source image lost a translated X region: "
                + string.Join(",", plan.Rejections.Select(item => item.Reason)));
        var shortFont = plan.Items.Single(item => item.SourceRegion?.Id == "x-short")
            .TextLayout!.FontSizePixels;
        var paragraphFont = plan.Items.Single(item => item.SourceRegion?.Id == "x-paragraph")
            .TextLayout!.FontSizePixels;
        Console.WriteLine($"REAL_X_FONT short={shortFont:F1} paragraph={paragraphFont:F1}");
        if (paragraphFont / shortFont > 1.1 || shortFont < 19)
            throw new Exception("A tight single-line OCR box visibly shrank a body peer.");
    }

    public static void AConstrainedSidebarCannotShrinkDistantArticleText()
    {
        using var bitmap = new Bitmap(1100, 120);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 24, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.Black);
            graphics.DrawString("Main article.", font, Brushes.White, 30, 15);
            graphics.DrawString("Sidebar text.", font, Brushes.White, 800, 15);
        }
        var first = new OcrTextBlock("Main article.", new(30, 15, 550, 40),
            [new("Main article.", new(30, 15, 550, 40))]);
        var second = new OcrTextBlock("Sidebar text.", new(800, 15, 180, 40),
            [new("Sidebar text.", new(800, 15, 180, 40))]);
        var regions = new[] { first, second }.Select((block, index) =>
            new VisualTextRegion($"r{index}", block.Text, [block], block.Bounds,
                VisualTextRole.Body, VisualStructureConfidence.High)).ToArray();
        var plan = InPlaceLayoutService.BuildRegionPlan(bitmap, regions,
            [new(true, "主要文章", "machine-completed"),
                new(true, "这是一个很长很长的翻译", "machine-completed")]);
        if (plan.Items.Count != 2)
            throw new Exception("The distant-column fixture must render both translations: "
                + string.Join(",", plan.Rejections.Select(item => item.Reason)));
        var articleSize = plan.Items.Single(item => item.SourceRegion?.Id == "r0")
            .TextLayout!.FontSizePixels;
        var sidebarSize = plan.Items.Single(item => item.SourceRegion?.Id == "r1")
            .TextLayout!.FontSizePixels;
        var articleOnly = InPlaceLayoutService.BuildRegionPlan(bitmap, [regions[0]],
            [new(true, "主要文章", "machine-completed")]);
        var isolatedArticleSize = articleOnly.Items.Single().TextLayout!.FontSizePixels;
        if (articleSize < isolatedArticleSize - .01 || articleSize <= sidebarSize + 3)
            throw new Exception($"A constrained sidebar reduced the article font: {articleSize} "
                + $"vs isolated {isolatedArticleSize}; sidebar {sidebarSize}.");

        ARemoteCompactControlCannotShrinkOneNavigationColumn();
        ASectionTitleUsesItsLocalLeftEdge();
        ALeftCardTitleBeatsLooseCenteredAccountRows();
        ACompactSingleLineTitleKeepsItsSourceInkScale();
        ACardTitleDoesNotInheritOrdinaryRowFont();
    }

    private static void ARemoteCompactControlCannotShrinkOneNavigationColumn()
    {
        using var bitmap = new Bitmap(1400, 400);
        var labels = new (string Source, string Target, PixelRect Bounds)[]
        {
            ("Home", "主页", new PixelRect(100, 40, 110, 36)),
            ("Explore", "探索", new PixelRect(101, 108, 120, 36)),
            ("Notifications", "通知", new PixelRect(102, 176, 210, 36)),
            ("Follow", "关注", new PixelRect(100, 244, 110, 36)),
            ("Chat", "聊天", new PixelRect(101, 312, 100, 36)),
            ("Menu", "详细入口", new PixelRect(1100, 244, 76, 36)),
        };
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 26, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.Black);
            foreach (var label in labels)
                graphics.DrawString(label.Source, font, Brushes.White,
                    (float)label.Bounds.X + 1, (float)label.Bounds.Y);
        }

        var regions = labels.Select((label, index) =>
        {
            var block = new OcrTextBlock(label.Source, label.Bounds,
                [new(label.Source, label.Bounds)]);
            return new VisualTextRegion($"nav{index}", label.Source, [block],
                label.Bounds, VisualTextRole.Control, VisualStructureConfidence.High);
        }).ToArray();
        var results = labels.Select(label => new TranslationResult(true, label.Target, "machine-completed"))
            .ToArray();
        var plan = InPlaceLayoutService.BuildRegionPlan(bitmap, regions, results);
        if (plan.Items.Count != labels.Length)
            throw new Exception("The navigation fixture lost a translated control: "
                + string.Join(",", plan.Rejections.Select(item => item.Reason)));

        var navigationSizes = plan.Items.Where(item => item.SourceRegion?.Id != "nav5")
            .Select(item => item.TextLayout!.FontSizePixels).ToArray();
        var withoutRemote = InPlaceLayoutService.BuildRegionPlan(bitmap,
            regions.Take(5).ToArray(), results.Take(5).ToArray());
        var navigationOnlySizes = withoutRemote.Items.Select(item => item.TextLayout!.FontSizePixels).ToArray();
        if (navigationOnlySizes.Length != 5)
            throw new Exception("The isolated navigation column did not render all labels.");
        if (navigationSizes.Max() - navigationSizes.Min() > 0.01
            || navigationSizes.Where((size, index) => size < navigationOnlySizes[index] - 0.01).Any())
            throw new Exception("One navigation column inherited a remote compact control's font: "
                + string.Join(",", navigationSizes) + "; without remote "
                + string.Join(",", navigationOnlySizes) + ".");
    }

    private static void ASectionTitleUsesItsLocalLeftEdge()
    {
        using var bitmap = new Bitmap(1100, 480);
        var boxes = new (string Text, PixelRect Bounds)[]
        {
            ("Search", new PixelRect(581, 32, 213, 39)),
            ("Activity", new PixelRect(700, 120, 251, 38)),
            ("Category", new PixelRect(698, 182, 149, 32)),
            ("First story", new PixelRect(700, 211, 136, 28)),
            ("Related", new PixelRect(698, 294, 148, 29)),
            ("context", new PixelRect(822, 240, 120, 29)),
            ("context", new PixelRect(823, 351, 120, 29)),
        };
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 26, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.Black);
            graphics.DrawString(boxes[1].Text, font, Brushes.White, 701, 122);
        }
        var blocks = boxes.Select(box => new OcrTextBlock(box.Text, box.Bounds,
            [new(box.Text, box.Bounds)])).ToArray();
        var title = new VisualTextRegion("title", boxes[1].Text, [blocks[1]], boxes[1].Bounds,
            VisualTextRole.Control, VisualStructureConfidence.High);
        var style = SourceVisualStyleEstimator.EstimateRegion(bitmap, title, blocks, Color.Black);
        if (style.Alignment != SourceTextAlignment.Left)
            throw new Exception("A left-anchored section title inherited alignment from overlapping foreign columns.");
    }

    private static void ALeftCardTitleBeatsLooseCenteredAccountRows()
    {
        using var bitmap = new Bitmap(520, 750);
        var boxes = new (string Text, PixelRect Bounds)[]
        {
            ("Who to follow", new(20, 300, 193, 32)),
            ("Lincoln Riley", new(21, 63, 115, 29)),
            ("Sports Trending", new(21, 122, 147, 29)),
            ("Kamario Taylor", new(21, 146, 155, 29)),
            ("Show more", new(21, 214, 105, 27)),
            ("ZCode", new(90, 365, 76, 28)),
            ("@zcode_ai", new(90, 394, 94, 26)),
            ("Alvaro", new(90, 452, 76, 28)),
        };
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 25, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.Black);
            graphics.DrawString(boxes[0].Text, font, Brushes.White, 20, 300);
        }
        var blocks = boxes.Select(box => new OcrTextBlock(box.Text, box.Bounds,
            [new(box.Text, box.Bounds)])).ToArray();
        var title = new VisualTextRegion("card-title", boxes[0].Text, [blocks[0]], boxes[0].Bounds,
            VisualTextRole.Control, VisualStructureConfidence.High);
        var style = SourceVisualStyleEstimator.EstimateRegion(bitmap, title, blocks, Color.Black);
        if (style.Alignment != SourceTextAlignment.Left)
            throw new Exception("A left-aligned card title was centered by loosely aligned account rows.");
    }

    private static void ACompactSingleLineTitleKeepsItsSourceInkScale()
    {
        var style = new SourceVisualStyle(Color.White, 20, 30,
            SourceFontWeight.SemiBold, SourceFontCategory.SansSerif,
            false, SourceTextAlignment.Left, 0, VisualStyleConfidence.High, 20.5);
        var layout = TranslationTextLayoutPolicy.Fit("推荐关注", new(1292, 668, 193, 32), style);
        if (!layout.ShouldRender || layout.FontSizePixels < 20)
            throw new Exception($"A compact single-line card title shrank below its source ink scale: {layout.FontSizePixels}.");
    }

    private static void ACardTitleDoesNotInheritOrdinaryRowFont()
    {
        using var bitmap = new Bitmap(500, 360);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var titleFont = new Font("Arial Black", 25, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var rowFont = new Font("Segoe UI", 20, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.Black);
            graphics.DrawString("Who to follow", titleFont, Brushes.White, 20, 18);
            graphics.DrawString("Dante Moore", rowFont, Brushes.White, 20, 146);
            graphics.DrawString("Lincoln Riley", rowFont, Brushes.White, 20, 250);
        }
        var labels = new (string Source, string Target, PixelRect Bounds)[]
        {
            ("Who to follow", "推荐关注", new(20, 18, 193, 32)),
            ("Dante Moore", "丹特·摩尔", new(20, 146, 150, 30)),
            ("Lincoln Riley", "林肯·莱利", new(20, 250, 150, 30)),
        };
        var regions = labels.Select((label, index) =>
        {
            var block = new OcrTextBlock(label.Source, label.Bounds,
                [new(label.Source, label.Bounds)]);
            return new VisualTextRegion($"card{index}", label.Source, [block], label.Bounds,
                VisualTextRole.Control, VisualStructureConfidence.High);
        }).ToArray();
        var results = labels.Select(label => new TranslationResult(true, label.Target, "machine-completed"))
            .ToArray();
        var styles = regions.Select(region => SourceVisualStyleEstimator.EstimateRegion(
            bitmap, region, regions.SelectMany(item => item.Lines).ToArray(), Color.Black)).ToArray();
        if (styles[0].FontWeight == styles[1].FontWeight)
            throw new Exception($"The synthetic title and row need distinct visible weights: {styles[0].FontWeight}/{styles[1].FontWeight}.");
        var together = InPlaceLayoutService.BuildRegionPlan(bitmap, regions, results);
        var alone = InPlaceLayoutService.BuildRegionPlan(bitmap, [regions[0]], [results[0]]);
        var title = together.Items.Single(item => item.SourceRegion?.Id == "card0").TextLayout!;
        var isolated = alone.Items.Single().TextLayout!;
        if (title.FontSizePixels < isolated.FontSizePixels - .01)
            throw new Exception($"An ordinary account row shrank the card title: {title.FontSizePixels} vs {isolated.FontSizePixels}.");
    }

    public static void SameSourceSizeAcrossEnglishAndJapanese()
    {
        using var bitmap = new Bitmap(1000, 240);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 26, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.Black);
            graphics.DrawString("We need better text.", font, Brushes.White, 15, 15);
            graphics.DrawString("今日は使えますが、明日は使えません。", font, Brushes.White, 15, 145);
        }
        var blocks = new[] {
            new OcrTextBlock("We need better text.", new(15,15,850,40), [new("We need better text.",new(15,15,850,40))]),
            new OcrTextBlock("今日は使えますが、明日は使えません。", new(15,145,850,40), [new("今日は使えますが、明日は使えません。",new(15,145,850,40))])
        };
        var regions = blocks.Select((block,i) => new VisualTextRegion($"r{i}",block.Text,[block],block.Bounds,
            VisualTextRole.Body,VisualStructureConfidence.High)).ToArray();
        var plan = InPlaceLayoutService.BuildRegionPlan(bitmap,regions,[
            new(true,"这句话应保持相同的字号。","machine-completed"),
            new(true,"今天能使用，明天不能使用。","machine-completed")]);
        if(plan.Items.Count != 2) throw new Exception("A same-size translation was omitted: " + string.Join(",",plan.Rejections.Select(item=>item.Reason)));
        var sizes = plan.Items.Select(item => item.TextLayout!.FontSizePixels).ToArray();
        if(Math.Abs(sizes[0]-sizes[1]) > .01) throw new Exception($"Same source size became unequal Chinese fonts: {sizes[0]} vs {sizes[1]}.");
    }
}
