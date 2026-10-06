using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Translation;
using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;

internal static class LocalChangeTranslationTests
{
    public static void PaddingDoesNotMergeIndependentParagraphs()
    {
        using var before = new Bitmap(600,400);
        using var after = new Bitmap(before);
        after.SetPixel(50,50,Color.White);
        after.SetPixel(50,140,Color.White);
        var a = new VisualTextRegion("a","first",[],new PixelRect(30,30,220,70),
            VisualTextRole.Body,VisualStructureConfidence.High);
        var b = new VisualTextRegion("b","second",[],new PixelRect(30,115,220,70),
            VisualTextRole.Body,VisualStructureConfidence.High);
        var windows = LiveTranslationChangePlanner.Plan(before,after,[a,b]);
        if (windows.Count != 2 || !windows.Any(window => window.Contains(new Rectangle(30,30,220,70)))
            || !windows.Any(window => window.Contains(new Rectangle(30,115,220,70))))
            throw new Exception("Padding chained independent paragraphs into one crop.");
    }
    public static void AnimationDoesNotStarveLocalText()
    {
        var phase=0; var capture=0; var fullCalls=0; var textPublished=false;
        var paragraph=new VisualTextRegion("one","paragraph",[],new PixelRect(30,30,220,70),VisualTextRole.Body,VisualStructureConfidence.High);
        LiveTranslationSession? session=null;
        session=new(new Rectangle(0,0,600,400),_=>
        {
            var image=new Bitmap(600,400);
            image.SetPixel(50,50,phase==0?Color.White:Color.Red);
            image.SetPixel(500,300,Color.FromArgb(++capture%255,80,120));
            return image;
        },(_,_)=> { fullCalls++; return Task.FromResult(new LiveTranslationFrame("Machine","completed",[]){SourceRegions=[paragraph]}); },
            TranslationRouteKind.Machine,validateLayoutRegions:true);
        using(session)
        using(var timeout=new CancellationTokenSource(2500))
        {
            session.FrameReady+=(_,_)=> { if(phase==0) phase=1; else { textPublished=true; session.Stop(); } };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if(!textPublished || fullCalls<2) throw new Exception("Animation starved a stable changed paragraph.");
    }
    public static void PerpetualAnimationDoesNotRetranslateStableViewport()
    {
        var capture = 0;
        var translations = 0;
        var block = new OcrTextBlock("Stable paragraph", new PixelRect(30, 30, 220, 70), []);
        var region = new VisualTextRegion("stable", block.Text, [block], block.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var item = new InPlaceLayoutItem(block, "稳定段落", block.Bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: region);
        using var timeout = new CancellationTokenSource(1400);
        using var session = new LiveTranslationSession(new Rectangle(0, 0, 600, 400), _ =>
        {
            var image = new Bitmap(600, 400);
            using var graphics = Graphics.FromImage(image);
            graphics.FillRectangle(Brushes.White, 30, 30, 220, 70);
            graphics.FillRectangle(Brushes.DarkRed, 360, 100, 180, 180);
            graphics.FillRectangle(Brushes.White, 360 + (++capture % 120), 120, 40, 40);
            return image;
        }, (_, _) =>
        {
            translations++;
            return Task.FromResult(new LiveTranslationFrame("Machine", "completed", [item])
            {
                SourceRegions = [region],
                ExpectedLayoutCount = 1,
                ExpectedPrimaryLayoutCount = 1,
                ExpectedPrimaryLayoutArea = region.Bounds.Width * region.Bounds.Height,
            });
        }, TranslationRouteKind.Machine, validateLayoutRegions: true);

        session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        if (translations != 1)
            throw new Exception($"Perpetual local animation retriggered full translation {translations} times.");
    }
    public static void CropCoordinatesReturnToSourcePixels()
    {
        var block=new OcrTextBlock("body",new PixelRect(5,6,30,20),[new OcrWord("body",new PixelRect(5,6,30,20))]);
        var region=new VisualTextRegion("body","body",[block],block.Bounds,VisualTextRole.Body,VisualStructureConfidence.High);
        var item=new InPlaceLayoutItem(block,"正文",block.Bounds,InPlaceBackgroundMode.FlatSampled,Color.Black,Color.White,SourceRegion:region);
        var shifted=LiveTranslationFrameCoordinates.Offset(new LiveTranslationFrame("Machine","completed",[item]){SourceRegions=[region]},100,200);
        if(shifted.LayoutItems[0].Bounds.X!=105 || shifted.LayoutItems[0].SourceBlock.Words[0].Bounds.Y!=206
            || shifted.LayoutItems[0].SourceRegion!.Bounds.X!=105 || shifted.SourceRegions[0].Lines[0].Bounds.Y!=206
            || item.Bounds.X!=5) throw new Exception("Local OCR geometry was not transformed exactly once into source pixels.");
    }
    public static void SessionRebuildsChangedViewportFromFullFrame()
    {
        var phase=0; var fullCalls=0; var publications=0;
        var paragraph=new VisualTextRegion("one","First paragraph",[],new PixelRect(30,30,220,70),VisualTextRole.Body,VisualStructureConfidence.High);
        LiveTranslationSession? session=null;
        session=new(new Rectangle(0,0,600,400),_=> { var image=new Bitmap(600,400); if(phase==1) image.SetPixel(50,50,Color.White); return image; },
            (bitmap,_)=> { fullCalls++; if(bitmap.Width!=600 || bitmap.Height!=400)
                throw new Exception("A changed viewport was sent through a cropped OCR pipeline.");
                return Task.FromResult(new LiveTranslationFrame("Machine","completed",[]) { SourceRegions=[paragraph] }); },
            TranslationRouteKind.Machine,(_,_)=>Task.CompletedTask,validateLayoutRegions:true);
        using(session)
        using(var timeout=new CancellationTokenSource(2000))
        {
            session.FrameReady+=(_,_)=> { if(++publications==1) phase=1; else session.Stop(); };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if(fullCalls!=2 || publications!=2) throw new Exception("A changed viewport was not rebuilt from the full current frame.");
    }
    public static void OnlyChangedParagraphIsScheduled()
    {
        using var before=new Bitmap(600,400);
        using var after=new Bitmap(before);
        after.SetPixel(50,50,Color.White);
        var paragraphs=new[] {
            new VisualTextRegion("one","First paragraph",[new OcrTextBlock("First paragraph",new PixelRect(30,30,220,70),[])],new PixelRect(30,30,220,70),VisualTextRole.Body,VisualStructureConfidence.High),
            new VisualTextRegion("two","Second paragraph",[new OcrTextBlock("Second paragraph",new PixelRect(30,220,220,70),[])],new PixelRect(30,220,220,70),VisualTextRole.Body,VisualStructureConfidence.High)
        };
        var windows=LiveTranslationChangePlanner.Plan(before,after,paragraphs);
        if(windows.Count!=1 || !windows[0].Contains(30,30) || !windows[0].Contains(249,99)
            || windows[0].IntersectsWith(new Rectangle(30,220,220,70)))
            throw new Exception("A changed word did not schedule its whole paragraph independently.");
        if(LiveTranslationChangePlanner.Plan(before,before,paragraphs).Count!=0)
            throw new Exception("An unchanged frame scheduled OCR windows.");
    }

    public static void FullViewportScrollSchedulesOneFullFrame()
    {
        using var before = new Bitmap(600, 900);
        using var after = new Bitmap(600, 900);
        using (var graphics = Graphics.FromImage(before))
        using (var brush = new SolidBrush(Color.White))
            foreach (var y in new[] { 60, 240, 420, 600, 780 })
                graphics.FillRectangle(brush, 40, y, 500, 42);
        using (var graphics = Graphics.FromImage(after))
        using (var brush = new SolidBrush(Color.White))
            foreach (var y in new[] { 20, 200, 380, 560, 740 })
                graphics.FillRectangle(brush, 40, y, 500, 42);
        var paragraphs = new[] { 60, 240, 420, 600, 780 }
            .Select((y, index) => new VisualTextRegion($"p{index}", $"paragraph {index}", [],
                new PixelRect(40, y, 500, 42), VisualTextRole.Body, VisualStructureConfidence.High))
            .ToArray();

        var windows = LiveTranslationChangePlanner.Plan(before, after, paragraphs);

        if (windows.Count != 1 || windows[0] != new Rectangle(0, 0, 600, 900))
            throw new Exception("A full viewport scroll was split into stale first-frame paragraph crops.");
    }

    public static void SingleParagraphViewportScrollSchedulesOneFullFrame()
    {
        using var before = new Bitmap(600, 900);
        using var after = new Bitmap(600, 900);
        using (var graphics = Graphics.FromImage(before))
        using (var brush = new SolidBrush(Color.White))
        {
            graphics.FillRectangle(brush, 40, 120, 260, 10);
            graphics.FillRectangle(brush, 40, 150, 220, 10);
        }
        using (var graphics = Graphics.FromImage(after))
        using (var brush = new SolidBrush(Color.White))
        {
            graphics.FillRectangle(brush, 40, 520, 260, 10);
            graphics.FillRectangle(brush, 40, 550, 220, 10);
        }
        var paragraph = new VisualTextRegion("body", "one long paragraph", [],
            new PixelRect(40, 120, 260, 40), VisualTextRole.Body, VisualStructureConfidence.High);

        var windows = LiveTranslationChangePlanner.Plan(before, after, [paragraph]);

        if (windows.Count != 1 || windows[0] != new Rectangle(0, 0, 600, 900))
            throw new Exception("A one-paragraph viewport scroll reused stale paragraph geometry.");
    }

    public static void SparseParagraphScrollDoesNotDependOnCanvasArea()
    {
        using var before = new Bitmap(2400, 1400);
        using var after = new Bitmap(2400, 1400);
        for (var index = 0; index < 120; index++)
        {
            before.SetPixel(900 + index, 280 + index % 3, Color.White);
            before.SetPixel(900 + index, 300 + index % 3, Color.White);
            after.SetPixel(900 + index, 900 + index % 3, Color.White);
            after.SetPixel(900 + index, 920 + index % 3, Color.White);
        }
        var paragraph = new VisualTextRegion("sparse", "short text", [],
            new PixelRect(900, 280, 120, 24), VisualTextRole.Body, VisualStructureConfidence.High);

        var windows = LiveTranslationChangePlanner.Plan(before, after, [paragraph]);

        if (windows.Count != 1 || windows[0] != new Rectangle(0, 0, 2400, 1400))
            throw new Exception("Sparse paragraph scrolling depended on the total canvas area.");
    }

    public static void FixedChromeDoesNotHideViewportScroll()
    {
        using var before = new Bitmap(600, 900);
        using var after = new Bitmap(600, 900);
        var fixedRows = new[] { 20, 80, 140, 760 };
        using (var beforeGraphics = Graphics.FromImage(before))
        using (var afterGraphics = Graphics.FromImage(after))
        using (var fixedBrush = new SolidBrush(Color.Gray))
        using (var bodyBrush = new SolidBrush(Color.White))
        {
            foreach (var y in fixedRows)
            {
                beforeGraphics.FillRectangle(fixedBrush, 20, y, 120, 36);
                afterGraphics.FillRectangle(fixedBrush, 20, y, 120, 36);
            }
            beforeGraphics.FillRectangle(bodyBrush, 180, 280, 360, 70);
            beforeGraphics.FillRectangle(bodyBrush, 180, 480, 360, 70);
            afterGraphics.FillRectangle(bodyBrush, 180, 380, 360, 70);
            afterGraphics.FillRectangle(bodyBrush, 180, 580, 360, 70);
        }
        var paragraphs = fixedRows.Select((y, index) => new VisualTextRegion($"fixed-{index}", "fixed", [],
                new PixelRect(20, y, 120, 36), VisualTextRole.Control, VisualStructureConfidence.High))
            .ToArray();

        var windows = LiveTranslationChangePlanner.Plan(before, after, paragraphs);

        if (windows.Count != 1 || windows[0] != new Rectangle(0, 0, 600, 900))
            throw new Exception("Fixed chrome diluted a real body viewport scroll.");
    }

    public static void DistantSmallEditsStayLocal()
    {
        using var before = new Bitmap(600, 900);
        using var after = new Bitmap(before);
        after.SetPixel(60, 80, Color.White);
        after.SetPixel(60, 790, Color.White);
        var paragraphs = new[]
        {
            new VisualTextRegion("top", "top", [], new PixelRect(40, 60, 500, 42),
                VisualTextRole.Body, VisualStructureConfidence.High),
            new VisualTextRegion("bottom", "bottom", [], new PixelRect(40, 760, 500, 42),
                VisualTextRole.Body, VisualStructureConfidence.High),
        };

        var windows = LiveTranslationChangePlanner.Plan(before, after, paragraphs);

        if (windows.Count == 1 && windows[0] == new Rectangle(0, 0, 600, 900))
            throw new Exception("Two distant small edits were misclassified as a viewport scroll.");
    }

    public static void LargeLocalAnimationDoesNotBecomeAViewportScroll()
    {
        using var before = new Bitmap(600, 900);
        using var after = new Bitmap(before);
        using (var graphics = Graphics.FromImage(after))
        using (var brush = new SolidBrush(Color.DarkRed))
            graphics.FillRectangle(brush, 50, 280, 500, 300);
        var paragraphs = new[]
        {
            new VisualTextRegion("top", "top", [], new PixelRect(40, 60, 500, 42),
                VisualTextRole.Body, VisualStructureConfidence.High),
            new VisualTextRegion("bottom", "bottom", [], new PixelRect(40, 760, 500, 42),
                VisualTextRole.Body, VisualStructureConfidence.High),
        };

        var windows = LiveTranslationChangePlanner.Plan(before, after, paragraphs);

        if (windows.Count == 1 && windows[0] == new Rectangle(0, 0, 600, 900))
            throw new Exception("A local animated panel was misclassified as a viewport scroll.");
    }

    public static void LargeAnimationTouchingCaptionDoesNotBecomeAViewportScroll()
    {
        using var before = new Bitmap(600, 900);
        using var after = new Bitmap(before);
        using (var graphics = Graphics.FromImage(after))
        using (var brush = new SolidBrush(Color.DarkRed))
            graphics.FillRectangle(brush, 50, 120, 500, 640);
        var caption = new VisualTextRegion("caption", "caption", [], new PixelRect(120, 420, 360, 42),
            VisualTextRole.Body, VisualStructureConfidence.High);

        var windows = LiveTranslationChangePlanner.Plan(before, after, [caption]);

        if (windows.Count == 1 && windows[0] == new Rectangle(0, 0, 600, 900))
            throw new Exception("One contiguous animated panel touching a caption was misclassified as a viewport scroll.");
    }

    public static void ViewportScrollRestartsFromTheCurrentFullFrame()
    {
        var phase = 0;
        var fullCalls = 0;
        var oldRegions = new[] { 60, 240, 420, 600, 780 }
            .Select((y, index) => new VisualTextRegion($"old-{index}", $"old {index}", [],
                new PixelRect(40, y, 500, 42), VisualTextRole.Body, VisualStructureConfidence.High))
            .ToArray();
        var newRegions = new[] { 20, 200, 380, 560, 740 }
            .Select((y, index) => new VisualTextRegion($"new-{index}", $"new {index}", [],
                new PixelRect(40, y, 500, 42), VisualTextRole.Body, VisualStructureConfidence.High))
            .ToArray();
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 600, 900), _ =>
        {
            var image = new Bitmap(600, 900);
            using var graphics = Graphics.FromImage(image);
            using var brush = new SolidBrush(Color.White);
            foreach (var region in phase == 0 ? oldRegions : newRegions)
                graphics.FillRectangle(brush, new Rectangle(
                    (int)region.Bounds.X, (int)region.Bounds.Y,
                    (int)region.Bounds.Width, (int)region.Bounds.Height));
            return image;
        }, (_, _) =>
        {
            fullCalls++;
            return Task.FromResult(new LiveTranslationFrame("Machine", "completed", [])
                { SourceRegions = phase == 0 ? oldRegions : newRegions });
        }, TranslationRouteKind.Machine, (_, _) => Task.CompletedTask, validateLayoutRegions: true);
        using (session)
        using (var timeout = new CancellationTokenSource(2500))
        {
            session.FrameReady += (_, _) =>
            {
                if (phase == 0) phase = 1;
                else session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (fullCalls != 2)
            throw new Exception("A viewport scroll did not publish through the current full-frame path exactly once.");
    }

    public static void ViewportScrollPublishesTheCompleteCurrentFrame()
    {
        static (VisualTextRegion Region, InPlaceLayoutItem Item) Content(string id, string source, string translated, int y)
        {
            var bounds = new PixelRect(40, y, 500, 42);
            var block = new OcrTextBlock(source, bounds, []);
            var region = new VisualTextRegion(id, source, [block], bounds,
                VisualTextRole.Body, VisualStructureConfidence.High);
            var item = new InPlaceLayoutItem(block, translated, bounds,
                InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: region);
            return (region, item);
        }

        var oldContent = Content("old", "old viewport", "旧视口", 240);
        var newContent = Content("new", "new viewport", "新视口", 380);
        var phase = 0;
        var translatingNewViewport = false;
        LiveTranslationFrame? latest = null;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 600, 900), _ =>
        {
            var image = new Bitmap(600, 900);
            using var graphics = Graphics.FromImage(image);
            using var brush = new SolidBrush(Color.White);
            foreach (var y in phase == 0 ? new[] { 60, 240, 420, 600, 780 } : new[] { 20, 200, 380, 560, 740 })
                graphics.FillRectangle(brush, 40, y, 500, 42);
            if (translatingNewViewport)
                image.SetPixel(50, 390, Color.Red);
            return image;
        }, async (_, token) =>
            {
                if (phase == 0)
                    return new LiveTranslationFrame("Machine", "completed", [oldContent.Item])
                        { SourceRegions = [oldContent.Region], ExpectedLayoutCount = 1 };
                translatingNewViewport = true;
                await Task.Delay(220, token);
                return new LiveTranslationFrame("Machine", "completed", [newContent.Item])
                    { SourceRegions = [newContent.Region], ExpectedLayoutCount = 1 };
            }, TranslationRouteKind.Machine,
            (_, _) => Task.CompletedTask, validateLayoutRegions: true);
        using (session)
        using (var timeout = new CancellationTokenSource(4000))
        {
            session.FrameReady += (_, args) =>
            {
                latest = args.Frame;
                if (phase == 0) phase = 1;
                else session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (latest is null || latest.LayoutItems.Count != 1 || latest.LayoutItems[0].Text != "新视口")
            throw new Exception("A full viewport translation was reduced by a later incidental pixel change.");
    }

    public static void CropMarginNeverCutsNeighboringParagraph()
    {
        using var before = new Bitmap(600,400);
        using var after = new Bitmap(before);
        after.SetPixel(50,50,Color.White);
        var first = new VisualTextRegion("one","first",[],new PixelRect(30,30,220,70),VisualTextRole.Body,VisualStructureConfidence.High);
        var neighbor = new VisualTextRegion("two","neighbor",[],new PixelRect(30,115,320,70),VisualTextRole.Body,VisualStructureConfidence.High);
        var windows = LiveTranslationChangePlanner.Plan(before,after,[first,neighbor]);
        if (windows.Any(window => window.IntersectsWith(new Rectangle(30,115,320,70))
            && !window.Contains(new Rectangle(30,115,320,70))))
            throw new Exception("Capture margin cuts a known neighboring paragraph into a sentence fragment.");
    }
}
