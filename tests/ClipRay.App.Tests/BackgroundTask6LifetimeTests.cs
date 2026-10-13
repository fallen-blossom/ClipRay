using System.Drawing;
using System.IO;
using ClipRay.App.Capture;
using ClipRay.App.Scrolling;
using ClipRay.App.Settings;
using ClipRay.App.Infrastructure;

internal static partial class BackgroundTask6Tests
{
    private static void OutputSaveExitOwnership()
    {
        foreach(var accepted in new[]{false,true})
        {
            using var temp=new Temp();var app=NewOutputApp(temp.Root);var settings=BackgroundTask3LifetimeTests.Field<AppSettings>(app,"_settings");
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var copies=0;var feedback=0;var shutdown=0;
            var output=new ImageOutputService(settings.ScreenshotDirectory,true,clipboard:_=>{OnFixtureUiThread();copies++;});
            output.BeforeSaveOverride=bitmap=>{Check(bitmap.GetPixel(2,2)==PatternPixel(2,2),"Held save lost its owned input.");entered.TrySetResult();release.Task.GetAwaiter().GetResult();};
            BackgroundTask3LifetimeTests.Set(app,"_outputService",output);app.ImageFeedbackOverride=(_,_)=>{OnFixtureUiThread();feedback++;};
            var image=Pattern(24,24,0);var pending=app.RunQuickCaptureAsync(()=>Task.FromResult(new CapturedFrame(image,new(0,0,24,24))),_=>[new(0,0,24,24)]);
            try
            {
                Complete(entered.Task);
                Check(!Complete(app.RequestManagedShutdownAsync(()=>Task.FromResult(accepted),()=>shutdown++,TimeSpan.FromMilliseconds(100))),"Held save allowed final shutdown.");
                Check(shutdown==0&&!pending.IsCompleted&&image.GetPixel(2,2)==PatternPixel(2,2),"Physical save owner was released before its worker finished.");
                if(accepted)
                {
                    var captures=0;
                    Check(Complete(app.RunQuickCaptureAsync(()=>{captures++;throw new Exception("No new capture");},_=>[])) is null&&captures==0,"Accepted exit admitted new image work.");
                    Check(!Complete(app.DrainTranslationAsync(TimeSpan.FromMilliseconds(50))).Completed,"Logical exit budget was mistaken for physical drain.");
                }
            }
            finally{release.TrySetResult();Complete(pending);}
            var saved=pending.Result;Check(saved is not null&&File.Exists(saved.Path),"Accepted exit lost an admitted save's committed file.");
            using(var actual=new Bitmap(saved!.Path))AssertPattern(actual,24,24);
            Check(copies==(accepted?0:1)&&feedback==(accepted?0:1),"Late copy/feedback escaped exit guard or cancelled exit broke normal delivery.");
            var disposed=false;try{image.GetPixel(0,0);}catch(ArgumentException){disposed=true;}Check(disposed,"Save bitmap stayed owned after physical completion.");
            if(accepted)
                Check(Complete(app.RequestManagedShutdownAsync(finalShutdown:()=>shutdown++))&&shutdown==1,"Completed image owner did not drain on exit retry.");
            else
            {
                using var retry=Pattern(24,24,0);var frame=new CapturedFrame((Bitmap)retry.Clone(),new(0,0,24,24));
                Check(Complete(app.RunQuickCaptureAsync(()=>Task.FromResult(frame),_=>[])) is not null,"Cancelled exit closed image admission.");
            }
        }
        foreach(var failure in new[]{"save","copy"})
        {
            using var temp=new Temp();var app=NewOutputApp(temp.Root);var root=Path.Combine(temp.Root,"output");var shutdown=0;
            if(failure=="save")File.WriteAllText(root,"blocked");
            BackgroundTask3LifetimeTests.Set(app,"_outputService",new ImageOutputService(root,true,clipboard:_=>throw new IOException("copy")));
            var saved=Complete(app.RunQuickCaptureAsync(()=>Task.FromResult(new CapturedFrame(Pattern(24,24,0),new(0,0,24,24))),_=>[]));
            Check((saved is null)==(failure=="save"),"Business failure was classified incorrectly.");
            Check(Complete(app.RequestManagedShutdownAsync(finalShutdown:()=>shutdown++))&&shutdown==1,"Finished save/copy business failure became a permanent physical retirement failure.");
        }
    }

    private static void RecoveryExitOwnership()
    {
        foreach(var phase in new[]{"compose","save","confirm"})
        foreach(var accepted in new[]{false,true})
        {
            using var temp=new Temp();var app=NewOutputApp(temp.Root);var paths=BackgroundTask3LifetimeTests.Field<ApplicationPathSet>(app,"_paths");var settings=BackgroundTask3LifetimeTests.Field<AppSettings>(app,"_settings");
            var directory=RunPieces(paths.LongCaptureTempRoot);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var confirmRelease=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var presentation=new FakeRecoveryPresentation();var copies=0;var shutdown=0;
            app.RecoveryPresentationOverride=presentation;
            var output=new ImageOutputService(settings.LongCaptureDirectory,true,OutputArtifactKind.LongCapture,clipboard:_=>{OnFixtureUiThread();copies++;});
            BackgroundTask3LifetimeTests.Set(app,"_longCaptureOutputService",output);
            if(phase=="compose")app.RecoveryCompositeOverride=async actual=>{entered.TrySetResult();await release.Task;return await Task.Run(actual);};
            if(phase=="save")output.BeforeSaveOverride=_=>{entered.TrySetResult();release.Task.GetAwaiter().GetResult();};
            if(phase=="confirm"){presentation.Held=confirmRelease.Task;presentation.OnConfirm=()=>entered.TrySetResult();}
            var pending=app.RecoverLongCaptureAsync(directory);
            try
            {
                Complete(entered.Task);
                var exited=Complete(app.RequestManagedShutdownAsync(()=>Task.FromResult(accepted),()=>shutdown++,TimeSpan.FromMilliseconds(100)));
                Check(exited==(accepted&&phase=="confirm")&&shutdown==(exited?1:0),"Physical recovery work was dropped, or an idle confirmation dialog blocked exit.");
                if(accepted)
                    Check(Complete(app.RecoverLongCaptureAsync(directory)).Blocked,"Accepted exit admitted another recovery.");
                Check(!pending.IsCompleted,"Held recovery returned before its phase completed.");
            }
            finally{release.TrySetResult();confirmRelease.TrySetResult(true);Complete(pending);}
            var outcome=pending.Result;
            if(accepted&&phase!="save")
                Check(outcome.Saved is null&&copies==0&&presentation.Shows==0&&presentation.ConfirmCalls==(phase=="confirm"?1:0),"Late recovery showed UI or began saving after exit.");
            else
            {
                Check(outcome.Saved is not null&&File.Exists(outcome.Saved.Path),"Admitted recovery save lost its PNG.");
                using var image=new Bitmap(outcome.Saved!.Path);AssertPattern(image,24,48);
                Check(copies==(accepted?0:1)&&presentation.Shows==(accepted?0:1),"Recovery delivery ignored accepted/cancelled exit.");
                Check(new ScrollCaptureRecovery(paths.LongCaptureTempRoot).ReadValidated(directory).Status=="delivered","Recovery save success was lost during exit.");
            }
            Check(Directory.EnumerateFiles(directory,"*.png").Count()==3,"Exit erased original recovery pieces.");
            if(accepted&&phase!="confirm")
                Check(Complete(app.RequestManagedShutdownAsync(finalShutdown:()=>shutdown++))&&shutdown==1,"Recovered physical owner did not permit exit after release.");
        }
    }
}
