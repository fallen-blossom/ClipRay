using ClipRay.App.Translation;
using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;

internal static class SentenceTranslationTests
{
    public static void SocialCounterCannotJoinBody()
    {
        var regions=VisualTextRegionBuilder.Build([
            new OcrTextBlock("If Codex disappeared tomorrow, what would you switch to?",new PixelRect(66,24,620,36),[]),
            new OcrTextBlock("45",new PixelRect(71,72,70,37),[]),
            new OcrTextBlock("106",new PixelRect(400,72,65,37),[])]);
        if(regions.Any(region=>region.Role==VisualTextRole.Body && region.SourceText.Contains("45")))
            throw new Exception("A social counter joined a complete body sentence.");
    }
    public static void ControlTermsStayWithinControls()
    {
        using var provider=new LocalMachineTranslationProvider(new ControlTermProcess());
        var results=provider.TranslateManyAsync([
            new TranslationRequest("en","zh","Select effort",textKind:TranslationTextKind.UiControl),
            new TranslationRequest("en","zh","Less usage",textKind:TranslationTextKind.UiControl),
            new TranslationRequest("en","zh","High",textKind:TranslationTextKind.Body)],default).GetAwaiter().GetResult();
        if(results[0].Text!="选择推理强度" || results[1].Text!="更低用量" || results[2].Text!="正文翻译")
            throw new Exception("A control term was mistranslated or escaped into body translation.");
    }
    private sealed class ControlTermProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request,CancellationToken token)
        {
            if(request.Texts.Count!=1 || request.Texts[0]!="High") throw new Exception("Known control terms were sent to the model.");
            return Task.FromResult<IReadOnlyList<string>>(["正文翻译"]);
        }
    }
    public static void DecimalMultiplierKeepsItsPoint()
    {
        using var provider=new LocalMachineTranslationProvider(new DecimalUnitProcess());
        var result=provider.TranslateManyAsync([new TranslationRequest("en","zh","0.5x speed",textKind:TranslationTextKind.UiControl)],default).GetAwaiter().GetResult()[0];
        if(!result.IsSuccess || !result.Text.Contains("0.5x") || result.Text.Contains("05x")) throw new Exception("A numeric multiplier lost its decimal point.");
    }
    private sealed class DecimalUnitProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request,CancellationToken token)
        {
            if(request.Texts.Any(text=>text.Contains('5'))) throw new Exception("The protected decimal multiplier was fragmented.");
            return Task.FromResult<IReadOnlyList<string>>(request.Texts.Select(_=>"速度").ToArray());
        }
    }
    public static void IconFragmentsAndStandaloneModelsDoNotBecomeProse()
    {
        var sources=new[]{"S ...","C T","A","pp","GPT-6 Astra"};
        var regions=sources.Select((text,index)=>new VisualTextRegion($"r{index}",text,
            [new OcrTextBlock(text,new PixelRect(0,index*50,100,25),[])],new PixelRect(0,index*50,100,25),
            VisualTextRole.Control,VisualStructureConfidence.Medium)).ToArray();
        var requests=VisualTranslationRequestFactory.Create(regions,"zh",DeepSeekModels.Flash);
        if(requests.Any(request=>request.TextKind!=TranslationTextKind.BrandOrIdentifier))
            throw new Exception("An OCR icon fragment or standalone model label was sent as natural prose.");

        DistantPictogramCannotJoinAnotherControl();
        CoBoundedPictogramCannotSetTranslationAnchor();
        LegitimateLeadingLetterRemainsText();
    }
    public static void SocialMachineControlsUseSharedUiTerms()
    {
        var process = new SocialControlProcess();
        using var provider = new LocalMachineTranslationProvider(process);
        var labels = new[] { "Follow", "Post", "History", "Who to follow", "What's happening", "Show more" };
        var results = provider.TranslateManyAsync(labels.Select(label =>
            new TranslationRequest("en", "zh", label, textKind: TranslationTextKind.UiControl,
                scene: "social_media")).ToArray(), default).GetAwaiter().GetResult();
        var expected = new[] { "关注", "发布", "历史记录", "推荐关注", "正在发生什么", "显示更多" };
        if (process.Calls != 0 || results.Where((result, index) =>
                !result.IsSuccess || result.Text != expected[index]).Any())
            throw new Exception("Known social UI controls were sent to the model or mistranslated.");

        var generic = provider.TranslateManyAsync([
            new TranslationRequest("en", "zh", "Post", textKind: TranslationTextKind.UiControl,
                scene: "screen_capture")], default).GetAwaiter().GetResult();
        if (process.Calls != 1 || !generic[0].IsSuccess || generic[0].Text != "模型结果")
            throw new Exception("A context-dependent social UI term overrode a generic screen control.");
    }
    private sealed class SocialControlProcess : IMachineTranslationProcess
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request,
            CancellationToken token)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>(
                request.Texts.Select(static _ => "模型结果").ToArray());
        }
    }

    private static void DistantPictogramCannotJoinAnotherControl()
    {
        var blocks = OcrBlockGrouping.GroupWords([
            new OcrWord("Q", new PixelRect(10, 8, 34, 30)),
            new OcrWord("Q Search", new PixelRect(105, 11, 108, 27)),
        ]);
        if (blocks.Count != 2 || blocks[0].Text != "Q" || blocks[1].Text != "Q Search")
            throw new Exception("A distant single-glyph icon joined a separate control's OCR line.");
    }

    private static void CoBoundedPictogramCannotSetTranslationAnchor()
    {
        using var source = new System.Drawing.Bitmap(260, 100);
        using (var graphics = System.Drawing.Graphics.FromImage(source))
        {
            graphics.Clear(System.Drawing.Color.Black);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2);
            graphics.DrawEllipse(pen, 16, 17, 16, 16);
            graphics.DrawLine(pen, 29, 30, 36, 38);
            graphics.DrawRectangle(pen, 16, 61, 16, 20);
            for (var x = 47; x < 117; x += 8)
                graphics.FillRectangle(System.Drawing.Brushes.White, x, 21, 5, 14);
            for (var x = 50; x < 190; x += 8)
                graphics.FillRectangle(System.Drawing.Brushes.White, x, 64, 5, 14);
        }

        var searchBounds = new PixelRect(10, 12, 112, 30);
        var bookmarkBounds = new PixelRect(10, 57, 185, 30);
        var regions = VisualTextRegionBuilder.Build([
            new OcrTextBlock("Q Search", searchBounds, [new OcrWord("Q Search", searchBounds)]),
            new OcrTextBlock("Bookmarks", bookmarkBounds, [new OcrWord("Bookmarks", bookmarkBounds)]),
        ], source);
        var search = regions.SingleOrDefault(static region => region.SourceText == "Search");
        var bookmark = regions.SingleOrDefault(static region => region.SourceText == "Bookmarks");
        if (search is null || search.Bounds.Left < 44 || search.Bounds.Right != searchBounds.Right)
            throw new Exception("The search pictogram still determines the translated label's left edge.");
        if (bookmark is null || bookmark.Bounds.Left < 47 || bookmark.Bounds.Right != bookmarkBounds.Right)
            throw new Exception("The bookmark pictogram still determines the translated label's left edge.");
        using var searchMask = TextReplacementMask.Build(source, search);
        using var bookmarkMask = TextReplacementMask.Build(source, bookmark);
        if (searchMask.Mask.GetPixel(22, 24).A != 0 || bookmarkMask.Mask.GetPixel(22, 70).A != 0)
            throw new Exception("A translated label's text mask still erases its neighboring pictogram.");
        var requests = VisualTranslationRequestFactory.Create(regions, "zh", DeepSeekModels.Flash);
        if (!requests.Any(static request => request.OcrText == "Search")
            || requests.Any(static request => request.OcrText == "Q Search"))
            throw new Exception("The search pictogram still enters the translation request.");
    }

    private static void LegitimateLeadingLetterRemainsText()
    {
        using var source = new System.Drawing.Bitmap(160, 48);
        using (var graphics = System.Drawing.Graphics.FromImage(source))
        {
            graphics.Clear(System.Drawing.Color.Black);
            graphics.FillRectangle(System.Drawing.Brushes.White, 15, 15, 17, 18);
            for (var x = 43; x < 127; x += 8)
                graphics.FillRectangle(System.Drawing.Brushes.White, x, 17, 5, 14);
        }
        var bounds = new PixelRect(10, 10, 125, 30);
        var region = VisualTextRegionBuilder.Build([
            new OcrTextBlock("A Search", bounds, [new OcrWord("A Search", bounds)]),
        ], source).Single();
        if (region.SourceText != "A Search" || region.Bounds.Left != bounds.Left)
            throw new Exception("A legitimate leading one-letter label was removed as an icon.");
    }

    public static void PreservedNeighborKeepsOriginalInkAtOverlappingOcrBoundary()
    {
        using var source = new System.Drawing.Bitmap(220, 60);
        using (var graphics = System.Drawing.Graphics.FromImage(source))
            graphics.Clear(System.Drawing.Color.Black);
        for (var x = 24; x < 117; x += 8)
        for (var y = 19; y < 31; y++)
            source.SetPixel(x, y, System.Drawing.Color.Gray);
        for (var x = 117; x < 143; x += 6)
        for (var y = 19; y < 31; y++)
            source.SetPixel(x, y, System.Drawing.Color.DeepSkyBlue);
        source.SetPixel(113, 20, System.Drawing.Color.FromArgb(2, 8, 13));

        var translatedBounds = new PixelRect(20, 16, 100, 20);
        var preservedBounds = new PixelRect(116, 16, 35, 20);
        var translated = new VisualTextRegion("label", "Trending with",
            [new OcrTextBlock("Trending with", translatedBounds,
                [new OcrWord("Trending with", translatedBounds)])], translatedBounds,
            VisualTextRole.Control, VisualStructureConfidence.High);
        var preserved = new VisualTextRegion("link", "#UFCVegas121",
            [new OcrTextBlock("#UFCVegas121", preservedBounds,
                [new OcrWord("#UFCVegas121", preservedBounds)])], preservedBounds,
            VisualTextRole.Control, VisualStructureConfidence.High);

        var plan = InPlaceLayoutService.BuildRegionPlan(source, [translated, preserved],
            [new TranslationResult(true, "相关趋势", "machine-completed"),
                new TranslationResult(false, "#UFCVegas121", "source-preserved")]);
        var item = plan.Items.SingleOrDefault();
        if (item?.MaskedPatchPng is null || item.SourceRegion?.Id != "label")
            throw new Exception("The adjacent translated control did not produce a masked patch.");
        using var stream = new System.IO.MemoryStream(item.MaskedPatchPng);
        using var patch = new System.Drawing.Bitmap(stream);
        if (patch.GetPixel(97, 3).A != 0 || patch.GetPixel(93, 4).A != 0
            || patch.GetPixel(28, 3).A == 0)
            throw new Exception("The replacement mask erased protected blue link ink or missed its own source text.");
    }
    public static void PreservedEmojiDoesNotShieldTranslatedLastGlyph()
    {
        using var source = new System.Drawing.Bitmap(220, 60);
        using (var graphics = System.Drawing.Graphics.FromImage(source))
            graphics.Clear(System.Drawing.Color.Black);
        for (var x = 24; x < 116; x += 7)
        for (var y = 19; y < 31; y++)
            source.SetPixel(x, y, System.Drawing.Color.White);
        for (var y = 19; y < 31; y++)
        {
            source.SetPixel(114, y, System.Drawing.Color.White);
            source.SetPixel(117, y, System.Drawing.Color.Gold);
        }
        var translatedBounds = new PixelRect(20, 16, 100, 20);
        var emojiBounds = new PixelRect(116, 16, 28, 20);
        var translated = new VisualTextRegion("label", "This video was created using the MCP",
            [new OcrTextBlock("This video was created using the MCP", translatedBounds,
                [new OcrWord("This video was created using the MCP", translatedBounds)])],
            translatedBounds, VisualTextRole.Body, VisualStructureConfidence.High);
        var emoji = new VisualTextRegion("emoji", "👇",
            [new OcrTextBlock("👇", emojiBounds, [new OcrWord("👇", emojiBounds)])],
            emojiBounds, VisualTextRole.Control, VisualStructureConfidence.High);
        var plan = InPlaceLayoutService.BuildRegionPlan(source, [translated, emoji],
            [new TranslationResult(true, "此视频由 MCP 制作", "machine-completed"),
                new TranslationResult(false, "👇", "source-preserved")]);
        var item = plan.Items.SingleOrDefault();
        if (item?.MaskedPatchPng is null)
            throw new Exception("The translated sentence did not produce a masked patch.");
        using var stream = new System.IO.MemoryStream(item.MaskedPatchPng);
        using var patch = new System.Drawing.Bitmap(stream);
        if (patch.GetPixel(94, 4).A == 0 || patch.GetPixel(97, 4).A != 0)
            throw new Exception("The preserved emoji shielded the last source glyph or lost its own ink.");
    }
    public static void SameColorPreservedNeighborKeepsInkOutsideItsOcrBox()
    {
        using var source = new System.Drawing.Bitmap(220, 60);
        using (var graphics = System.Drawing.Graphics.FromImage(source))
            graphics.Clear(System.Drawing.Color.Black);
        for (var x = 24; x < 109; x += 7)
        for (var y = 19; y < 31; y++)
            source.SetPixel(x, y, System.Drawing.Color.White);
        for (var x = 112; x < 143; x += 5)
        for (var y = 19; y < 31; y++)
            source.SetPixel(x, y, System.Drawing.Color.White);
        var translatedBounds = new PixelRect(20, 16, 100, 20);
        var preservedBounds = new PixelRect(115, 16, 36, 20);
        var translated = new VisualTextRegion("label", "Translate this",
            [new OcrTextBlock("Translate this", translatedBounds,
                [new OcrWord("Translate this", translatedBounds)])], translatedBounds,
            VisualTextRole.Control, VisualStructureConfidence.High);
        var preserved = new VisualTextRegion("name", "@SameColor",
            [new OcrTextBlock("@SameColor", preservedBounds,
                [new OcrWord("@SameColor", preservedBounds)])], preservedBounds,
            VisualTextRole.Control, VisualStructureConfidence.High);
        var plan = InPlaceLayoutService.BuildRegionPlan(source, [translated, preserved],
            [new TranslationResult(true, "翻译它", "machine-completed"),
                new TranslationResult(false, "@SameColor", "source-preserved")]);
        var item = plan.Items.SingleOrDefault();
        if (item?.MaskedPatchPng is null)
            throw new Exception("The translated label did not produce a masked patch.");
        using var stream = new System.IO.MemoryStream(item.MaskedPatchPng);
        using var patch = new System.Drawing.Bitmap(stream);
        if (patch.GetPixel(92, 4).A != 0 || patch.GetPixel(4, 4).A == 0)
            throw new Exception("A same-color preserved neighbor lost ink outside its OCR box.");
    }
    public static void BlankParagraphGapKeepsSeparateBodyBlocks()
    {
        var regions=VisualTextRegionBuilder.Build([
            new OcrTextBlock("Something like a slow mode would help everyone",new PixelRect(20,0,500,34),[]),
            new OcrTextBlock("make the plan possible, right?",new PixelRect(20,32,350,31),[]),
            new OcrTextBlock("It would also be perfect for a different task.",new PixelRect(20,90,500,35),[])]);
        if(regions.Any(region=>region.SourceText.Contains("Something") && region.SourceText.Contains("different")))
            throw new Exception("A blank paragraph gap was discarded during sentence grouping.");
    }

    public static void CompleteSentenceRowsStayIndependentVisualRegions()
    {
        var regions = VisualTextRegionBuilder.Build([
            new OcrTextBlock("Line 61: The current viewport should translate.", new PixelRect(20, 0, 520, 24), []),
            new OcrTextBlock("Line 62: The current viewport should translate.", new PixelRect(20, 25, 520, 24), []),
            new OcrTextBlock("Line 63: The current viewport should translate.", new PixelRect(20, 50, 520, 24), [])]);
        var body = regions.Where(region => region.Role == VisualTextRole.Body).ToArray();
        if (body.Length != 3 || body.Any(region => region.Lines.Count != 1))
            throw new Exception("Complete OCR sentence rows were merged into one oversized layout region.");
    }
    public static void ClippedViewportTopKeepsWrappedSentenceTogether()
    {
        var regions = VisualTextRegionBuilder.Build([
            new OcrTextBlock("Platform four is closed today. Please use the stairs near the south",
                new PixelRect(49, 0, 1401, 38), []),
            new OcrTextBlock("entrance.", new PixelRect(45, 54, 206, 57), []),
            new OcrTextBlock("Safety notice", new PixelRect(48, 421, 369, 73), [])]);
        if (regions.Count != 2 || regions[0].Role != VisualTextRole.Body
            || regions[0].Lines.Count != 2 || !regions[0].SourceText.EndsWith("south\nentrance.", StringComparison.Ordinal))
            throw new Exception("A clipped first line lost its sentence continuation at the viewport edge.");
    }
    public static void TallOcrWordCannotPullAdjacentRowIntoHeader()
    {
        var blocks=OcrBlockGrouping.GroupWords([
            new OcrWord("Irushi",new PixelRect(20,10,70,40)),
            new OcrWord("@Im_IrushiK",new PixelRect(100,17,120,20)),
            new OcrWord("Give us a slow mode for Codex.",new PixelRect(20,43,450,20))]);
        if(blocks.Any(block=>block.Text.Contains("Irushi") && block.Text.Contains("Give")))
            throw new Exception("A tall OCR word pulled the next text row into the identity line.");
    }
    public static void SeparateNameAndHandleStayIdentityMetadata()
    {
        var regions=VisualTextRegionBuilder.Build([
            new OcrTextBlock("Loic",new PixelRect(20,10,50,21),[]),
            new OcrTextBlock("@LDT0545 · 11h",new PixelRect(80,10,160,21),[]),
            new OcrTextBlock("If Codex disappeared tomorrow, what would you use?",new PixelRect(20,36,450,21),[])]);
        var requests=VisualTranslationRequestFactory.Create(regions,"zh",DeepSeekModels.Flash);
        if(!requests.Any(request=>request.OcrText=="Loic" && request.TextKind==TranslationTextKind.IdentityMetadata)
            || requests.Any(request=>request.TextKind==TranslationTextKind.Body && request.OcrText.Contains("Loic")))
            throw new Exception("A separately styled social name was translated or merged into body.");
    }
    public static void NarrowControlCannotShrinkBodyTypography()
    {
        using var source=new System.Drawing.Bitmap(500,130);
        using(var graphics=System.Drawing.Graphics.FromImage(source))
        using(var font=new System.Drawing.Font("Arial",20,System.Drawing.FontStyle.Regular,System.Drawing.GraphicsUnit.Pixel))
        { graphics.Clear(System.Drawing.Color.Black); graphics.DrawString("Go",font,System.Drawing.Brushes.White,20,10);
          graphics.DrawString("A complete sentence for the body.",font,System.Drawing.Brushes.White,20,45); }
        var controlBlock=new OcrTextBlock("Go",new PixelRect(20,10,35,24),[new OcrWord("Go",new PixelRect(20,10,35,24))]);
        var bodyBlock=new OcrTextBlock("A complete sentence for the body.",new PixelRect(20,45,350,24),[new OcrWord("sentence",new PixelRect(20,45,350,24))]);
        var control=new VisualTextRegion("control","Go",[controlBlock],controlBlock.Bounds,VisualTextRole.Control,VisualStructureConfidence.High);
        var body=new VisualTextRegion("body",bodyBlock.Text,[bodyBlock],bodyBlock.Bounds,VisualTextRole.Body,VisualStructureConfidence.High);
        var bodyResult=new TranslationResult(true,"这是一个完整的正文句子。","machine-completed");
        var alone=InPlaceLayoutService.BuildRegions(source,[body],[bodyResult]).Single();
        var together=InPlaceLayoutService.BuildRegions(source,[control,body],
            [new TranslationResult(true,"非常狭窄的控件按钮","machine-completed"),bodyResult]).Single(item=>item.SourceRegion!.Role==VisualTextRole.Body);
        if(together.TextLayout!.FontSizePixels < alone.TextLayout!.FontSizePixels-.01)
            throw new Exception("A narrow control forced an unrelated paragraph to shrink.");
    }
    public static void InlineProtectedTextKeepsOneSentence()
    {
        using var provider=new LocalMachineTranslationProvider(new InlineSentenceProcess());
        var result=provider.TranslateManyAsync([new TranslationRequest("en","zh",
            "Please visit https://example.com/help and ask @Alice about Codex.",textKind:TranslationTextKind.Body)],default)
            .GetAwaiter().GetResult()[0];
        if(!result.IsSuccess || result.Text!="请访问https://example.com/help，并向@Alice咨询Codex。")
            throw new Exception("Inline protected content split the sentence or was not restored exactly.");
    }

    private sealed class InlineSentenceProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request,CancellationToken token)
        {
            if(request.Texts.Count!=1 || request.Texts[0]!="Please visit https://example.com/help and ask @Alice about Codex.")
                throw new Exception("Protected inline text was translated as disconnected fragments.");
            return Task.FromResult<IReadOnlyList<string>>(["请访问https://example.com/help，并向@Alice咨询Codex。"]);
        }
    }
    public static void SingleLineBodyDoesNotAlignToUnrelatedControls()
    {
        using var image=new System.Drawing.Bitmap(500,300);
        var body=new OcrTextBlock("This is a complete body sentence.",new PixelRect(20,80,380,22),[]);
        var controls=new[]{body,new OcrTextBlock("Follow",new PixelRect(175,10,70,22),[]),
            new OcrTextBlock("Profile",new PixelRect(170,160,80,22),[])};
        var region=new VisualTextRegion("body",body.Text,[body],body.Bounds,VisualTextRole.Body,VisualStructureConfidence.High);
        if(SourceVisualStyleEstimator.EstimateRegion(image,region,controls,System.Drawing.Color.Black).Alignment!=SourceTextAlignment.Left)
            throw new Exception("A single-line body inherited centered alignment from unrelated controls.");
    }
    public static void TightOcrWordBoundsDoNotShrinkRealGlyphs()
    {
        using var bitmap=new System.Drawing.Bitmap(400,80);
        using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
        using(var font=new System.Drawing.Font("Arial",24,System.Drawing.FontStyle.Regular,System.Drawing.GraphicsUnit.Pixel))
        { graphics.Clear(System.Drawing.Color.Black); graphics.DrawString("Translation sentence",font,System.Drawing.Brushes.White,10,10); }
        var ink=new List<System.Drawing.Point>();
        for(var y=0;y<80;y++) for(var x=0;x<400;x++) if(bitmap.GetPixel(x,y).R>180) ink.Add(new(x,y));
        var left=ink.Min(p=>p.X); var top=ink.Min(p=>p.Y);
        var width=ink.Max(p=>p.X)-left+1; var height=ink.Max(p=>p.Y)-top+1;
        var bounds=new PixelRect(left,top,width,height);
        var block=new OcrTextBlock("Translation sentence",bounds,[new OcrWord("Translation",bounds)]);
        var style=SourceVisualStyleEstimator.Estimate(bitmap,block,[block],System.Drawing.Color.Black);
        if(style.GlyphHeightPixels < height*.85)
            throw new Exception("Tightly measured ordinary OCR glyphs were mistaken for a badge and shrunk.");
    }
    public static void SocialIdentityStaysOutsideBody()
    {
        foreach(var header in new[]{"Tibo @thsottiaux Sep 11","Tiboe @thsottiaux。Se p 11"})
        {
        var regions=VisualTextRegionBuilder.Build([
            new OcrTextBlock(header,new PixelRect(20,10,330,21),[]),
            new OcrTextBlock("I tried the new model and the results were",new PixelRect(20,36,390,21),[]),
            new OcrTextBlock("worse than before after three hours of use.",new PixelRect(20,62,390,21),[])]);
        if(regions.Any(region => region.SourceText.Contains("@thsottiaux") && region.Lines.Count>1)
            || !regions.Any(region => region.Role==VisualTextRole.Body && region.Lines.Count==2))
            throw new Exception("Social identity was merged into body, or a duration split a sentence.");
        }
    }
    public static void FailedEmptyFrameWaitsForChangeWithoutPublishing()
    {
        var calls=0; var publications=0; var delays=0;
        using var stop=new CancellationTokenSource();
        ClipRay.App.LiveTranslation.LiveTranslationSession? session=null;
        session=new(new System.Drawing.Rectangle(0,0,40,40), _=>new System.Drawing.Bitmap(40,40),
            (_,_)=> { calls++; return Task.FromResult(new ClipRay.App.LiveTranslation.LiveTranslationFrame(
                "Machine","layout-incomplete",[],"layout-incomplete")); },TranslationRouteKind.Machine,
            (_,_)=> { if(++delays==3) stop.Cancel(); return Task.CompletedTask; },validateLayoutRegions:true);
        using(session)
        {
            session.FrameReady+=(_,_)=>publications++;
            session.StartAsync(stop.Token).GetAwaiter().GetResult();
        }
        if(calls!=3 || publications!=0
            || session.State==ClipRay.App.LiveTranslation.LiveTranslationSessionState.StoppedAfterConsecutiveFailures)
            throw new Exception("A failed empty frame was published or stopped instead of remaining recoverable.");
    }
    public static void RendererUsesMeasuredLineGeometry()
    {
        const string text = "这是一个完整的中文句子，应该按段落换行。";
        var bounds = new PixelRect(10,10,160,80);
        var layout = new TranslationTextLayout(true,18,22,SourceFontWeight.Normal,
            SourceFontCategory.SansSerif,false,SourceTextAlignment.Left,0,"Microsoft YaHei UI");
        var block = new OcrTextBlock(text,bounds,[]);
        var item = new InPlaceLayoutItem(block,text,bounds,InPlaceBackgroundMode.FlatSampled,
            System.Drawing.Color.Black,System.Drawing.Color.White,TextLayout:layout);
        var actual = ClipRay.App.Overlay.TranslatedSelectionRenderer.RenderOverlay(200,110,[item]);
        var visual = new System.Windows.Media.DrawingVisual();
        using(var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(System.Windows.Media.Brushes.Black,null,new System.Windows.Rect(10,10,160,80));
            var formatted = new System.Windows.Media.FormattedText(text,System.Globalization.CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,new System.Windows.Media.Typeface("Microsoft YaHei UI"),18,
                System.Windows.Media.Brushes.White,1) { MaxTextWidth=160,LineHeight=22 };
            drawing.DrawText(formatted,new System.Windows.Point(10,10));
        }
        var expected = new System.Windows.Media.Imaging.RenderTargetBitmap(200,110,96,96,System.Windows.Media.PixelFormats.Pbgra32);
        expected.Render(visual);
        var first=new byte[200*110*4]; var second=new byte[first.Length];
        actual.CopyPixels(first,200*4,0); expected.CopyPixels(second,200*4,0);
        if(!first.SequenceEqual(second))
        {
            var diff=Enumerable.Range(0,first.Length).Where(i=>first[i]!=second[i]).ToArray();
            var maximum=diff.Max(i=>Math.Abs(first[i]-second[i]));
            if(maximum > 2) throw new Exception($"Rendered glyph baselines differ from the measured FormattedText layout: max {maximum}, {diff.Length} bytes, first pixel {diff[0]/4%200},{diff[0]/800}, actual {first[diff[0]]}, expected {second[diff[0]]}.");
        }
    }
    public static void NumericBodyKeepsSentenceContext()
    {
        const string source = "I tried GPT-5.4 for 3 hours, but the results were worse than before.";
        using var provider = new LocalMachineTranslationProvider(new FullSentenceProcess(source));
        var result = provider.TranslateManyAsync([new TranslationRequest("en", "zh", source,
            textKind: TranslationTextKind.Body)], default).GetAwaiter().GetResult()[0];
        if (!result.IsSuccess || result.Text != "我试了3小时GPT-5.4，但结果比以前更糟。")
            throw new Exception("Numbers and model identifiers cut the sentence context.");
    }

    public static void VisualLineBreakIsNotSentenceBoundary()
    {
        var region = new VisualTextRegion("body", "I tried GPT-5.4 for 3 hours,\nbut the results were worse than before.",
            [new OcrTextBlock("I tried GPT-5.4 for 3 hours,", new PixelRect(0,0,300,20), []),
             new OcrTextBlock("but the results were worse than before.", new PixelRect(0,25,350,20), [])],
            new PixelRect(0,0,350,45), VisualTextRole.Body, VisualStructureConfidence.High);
        var request = VisualTranslationRequestFactory.Create([region], "zh", DeepSeekModels.Flash)[0];
        if (request.OcrText != "I tried GPT-5.4 for 3 hours, but the results were worse than before.")
            throw new Exception("A visual OCR line break remained in semantic MT input.");
    }

    public static void CompleteOcrLinesAreIndependentMachineUnits()
    {
        var region = new VisualTextRegion("body",
            "Line 8: The quick brown fox checks live translation after scrolling.\n" +
            "Line 9: The quick brown fox checks live translation after scrolling.\n" +
            "Line 10: The quick brown fox checks live translation after scrolling.",
            [new OcrTextBlock("Line 8: The quick brown fox checks live translation after scrolling.", new PixelRect(0,0,600,24), []),
             new OcrTextBlock("Line 9: The quick brown fox checks live translation after scrolling.", new PixelRect(0,28,600,24), []),
             new OcrTextBlock("Line 10: The quick brown fox checks live translation after scrolling.", new PixelRect(0,56,600,24), [])],
            new PixelRect(0,0,600,80), VisualTextRole.Body, VisualStructureConfidence.High);
        var request = VisualTranslationRequestFactory.Create([region], "zh", DeepSeekModels.Flash)[0];
        using var provider = new LocalMachineTranslationProvider(new IndependentLineProcess());
        var result = provider.TranslateManyAsync([request], default).GetAwaiter().GetResult()[0];
        if (!result.IsSuccess || !result.Text.Contains("第8行") || !result.Text.Contains("第10行")
            || result.Text.Split('\n').Length != 3)
            throw new Exception("Complete OCR lines were flattened into one fragile machine-translation item.");
    }

    private sealed class IndependentLineProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            if (request.Texts.Count != 3 || request.Texts.Any(static text => text.Contains('\n')))
                throw new Exception("A scrolling paragraph was sent to MT as one oversized item.");
            return Task.FromResult<IReadOnlyList<string>>(
                ["第8行：滚动后的实时翻译。", "第9行：滚动后的实时翻译。", "第10行：滚动后的实时翻译。"]);
        }
    }

    private sealed class FullSentenceProcess(string expected) : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            if (request.Texts.Count != 1 || request.Texts[0] != expected)
                throw new Exception("MT must receive one intact sentence with its numbers and model token.");
            return Task.FromResult<IReadOnlyList<string>>(["我试了3小时GPT-5.4，但结果比以前更糟。"]);
        }
    }
}
