using System.IO;
using ClipRay.App.Cleanup;
using ClipRay.App.Infrastructure;
using ClipRay.App.Settings;
using ClipRay.App.Scrolling;

internal static partial class BackgroundTask6Tests
{
    internal static void SavedRecoveryCleanupContract()
    {
        using var temp=new Temp();var app=NewOutputApp(temp.Root,false);
        var paths=BackgroundTask3LifetimeTests.Field<ApplicationPathSet>(app,"_paths");
        var directory=RunPieces(paths.LongCaptureTempRoot);
        app.RecoveryPresentationOverride=new FakeRecoveryPresentation();
        var result=Complete(app.RecoverLongCaptureAsync(directory));
        Check(result.Saved is not null&&File.Exists(result.Saved.Path),"Actual recovery did not save its final PNG.");
        var settings=BackgroundTask3LifetimeTests.Field<AppSettings>(app,"_settings");
        var recycle=new BackgroundTask7Tests.Recycle();var cleanup=new CleanupService(recycle:recycle);
        var plan=BackgroundTask7Tests.Plan(settings.LongCaptureDirectory);
        Check(cleanup.Scan(plan).Single().Path==result.Saved!.Path,"Actual recovery final commit lacked completion receipt.");
        var outcomes=cleanup.ExecuteCandidates(plan,cleanup.Scan(plan));
        Check(outcomes.Single().Status=="recycled"&&recycle.Paths.Single()==result.Saved.Path&&File.Exists(Path.Combine(directory,"000.png")),"Recovery output did not reach fake recycle or recovery pieces were touched.");
        Check(Complete(app.RequestManagedShutdownAsync(finalShutdown:()=>{})),"Recovery physical owner did not retire.");
    }
}
