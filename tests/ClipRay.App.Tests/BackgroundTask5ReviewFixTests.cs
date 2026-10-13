using System.Drawing;
using System.IO;
using ClipRay.App.Recording;
using ClipRay.App.Settings;

internal static partial class BackgroundTask5Tests
{
    private static async Task ReviewStartupExit()
    {
        foreach (var waitingForSilentCleanup in new[] { false, true })
        {
            using var held = new ManualResetEventSlim();
            var env = waitingForSilentCleanup
                ? new Environment { HoldFinalize = held, AudioFailure = RecordingStartupStage.SystemAudio }
                : new Environment { HoldStart = held };
            var app = NewApp(env); var restored = 0; var started = 0; var finals = 0;
            var start = app.BeginRecordingAsync(new(new(-100,-30,101,99)), _ => restored++, () => started++);
            Task? physical = null;
            try
            {
                await env.BackendCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await env.Backends[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Check(!await app.RequestManagedShutdownAsync(() => Task.FromResult(true), () => finals++, TimeSpan.FromMilliseconds(25)),
                    "Held startup was reported retired at accepted exit.");
                physical = app.RecordingController.PhysicalCompletion;
                Check(physical is { IsCompleted:false } && env.Backends[0].Releases == 0 && app.RecordingController.IsBusy && finals == 0,
                    "Accepted exit released the physical starting owner.");
            }
            finally
            {
                held.Set();
                await start.WaitAsync(TimeSpan.FromSeconds(3));
                if (physical is not null) await physical.WaitAsync(TimeSpan.FromSeconds(3));
            }
            Check(restored == 0 && started == 0 && env.Prompts == 0,
                "Accepted exit allowed a late restore/started/silent startup UI delivery.");
            Check(!await app.BeginRecordingAsync(new(new(-100,-30,101,99)), _ => restored++, () => started++)
                && restored == 0 && started == 0 && env.Backends.Count == 1 && env.Copies == 0,
                "Retired selection admitted or restored a second startup.");
            Check(!app.RecordingController.IsBusy && env.Backends.All(b => b.Active == 0 && b.Releases == 1 && b.Shutdowns == 1)
                && env.Audios.All(a => a.Disposes == 1) && env.Videos.All(v => v.Disposes == 1), "Starting exit retained fake owners.");
            Check(await app.RequestManagedShutdownAsync(finalShutdown: () => finals++) && finals == 1,
                "Physical startup retirement did not permit the final managed drain.");
        }
    }

    private static async Task ReviewCancelExit()
    {
        using var held = new ManualResetEventSlim(); var env = new Environment { HoldStart = held }; var app = NewApp(env);
        var restored = 0; var started = 0; var finals = 0;
        var start = app.BeginRecordingAsync(new(new(-100,-30,101,99)), _ => restored++, () => started++);
        try
        {
            await env.BackendCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await env.Backends[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            env.Backends[0].FailStart = RecordingStartupStage.VideoEncoder;
            Check(!await app.RequestManagedShutdownAsync(() => Task.FromResult(false), () => finals++) && finals == 0
                && env.Backends[0].Releases == 0, "Cancelled exit retired the active startup.");
        }
        finally { held.Set(); await start.WaitAsync(TimeSpan.FromSeconds(3)); }
        Check(restored == 1 && started == 0 && !app.RecordingController.IsBusy && env.Copies == 0,
            "Cancelled exit suppressed an effective startup failure or copied its output.");
        env.HoldStart = null;
        Check(await Start(app), "Cancelled exit closed future recording admission.");
        await app.RecordingController.StopAsync();
        Check(env.Backends.All(b => b.Active == 0 && b.Releases == 1 && b.Shutdowns == 1), "Cancel control retained an owner.");
    }

    private static async Task ReviewSelectionFeedback()
    {
        foreach (var language in new[] { "zh-CN", "en" })
        foreach (var region in new[] { new Rectangle(-10,0,2000,20), new Rectangle(-100,-30,1,99) })
        {
            var env = new Environment(); var app = NewApp(env); SetLanguage(app, language); string? message = null;
            Check(!await app.BeginRecordingAsync(new(region), value => message = value, () => throw new Exception("Invalid selection started.")),
                "Invalid selection succeeded.");
            Check(message is not null && message.Contains(language == "en" ? "one screen" : "一个屏幕")
                && message.Contains("2x2") && message.Contains(language == "en" ? "right and bottom" : "右侧和下侧"),
                "Known selection failure did not explain single-screen and minimum even crop rules.");
            Check(env.Backends.Count == 0 && !app.RecordingController.IsBusy, "Selection validation started a backend.");
        }
        Check(!RecordingFailureText.Format(new IOException("private arbitrary detail"), null, "en").Contains("private arbitrary detail"),
            "Selection guidance exposed arbitrary exception messages.");
    }

    private static async Task ReviewStartupDelivery()
    {
        foreach (var mode in new[] { "closed", "replaced", "closed-before-silent", "ordinary-stop" })
        {
            using var held = new ManualResetEventSlim();
            var env = mode == "closed-before-silent"
                ? new Environment { HoldFinalize = held, AudioFailure = RecordingStartupStage.SystemAudio }
                : new Environment { HoldStart = held };
            var app = NewApp(env); var selected = new object(); object current = selected; var closed = false;
            var restored = 0; var started = 0;
            var start = app.BeginRecordingAsync(new(new(-100,-30,101,99)), _ => restored++, () => started++,
                () => !closed && ReferenceEquals(selected,current));
            Task? physical = null;
            try
            {
                await env.BackendCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await env.Backends[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                if (mode == "ordinary-stop")
                {
                    app.RecordingController.StopBudget = TimeSpan.FromMilliseconds(25);
                    Check((await app.RecordingController.StopAsync()) is { PhysicalCompleted:false }, "Ordinary stop released held startup.");
                    physical = app.RecordingController.PhysicalCompletion;
                }
                else if (mode == "replaced") current = new object();
                else closed = true;
                Check(env.Backends[0].Releases == 0, "Invalidated UI prematurely released its physical owner.");
            }
            finally
            {
                held.Set(); await start.WaitAsync(TimeSpan.FromSeconds(3));
                if (physical is not null) await physical.WaitAsync(TimeSpan.FromSeconds(3));
            }
            Check(restored == (mode == "ordinary-stop" ? 1 : 0) && started == 0 && env.Prompts == 0 && env.Copies == 0,
                "Closed/replaced UI received startup delivery, or ordinary Stop lost valid recovery.");
            Check(!app.RecordingController.IsBusy && env.Backends.Count == 1 && env.Backends[0].Active == 0
                && env.Backends[0].Releases == 1 && env.Audios.All(a => a.Disposes == 1) && env.Videos.All(v => v.Disposes == 1),
                "UI invalidation retained an owner or admitted another backend.");
        }

        // A valid busy-selection callback can itself close the target/accept exit and throw.
        // The App fallback must recheck ownership rather than invoking it a second time.
        using var busyHeld = new ManualResetEventSlim(); var busyEnv = new Environment { HoldStart = busyHeld }; var busyApp = NewApp(busyEnv);
        var busyStart = Start(busyApp); var busyRestores = 0; var targetCurrent = true;
        try
        {
            await busyEnv.BackendCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await busyEnv.Backends[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(!await busyApp.BeginRecordingAsync(new(new(-100,-30,101,99)), _ =>
            {
                busyRestores++; targetCurrent = false; busyApp.RequestRecordingRetirement();
                throw new IOException("UI closed during restore");
            }, () => throw new Exception("Busy request started."), () => targetCurrent), "Busy request unexpectedly started.");
            Check(busyRestores == 1 && busyEnv.Backends.Count == 1 && busyEnv.Backends[0].Releases == 0,
                "App outer fallback repeated stale UI delivery or released a held owner.");
        }
        finally
        {
            busyHeld.Set(); await busyStart.WaitAsync(TimeSpan.FromSeconds(3));
            if (busyApp.RecordingController.PhysicalCompletion is { } completion) await completion.WaitAsync(TimeSpan.FromSeconds(3));
        }
        Check(!busyApp.RecordingController.IsBusy && busyEnv.Copies == 0 && busyEnv.Backends[0].Active == 0
            && (await busyApp.DrainTranslationAsync(TimeSpan.FromSeconds(2))).Completed, "Fallback fixture retained retirement.");
    }

    private static async Task ReviewSilentFiles()
    {
        foreach (var language in new[] { "zh-CN", "en" })
        foreach (var stage in new[] { RecordingStartupStage.AudioDevice, RecordingStartupStage.AudioEncoder, RecordingStartupStage.SystemAudio })
        {
            var env = new Environment { AudioFailure = stage }; var app = NewApp(env); SetLanguage(app, language);
            Check(!await Start(app) && env.Prompts == 1, "Audio failure did not offer an explicit choice.");
            var paths = env.CreatedPaths.Single(); var message = env.PromptMessages.Single();
            if (stage == RecordingStartupStage.AudioDevice)
                Check(!File.Exists(paths.PartialPath) && !File.Exists(paths.FinalPath)
                    && !message.Contains(paths.PartialPath) && !message.Contains(paths.FinalPath)
                    && message.Contains(language == "en" ? "No file was generated" : "尚未生成文件"),
                    "Unavailable audio device advertised or created a nonexistent recovery file.");
            else
            {
                var expected = stage == RecordingStartupStage.AudioEncoder ? paths.PartialPath : paths.FinalPath;
                Check(message.Contains(expected) && File.ReadAllBytes(expected).SequenceEqual(Backend.Bytes),
                    "Silent prompt omitted or changed the actual existing recording file.");
            }
            Check(env.Copies == 0 && !app.RecordingController.IsBusy, "Cancelled silent choice copied output or retained a released owner.");
        }
    }

    private static void SetLanguage(ClipRay.App.App app, string language)
    {
        var settings = BackgroundTask3LifetimeTests.Field<AppSettings>(app, "_settings");
        BackgroundTask3LifetimeTests.Set(app, "_settings", settings with { InterfaceLanguage = language });
    }
}
