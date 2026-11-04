using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

public static class LiveReadingLifecycleTests
{
    public static void Run()
    {
        var first=Region("first",new(10,10,100,30));
        var second=Region("second",new(160,90,100,30));
        var outcome=Outcome([first,second],[new(true,"首段译文","completed"),new(true,"次段译文","completed")]);
        var frame=LiveTranslationFrame.FromOutcome(TranslationRouteKind.Machine,outcome);
        Assert(frame.ReadingItems.Count==2 && frame.LayoutItems.Count==0,"Successful unplaced translations must survive FromOutcome.");
        using var pixels=new Bitmap(300,150);
        using(var graphics=Graphics.FromImage(pixels)) graphics.Clear(Color.White);
        var source=new LiveTranslationPixelSnapshot(pixels);
        var committed=Commit(source,source,frame);
        Assert(committed.ResultState==LiveTranslationViewportResultState.AcceptablePartial
            && committed.ReadingWitness.Count==2,"All-layout-failed must still publish independently witnessed reading text.");
        pixels.SetPixel(20,20,Color.Red);
        var changed=new LiveTranslationPixelSnapshot(pixels);
        var retained=committed.ReadingWitness.RetainUnchanged(changed);
        Assert(retained.Count==1 && retained.Items.Single().Region.Id==second.Id,
            "Local changes must expire only the affected reading entry.");
        var late=Commit(source,changed,frame);
        Assert(late.ReadingWitness.Count==1,"A late pipeline must never republish the changed region's old reading text.");
        pixels.SetPixel(180,100,Color.Red);
        Assert(committed.ReadingWitness.RetainUnchanged(new(pixels)).Count==0,
            "Viewport content replacement must expire all old reading entries.");
        var rendered=Layout(first,"首段译文");
        var withLayout=frame with {LayoutItems=[rendered]};
        var partial=Commit(source,source,withLayout);
        Assert(partial.ReadingWitness.Count==1 && partial.ReadingWitness.Items[0].Region.Id==second.Id,
            "A final visible layout must not also appear in the reading list.");
        // PublishDisplayed remeasures retained layouts without running commit's
        // quarantine again. Verified reading text must never bypass that gate.
        IReadOnlyList<InPlaceLayoutItem>[] unsafeRetained = [
            [rendered with {Bounds=new(-1,10,100,30)}],
            [rendered with {TextLayout=rendered.TextLayout! with {ShouldRender=false}}],
            [rendered,rendered with {Text="碰撞译文"}]
        ];
        foreach(var unsafeItems in unsafeRetained)
        {
            var unsafeFrame=withLayout with {LayoutItems=unsafeItems};
            var metrics=LiveTranslationViewportCommitPolicy.Measure(source,unsafeFrame,unsafeItems.Count,unsafeItems);
            Assert(metrics.HasUnsafeLayout
                && LiveTranslationViewportResultPolicy.Classify(unsafeFrame,metrics,partial.ReadingWitness.Count)
                    ==LiveTranslationViewportResultState.Failed,
                "Verified reading text must not authorize unsafe retained overlay geometry or typography.");
        }
        var quarantine=Commit(source,source,withLayout with {LayoutItems=[rendered with {
            TextLayout=rendered.TextLayout! with {ShouldRender=false}}]});
        Assert(quarantine.Witness.Count==0 && quarantine.ReadingWitness.Count==2,
            "A quarantined layout must retain its successful translation in reading form.");
        var offset=LiveTranslationFrameCoordinates.Offset(frame,7,9);
        Assert(offset.ReadingItems[0].Region.Bounds==new PixelRect(17,19,100,30)
            && offset.ReadingItems[0].Region.Id==offset.SourceRegions[0].Id
            && offset.ReadingCandidates[0].Region.Id==offset.SourceRegions[0].Id,
            "Crop reading witnesses must use absolute geometry and matching source ids.");
        var cropRegion=Region("replacement",first.Bounds);
        var crop=LiveTranslationFrame.FromOutcome(TranslationRouteKind.Machine,
            Outcome([cropRegion],[new(true,"更新译文","completed")]));
        var merged=LiveTranslationRegionScheduler.Merge(crop,frame,[],new(0,0,140,60),retained.Items);
        Assert(merged.ReadingCandidates.Select(item=>item.Text).SequenceEqual(new[]{"次段译文","更新译文"}),
            "Regional merge must preserve only verified outside siblings and replace the dirty crop.");
        var failed=LiveTranslationFrame.FromOutcome(TranslationRouteKind.Machine,
            Outcome([first],[new(false,"","translation-failed")]));
        Assert(failed.ReadingItems.Count==0 && Commit(source,source,failed).ResultState==LiveTranslationViewportResultState.Failed,
            "A genuine translation failure cannot become a successful reading result.");
        var preserved=LiveTranslationFrame.FromOutcome(TranslationRouteKind.Machine,
            Outcome([first,second],[new(true,"first","source-preserved"),new(true,"second","completed")]));
        Assert(preserved.ReadingItems.Count==0,"Preserved and unchanged source text are not reading translations.");
        ReadingOnlySessionInvalidatesOnNextCapture();
        Console.WriteLine("LIVE_READING_LIFECYCLE_TESTS_PASSED");
    }

    private static void ReadingOnlySessionInvalidatesOnNextCapture()
    {
        var phase=0;
        var calls=0;
        var published=0;
        var invalidated=0;
        var region=Region("source",new(10,10,100,30));
        LiveTranslationSession? session=null;
        session=new(new(0,0,180,80),_=>
        {
            var bitmap=new Bitmap(180,80);
            using(var graphics=Graphics.FromImage(bitmap)) graphics.Clear(Color.White);
            if(Volatile.Read(ref phase)!=0) bitmap.SetPixel(20,20,Color.Red);
            return bitmap;
        },(_,_)=>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(LiveTranslationFrame.FromOutcome(TranslationRouteKind.Machine,
                Outcome([region],[new(true,"未原位显示的译文","completed")])));
        },TranslationRouteKind.Machine,validateLayoutRegions:true,scheduleRegions:true);
        using(session)
        using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(4)))
        {
            session.FrameReady+=(_,args)=>
            {
                Assert(args.Frame.LayoutItems.Count==0 && args.Frame.ReadingItems.Count==1,
                    "The committed reading-only frame must be publishable.");
                Interlocked.Increment(ref published);
                Volatile.Write(ref phase,1);
            };
            session.FrameInvalidated+=(_,_)=>
            {
                Interlocked.Increment(ref invalidated);
                session.Stop();
            };
            session.StartAsync(deadline.Token).GetAwaiter().GetResult();
        }
        Assert(published==1 && invalidated>0 && calls==1,
            $"Reading-only invalidation must happen on capture before another translation: frames={published}, clears={invalidated}, calls={calls}.");
    }

    private static LiveTranslationViewportCommit Commit(LiveTranslationPixelSnapshot source,
        LiveTranslationPixelSnapshot current,LiveTranslationFrame frame)=>
        LiveTranslationViewportCommitPolicy.Commit(source,current,frame.SourceRegions,
            LiveTranslationRegionWitness.Create(source,frame.LayoutItems),frame);
    private static VisualTextRegion Region(string text,PixelRect bounds)
    {
        var block=new OcrTextBlock(text,bounds,[]);
        return new(text,text,[block],bounds,VisualTextRole.Body,VisualStructureConfidence.High);
    }
    private static InPlaceLayoutItem Layout(VisualTextRegion region,string text)=>new(region.Lines[0],text,region.Bounds,
        InPlaceBackgroundMode.FlatSampled,Color.White,Color.Black,TextLayout:new(true,16,20,
            SourceFontWeight.Normal,SourceFontCategory.SansSerif,false,SourceTextAlignment.Left,0,"Segoe UI"),SourceRegion:region);
    private static ApplicationTranslationOutcome Outcome(IReadOnlyList<VisualTextRegion> regions,IReadOnlyList<TranslationResult> results)=>
        new(false,TranslationPipelineStage.Layout,"translation-partial",regions.SelectMany(region=>region.Lines).ToArray(),
            regions,results,[]);
    private static void Assert(bool condition,string message) {if(!condition) throw new Exception(message);}
}
