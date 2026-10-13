using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using ClipRay.App.Recording;
using ClipRay.App.Settings;

internal static partial class BackgroundTask5Tests
{
    private static (string,Action)[] MoreCases() => [
        ("audio-capacity", () => AudioCapacity().GetAwaiter().GetResult()),
        ("video-leases", () => VideoLeases().GetAwaiter().GetResult()),
        ("encoder-held-write", () => EncoderHeld(false).GetAwaiter().GetResult()),
        ("encoder-held-finalize", () => EncoderHeld(true).GetAwaiter().GetResult()),
        ("commit-and-cleanup", () => CommitCleanup().GetAwaiter().GetResult()),
        ("commit-conflict", () => CommitConflict().GetAwaiter().GetResult()),
        ("app-pending", () => AppPending(false).GetAwaiter().GetResult()),
        ("app-closed-preview", () => AppPending(true).GetAwaiter().GetResult()),
        ("app-first-fault", () => AppFirstFault().GetAwaiter().GetResult()),
        ("app-delivery", () => AppDelivery().GetAwaiter().GetResult()),
        ("app-preparation", () => AppPreparation().GetAwaiter().GetResult()),
        ("app-silent-choice", () => AppSilent().GetAwaiter().GetResult()),
        ("app-held-start", () => AppHeldStart().GetAwaiter().GetResult()),
        ("app-managed-exit", () => AppExit().GetAwaiter().GetResult()),
        ("factory-construction", () => Construction().GetAwaiter().GetResult()),
        ("production-write-fault", () => ProductionWriteFault().GetAwaiter().GetResult()),
        ("readiness-combinations", () => ReadinessCombinations().GetAwaiter().GetResult()),
        ("held-audio-start", () => HeldAudioStart().GetAwaiter().GetResult()),
        ("finalize-failure", () => FinalizeFailure().GetAwaiter().GetResult()),
        ("startup-reentrant-stop", () => StartupReentrantStop().GetAwaiter().GetResult()),
        ("clock-pacer", ClockPacer),
        ("review-startup-exit", () => ReviewStartupExit().GetAwaiter().GetResult()),
        ("review-cancel-exit", () => ReviewCancelExit().GetAwaiter().GetResult()),
        ("review-startup-delivery", () => ReviewStartupDelivery().GetAwaiter().GetResult()),
        ("review-selection-feedback", () => ReviewSelectionFeedback().GetAwaiter().GetResult()),
        ("review-silent-files", () => ReviewSilentFiles().GetAwaiter().GetResult())
    ];
    private static async Task AudioCapacity()
    {
        var entered=Signal();var release=Signal();var faults=new RecordingFaultSignal();var faultCount=0;
        faults.Raised += _ => faultCount++;
        var pump=new RecordingAudioPump(new(48000,2,16,false),async sample =>
        { entered.TrySetResult();await release.Task;Check(sample.PcmBytes[0]==7,"Held audio changed."); },faults,16,2);
        try
        {
            Check(pump.TryPost([7,0,0,0,0,0,0,0],1),"First audio rejected.");await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(pump.TryPost([7,0,0,0,0,0,0,0],2),"Second audio rejected.");
            for(var i=0;i<1000;i++) Check(!pump.TryPost([7,0,0,0],3+i),"Overflow admitted audio.");
            Check(pump.HeldBytes==16&&pump.HeldSamples==2&&faultCount==1,"Audio bound excludes in-flight or fault repeats.");
        }
        finally { release.TrySetResult();await pump.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
        Check(pump.HeldBytes==0&&pump.HeldSamples==0,"Audio leases retained.");
        var bad=new RecordingAudioPump(new(48000,2,16,false),_=>ValueTask.CompletedTask,new());
        Check(!bad.TryPost([1,2,3],0),"Misaligned audio accepted.");await bad.CompleteAsync();
    }
    private static async Task VideoLeases()
    {
        var pool=new RecordingPixelBuffers(16);var entered=Signal();var release=Signal();var written=new List<long>();var returned=0;
        var pump=new RecordingVideoPump(async sample => { written.Add(sample.Timestamp100Nanoseconds);if(written.Count==1){entered.TrySetResult();await release.Task;Check(sample.BgraPixels[0]==1,"Held pixels recycled early.");}});
        VideoRecordingSample Sample(int i){var bytes=pool.Rent()!;Check(bytes is not null,"Pool exceeded four buffers.");bytes![0]=(byte)i;return new VideoRecordingSample(bytes,2,2,i,1).Own(()=>{Interlocked.Increment(ref returned);pool.Return(bytes);});}
        try
        {
            pump.TryPost(Sample(1));await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            for(var i=2;i<=80;i++) Check(pump.TryPost(Sample(i)),"Newest frame unexpectedly rejected.");
            Check(pump.PendingCount==2&&pool.Held==3,"Video is not bounded to encoder + newest two.");
            pump.RequestStop();Check(!pump.TryPost(Sample(81)),"Stopped queue accepted a frame.");
            Check(pool.Held==3,"Rejected lease retained.");
        }
        finally {release.TrySetResult();await pump.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(2));}
        Check(written.SequenceEqual([1L,79L,80L])&&returned==81&&pool.Held==0,"Dropped/final leases or newest semantics incorrect.");
        var faults=new RecordingFaultSignal();var bad=new RecordingVideoPump(_=>ValueTask.FromException(new IOException("write")),faults);
        bad.TryPost(Sample(82));try{await bad.CompleteAsync();}catch(IOException){}
        Check(faults.First?.Stage=="video-write"&&pool.Held==0,"Video consumer failure not reported/released.");
    }
    private static async Task EncoderHeld(bool finalize)
    {
        var paths=NewPaths();var backend=new Backend(paths);using var held=new ManualResetEventSlim();backend.Hold=held;
        backend.HoldFinalize=finalize;backend.HoldWrite=!finalize;
        var encoder=new MediaFoundationRecorder(paths,backend,new());await encoder.StartAsync(default);
        Task write=Task.CompletedTask;
        if(!finalize)write=encoder.WriteVideoAsync(new(new byte[16],2,2,0,1)).AsTask();
        var stop=finalize?encoder.StopAsync():Task.CompletedTask;
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if(!finalize)stop=encoder.StopAsync();var second=encoder.StopAsync();var dispose=encoder.DisposeAsync().AsTask();
        try{Check(!stop.IsCompleted&&!second.IsCompleted&&!dispose.IsCompleted&&backend.Releases==0&&!File.Exists(paths.FinalPath),"Held native owner was finalized/released early.");}
        finally{held.Set();await Task.WhenAll(write,stop,second,dispose).WaitAsync(TimeSpan.FromSeconds(3));}
        Check(encoder.Report is {Saved:true,Encoded:true,ResourcesReleased:true}&&backend.Finalizes==1&&backend.Releases==1&&backend.Active==0,
            "Physical encoder completion did not commit/release exactly once.");
        Check(File.ReadAllBytes(paths.FinalPath).SequenceEqual(Backend.Bytes),"Committed bytes differ.");
    }
    private static async Task CommitCleanup()
    {
        foreach(var stage in new[]{"writer","mf"})
        {
            var paths=NewPaths();var backend=new Backend(paths){FailRelease=stage=="writer",FailShutdown=stage=="mf"};
            var encoder=new MediaFoundationRecorder(paths,backend,new());await encoder.StartAsync(default);await encoder.DisposeAsync();
            Check(encoder.Report is {Saved:true,Encoded:true,ResourcesReleased:false}&&backend.Releases==1&&backend.Shutdowns==1
                &&File.ReadAllBytes(paths.FinalPath).SequenceEqual(Backend.Bytes),"Cleanup error erased commit or skipped a release.");
        }
    }
    private static async Task CommitConflict()
    {
        var paths=NewPaths();File.WriteAllBytes(paths.FinalPath,[9,8,7]);var backend=new Backend(paths);var encoder=new MediaFoundationRecorder(paths,backend,new());
        await encoder.StartAsync(default);await encoder.StopAsync();
        Check(encoder.Report is {Encoded:true,Saved:false}&&encoder.Report.FirstFault?.Stage=="file-commit"
            &&File.ReadAllBytes(paths.PartialPath).SequenceEqual(Backend.Bytes)&&File.ReadAllBytes(paths.FinalPath).SequenceEqual(new byte[]{9,8,7}),"Commit conflict overwrote/lost bytes.");
    }
    private static async Task AppPending(bool close)
    {
        using var held=new ManualResetEventSlim();var env=new Environment {HoldFinalize=held};var app=NewApp(env);app.RecordingController.StopBudget=TimeSpan.FromMilliseconds(40);
        Check(await Start(app),"Actual App start failed.");var observe=app.RecordingController.StopAsync();await env.Backends[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task? physical=app.RecordingController.PhysicalCompletion;
        try
        {
            var pending=await observe;Check(pending is {PhysicalCompleted:false,Saved:false}&&env.Preview.Creates==1&&env.Feedback.Disposed==1&&env.Backends[0].Releases==0,"Timeout pretended save/release or feedback remained.");
            Check(!await Start(app)&&env.Backends.Count==1,"Retiring owner admitted second recording.");
            env.Preview.IsClosed=close;Check(env.Preview.Attaches==0&&env.Preview.PendingCount==1,"Pending preview was failed/completed too early.");
        }
        finally{held.Set();if(physical is not null)await physical.WaitAsync(TimeSpan.FromSeconds(3));}
        Check(app.RecordingController.LastReport is {Saved:true}&&!app.RecordingController.IsBusy&&env.Preview.Creates==1&&env.Preview.Attaches==(close?0:1),"Late completion reopened preview or lost save.");
        var events=env.Events.ToArray();Check(Array.IndexOf(events,"feedback-close")<Array.IndexOf(events,"preview")&&Array.IndexOf(events,"preview")<Array.IndexOf(events,"finalize"),"Pending preview order regressed.");
    }
    private static async Task AppFirstFault()
    {
        foreach(var stage in new[]{"graphics-capture","video-write","audio-device","audio-write","video-admission"})
        {
            var env=new Environment();var app=NewApp(env);Check(await Start(app),"Fault fixture failed start.");
            var first=new IOException(stage);env.Faults!.TrySet(stage,first);env.Faults.TrySet("later",new Exception("later"));
            await app.RecordingController.StopAsync();
            Check(env.Videos[0].Closed&&env.Audios[0].Closed&&app.RecordingController.LastReport?.FirstFault?.Error==first
                &&env.Feedback.Disposed==1,"First fault not propagated through actual App or admissions stayed open.");
        }
    }
    private static async Task AppDelivery()
    {
        foreach(var mode in new[]{"off","copy-fail","copy-throw","preview-create","preview-attach","cleanup"})
        {
            var env=new Environment {CopyFailure=mode=="copy-fail",CopyThrow=mode=="copy-throw",PreviewCreateFailure=mode=="preview-create",
                PreviewAttachFailure=mode=="preview-attach",CleanupFailure=mode=="cleanup"};var app=NewApp(env,mode!="off");
            Check(await Start(app),"Delivery fixture failed start.");var result=await app.RecordingController.StopAsync();
            Check(result is {Saved:true}&&File.ReadAllBytes(result.Paths!.FinalPath).SequenceEqual(Backend.Bytes)&&env.Copies==(mode=="off"?0:1),"Delivery changed saved result or ignored copy preference.");
            Check(app.RecordingController.IsBusy==(mode=="cleanup"),"Unreleased owner was forgotten.");
        }
    }
    private static async Task AppPreparation()
    {
        foreach(var mode in new[]{"monitor","paths","feedback","cross","ready"})
        {
            var env=new Environment {FailAt=mode};var app=NewApp(env);var restored=0;
            var region=mode=="cross"?new Rectangle(-10,0,2000,20):new Rectangle(-100,-30,101,99);
            Check(!await app.BeginRecordingAsync(new(region),_=>restored++,()=>{}),"Unsafe preparation succeeded.");
            Check(restored==1&&env.Backends.Count==0&&!app.RecordingController.IsBusy,"Pre-try failure escaped or started a backend.");
        }
        var valid=new Environment();var actual=NewApp(valid);Check(await Start(actual),"Valid geometry rejected.");
        Check(valid.Region==new Rectangle(-100,-30,100,98)&&RecordingGeometry.EffectiveRegion(valid.Mapping)==valid.Region,"Border/control/encoder use different effective rectangles.");
        await actual.RecordingController.StopAsync();
    }
    private static async Task AppSilent()
    {
        foreach(var stage in new[]{RecordingStartupStage.AudioDevice,RecordingStartupStage.AudioEncoder,RecordingStartupStage.SystemAudio})
        foreach(var accept in new[]{false,true})
        {
            var env=new Environment {AudioFailure=stage,SilentAccepted=accept};var app=NewApp(env);var started=await Start(app);
            Check(started==accept&&env.Prompts==1,"Audio unavailability was silently changed or choice ignored.");
            if(accept){Check(env.SilentAttempts.SequenceEqual(new[]{false,true})&&env.Videos.Last().Starts==1&&env.Audios.All(a=>a.Starts<=1),"Silent fallback added an audio stream or captured before consent.");await app.RecordingController.StopAsync();}
            else Check(env.Videos.All(v=>v.Starts==0)&&!app.RecordingController.IsBusy,"Cancel started capture or retained finished owner.");
        }
    }
    private static async Task AppHeldStart()
    {
        using var held=new ManualResetEventSlim();var env=new Environment {HoldStart=held};var app=NewApp(env);app.RecordingController.StopBudget=TimeSpan.FromMilliseconds(30);
        var start=Start(app);await env.BackendCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));await env.Backends[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stop=app.RecordingController.StopAsync();var physical=app.RecordingController.PhysicalCompletion;
        try{Check((await stop) is {PhysicalCompleted:false}&&!await Start(app)&&env.Backends.Count==1&&env.Backends[0].Releases==0,"Held startup admitted overlap or released active backend.");}
        finally{held.Set();await start.WaitAsync(TimeSpan.FromSeconds(3));if(physical is not null)await physical.WaitAsync(TimeSpan.FromSeconds(3));}
        Check(env.Videos.All(v=>v.Starts==0)&&env.Audios.All(a=>a.Starts==0)&&env.Backends[0].Active==0&&!app.RecordingController.IsBusy,"Late start revived capture/audio.");
    }
    private static async Task AppExit()
    {
        using var held=new ManualResetEventSlim();var env=new Environment{HoldFinalize=held};var app=NewApp(env);Check(await Start(app),"Exit fixture failed start.");var finals=0;
        Check(!await app.RequestManagedShutdownAsync(()=>Task.FromResult(false),()=>finals++),"Cancelled exit completed.");
        Check(!env.Videos[0].Closed&&env.Backends[0].Finalizes==0,"Cancelled draft stopped recording.");
        try
        {
            Check(!await app.RequestManagedShutdownAsync(()=>Task.FromResult(true),()=>finals++,TimeSpan.FromMilliseconds(30))&&finals==0
                &&app.LastExitWarning is not null&&app.RecordingController.IsBusy,"Managed exit ignored held recording ownership.");
        }
        finally{held.Set();if(app.RecordingController.PhysicalCompletion is {} task)await task.WaitAsync(TimeSpan.FromSeconds(3));}
        Check(await app.RequestManagedShutdownAsync(()=>Task.FromResult(true),()=>finals++,TimeSpan.FromSeconds(2))&&finals==1,"Retired recording blocked final managed exit.");
    }
    private static async Task Construction()
    {
        foreach(var failAt in new[]{"encoder","video"})
        {
            var audio=new Audio();var encoder=new Encoder();var factory=new RecordingRuntimeFactory(()=>audio,
                _=>failAt=="encoder"?throw new IOException("constructor"):encoder,
                ()=>throw new IOException("video constructor"),new());
            try{await factory.StartAsync(default);}catch(IOException){}
            await factory.DisposeAsync();Check(audio.Disposed==1&&(failAt=="encoder"||encoder.Disposed==1),"Construction orphaned earlier resource.");
        }
    }
    private static async Task ProductionWriteFault()
    {
        foreach(var audio in new[]{false,true})
        {
            var env=new Environment();var app=NewApp(env);Check(await Start(app),"Write-fault fixture start failed.");
            env.Backends[0].FailWrite=true;
            if(audio) await env.Audios[0].EmitAsync(); else env.Videos[0].Emit();
            await env.FaultObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await app.RecordingController.StopAsync();
            Check(app.RecordingController.LastReport?.FirstFault?.Stage==(audio?"audio-encode":"video-encode")
                &&env.Audios[0].Closed&&env.Videos[0].Closed&&env.Backends[0].Active==0,"Production encoder/pump failure did not reach actual App or retire both sources.");
        }
    }
    private static async Task ReadinessCombinations()
    {
        foreach(var border in new[]{false,true})foreach(var affinity in new[]{false,true})foreach(var outside in new[]{false,true})
        {
            var expected=border&&(affinity||outside);var env=new Environment{ReadyOverride=RecordingControlReadiness.CanStart(border,affinity,outside)};var app=NewApp(env);
            Check(await Start(app)==expected,"Readiness combination changed.");
            Check(expected||env.Backends.Count==0,"Unsafe controls started capture.");if(expected)await app.RecordingController.StopAsync();
        }
    }
    private static async Task HeldAudioStart()
    {
        var held=Signal();var env=new Environment{HoldAudioStart=held.Task};var app=NewApp(env);app.RecordingController.StopBudget=TimeSpan.FromMilliseconds(25);
        var start=Start(app);await env.AudioEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));var stop=app.RecordingController.StopAsync();var physical=app.RecordingController.PhysicalCompletion;
        try{Check((await stop) is {PhysicalCompleted:false}&&env.Videos.All(v=>v.Starts==0)&&env.Backends[0].Releases==0,"Held audio startup released the encoder or admitted video.");}
        finally{held.TrySetResult();await start.WaitAsync(TimeSpan.FromSeconds(3));if(physical is not null)await physical.WaitAsync(TimeSpan.FromSeconds(3));}
        Check(env.Videos.All(v=>v.Starts==0)&&env.Audios[0].Disposes==1&&!app.RecordingController.IsBusy,"Late audio startup revived video or leaked owner.");
    }
    private static async Task FinalizeFailure()
    {
        var env=new Environment();var app=NewApp(env);Check(await Start(app),"Finalize-fault start failed.");env.Backends[0].FailFinalize=true;
        var report=await app.RecordingController.StopAsync();
        Check(report is {Saved:false,Encoded:false,ResourcesReleased:true}&&report.FirstFault?.Stage=="encoder-finalize"
            &&File.ReadAllBytes(report.Paths!.PartialPath).SequenceEqual(Backend.Bytes)&&!File.Exists(report.Paths.FinalPath)
            &&env.Backends[0].Releases==1&&env.Backends[0].Shutdowns==1,"Finalize failure lost partial or skipped safe release.");
    }
    private static async Task StartupReentrantStop()
    {
        var env=new Environment{StopInFeedback=true};var app=NewApp(env);
        Check(!await Start(app)&&env.Feedback.Disposed==1&&env.Backends.Count==0&&!app.RecordingController.IsBusy,"Stop re-entered during feedback creation and orphaned UI/startup.");
        var exitEnv=new Environment();var exitApp=NewApp(exitEnv);exitApp.RequestRecordingRetirement();
        Check(!await Start(exitApp)&&exitEnv.Backends.Count==0,"Accepted exit without an existing recording allowed new admission.");
    }
    private static void ClockPacer()
    {
        Check(RecordingFrameRatePolicy.SampleDuration100Nanoseconds==10_000_000L/30,"Sample clock mismatches encoder target.");
        var pacer=new RecordingFramePacer(30,1_000_000);var captured=0;
        for(var i=0;i<120;i++)if(pacer.ShouldCapture(i*16600L))captured++;
        Check(captured is >=59 and <=61,"Near-60Hz pacing collapsed.");
        Check(pacer.ShouldCapture(100_000_000)&&!pacer.ShouldCapture(100_000_001),"Long stall caused a burst.");
        var timeline=new RecordingTimeline(100,1000);Check(timeline.GetTimestamp100Nanoseconds(RecordingStream.Video,101)==10000,"QPC timeline changed.");
        var rejected=false;try{timeline.GetTimestamp100Nanoseconds(RecordingStream.Video,101);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Nonmonotonic timestamp accepted.");
    }
    internal static RecordingOutputPaths NewPaths()
    {var root=Path.Combine(Path.GetTempPath(),"clipray-task5-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);return new(Path.Combine(root,"own.partial.mp4"),Path.Combine(root,"own.mp4"));}
    private static ClipRay.App.App NewApp(Environment env,bool copy=true)
    {
        var app=BackgroundTask3LifetimeTests.FakeApp();app.RecordingEnvironmentOverride=env;
        var settings=BackgroundTask3LifetimeTests.Field<AppSettings>(app,"_settings");BackgroundTask3LifetimeTests.Set(app,"_settings",settings with{CopyToClipboard=copy});return app;
    }
    private static Task<bool> Start(ClipRay.App.App app)=>app.BeginRecordingAsync(new(new(-100,-30,101,99)),_=>{},()=>{});
    internal sealed class Backend(RecordingOutputPaths paths) : IRecordingEncoderBackend
    {
        internal static readonly byte[] Bytes=[1,3,5,7,9,11];
        internal readonly TaskCompletionSource Entered=Signal();internal ManualResetEventSlim? Hold;
        internal bool HoldFinalize,HoldWrite,HoldStart,FailRelease,FailShutdown,FailWrite,FailFinalize;internal RecordingStartupStage? FailStart;
        internal int Active,Releases,Shutdowns,Finalizes;internal Action? OnFinalize;
        public bool BeganWriting{get;private set;}
        public void Start(CancellationToken token){Interlocked.Increment(ref Active);try{if(HoldStart){Entered.TrySetResult();Hold!.Wait();}File.WriteAllBytes(paths.PartialPath,Bytes);if(FailStart is {} stage)throw new RecordingStartupException(stage,new IOException("fake audio"));BeganWriting=true;}finally{Interlocked.Decrement(ref Active);}}
        public void WriteVideo(VideoRecordingSample sample){Interlocked.Increment(ref Active);try{if(HoldWrite){Entered.TrySetResult();Hold!.Wait();}Check(sample.BgraPixels.Length==16,"Held sample disappeared.");if(FailWrite)throw new IOException("video write");}finally{Interlocked.Decrement(ref Active);}}
        public void WriteAudio(AudioRecordingSample sample){if(FailWrite)throw new IOException("audio write");}
        public void FinalizeEncoding(){Interlocked.Increment(ref Active);try{Finalizes++;OnFinalize?.Invoke();if(HoldFinalize){Entered.TrySetResult();Hold!.Wait();}if(FailFinalize)throw new IOException("finalize");}finally{Interlocked.Decrement(ref Active);}}
        public void ReleaseWriter(){Check(Active==0,"Active native writer released.");Releases++;if(FailRelease)throw new IOException("release");}
        public void Shutdown(){Check(Active==0,"Active MF runtime released.");Shutdowns++;if(FailShutdown)throw new IOException("shutdown");}
    }
    private sealed class Environment : IRecordingEnvironment
    {
        internal readonly ConcurrentQueue<string> Events=new();internal readonly List<Backend> Backends=[];internal readonly List<Source> Videos=[];internal readonly List<Sound> Audios=[];
        internal readonly List<bool> SilentAttempts=[];internal readonly List<RecordingOutputPaths> CreatedPaths=[];internal readonly List<string> PromptMessages=[];internal readonly TaskCompletionSource BackendCreated=Signal();internal readonly FakePreview Preview=new();internal readonly Feedback Feedback=new();
        internal Rectangle Region;internal RecordingMonitorMapping Mapping;internal RecordingFaultSignal? Faults;
        internal bool? ReadyOverride;internal bool StopInFeedback;internal Task? HoldAudioStart;internal readonly TaskCompletionSource AudioEntered=Signal(),FaultObserved=Signal();
        internal ManualResetEventSlim? HoldFinalize,HoldStart;internal string? FailAt;internal RecordingStartupStage? AudioFailure;
        internal bool SilentAccepted,CopyFailure,CopyThrow,PreviewCreateFailure,PreviewAttachFailure,CleanupFailure;internal int Copies,Prompts;
        public IReadOnlyList<Rectangle> Monitors()=>FailAt=="monitor"?throw new IOException("monitors"):[new(-200,-100,500,400)];
        public RecordingOutputPaths Paths(string root){if(FailAt=="paths")throw new IOException("paths");var paths=NewPaths();CreatedPaths.Add(paths);return paths;}
        public IRecordingFeedback PrepareFeedback(Rectangle region,Action stop){if(FailAt=="feedback")throw new IOException("feedback");Region=region;Feedback.Ready=ReadyOverride??FailAt!="ready";Feedback.OnClose=()=>Events.Enqueue("feedback-close");if(StopInFeedback)stop();return Feedback;}
        public IRecordingRuntime CreateRuntime(RecordingMonitorMapping mapping,RecordingOutputPaths paths,bool silent,RecordingFaultSignal faults)
        {
            Mapping=mapping;Faults=faults;SilentAttempts.Add(silent);faults.Raised+=_=>FaultObserved.TrySetResult();
            return new RecordingRuntimeFactory(()=>
            {
                if(silent)return null;
                if(AudioFailure==RecordingStartupStage.AudioDevice)throw new IOException("no default audio");
                var audio=new Sound{FailStart=AudioFailure==RecordingStartupStage.SystemAudio,HoldStart=HoldAudioStart,Entered=AudioEntered,Faults=faults};Audios.Add(audio);return audio;
            },format=>
            {
                Check((format is null)==silent,"Silent attempt configured AAC.");
                var backend=new Backend(paths){Hold=HoldFinalize??HoldStart,HoldFinalize=HoldFinalize is not null,HoldStart=HoldStart is not null,
                    FailStart=!silent&&AudioFailure==RecordingStartupStage.AudioEncoder?RecordingStartupStage.AudioEncoder:null,FailRelease=CleanupFailure,OnFinalize=()=>Events.Enqueue("finalize")};
                Backends.Add(backend);BackendCreated.TrySetResult();return new MediaFoundationRecorder(paths,backend,faults);
            },()=>{var video=new Source{Faults=faults};Videos.Add(video);return video;},faults);
        }
        public Task<bool> ConfirmSilentAsync(string message){Prompts++;PromptMessages.Add(message);Check(Videos.All(v=>v.Starts==0),"Captured before silent consent.");return Task.FromResult(SilentAccepted);}
        public IRecordingPendingPreview OpenPendingPreview(string path,string language){Events.Enqueue("preview");Preview.Creates++;if(PreviewCreateFailure)throw new IOException("preview create");Preview.AttachFailure=PreviewAttachFailure;return Preview;}
        public bool CopyFile(string path){Copies++;Check(File.ReadAllBytes(path).SequenceEqual(Backend.Bytes),"Copied before actual commit.");if(CopyThrow)throw new IOException("clipboard");return !CopyFailure;}
        public void Notify(string message){ }
        public Task DispatchAsync(Action action){action();return Task.CompletedTask;}
    }
    private sealed class Feedback : IRecordingFeedback{internal bool Ready=true;internal int Disposed;internal Action? OnClose;public bool IsReady=>Ready;public void Dispose(){Disposed++;OnClose?.Invoke();}}
    private sealed class FakePreview : IRecordingPendingPreview{public bool IsClosed{get;set;}internal int Creates,Attaches,PendingCount;internal bool AttachFailure;public void Pending(string message){PendingCount++;}public void Attach(bool copied){Attaches++;if(AttachFailure)throw new IOException("attach");}public void Fail(string message){}}
    private sealed class Source : IGraphicsCaptureSource
    {
        internal int Starts,Disposes;internal bool Closed;internal RecordingFaultSignal? Faults;private RecordingVideoPump? _pump;
        public void RequestStop(){Closed=true;_pump?.RequestStop();}
        public Task StartAsync(Func<VideoRecordingSample,ValueTask> write,CancellationToken token){Check(!Closed,"Source revived after stop.");Starts++;_pump=new(write,Faults);return Task.CompletedTask;}
        internal void Emit()=>_pump!.TryPost(new(new byte[16],2,2,1,1));
        public Task StopAsync(){RequestStop();return _pump?.CompleteAsync()??Task.CompletedTask;}
        public async ValueTask DisposeAsync(){Disposes++;try{await StopAsync();}catch{}}
    }
    private sealed class Sound : ISystemAudioCapture
    {
        public AudioCaptureFormat Format=>new(48000,2,16,false);internal int Starts,Disposes;internal bool Closed,FailStart;internal Task? HoldStart;internal TaskCompletionSource? Entered;internal RecordingFaultSignal? Faults;private RecordingAudioPump? _pump;
        public void RequestStop(){Closed=true;_pump?.RequestStop();}
        public async Task StartAsync(Func<AudioRecordingSample,ValueTask> write,CancellationToken token){Check(!Closed,"Audio revived after stop.");Starts++;if(FailStart)throw new IOException("audio start");_pump=new(Format,write,Faults!);Entered?.TrySetResult();if(HoldStart is not null)await HoldStart;}
        internal Task EmitAsync(){_pump!.TryPost([1,2,3,4],1);return Task.CompletedTask;}
        public Task StopAsync(){RequestStop();return _pump?.CompleteAsync()??Task.CompletedTask;}
        public async ValueTask DisposeAsync(){Disposes++;await StopAsync();}
    }
}
