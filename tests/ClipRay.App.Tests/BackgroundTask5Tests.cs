using System.Drawing;
using ClipRay.App.Recording;

internal static partial class BackgroundTask5Tests
{
    internal static (string Name, Action Test)[] CreateCases() => [
            ("daily-policy", DailyPolicy), ("single-monitor", SingleMonitor),
            ("runtime-epoch", () => RuntimeEpoch().GetAwaiter().GetResult()),
            ("release-peers", () => ReleasePeers().GetAwaiter().GetResult()),
            ("shared-stop", () => SharedStop().GetAwaiter().GetResult()),
            .. MoreCases()
        ];

    internal static int Run(string[] args)
    {
        var cases = CreateCases();
        var names = args.Where(a => !a.StartsWith("--")).ToHashSet();
        if (names.Except(cases.Select(c => c.Name)).Any()) return 2;
        var selected = cases.Where(c => names.Count == 0 || names.Contains(c.Name)).ToArray();
        var failed = 0;
        foreach (var test in selected)
        {
            try { test.Test(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"TASK5 {selected.Length-failed}/{selected.Length}");
        return failed == 0 ? 0 : 1;
    }
    internal static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void DailyPolicy()
    {
        Check(RecordingFrameRatePolicy.FramesPerSecond == 60, "Default capture/encoder policy is not 60fps.");
        var rate = RecordingBitratePolicy.ForFrameSize(1920,1080,60);
        Check(rate is >= 6_000_000 and <= 8_000_000, "1080p60 rate outside daily budget: " + rate);
        Check(RecordingBitratePolicy.ForFrameSize(int.MaxValue,int.MaxValue,int.MaxValue) == 8_000_000, "Huge dimensions escaped upper bound.");
    }
    private static void SingleMonitor()
    {
        var rejected = false;
        try { RecordingGeometry.MapToMonitor(new Rectangle(90,0,20,20), [new(0,0,100,100), new(100,0,100,100)]); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Cross-monitor selection silently cropped.");
    }
    private static async Task ReleasePeers()
    {
        var audio = new Audio { FailDispose = true }; var video = new Video(); var encoder = new Encoder();
        var runtime = new RegionRecordingRuntime(video,audio,encoder);
        try { await runtime.DisposeAsync(); } catch { }
        Check(audio.Disposed == 1 && video.Disposed == 1 && encoder.Disposed == 1, "An audio release failure skipped other owners.");
    }
    private static async Task SharedStop()
    {
        var held = Signal(); var audio = new Audio { Hold = held.Task }; var video = new Video(); var encoder = new Encoder();
        var runtime = new RegionRecordingRuntime(video,audio,encoder);
        var first = runtime.StopAsync(); var second = runtime.StopAsync(); var disposal = runtime.DisposeAsync().AsTask();
        try { Check(!second.IsCompleted && !disposal.IsCompleted && encoder.Disposed == 0, "Repeated Stop/Dispose returned before physical stop."); }
        finally { held.TrySetResult(); try { await Task.WhenAll(first,second,disposal); } catch { } }
    }
    private static async Task RuntimeEpoch()
    {
        var timeline = RecordingTimeline.CreatePending(1000);
        var order = new List<string>();
        var encoder = new Encoder { OnStart = () =>
        {
            order.Add("encoder-ready");
            var rejected = false;
            try { timeline.GetTimestamp100Nanoseconds(RecordingStream.Video, 100); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Encoder initialization started the media clock.");
        }};
        var audio = new Audio { OnStart = () =>
        {
            order.Add("audio");
            Check(timeline.GetTimestamp100Nanoseconds(RecordingStream.Audio, 5001) == 10000, "Audio included encoder startup time.");
        }};
        var video = new Video { OnPrepare = () => order.Add("permission-ready"), OnStart = () =>
        {
            order.Add("video");
            Check(timeline.GetTimestamp100Nanoseconds(RecordingStream.Video, 5003) == 30000, "Audio/video did not share an epoch.");
        }};
        await using var runtime = new RecordingRuntimeFactory(() => audio, _ => encoder, () => video, new(), () =>
        {
            order.Add("epoch"); timeline.BeginCapture(5000);
        });
        await runtime.StartAsync(default);
        Check(order.SequenceEqual(new[] { "encoder-ready", "permission-ready", "epoch", "audio", "video" }), "Sources started before the encoder, permission and common clock were ready.");
    }
    internal sealed class Audio : ISystemAudioCapture
    {
        public AudioCaptureFormat Format => new(48000,2,16,false);
        internal bool FailDispose; internal Task Hold = Task.CompletedTask; internal int Disposed;
        internal Action? OnStart;
        public Task StartAsync(Func<AudioRecordingSample,ValueTask> sample,CancellationToken token) { OnStart?.Invoke(); return Task.CompletedTask; }
        public Task StopAsync() => Hold;
        public ValueTask DisposeAsync() { Disposed++; return FailDispose ? ValueTask.FromException(new Exception("audio release")) : ValueTask.CompletedTask; }
    }
    internal sealed class Video : IGraphicsCaptureSource
    {
        internal int Disposed;
        internal Action? OnPrepare;
        public Task PrepareAsync(CancellationToken token) { OnPrepare?.Invoke(); return Task.CompletedTask; }
        internal Action? OnStart;
        public Task StartAsync(Func<VideoRecordingSample,ValueTask> sample,CancellationToken token) { OnStart?.Invoke(); return Task.CompletedTask; }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed++; return ValueTask.CompletedTask; }
    }
    internal sealed class Encoder : IMediaFoundationRecorder
    {
        internal int Disposed;
        internal Action? OnStart;
        public Task StartAsync(CancellationToken token) { OnStart?.Invoke(); return Task.CompletedTask; }
        public ValueTask WriteVideoAsync(VideoRecordingSample sample) => ValueTask.CompletedTask;
        public ValueTask WriteAudioAsync(AudioRecordingSample sample) => ValueTask.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed++; return ValueTask.CompletedTask; }
    }
}
