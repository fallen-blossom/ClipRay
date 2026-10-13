using System.Drawing;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using ClipRay.App.Capture;
using ClipRay.App.Diagnostics;
using ClipRay.App.Editing;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Scrolling;
using ClipRay.App.Recording;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using RapidOcrNet;

internal static partial class BackgroundTask8Tests
{
    private static void RawLines()
    {
        RapidOcrNet.TextBlock Block(float[] scores)=>new() { Text="https://local/a_b",Chars=["https://local/a_b"], BoxPoints=[new(2,4),new(92,4),new(92,24),new(2,24)],CharScores=scores };
        var words=RapidOcrRuntime.ConvertResult(new OcrResult { TextBlocks=[Block([.2f,.8f])],StrRes="fixture" },"locked-model","latin","hash");
        Check(words.Count==1&&words[0].IsDetectedLine&&Math.Abs(words[0].Evidence!.RawScore!.Value-.5)<.00001
            &&words[0].Bounds==new PixelRect(2,4,90,20)&&words[0].Evidence!.ModelSha256=="hash","Locked raw line schema lost provenance/geometry/confidence.");
        Check(RapidOcrRuntime.ConvertResult(new OcrResult { TextBlocks=[Block([])],StrRes="fixture" },"m","latin")[0].Evidence!.RawScore is null,"Empty score became fabricated certainty.");
        Check(RapidOcrRuntime.ConvertResult(new OcrResult { TextBlocks=[Block([float.NaN])],StrRes="fixture" },"m","latin")[0].Evidence!.RawScore is null,"Nonfinite score survived.");
        Check(OcrWordEvidence.FromScriptCandidate(ScriptOcrCandidate.FromRecognition("unknown",[],"latin"),"m").RawScore is null,"Common raw unknown became low confidence.");
    }
    private static void QualityGuards()
    {
        using var bitmap=new Bitmap(200,100);
        foreach(var score in new double?[]{.95,null})
        {var a=new FakeOcr([Word("a",score)]);var b=new FakeOcr([Word("invented longer",.99)]);
         var result=new FastFirstOcrRuntime(a,b).RecognizeAsync(bitmap,default).GetAwaiter().GetResult();Check(b.Calls==0&&result[0].Text=="a","Normal/unknown score lost fast path.");}
        foreach(var candidate in new[]{Array.Empty<OcrWord>(),new[]{Word("long invented",.99,new(140,60,40,20))},new[]{Word("low",.2)}})
        {var a=new FakeOcr([Word("retained",.3)]);var b=new FakeOcr(candidate);
         Check(new FastFirstOcrRuntime(a,b).RecognizeAsync(bitmap,default).GetAwaiter().GetResult()[0].Text=="retained","Empty/unrelated/worse fallback replaced existing geometry.");}
        var edge=new FakeOcr([Word("edge",null,new(0,10,50,8))]);var next=new FakeOcr([Word("edge",.9,new(0,10,50,8))]);
        new FastFirstOcrRuntime(edge,next).RecognizeAsync(bitmap,default).GetAwaiter().GetResult();Check(next.Calls==1,"Tiny edge text never reached the one bounded review.");
        var held=new FakeOcr([]){Hold=_=>Task.FromException(new OperationCanceledException())};var spare=new FakeOcr([]);
        try{new FastFirstOcrRuntime(held,spare).RecognizeAsync(bitmap,default).GetAwaiter().GetResult();throw new Exception("Cancellation swallowed.");}catch(OperationCanceledException){}
        Check(spare.Calls==0,"Cancellation scheduled another runtime.");
    }
    private static void ServiceTrace()
    {
        var runtime=new FakeOcr([Word("a/b",.9,new(8,8,80,24)),Word("c_d",.8,new(90,8,40,24))]);
        var service=new OcrService(()=>runtime);using var bitmap=new Bitmap(40,40);OcrRecognitionTrace? trace=null;
        var result=service.RecognizeAsync(bitmap,default,x=>trace=x).GetAwaiter().GetResult();
        Check(trace is { Scale:4,PreparedWidth:160,PreparedHeight:160 }&&trace.RawLines.Count==2&&trace.RawLines[0].IsDetectedLine
            &&result.Blocks.Count==2&&result.Blocks[0].Text=="a/b"&&result.Blocks[0].Words[0].Bounds==new PixelRect(2,2,20,6),"Raw→prepared→restored line trace changed text/granularity.");
        service.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    private static void DemandIdle()=>DemandIdleAsync().GetAwaiter().GetResult();
    private static async Task DemandIdleAsync()
    {
        var clock=new FakeClock();var instances=new List<FakeOcr>();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service=new OcrService(()=>{var r=new FakeOcr([Word("a",.9)]);instances.Add(r);return r;},clock:clock,idleTimeout:TimeSpan.FromMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(2));Check(instances.Count==0,"Idle startup initialized OCR.");
        await service.WarmupAsync(default);Check(instances.Count==1&&instances[0].Warms==1,"Explicit demand did not warm once.");
        instances[0].Hold=async _=>{entered.TrySetResult();await gate.Task;};using var bitmap=new Bitmap(100,100);
        var pending=service.RecognizeAsync(bitmap,default);await entered.Task;clock.Advance(TimeSpan.FromMinutes(2));await service.ReleaseIdleAsync();
        Check(instances[0].Releases==0,"Idle timer released active native ownership.");
        gate.SetResult();await pending;clock.Advance(TimeSpan.FromMinutes(2));await service.ReleaseIdleAsync();
        Check(instances[0].Releases==1,"Idle runtime not released after physical return.");
        await service.RecognizeAsync(bitmap,default);Check(instances.Count==2,"Post-idle demand did not recreate exactly one runtime.");
        await service.DisposeAsync();Check(instances[1].Releases==1,"Service close omitted runtime release.");
        try{await service.RecognizeAsync(bitmap,default);throw new Exception("Closed service revived.");}catch(ObjectDisposedException){}
        Check(instances.Count==2,"Closed admission rebuilt model.");
        var app=BackgroundTask3LifetimeTests.FakeApp();var starts=0;Set(app,"_translationServices",new TranslationRuntimeServices(()=>throw new Exception("cloud forbidden"),()=>{starts++;return new OcrService(()=>new FakeOcr([]));}));
        await app.WarmStartupOcrAsync();Check(starts==0,"Normal startup initialized lazy visual OCR.");
        var services=BackgroundTask3LifetimeTests.Field<TranslationRuntimeServices>(app,"_translationServices");await services.RetireAsync();
        await BackgroundTask3LifetimeTests.Field<OcrService>(app,"_machineOcr").DisposeAsync();
    }
    private static void ThreadPolicy()
    {foreach(var cpus in new[]{1,2,4,8,64,256})Check(OcrExecutionPolicy.SelectThreads(cpus) is >=1 and <=4,"Unbounded OCR thread budget.");
     Check(OcrExecutionPolicy.SessionEntries.Length==2&&OcrExecutionPolicy.SessionEntries.All(e=>e.Value=="0"),"Spinning policy not explicit.");}
    private static Bitmap Source(int width=100,int height=80)
    {var bitmap=new Bitmap(width,height);using var g=Graphics.FromImage(bitmap);g.Clear(Color.White);g.FillRectangle(Brushes.Blue,0,0,width/2,height);g.FillRectangle(Brushes.Green,width/2,0,width/2,height/2);return bitmap;}
    private static void Pixels(Bitmap expected,Bitmap actual,string message) => Check(expected.Width==actual.Width&&expected.Height==actual.Height
        &&PixelFingerprint.Create(expected).sha256==PixelFingerprint.Create(actual).sha256,message);
    private static void PreviewCache()
    {
        using var source=Source();var layer=new EditLayer(new(10,10,70,50));using var cache=new EditPreviewCache();
        layer.AddMosaicStroke(new([new(30,25),new(45,30)],18,12));layer.AddPenStroke(new([new(25,25),new(60,25)],Color.Red,6));
        layer.AddMosaicStroke(new([new(40,25)],20,8));
        void Verify(MosaicStroke? active=null){using var expected=EditRenderer.RenderOverlay(source,layer,active);Pixels(expected,cache.GetOverlay(source,layer,active),"Cached ordered pixels differ.");}
        Verify();var count=cache.ReplayedOperations;Verify();Check(cache.ReplayedOperations==count,"Unchanged committed history replayed.");
        var visible=cache.GetOverlay(source,layer,null);
        Check(visible.GetPixel(20,15).ToArgb()==Color.Blue.ToArgb()&&visible.GetPixel(68,48).A==0,"Raw mosaic order or unedited transparency changed.");
        using(var translated=new Bitmap(70,50))
        {using(var g=Graphics.FromImage(translated))g.Clear(Color.Cyan);using var exported=EditRenderer.CompositeSelection(translated,source,layer);
         Check(exported.GetPixel(20,15).ToArgb()==Color.Blue.ToArgb()&&exported.GetPixel(68,48).ToArgb()==Color.Cyan.ToArgb(),"Frozen raw mosaic / unedited translation export changed.");}
        Verify(new([new(25,25)],12,6));Verify(new([new(25,25),new(60,35)],12,6));Check(cache.ActivePoints==2,"Active points replayed prefix.");
        Verify();layer.UndoLast();Verify();layer.RedoLast();Verify();layer.UpdateSelection(new(5,5,60,40));Verify();
        layer.AddPenStroke(new([new(8,8)],Color.Black,8));Verify();
        using var composed=EditRenderer.Composite(source,layer);using var capture=EditRenderer.CompositeCapture(source,layer,layer.Selection);
        Pixels(composed,capture,"Cache companion changed capture/export.");
    }
    private static void MosaicWorkload()
    {
        using var source=Source(1600,1000);var layer=new EditLayer(new(0,0,1600,1000));
        var points=Enumerable.Range(0,500).Select(i=>new PixelPoint(30+i*3%1540,30+i*7%940)).ToArray();
        layer.AddMosaicStroke(new(points,32,12));using var cache=new EditPreviewCache();
        var legacy=new List<double>();var cached=new List<double>();
        for(var i=0;i<7;i++)
        {var sw=Stopwatch.StartNew();using var expected=EditRenderer.RenderOverlay(source,layer);legacy.Add(sw.Elapsed.TotalMilliseconds);
         sw.Restart();var actual=cache.GetOverlay(source,layer,null);cached.Add(sw.Elapsed.TotalMilliseconds);Pixels(expected,actual,"1600×1000 workload pixels differ.");}
        Check(cache.ReplayedOperations==1,"500-point committed workload replayed on every update.");
        Console.WriteLine(JsonSerializer.Serialize(new { workload="1600x1000/500",render_ms=legacy,cached_ms=cached,replayed_operations=cache.ReplayedOperations }));
    }
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    private static object? Invoke(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,args);
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(target,value);
    private static void OnDispatcher(Func<Task> action)
    {
        var dispatcher=System.Windows.Threading.Dispatcher.CurrentDispatcher;var prior=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
        try
        {var task=action();if(!task.IsCompleted){var frame=new System.Windows.Threading.DispatcherFrame();
         _=task.ContinueWith(_=>dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);System.Windows.Threading.Dispatcher.PushFrame(frame);}task.GetAwaiter().GetResult();}
        finally{SynchronizationContext.SetSynchronizationContext(prior);}
    }
    private sealed class OverlayFixture:IDisposable
    {
        internal readonly Bitmap Original;internal readonly SelectionOverlayWindow Overlay;
        internal OverlayFixture(IUiElementCandidateSource? elements=null,int width=100,int height=80)
        {
            var app=System.Windows.Application.Current??new System.Windows.Application();app.ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;
            app.Resources["AppFont"]=new System.Windows.Media.FontFamily("Segoe UI");
            Original=Source(width,height);
            Overlay=new(new CapturedFrame(new Bitmap(Original),new Rectangle(0,0,width,height)),new(10,10,width-20,height-20),
                clipboardTextSink:new NoClipboard(),windowCandidateSource:new FakeWindows(width,height),uiElementCandidateSource:elements??new FakeElements());
            Overlay.SnapQueryDelay=_=>Task.CompletedTask;
        }
        public void Dispose(){Overlay.Close();Original.Dispose();}
        internal void Loaded(bool value)=>typeof(System.Windows.FrameworkElement).GetProperty("IsLoadedCache",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(Overlay,value);
    }
    private sealed class NoClipboard:IClipboardTextSink{public void SetText(string text)=>throw new Exception("Real clipboard forbidden.");}
    private sealed class FakeWindows(int width,int height):IWindowCandidateSource
    {public IEnumerable<WindowSnapCandidate> Enumerate()=>[new(new(123),new(0,0,width,height),false,true,false,false,false,"fixture")];}
    private sealed class FakeElements(Func<IReadOnlyList<PixelRect>>? get=null):IUiElementCandidateSource
    {public IReadOnlyList<PixelRect> GetHierarchy(System.Drawing.Point point,IntPtr window)=>get?.Invoke()??[];}
    private static void PreviewEntry()=>OnDispatcher(()=>
    {
        using var f=new OverlayFixture();var layer=new EditLayer(new(10,10,80,60));layer.AddMosaicStroke(new([new(30,30)],20,12));
        Set(f.Overlay,"_editLayer",layer);Invoke(f.Overlay,"UpdateEditPreview");var cache=Field<EditPreviewCache>(f.Overlay,"_editPreviewCache");
        var count=cache.ReplayedOperations;Invoke(f.Overlay,"UpdateEditPreview");Check(cache.ReplayedOperations==count,"Actual preview handler replayed history.");
        var image=(System.Windows.Controls.Image)f.Overlay.FindName("EditPreview");using var expected=EditRenderer.RenderOverlay(f.Original,layer);
        Check(PixelFingerprint.Create((System.Windows.Media.Imaging.BitmapSource)image.Source).sha256==PixelFingerprint.Create(expected).sha256,"Actual preview pixels differ from transparent export operations.");
        return Task.CompletedTask;
    });
    private static Task Refresh(SelectionOverlayWindow overlay)
    {var interaction=Field<OverlaySnapInteraction>(overlay,"_snapInteraction");return (Task)Invoke(overlay,"RefreshSnapCandidatesAsync",interaction.BeginRefinement(),new System.Drawing.Point(30,30),overlay.LifetimeToken)!;}
    private static void UiOverlays()=>OnDispatcher(async()=>
    {
        var scheduler=new UiQueryScheduler();using var hold=new ManualResetEventSlim();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls=0;var bCalls=0;var a=new OverlayFixture(new FakeElements(()=>{Interlocked.Increment(ref calls);entered.TrySetResult();hold.Wait();return [new(20,20,20,20)];}));
        var defaultOwner=a.Overlay.SnapQueryScheduler;
        a.Overlay.SnapQueryScheduler=scheduler;a.Overlay.SnapQueryBudget=TimeSpan.FromMilliseconds(30);
        var first=Refresh(a.Overlay);await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));a.Dispose();
        using var b=new OverlayFixture(new FakeElements(()=>{Interlocked.Increment(ref bCalls);return [new(25,25,20,20)];}));
        Check(ReferenceEquals(defaultOwner,b.Overlay.SnapQueryScheduler),"Production default owner does not survive close/reopen.");
        b.Overlay.SnapQueryScheduler=scheduler;b.Overlay.SnapQueryBudget=TimeSpan.FromSeconds(2);Set(b.Overlay,"_snapDetectionMode",SnapDetectionMode.ElementFirst);var second=Refresh(b.Overlay);
        try{await Task.Delay(50);Check(calls==1&&bCalls==0,"Close/reopen started second physical query while A held.");}
        finally{hold.Set();}
        await first;await second;Check(bCalls==1,"Latest reopened client never ran after physical return.");await scheduler.RetireAsync();
        Check(Field<PixelRect>(b.Overlay,"_selection")==new PixelRect(25,25,20,20),"Current reopened candidate was not applied.");
    });
    private static void UiLatestDeadline()
    {
        var scheduler=new UiQueryScheduler();using var hold=new ManualResetEventSlim();var entered=new ManualResetEventSlim();var calls=0;var obsolete=0;
        var first=scheduler.QueryAsync(()=>{Interlocked.Increment(ref calls);entered.Set();hold.Wait();return [new(1,1,2,2)];},TimeSpan.FromMilliseconds(30),default);
        Check(entered.Wait(2000),"Fake query did not start.");
        var stale=scheduler.QueryAsync(()=>{obsolete++;return [];},TimeSpan.FromSeconds(2),default);
        var latest=scheduler.QueryAsync(()=>{calls++;return [new(3,3,4,4)];},TimeSpan.FromSeconds(2),default);
        Check(first.GetAwaiter().GetResult().Count==0&&calls==1,"Deadline released physical slot.");
        var retirement=scheduler.RetireAsync();Check(!retirement.IsCompleted,"Retirement dropped held physical owner.");hold.Set();
        Task.WhenAll(stale,latest,retirement).GetAwaiter().GetResult();Check(obsolete==0&&calls==1,"Retirement admitted pending query.");
        var nextScheduler=new UiQueryScheduler();using var nextHold=new ManualResetEventSlim();var start=new ManualResetEventSlim();
        var active=nextScheduler.QueryAsync(()=>{start.Set();nextHold.Wait();return [];},TimeSpan.FromSeconds(2),default);Check(start.Wait(2000),"Query start.");
        var replaced=nextScheduler.QueryAsync(()=>{obsolete++;return [];},TimeSpan.FromSeconds(2),default);
        var current=nextScheduler.QueryAsync(()=>[new(3,3,4,4)],TimeSpan.FromSeconds(2),default);nextHold.Set();
        Check(current.GetAwaiter().GetResult().Single()==new PixelRect(3,3,4,4)&&replaced.GetAwaiter().GetResult().Count==0&&obsolete==0,"Latest pending was not coalesced.");
        active.GetAwaiter().GetResult();nextScheduler.RetireAsync().GetAwaiter().GetResult();
    }
    private sealed class Node { internal Node? Child,Next;internal PixelRect Bounds; }
    private static void UiTraversal()
    {
        var root=new Node{Bounds=new(0,0,100,100)};var node=root;for(var i=0;i<100;i++){node.Child=new(){Bounds=new(0,0,100,100)};node=node.Child;}
        var inspected=0;var result=UiAutomationSnapCandidateSource.CollectHierarchy(root,new(30,30),n=>n.Child,n=>n.Next,n=>{inspected++;return n.Bounds;});
        Check(result.Count==33&&inspected==33,"Depth budget did not bound fake provider traversal.");
        node=root;root.Child=new(){Bounds=new(0,0,1,1)};node=root.Child;for(var i=0;i<500;i++){node.Next=new(){Bounds=new(0,0,1,1)};node=node.Next;}
        inspected=0;UiAutomationSnapCandidateSource.CollectHierarchy(root,new(30,30),n=>n.Child,n=>n.Next,n=>{inspected++;return n.Bounds;});Check(inspected<=129,"Sibling budget unbounded.");
    }
    private static void UiFallbackVersion()=>OnDispatcher(async()=>
    {
        var scheduler=new UiQueryScheduler();using var hold=new ManualResetEventSlim();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f=new OverlayFixture(new FakeElements(()=>{entered.TrySetResult();hold.Wait();return [new(20,20,20,20)];}));
        f.Overlay.SnapQueryScheduler=scheduler;f.Overlay.SnapQueryBudget=TimeSpan.FromMilliseconds(30);
        Set(f.Overlay,"_windowHandle",new IntPtr(99));Set(f.Overlay,"_selection",default(PixelRect));Set(f.Overlay,"_snapDetectionMode",SnapDetectionMode.ElementFirst);
        Invoke(f.Overlay,"UpdateSnapProposal",new PixelPoint(30,30));await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {await Task.Delay(60);Check(Field<PixelRect>(f.Overlay,"_selection")==new PixelRect(0,0,100,80),"Deadline discarded instant window fallback.");
         Invoke(f.Overlay,"StopSnapTracking");Invoke(f.Overlay,"SetSelection",new PixelRect(15,15,40,30));}
        finally{hold.Set();}
        await scheduler.RetireAsync();await Task.Delay(15);Check(Field<PixelRect>(f.Overlay,"_selection")==new PixelRect(15,15,40,30),"Late UIA result rewrote newer selection.");
    });
    private static ClipRay.App.App ExtractionApp(string root,FakeOcr runtime)
    {
        var app=BackgroundTask3LifetimeTests.FakeApp();Set(app,"_translationServices",new TranslationRuntimeServices(()=>throw new Exception("cloud forbidden"),()=>new OcrService(()=>runtime)));
        app.ExtractionDiagnosticsRoot=root;return app;
    }
    private static void ExtractionEntry()=>OnDispatcher(async()=>
    {
        using var f=new OverlayFixture();f.Loaded(true);Check(f.Overlay.IsLoaded,"Current extraction oracle has no commit state.");
        var root=TempRoot();var runtime=new FakeOcr([Word("PRIVATE-url",.91,new(80,80,80,24)),Word("padding",.8,new(0,0,20,20))]);
        var app=ExtractionApp(root,runtime);Task? work=null;
        f.Overlay.TextExtractionRequested+=(_,capture)=>work=app.ExtractTextAsync(f.Overlay,capture);
        Invoke(f.Overlay,"RequestTextExtraction");Check(work is not null,"Real extraction event did not fire.");await work!;
        var trace=File.ReadAllText(Directory.GetFiles(root,"extraction-*.jsonl").Single());
        Check(trace.Contains("request")&&trace.Contains("recognition")&&trace.Contains("selection-filter")&&trace.Contains("RawScore")
            &&trace.Contains("center-outside-selection")&&trace.Contains("selected")&&!trace.Contains("PRIVATE-url"),"Actual extraction stages/filter reasons absent or plaintext leaked.");
        var box=(System.Windows.Controls.TextBox)f.Overlay.FindName("ExtractedTextBox");Check(box.Text.Contains("PRIVATE-url")&&!box.Text.Contains("padding"),"Selection filtering altered extraction text.");
        await BackgroundTask3LifetimeTests.Field<TranslationRuntimeServices>(app,"_translationServices").RetireAsync();
        await BackgroundTask3LifetimeTests.Field<OcrService>(app,"_machineOcr").DisposeAsync();
        f.Loaded(false);
    });
    private static void ExtractionLate()=>OnDispatcher(async()=>
    {
        using var f=new OverlayFixture();f.Loaded(true);Check(f.Overlay.IsLoaded,"Late extraction oracle is vacuous.");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var held=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime=new FakeOcr([Word("late",.9)]){Hold=async _=>{entered.TrySetResult();await held.Task;}};var app=ExtractionApp(TempRoot(),runtime);Task? work=null;TranslationCapture? input=null;
        f.Overlay.TextExtractionRequested+=(_,capture)=>{input=capture;work=app.ExtractTextAsync(f.Overlay,capture);};
        Invoke(f.Overlay,"RequestTextExtraction");await entered.Task;Invoke(f.Overlay,"SetSelection",new PixelRect(20,20,40,30));
        Check(input!.Token.IsCancellationRequested,"Geometry failed to cancel actual extraction request.");input.Bitmap.GetPixel(0,0);
        held.SetResult();await work!;Check(runtime.PixelAfterHold is not null,"Uncooperative OCR lost its owned bitmap before physical return.");
        var box=(System.Windows.Controls.TextBox)f.Overlay.FindName("ExtractedTextBox");Check(box.Text.Length==0,"Late extraction bypassed selection version guard.");
        await BackgroundTask3LifetimeTests.Field<TranslationRuntimeServices>(app,"_translationServices").RetireAsync();await BackgroundTask3LifetimeTests.Field<OcrService>(app,"_machineOcr").DisposeAsync();
        f.Loaded(false);
    });
    private static void DiagnosticBudgets()
    {
        var root=TempRoot();var budget=new DiagnosticBudget(512,700,1000,100);var store=new LocalDiagnosticSession(root,budget);var path=Path.Combine(root,"events.jsonl");
        for(var i=0;i<100;i++)store.Json(path,new {kind="event",index=i,value=new string('a',100)},includeContent:true);
        Check(store.Truncated&&new FileInfo(path).Length<=700,"Per-run budget unbounded.");
        Check(Directory.GetFiles(root,"diagnostic-truncated-*.json").Length==1,"Run budget did not retain a truncation marker.");
        var other=new LocalDiagnosticSession(root,budget);for(var i=0;i<100;i++)other.Json(Path.Combine(root,"peer.jsonl"),new {kind="event",index=i});
        Check(Directory.GetFiles(root).Sum(p=>new FileInfo(p).Length)<=1000&&other.Truncated,"Shared total budget unbounded.");
        var oldRoot=TempRoot();File.WriteAllBytes(Path.Combine(oldRoot,"old.jsonl"),new byte[980]);var existing=new LocalDiagnosticSession(oldRoot,budget);
        Check(!existing.Json(Path.Combine(oldRoot,"new.jsonl"),new { kind="event",count=12 })&&File.ReadAllBytes(Path.Combine(oldRoot,"old.jsonl")).Length==980,"Existing files ignored/cleared by total budget.");
        Check(!store.Image(Path.Combine(root,"large.png"),s=>s.Write(new byte[101])),"Image encoder exceeded bounded buffer.");
        var entryRoot=TempRoot();var entryStore=new LocalDiagnosticSession(entryRoot,budget);var entryPath=Path.Combine(entryRoot,"oversized.jsonl");
        entryStore.Json(entryPath,new { samples=Enumerable.Repeat(new string('a',100),256).ToArray() },includeContent:true);
        Check(entryStore.Truncated&&File.ReadAllText(entryPath).Contains("entry-budget")&&new FileInfo(entryPath).Length<=512,"Entry budget marker missing.");
        var collectionRoot=TempRoot();var collection=new LocalDiagnosticSession(collectionRoot);
        collection.Json(Path.Combine(collectionRoot,"collection.jsonl"),new { values=Enumerable.Range(0,300).ToArray() });
        Check(collection.Truncated&&File.ReadAllText(Path.Combine(collectionRoot,"collection.jsonl")).Contains("original_count"),"Collection projection silently truncated evidence.");
    }
    private static void DiagnosticWriters()
    {
        var root=TempRoot();LocalDiagnosticSession.RegisterRoot(root);
        var diag=new LongCaptureRunDiagnostics("fixture","version",root);diag.RecordEvent("sample",new { OcrText="PRIVATE-URL" });LongCaptureRunDiagnosticStore.Write(root,diag);
        using(var live=new LiveTranslationAcceptanceTrace(root,true)) live.Record("sample",data:new {Text="PRIVATE-URL",width=23});
        var prior=MediaDeliveryTrace.RootOverride;var sink=MediaDeliveryTrace.SinkOverride;
        try{MediaDeliveryTrace.RootOverride=root;MediaDeliveryTrace.SinkOverride=null;MediaDeliveryTrace.Record("sample","fixture",new { OcrText="PRIVATE-URL",width=42 });}
        finally{MediaDeliveryTrace.RootOverride=prior;MediaDeliveryTrace.SinkOverride=sink;}
        RecordingBorderProbeStore.Write(root,RecordingBorderDiagnostics.NotStarted);
        var all=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>p.EndsWith("json")||p.EndsWith("jsonl")).Select(File.ReadAllText);
        Check(all.All(t=>!t.Contains("PRIVATE-URL"))&&Directory.GetFiles(root,"media-delivery*.jsonl").Length==1,"Sibling writer leaked content or fake root did not precede Record.");
    }
    private static void DiagnosticOptin()
    {
        var root=TempRoot();using var trace=ScreenshotTranslationEvidence.StartForTest(root,Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow);
        trace.Record("sample",new { Text="explicit-content" });using var bitmap=new Bitmap(2,2);trace.SaveSource(bitmap);
        Check(File.ReadAllText(Path.Combine(trace.DirectoryPath,"events.jsonl")).Contains("explicit-content")&&File.Exists(Path.Combine(trace.DirectoryPath,"source.png")),"Explicit test/image content mode no longer opt-in usable.");
    }
    private static void DiagnosticFailure()
    {
        var root=TempRoot();var blocker=Path.Combine(root,"file");File.WriteAllText(blocker,"keep");
        var store=new LocalDiagnosticSession(blocker);Check(!store.Json(Path.Combine(blocker,"event"),new {kind="sample"})&&store.Truncated,"Best-effort invalid root not isolated.");
        var old=MediaDeliveryTrace.SinkOverride;var before=MediaDeliveryTrace.WriteFailures;
        try{MediaDeliveryTrace.SinkOverride=(_,_,_)=>throw new IOException("fake");MediaDeliveryTrace.Record("fake",null,null);}
        finally{MediaDeliveryTrace.SinkOverride=old;}Check(MediaDeliveryTrace.WriteFailures==before+1,"Trace failure escaped media writer.");
    }
    private sealed class FakeClock:TimeProvider
    {
        private DateTimeOffset _now=DateTimeOffset.UnixEpoch;private readonly List<ClockTimer> _timers=[];
        public override DateTimeOffset GetUtcNow()=>_now;
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {var timer=new ClockTimer(callback,state);_timers.Add(timer);return timer;}
        internal void Advance(TimeSpan span){_now+=span;foreach(var timer in _timers.ToArray())timer.Fire();}
        private sealed class ClockTimer(TimerCallback callback,object? state):ITimer
        {private bool _disposed;internal void Fire(){if(!_disposed)callback(state);}public bool Change(TimeSpan dueTime,TimeSpan period)=>!_disposed;
         public void Dispose()=>_disposed=true;public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}}
    }
}
