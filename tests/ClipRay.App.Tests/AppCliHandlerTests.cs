using System.IO;
using System.Text.Json;
using ClipRay.App.Infrastructure;
using ClipRay.App.Recording;
using ClipRay.App.Settings;

internal static class AppCliHandlerTests
{
    public static void Run() => BackgroundTask5Tests.CliExerciseAsync().GetAwaiter().GetResult();
}

internal static partial class BackgroundTask5Tests
{
    internal static async Task CliExerciseAsync()
    {
        var root = Path.Combine(Path.GetTempPath(),"clipray-cli-handler-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var initial = AppSettings.CreateDefault(root) with { Provider="DeepSeek", HasExplicitProviderChoice=true, MachineTranslationFallbackEnabled=false, CopyToClipboard=true };
        var store = new SettingsStore(Path.Combine(root,"settings.json"),root);
        store.Save(initial);
        var app = BackgroundTask3LifetimeTests.FakeApp();
        BackgroundTask3LifetimeTests.Set(app,"_settings",initial);
        BackgroundTask3LifetimeTests.Set(app,"_settingsStore",store);
        using var finalizeGate = new ManualResetEventSlim(true);
        var environment = new Environment { HoldFinalize=finalizeGate };
        app.RecordingEnvironmentOverride = environment;
        app.RecordingController.StopBudget=TimeSpan.FromMilliseconds(25);
        try
        {
        AppCliRequest Request(string command) => new(1,Guid.NewGuid(),System.Environment.ProcessPath!,command);
        var patch = await app.HandleCliAsync(Request("recording-settings") with
        { FramesPerSecond=24, Quality="High", IncludeCursor=false, IncludeSystemAudio=false });
        Check(patch.Success && patch.Status=="settings-saved","CLI recording preferences were not committed.");
        var saved = store.Load();
        Check(saved.RecordingFramesPerSecond==24 && saved.RecordingQuality==RecordingQuality.High
            && !saved.RecordingIncludeCursor && !saved.RecordingIncludeSystemAudio,"CLI preference roundtrip lost a field.");
        Check(saved.Provider==initial.Provider && saved.ScreenshotDirectory==initial.ScreenshotDirectory
            && saved.CopyToClipboard==initial.CopyToClipboard,"CLI recording preferences changed unrelated settings.");
        store.Save(saved with { RecordingFramesPerSecond=30 });
        var drift = await app.HandleCliAsync(Request("recording-settings") with { IncludeSystemAudio=true });
        Check(!drift.Success && drift.Status=="settings-changed-on-disk" && store.Load().RecordingFramesPerSecond==30,
            "A CLI update overwrote settings changed by another owner.");
        store.Save(saved);
        var startRequest = Request("recording-start") with { Region=[-100,-30,100,98],NoCopy=true };
        var start = await app.HandleCliAsync(startRequest);
        Check(start.Success && app.RecordingController.ActiveRunId is not null,"CLI did not start the production recording controller.");
        var firstId = app.RecordingController.ActiveRunId!.Value;
        Check(environment.LastInteractive==false && environment.SilentAttempts.SequenceEqual([true])
            && environment.SelectedOptions==saved.CreateRecordingVideoOptions(),"CLI lost saved options or opened interactive recording controls.");
        var mismatch = await app.HandleCliAsync(Request("recording-stop") with { RunId=Guid.NewGuid() });
        Check(!mismatch.Success && app.RecordingController.ActiveRunId==firstId,"An unrelated stop ID stopped this recording.");
        finalizeGate.Reset();
        var pending = await app.HandleCliAsync(Request("recording-stop") with { RunId=firstId });
        var pendingData = JsonSerializer.SerializeToElement(pending.Data);
        Check(pending.Status=="saving" && pendingData.GetProperty("State").GetString()=="saving"
            && pendingData.GetProperty("LastResult").ValueKind==JsonValueKind.Null,"A first pending save claimed completed resources.");
        Check(pendingData.GetProperty("ActivePartialPath").GetString()==app.RecordingController.ActivePaths?.PartialPath
            && pendingData.GetProperty("ActivePartialPath").ValueKind==JsonValueKind.String,"Pending status lost the active recovery path.");
        var firstCompletion = app.RecordingController.PhysicalCompletion!;
        finalizeGate.Set();
        await firstCompletion.WaitAsync(TimeSpan.FromSeconds(3));
        var stopped = await app.HandleCliAsync(Request("recording-stop") with { RunId=firstId });
        Check(stopped.Success && app.RecordingController.LastReport is { Saved:true,ResourcesReleased:true }
            && !environment.Events.Contains("preview") && environment.Copies==0 && environment.Prompts==0,
            "Headless CLI opened a preview, copied despite --no-copy, or returned before saving.");
        var state = await app.HandleCliAsync(Request("status"));
        var data = JsonSerializer.SerializeToElement(state.Data);
        Check(data.GetProperty("Recording").GetProperty("State").GetString()=="idle"
            && data.GetProperty("Recording").GetProperty("LastResult").GetProperty("Saved").GetBoolean(),"CLI status omitted the actual completed result.");
        Check((await app.HandleCliAsync(startRequest with { RequestId=Guid.NewGuid() })).Success,"A second recording could not start.");
        var secondId = app.RecordingController.ActiveRunId!.Value;
        Check(!(await app.HandleCliAsync(Request("recording-stop") with { RunId=firstId })).Success
            && app.RecordingController.ActiveRunId==secondId,"A stale stop ID stopped a newer run.");
        finalizeGate.Reset();
        var secondPending = await app.HandleCliAsync(Request("recording-stop") with { RunId=secondId });
        var secondData = JsonSerializer.SerializeToElement(secondPending.Data);
        Check(secondPending.Status=="saving" && secondData.GetProperty("ActiveRunId").GetGuid()==secondId
            && secondData.GetProperty("LastResult").GetProperty("RunId").GetGuid()==firstId,
            "A previous completed result was confused with the currently saving run.");
        var secondCompletion = app.RecordingController.PhysicalCompletion!;
        finalizeGate.Set();
        await secondCompletion.WaitAsync(TimeSpan.FromSeconds(3));

        Check((await app.HandleCliAsync(Request("recording-settings") with { IncludeSystemAudio=true })).Success,"Audio preference could not be enabled.");
        environment.AudioFailure=RecordingStartupStage.AudioDevice;
        var failed = await app.HandleCliAsync(startRequest with { RequestId=Guid.NewGuid() });
        Check(!failed.Success && environment.Prompts==0,"Background audio failure opened a confirmation prompt.");
        }
        finally { finalizeGate.Set(); await app.RecordingController.StopAsync(); }
    }

}
