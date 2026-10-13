using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipRay.App;
using ClipRay.App.Capture;
using ClipRay.App.Editing;
using ClipRay.App.Infrastructure;
using ClipRay.App.Overlay;
using ClipRay.App.Scrolling;
using ClipRay.App.Settings;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static partial class BackgroundTask6Tests
{
    private static readonly int FixtureUiThread=Environment.CurrentManagedThreadId;
    private static void OnFixtureUiThread()=>Check(Environment.CurrentManagedThreadId==FixtureUiThread,"Actual delivery callback left its caller dispatcher.");
    private static ClipRay.App.App NewOutputApp(string root,bool copy=true)
    {
        var app=BackgroundTask3LifetimeTests.FakeApp();
        var settings=AppSettings.CreateDefault(root) with{CopyToClipboard=copy};
        BackgroundTask3LifetimeTests.Set(app,"_settings",settings);
        BackgroundTask3LifetimeTests.Set(app,"_paths",new ApplicationPathSet(root,root,Path.Combine(root,"pieces"),root,"fixture",Path.Combine(root,"diagnostics")));
        BackgroundTask3LifetimeTests.Set(app,"_outputService",new ImageOutputService(settings.ScreenshotDirectory,copy,clipboard:_=>{}));
        BackgroundTask3LifetimeTests.Set(app,"_longCaptureOutputService",new ImageOutputService(settings.LongCaptureDirectory,copy,OutputArtifactKind.LongCapture,clipboard:_=>{}));
        app.OutputWarningOverride=_=>OnFixtureUiThread();app.ImageFeedbackOverride=(_,_)=>OnFixtureUiThread();
        return app;
    }
    private static void AppRecoveryOutcomes()
    {
        foreach(var mode in new[]{"cancel","save-fail","copy-fail","preview-fail","status-fail","normal","snapshot"})
        {
            using var temp=new Temp();var app=NewOutputApp(temp.Root);var paths=BackgroundTask3LifetimeTests.Field<ApplicationPathSet>(app,"_paths");
            var directory=RunPieces(paths.LongCaptureTempRoot);var before=Directory.EnumerateFiles(directory).ToDictionary(p=>p,File.ReadAllBytes);
            var presentation=new FakeRecoveryPresentation {Confirm=mode!="cancel",FailPreview=mode=="preview-fail"};app.RecoveryPresentationOverride=presentation;
            var copies=0;var settings=BackgroundTask3LifetimeTests.Field<AppSettings>(app,"_settings");var outputRoot=settings.LongCaptureDirectory;
            if(mode=="save-fail"){outputRoot=Path.Combine(temp.Root,"blocked");File.WriteAllText(outputRoot,"blocked");}
            var output=new ImageOutputService(outputRoot,true,OutputArtifactKind.LongCapture,clipboard:_=>{OnFixtureUiThread();copies++;if(mode=="copy-fail")throw new IOException("copy");});
            BackgroundTask3LifetimeTests.Set(app,"_longCaptureOutputService",output);
            if(mode=="status-fail")app.RecoveryMarkDeliveredOverride=_=>throw new IOException("status");
            if(mode=="snapshot")presentation.OnConfirm=()=>
            {
                BackgroundTask3LifetimeTests.Set(app,"_settings",settings with{CopyToClipboard=false,InterfaceLanguage="en",LongCaptureDirectory=Path.Combine(temp.Root,"different")});
                BackgroundTask3LifetimeTests.Set(app,"_longCaptureOutputService",new ImageOutputService(Path.Combine(temp.Root,"different"),false));
            };
            var result=Complete(app.RecoverLongCaptureAsync(directory));
            Check(before.All(p=>File.Exists(p.Key)),"Recovery removed its original pieces.");
            if(mode is "cancel" or "save-fail")
            {
                Check(result.Saved is null&&copies==0&&presentation.Shows==0&&before.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(p.Key))),"Cancel/save failure changed artifacts or claimed success.");
                Check(mode=="cancel"?result.Cancelled:result.Failure is not null,"Failure/close was not expressed.");
            }
            else
            {
                Check(result.Saved is {} saved&&File.Exists(saved.Path)&&copies==1,"Recovery lost an already saved PNG.");
                using var image=new Bitmap(result.Saved!.Path);AssertPattern(image,24,48);
                Check(result.Saved.ClipboardCopied==(mode!="copy-fail"),"Clipboard outcome was wrong.");
                Check((result.PreviewFailure is not null)==(mode=="preview-fail")&&(result.CleanupFailure is not null)==(mode=="status-fail"),"Preview/status failures were conflated.");
                Check(new ScrollCaptureRecovery(paths.LongCaptureTempRoot).ReadValidated(directory).Status==(mode=="status-fail"?"recoverable":"delivered"),"Delivery state was updated before file commit or lost after preview failure.");
                if(mode=="snapshot")Check(result.Saved.Path.StartsWith(settings.LongCaptureDirectory,StringComparison.OrdinalIgnoreCase)&&presentation.Language==settings.InterfaceLanguage,"Recovery used a changed output/settings snapshot.");
            }
            var shutdown=0;
            Check(Complete(app.RequestManagedShutdownAsync(finalShutdown:()=>shutdown++))&&shutdown==1,"Completed recovery business outcome blocked physical exit.");
        }
        using(var temp=new Temp())
        {
            var app=NewOutputApp(temp.Root);var directory=RunPieces(BackgroundTask3LifetimeTests.Field<ApplicationPathSet>(app,"_paths").LongCaptureTempRoot);
            var hold=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            app.RecoveryPresentationOverride=new FakeRecoveryPresentation {Held=hold.Task,OnConfirm=()=>entered.TrySetResult()};
            var pending=app.RecoverLongCaptureAsync(directory);
            try
            {
                Complete(entered.Task);Check(Complete(app.RecoverLongCaptureAsync(directory)).Blocked,"Two recovery operations raced.");
            }
            finally{hold.TrySetResult(false);Complete(pending);}
            using var cancellation=new CancellationTokenSource();BackgroundTask3LifetimeTests.Set(app,"_longCaptureCancellation",cancellation);
            Check(Complete(app.RecoverLongCaptureAsync(directory)).Blocked,"Ongoing capture was read concurrently.");
            BackgroundTask3LifetimeTests.Set(app,"_longCaptureCancellation",null!);
        }
    }
    private static void ActualTrayRecoveryEntry()
    {
        using var temp=new Temp();var app=NewOutputApp(temp.Root);var paths=BackgroundTask3LifetimeTests.Field<ApplicationPathSet>(app,"_paths");
        var presentation=new FakeRecoveryPresentation {Confirm=false};app.RecoveryPresentationOverride=presentation;
        using var menu=app.CreateTrayMenu();var item=menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(i=>i.Text=="恢复长截图");
        OpenRecovery(item,app);Check(item.DropDownItems.Count==1&&!item.DropDownItems[0].Enabled,"Recovery empty state absent.");
        RunPieces(paths.LongCaptureTempRoot);OpenRecovery(item,app);
        var choice=item.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(i=>i.Enabled);
        Check(choice.Text!.Contains("24×48")&&choice.Text.Contains("3 帧"),"Tray recovery omitted dimensions/frame count.");
        choice.PerformClick();Complete(app.LastRecoveryOperation!);
        Check(presentation.ConfirmCalls==1&&presentation.Shows==0&&new ScrollCaptureRecovery(paths.LongCaptureTempRoot).List().Count==1,"Actual tray click did not reach cancellable recovery or close lost materials.");
    }
    private static void OpenRecovery(System.Windows.Forms.ToolStripMenuItem item,ClipRay.App.App app)
    {
        typeof(System.Windows.Forms.ToolStripDropDownItem).GetMethod("OnDropDownShow",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(item,[EventArgs.Empty]);
        Complete(app.LastRecoveryLoad!);
    }
    private static void ActualQuickCaptureOutcomes()
    {
        foreach(var mode in new[]{"copy","decode","diagnostic","feedback","off","save"})
        {
            using var temp=new Temp();var app=NewOutputApp(temp.Root,mode!="off");var settings=BackgroundTask3LifetimeTests.Field<AppSettings>(app,"_settings");var calls=0;var warnings=0;
            var root=settings.ScreenshotDirectory;
            if(mode=="save"){root=Path.Combine(temp.Root,"blocked");File.WriteAllText(root,"blocked");}
            var output=new ImageOutputService(root,mode!="off",clipboard:source=>{OnFixtureUiThread();calls++;Check(source.IsFrozen&&source.PixelWidth==24,"Copy source lost dimensions/freeze.");if(mode=="copy")throw new IOException("copy");});
            if(mode=="decode")output.ClipboardPreparationOverride=_=>throw new IOException("decode");
            if(mode=="diagnostic")output.SavedDiagnosticOverride=(_,_,_)=>throw new IOException("readback");
            BackgroundTask3LifetimeTests.Set(app,"_outputService",output);app.OutputWarningOverride=_=>warnings++;
            if(mode=="feedback")app.ImageFeedbackOverride=(_,_)=>throw new IOException("feedback");
            ImageDeliveryOutcome? observed=null;app.ImageDeliveryObserver=(d,_)=>observed=d;
            var held=new TaskCompletionSource<CapturedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task=app.RunQuickCaptureAsync(()=>held.Task,_=>[new(0,0,24,24)]);
            BackgroundTask3LifetimeTests.Set(app,"_settings",settings with{ScreenshotDirectory=Path.Combine(temp.Root,"other"),CopyToClipboard=false});
            BackgroundTask3LifetimeTests.Set(app,"_outputService",new ImageOutputService(Path.Combine(temp.Root,"other"),false));
            var bitmap=Pattern(24,24,0);ImageDeliveryOutcome? saved;
            try { held.SetResult(new(bitmap,new(0,0,24,24)));saved=Complete(task); }
            finally { if(!held.Task.IsCompleted)held.TrySetResult(new(bitmap,new(0,0,24,24)));Complete(task); }
            if(mode=="save")Check(saved is null&&observed is null&&warnings>0,"Save failure was hidden.");
            else
            {
                Check(saved is not null&&ReferenceEquals(observed,saved)&&saved.Path.StartsWith(settings.ScreenshotDirectory,StringComparison.OrdinalIgnoreCase),"Actual quick entry lost saved path or snapshot.");
                using var image=new Bitmap(saved!.Path);AssertPattern(image,24,24);
                Check(saved.ClipboardRequested==(mode!="off")&&saved.ClipboardCopied==(mode is not ("off" or "copy" or "decode")),"Actual quick copy outcome changed preferences.");
                Check(calls==(mode is "off" or "decode"?0:1),"Copy policy changed.");
                if(mode=="diagnostic")
                {
                    // The same save path's diagnostic boundary uses actual PNG bytes below.
                    using var source=Pattern(24,24,0);var outcome=Complete(output.SaveWithDeliveryAsync(source,new LongCaptureRunDiagnostics("diagnostic","fixture")));
                    Check(outcome.DiagnosticFailure is not null&&File.Exists(outcome.Path),"Diagnostic readback recategorized a committed PNG.");
                }
            }
            var disposed=false;try{bitmap.GetPixel(0,0);}catch(ArgumentException){disposed=true;}Check(disposed,"Capture bitmap leaked or was never owned.");
        }
    }
    private static void ActualOverlayOwnsAsyncBitmap()
    {
        var application=System.Windows.Application.Current??new System.Windows.Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        application.Resources["AppFont"]=new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        foreach(var mode in new[]{"failure","revision","closed"})
        {
            using var temp=new Temp();using var frame=new CapturedFrame(Pattern(24,24,0),new(0,0,24,24));var windows=new FakeCandidates();
            var overlay=new SelectionOverlayWindow(frame,new PixelRect(0,0,24,24),clipboardTextSink:new FakeText(),windowCandidateSource:windows,uiElementCandidateSource:new FakeElements());
            var held=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);Bitmap? owned=null;var calls=0;
            overlay.CaptureDeliveryAsync=bitmap=>{owned=bitmap;calls++;return held.Task;};
            try
            {
                InvokeOverlay(overlay,"ConfirmSelection");InvokeOverlay(overlay,"ConfirmSelection");
                Check(windows.Calls==1&&calls==1&&owned!.GetPixel(4,9)==PatternPixel(4,9),"Overlay duplicate save or disposed async bitmap.");
                if(mode=="revision")InvokeOverlay(overlay,"InvalidateInputResults");
                if(mode=="closed")overlay.Close();
                held.SetResult(mode!="failure");PumpUntil(()=>!BackgroundTask3LifetimeTests.Field<bool>(overlay,"_captureDeliveryPending"));
                Check(!overlay.IsVisible,"Async save reopened a closed overlay.");
                if(mode!="closed")Check(!overlay.LifetimeToken.IsCancellationRequested,"Failure/new edits were closed by an older save.");
                var disposed=false;try{owned!.GetPixel(0,0);}catch(ArgumentException){disposed=true;}Check(disposed,"Saved snapshot bitmap not disposed after await.");
            }
            finally{held.TrySetResult(false);PumpUntil(()=>!BackgroundTask3LifetimeTests.Field<bool>(overlay,"_captureDeliveryPending"));overlay.Close();}
        }
    }
    private static void ActualRegionDelivery()
    {
        var application=System.Windows.Application.Current??new System.Windows.Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        application.Resources["AppFont"]=new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        foreach(var failSave in new[]{false,true})
        {
            using var temp=new Temp();var app=NewOutputApp(temp.Root);var settings=BackgroundTask3LifetimeTests.Field<AppSettings>(app,"_settings");var copies=0;
            var root=settings.ScreenshotDirectory;
            if(failSave){root=Path.Combine(temp.Root,"blocked");File.WriteAllText(root,"blocked");}
            BackgroundTask3LifetimeTests.Set(app,"_outputService",new ImageOutputService(root,true,clipboard:_=>{OnFixtureUiThread();copies++;throw new IOException("copy");}));
            var overlay=new SelectionOverlayWindow(new CapturedFrame(Pattern(24,24,0),new(0,0,24,24)),new PixelRect(0,0,24,24),clipboardTextSink:new FakeText(),windowCandidateSource:new FakeCandidates(),uiElementCandidateSource:new FakeElements());
            var closed=false;overlay.Closed+=(_,_)=>closed=true;
            overlay.CaptureDeliveryAsync=bitmap=>(Task<bool>)BackgroundTask3LifetimeTests.Invoke(app,"HandleConfirmedCaptureAsync",bitmap,overlay)!;
            try
            {
                InvokeOverlay(overlay,"ConfirmSelection");
                BackgroundTask3LifetimeTests.Set(app,"_outputService",new ImageOutputService(Path.Combine(temp.Root,"other"),false));
                BackgroundTask3LifetimeTests.Set(app,"_settings",settings with{CopyToClipboard=false});
                PumpUntil(()=>!BackgroundTask3LifetimeTests.Field<bool>(overlay,"_captureDeliveryPending"));
                Check(closed==!failSave&&copies==(failSave?0:1),"Actual region entry closed failed edits or mistook copy failure for save failure.");
                if(!failSave)
                {
                    var saved=Directory.EnumerateFiles(settings.ScreenshotDirectory,"*.png",SearchOption.AllDirectories).Single();
                    using var image=new Bitmap(saved);AssertPattern(image,24,24);
                }
            }
            finally{overlay.Close();}
        }
    }
    private static void ActualAppCostFailureKeepsText()
    {
        using var temp=new Temp();var app=NewOutputApp(temp.Root);var machine=new Machine();var warnings=0;
        BackgroundTask3LifetimeTests.Set(app,"_settings",BackgroundTask3LifetimeTests.Field<AppSettings>(app,"_settings") with{CredentialName="fixture",MachineTranslationFallbackEnabled=true});
        BackgroundTask3LifetimeTests.Set(app,"_translationServices",new TranslationRuntimeServices(()=>new Provider(),()=>throw new Exception("No OCR")));
        BackgroundTask3LifetimeTests.Set(app,"_machineTranslationProvider",machine);
        var blocked=Path.Combine(temp.Root,"blocked");File.WriteAllText(blocked,"blocked");
        BackgroundTask3LifetimeTests.Set(app,"_deepSeekCostLedger",new DeepSeekCostLedger(Path.Combine(blocked,"cost.json")));app.OutputWarningOverride=_=>warnings++;
        var request=new TranslationRequest("en","zh-CN","Hello");
        var task=(Task<TranslationResult>)BackgroundTask3LifetimeTests.Invoke(app,"TranslateUsingRouteAsync",request,TranslationRouteKind.DeepSeekFlash,CancellationToken.None)!;
        var result=Complete(task);Check(result is{IsSuccess:true,Text:"你好"}&&machine.Calls==0&&warnings==1,"Actual App ledger callback lost text or triggered fallback.");
    }
    private static T Complete<T>(Task<T> task){Complete((Task)task);return task.GetAwaiter().GetResult();}
    private static void Complete(Task task){PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void PumpUntil(Func<bool> done)
    {
        var deadline=DateTime.UtcNow.AddSeconds(8);
        while(!done())
        {
            if(DateTime.UtcNow>deadline)throw new TimeoutException("Held fake/dispatcher did not finish.");
            var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(1);
        }
    }
    private static void InvokeOverlay(SelectionOverlayWindow overlay,string method)=>typeof(SelectionOverlayWindow).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(overlay,null);
    private sealed class FakeCandidates : IWindowCandidateSource {public int Calls;public IEnumerable<WindowSnapCandidate> Enumerate(){Calls++;return [];}}
    private sealed class FakeElements : IUiElementCandidateSource {public IReadOnlyList<PixelRect> GetHierarchy(System.Drawing.Point point,IntPtr root)=>[];}
    private sealed class FakeText : IClipboardTextSink {public void SetText(string text)=>throw new Exception("No real clipboard");}
    private sealed class FakeRecoveryPresentation : ILongCaptureRecoveryPresentation
    {
        public bool Confirm=true,FailPreview;public int ConfirmCalls,Shows;public string? Language;public Task<bool>? Held;public Action? OnConfirm;
        public Task<bool> ConfirmSaveAsync(ScrollCaptureRecoveryCandidate candidate,BitmapSource preview,string language)
        {OnFixtureUiThread();ConfirmCalls++;Check(preview.IsFrozen&&preview.PixelHeight==48,"Recovery preview lost pixels/geometry.");OnConfirm?.Invoke();return Held??Task.FromResult(Confirm);}
        public void ShowSaved(Bitmap bitmap,BitmapSource preview,ImageDeliveryOutcome saved,ScrollCaptureStopReason reason,string language,Func<bool> copy)
        {OnFixtureUiThread();Shows++;Language=language;if(FailPreview)throw new IOException("preview");Check(File.Exists(saved.Path),"Preview represented a missing committed file.");}
    }
}
