using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using ClipRay.App.Recording;
using NAudio.Wave;
using Vortice.MediaFoundation;
using Forms = System.Windows.Forms;
using System.Text.Json;
using System.Media;

_ = SetProcessDpiAwarenessContext(new IntPtr(-4));
var workspace = FindWorkspace(AppContext.BaseDirectory);
var output = args.FirstOrDefault(value => value.StartsWith("--output=", StringComparison.Ordinal)) is string outputArgument
    ? Path.GetFullPath(outputArgument["--output=".Length..]) : Path.Combine(workspace, "artifacts", "recording-fixture");
Directory.CreateDirectory(output);
if (args.Contains("--motion-source-only", StringComparer.Ordinal))
{
    var readyPath = args.Single(value => value.StartsWith("--ready=", StringComparison.Ordinal))["--ready=".Length..];
    var stopPath = args.Single(value => value.StartsWith("--stop=", StringComparison.Ordinal))["--stop=".Length..];
    if (!Path.IsPathFullyQualified(readyPath) || !Path.IsPathFullyQualified(stopPath))
        throw new ArgumentException("Source control paths must be absolute.");
    var sourceThread = new Thread(() =>
    {
        var screen = Forms.Screen.PrimaryScreen?.WorkingArea ?? throw new InvalidOperationException("No primary monitor.");
        var region = new Rectangle(screen.X+40,screen.Y+40,640,360);
        using var form = new MotionSourceForm(region);
        using var lifetime = new Forms.Timer { Interval=100 };
        var deadline = DateTime.UtcNow+TimeSpan.FromSeconds(45);
        lifetime.Tick += (_,_) => { if (File.Exists(stopPath) || DateTime.UtcNow>=deadline) form.Close(); };
        form.Shown += (_,_) =>
        {
            form.Update();
            File.WriteAllText(readyPath,JsonSerializer.Serialize(new { Region=new[]{region.X,region.Y,region.Width,region.Height} }));
            lifetime.Start();
        };
        Forms.Application.Run(form);
    });
    sourceThread.SetApartmentState(ApartmentState.STA);
    sourceThread.Start();
    sourceThread.Join();
    return 0;
}
if (args.Contains("--wgc-probe", StringComparer.OrdinalIgnoreCase))
{
    var screen = Forms.Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("No primary monitor.");
    var mapping = RecordingGeometry.MapToMonitor(screen, Forms.Screen.AllScreens.Select(static item => item.Bounds).ToArray());
    var frames = new List<byte[]>();
    await using var source = new GraphicsCaptureSource(mapping, RecordingTimeline.StartNow());
    await source.StartAsync(sample =>
    {
        if (frames.Count < 60) frames.Add(sample.BgraPixels);
        return ValueTask.CompletedTask;
    }, CancellationToken.None);
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
    while (frames.Count < 30 && DateTime.UtcNow < deadline) await Task.Delay(50);
    await source.StopAsync();
    var nonBlack = frames.Any(frame => frame.Where((_, index) => index % 4 != 3).Any(value => value != 0));
    var changing = frames.Skip(1).Any(frame => !frame.AsSpan().SequenceEqual(frames[0]));
    Console.WriteLine($"WGC_PROBE frames={frames.Count} nonBlack={nonBlack} changing={changing}");
    return frames.Count >= 3 && nonBlack && changing ? 0 : 2;
}

if (args.Contains("--mft-encoders", StringComparer.OrdinalIgnoreCase))
{
    MediaFactory.MFStartup();
    try
    {
        var videoMajor = new Guid("73646976-0000-0010-8000-00AA00389B71");
        var h264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        var nv12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");
        var rgb32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        foreach (var (categoryLabel, category, inputLabel, inputSubtype, outputSubtype) in new[]
        {
            ("encoder", TransformCategoryGuids.VideoEncoder, "NV12", nv12, h264),
            ("encoder", TransformCategoryGuids.VideoEncoder, "RGB32", rgb32, h264),
            ("decoder", TransformCategoryGuids.VideoDecoder, "H264", h264, nv12),
        })
        {
            MediaFactory.MFTEnum(
                category,
                0x3Fu,
                new RegisterTypeInfo { GuidMajorType = videoMajor, GuidSubtype = inputSubtype },
                new RegisterTypeInfo { GuidMajorType = videoMajor, GuidSubtype = outputSubtype },
                null!,
                out var clsidPointer,
                out var clsidCount);
            Console.WriteLine($"MFT_ENCODERS category={categoryLabel} input={inputLabel} count={clsidCount}");
            for (var index = 0; index < clsidCount; index++)
            {
                var bytes = new byte[16];
                Marshal.Copy(IntPtr.Add(clsidPointer, index * 16), bytes, 0, 16);
                var clsid = new Guid(bytes);
                var name = "(unknown)";
                var nameBuffer = Marshal.AllocHGlobal(IntPtr.Size);
                try
                {
                    Marshal.WriteIntPtr(nameBuffer, IntPtr.Zero);
                    MediaFactory.MFTGetInfo(clsid, nameBuffer, out _, out _, out _, out _, out _);
                    var namePointer = Marshal.ReadIntPtr(nameBuffer);
                    if (namePointer != IntPtr.Zero)
                    {
                        name = Marshal.PtrToStringUni(namePointer) ?? "(unknown)";
                        Marshal.FreeCoTaskMem(namePointer);
                    }
                }
                catch (Exception exception) { name = $"(lookup failed: {exception.GetType().Name})"; }
                finally { Marshal.FreeHGlobal(nameBuffer); }
                Console.WriteLine($"MFT_ENCODERS   {clsid} {name}");
            }
            if (clsidPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(clsidPointer);
        }
    }
    finally { MediaFactory.MFShutdown(); }
    return 0;
}

if (args.Contains("--audio-lifecycle", StringComparer.Ordinal))
{
    for (var iteration = 0; iteration < 2; iteration++)
    {
        var faults = new RecordingFaultSignal();
        var audio = new WasapiSystemAudioCapture(RecordingTimeline.StartNow(), faults);
        var callbacks = 0;
        await audio.StartAsync(_ => { Interlocked.Increment(ref callbacks); return ValueTask.CompletedTask; }, default);
        if (iteration == 1) await Task.Delay(250);
        await Task.WhenAll(audio.StopAsync(), audio.StopAsync());
        await audio.DisposeAsync(); await audio.DisposeAsync();
        var stoppedCallbacks = Volatile.Read(ref callbacks);
        await Task.Delay(120);
        if (faults.First is not null || callbacks != stoppedCallbacks || iteration == 1 && callbacks == 0)
            throw new InvalidOperationException("Audio stop/restart did not close its sources cleanly.");
        Console.WriteLine($"AUDIO_LIFECYCLE iteration={iteration} callbacks={callbacks} released=True");
    }
    return 0;
}

if (args.Contains("--rate-probe", StringComparer.OrdinalIgnoreCase))
{
    var seconds = 6;
    var rateIndex = Array.FindIndex(args, static value => string.Equals(value, "--rate-probe", StringComparison.OrdinalIgnoreCase));
    if (rateIndex >= 0 && rateIndex + 1 < args.Length && int.TryParse(args[rateIndex + 1], out var parsedSeconds)) seconds = parsedSeconds;
    var requestedSize = args.FirstOrDefault(static value => value.StartsWith("--size=", StringComparison.OrdinalIgnoreCase));
    var probeWidth = 1280;
    var probeHeight = 720;
    if (requestedSize is not null)
    {
        var parts = requestedSize["--size=".Length..].Split('x');
        probeWidth = int.Parse(parts[0]);
        probeHeight = int.Parse(parts[1]);
    }
    var screens = Forms.Screen.AllScreens.Select(static item => item.Bounds).ToArray();
    var primary = Forms.Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("No primary monitor.");
    var region = new Rectangle(
        primary.X + Math.Max(0, (primary.Width - probeWidth) / 2),
        primary.Y + Math.Max(0, (primary.Height - probeHeight) / 2),
        probeWidth,
        probeHeight);
    var mapping = RecordingGeometry.MapToMonitor(region, screens);
    var crop = RecordingGeometry.NormalizeForEncoder(mapping.CropInMonitor);
    var fpsArgument = args.FirstOrDefault(value => value.StartsWith("--fps=", StringComparison.Ordinal));
    var qualityArgument = args.FirstOrDefault(value => value.StartsWith("--quality=", StringComparison.Ordinal));
    var options = new RecordingVideoOptions(fpsArgument is null ? 60 : int.Parse(fpsArgument[6..]),
        qualityArgument is null ? RecordingQuality.Balanced : Enum.Parse<RecordingQuality>(qualityArgument[10..]));
    options.Validate();
    MotionSourceForm? motion = null;
    var motionReady = new ManualResetEventSlim();
    var externalSource = args.Contains("--external-source", StringComparer.Ordinal);
    await CaptureBorderAccess.GetAsync();
    var motionThread = new Thread(() =>
    {
        motion = new MotionSourceForm(primary, region);
        motionReady.Set();
        Forms.Application.Run(motion);
    })
    { IsBackground = true };
    motionThread.SetApartmentState(ApartmentState.STA);
    if (!externalSource)
    {
        motionThread.Start();
        if (!motionReady.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("动画录制源窗口没有启动。");
    }
    await Task.Delay(500);
    var probePaths = RecordingOutputPaths.Create(output, DateTime.Now);
    var timeline = RecordingTimeline.CreatePending();
    var recordAudio = args.Contains("--system-audio", StringComparer.Ordinal);
    await using var probeAudio = recordAudio ? new WasapiSystemAudioCapture(timeline) : null;
    using var tone = recordAudio && !args.Contains("--no-tone", StringComparer.Ordinal)
        ? new SoundPlayer(CreateTone(output)) : null;
    // Either video-only, or a real WASAPI loopback stream with a self-authored quiet tone.
    await using var probeRecorder = new MediaFoundationRecorder(probePaths, crop.Width, crop.Height, probeAudio?.Format, options,
        preferGpuInput:!args.Contains("--cpu", StringComparer.Ordinal));
    await probeRecorder.StartAsync(CancellationToken.None);
    var audioPackets=0;
    Console.WriteLine("ENCODER_DIAGNOSTICS " + JsonSerializer.Serialize(probeRecorder.EncoderDiagnostics));
    var delivered = 0;
    var hashes = new List<ulong>();
    var writeMilliseconds = new List<double>();
    var gate = new object();
    await using var probeSource = new GraphicsCaptureSource(mapping, timeline, reuseBuffers:true, options:options,
        sharedDevice:() => probeRecorder.AcceptsGpuFrames ? probeRecorder.SharedDevice : null);
    await probeSource.PrepareAsync(CancellationToken.None);
    tone?.Load();
    tone?.PlayLooping();
    var toneSeconds = args.FirstOrDefault(value => value.StartsWith("--tone-seconds=", StringComparison.Ordinal));
    var stopTone = tone is not null && toneSeconds is not null
        ? StopToneAsync(tone, double.Parse(toneSeconds[15..], System.Globalization.CultureInfo.InvariantCulture))
        : Task.CompletedTask;
    timeline.BeginCapture();
    if (probeAudio is not null)
        await probeAudio.StartAsync(sample => { Interlocked.Increment(ref audioPackets); return probeRecorder.WriteAudioAsync(sample); }, CancellationToken.None);
    var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
    var sourceFramesStart = motion?.PaintCount ?? 0;
    var captureWatch = Stopwatch.StartNew();
    await probeSource.StartAsync(async sample =>
    {
        Interlocked.Increment(ref delivered);
        var hash = sample.GpuTexture is null ? Fnv(sample.BgraPixels) : 0;
        var writeWatch = Stopwatch.GetTimestamp();
        await probeRecorder.WriteVideoAsync(sample);
        var elapsedMs = (Stopwatch.GetTimestamp() - writeWatch) * 1000.0 / Stopwatch.Frequency;
        lock (gate)
        {
            if (sample.GpuTexture is null) hashes.Add(hash);
            writeMilliseconds.Add(elapsedMs);
        }
    }, CancellationToken.None);
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    var captureSeconds = captureWatch.Elapsed.TotalSeconds;
    var cpuSeconds = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalSeconds;
    var sourceFps = ((motion?.PaintCount ?? 0) - sourceFramesStart) / captureSeconds;
    var drainWatch = Stopwatch.StartNew();
    await probeSource.StopAsync();
    if (probeAudio is not null) await probeAudio.StopAsync();
    await stopTone;
    tone?.Stop();
    var drain = drainWatch.Elapsed;
    var recorderStopWatch = Stopwatch.StartNew();
    await probeRecorder.StopAsync();
    var recorderStop = recorderStopWatch.Elapsed;
    motion?.BeginInvoke(new Action(Forms.Application.ExitThread));
    var distinct = hashes.Distinct().Count();
    var decodedChanges = -1;
    var ffmpegArgument = args.FirstOrDefault(value => value.StartsWith("--ffmpeg=", StringComparison.Ordinal));
    if (ffmpegArgument is not null)
        try { decodedChanges = await CountDecodedMarkerChanges(ffmpegArgument[9..], probePaths.FinalPath, crop.Width, crop.Height); }
        catch (Exception error) { Console.WriteLine($"DECODE_FAILED {error.GetType().Name}: {error.Message}"); }
    var deliveredFps = delivered / captureSeconds;
    var distinctFps = distinct / captureSeconds;
    var writeMax = writeMilliseconds.DefaultIfEmpty(0).Max();
    var probeWatch = Stopwatch.StartNew();
    var probedDuration = RecordingMediaProbe.TryReadDuration(probePaths.FinalPath);
    var probeElapsed = probeWatch.Elapsed;
    Console.WriteLine($"CAPTURE_DIAGNOSTICS fps={options.FramesPerSecond} sourcePaintFps={sourceFps:F2} quality={options.Quality} borderAccess={probeSource.BorderAccess?.Status} systemBorderRequired={probeSource.SystemBorderRequired} cpuSeconds={cpuSeconds:F2}");
    Console.WriteLine("CAPTURE_STATISTICS " + JsonSerializer.Serialize(probeSource.Statistics));
    Console.WriteLine($"AUDIO_CAPTURE enabled={recordAudio} packets={audioPackets}");
    Console.WriteLine($"DECODED_MOTION changes={decodedChanges} changedFramesPerSecond={decodedChanges / captureSeconds:F2}");
    Console.WriteLine($"RATE_PROBE crop={crop.Width}x{crop.Height} seconds={captureSeconds:F2} delivered={delivered} deliveredFps={deliveredFps:F2} distinct={distinct} distinctFps={distinctFps:F2}");
    Console.WriteLine($"RATE_PROBE writeMeanMs={writeMilliseconds.DefaultIfEmpty(0).Average():F2} writeMaxMs={writeMax:F2} drainMs={drain.TotalMilliseconds:F0} recorderStopMs={recorderStop.TotalMilliseconds:F0} probeMs={probeElapsed.TotalMilliseconds:F0} fileDuration={probedDuration?.TotalSeconds:F2}");
    Console.WriteLine($"STOP_BREAKDOWN videoStopMs={drain.TotalMilliseconds:F0} recorderStopMs={recorderStop.TotalMilliseconds:F0} probeMs={probeElapsed.TotalMilliseconds:F0} totalStopMs={(drain + recorderStop + probeElapsed).TotalMilliseconds:F0}");
    Console.WriteLine($"RATE_PROBE file={probePaths.FinalPath}");
    // Count actual changed frames, not the stream's nominal frame-rate header.
    var motionFps = decodedChanges >= 0 ? decodedChanges / captureSeconds : distinctFps;
    var pass = (!recordAudio || audioPackets>0) && (externalSource || sourceFps >= options.FramesPerSecond * .85) && deliveredFps >= options.FramesPerSecond * .85 && motionFps >= options.FramesPerSecond * .85 && writeMax < 200 && drain.TotalMilliseconds < 1500
        && probedDuration is not null && Math.Abs(probedDuration.Value.TotalSeconds - captureSeconds) < 1.5;
    Console.WriteLine(pass ? "RATE_PROBE result=PASS" : "RATE_PROBE result=FAIL");
    return pass ? 0 : 3;
}

static async Task StopToneAsync(SoundPlayer tone, double seconds)
{
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    tone.Stop();
}

var paths = new RecordingOutputPaths(
    Path.Combine(output, "generated-motion-audio.partial.mp4"),
    Path.Combine(output, "generated-motion-audio.mp4"));
if (File.Exists(paths.PartialPath) || File.Exists(paths.FinalPath))
{
    var suffix = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
    paths = new RecordingOutputPaths(Path.Combine(output, $"generated-{suffix}.partial.mp4"), Path.Combine(output, $"generated-{suffix}.mp4"));
}
await using var recorder = new MediaFoundationRecorder(paths, 320, 180, new AudioCaptureFormat(48_000, 2, 32, true));
await recorder.StartAsync(CancellationToken.None);
var videoDuration = RecordingFrameRatePolicy.SampleDuration100Nanoseconds;
const int videoFrameCount = 2 * RecordingFrameRatePolicy.FramesPerSecond;
for (var frameIndex = 0; frameIndex < videoFrameCount; frameIndex++)
{
    var pixels = new byte[320 * 180 * 4];
    for (var y = 0; y < 180; y++)
    for (var x = 0; x < 320; x++)
    {
        var offset = (y * 320 + x) * 4;
        pixels[offset] = (byte)((x + frameIndex * 5) & 255);
        pixels[offset + 1] = (byte)((y * 2 + frameIndex * 7) & 255);
        pixels[offset + 2] = (byte)((x + y + frameIndex * 11) & 255);
        pixels[offset + 3] = 255;
    }
    await recorder.WriteVideoAsync(new VideoRecordingSample(pixels, 320, 180, frameIndex * videoDuration, videoDuration));
}
const int packetFrames = 480;
for (var packet = 0; packet < 200; packet++)
{
    var pcm = new byte[packetFrames * 2 * sizeof(float)];
    Span<float> samples = MemoryMarshal.Cast<byte, float>(pcm.AsSpan());
    for (var index = 0; index < packetFrames; index++)
    {
        var value = 0.2f * MathF.Sin(2 * MathF.PI * 440 * (packet * packetFrames + index) / 48_000f);
        samples[index * 2] = value;
        samples[index * 2 + 1] = value;
    }
    await recorder.WriteAudioAsync(new AudioRecordingSample(pcm, 48_000, 2, 32, packet * 100_000L, 100_000));
}
await recorder.StopAsync();
using (var reader = new MediaFoundationReader(paths.FinalPath))
{
    var samples = reader.ToSampleProvider();
    var buffer = new float[4096];
    var peak = 0f;
    int read;
    while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
    {
        for (var index = 0; index < read; index++) peak = Math.Max(peak, Math.Abs(buffer[index]));
    }
    Console.WriteLine($"AUDIO_DECODE peak={peak:F6}");
    if (peak <= 0.01f) return 3;
}
Console.WriteLine(paths.FinalPath);
return 0;

static string FindWorkspace(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "ClipRay.sln"))) return directory.FullName;
    }
    throw new InvalidOperationException("Workspace not found.");
}

static ulong Fnv(byte[] pixels)
{
    var hash = 14695981039346656037UL;
    for (var index = 0; index < pixels.Length; index += 64)
    {
        hash = (hash ^ pixels[index]) * 1099511628211UL;
    }
    return hash;
}

static async Task<int> CountDecodedMarkerChanges(string ffmpeg, string path, int width, int height)
{
    var start = new ProcessStartInfo(Path.GetFullPath(ffmpeg)) { UseShellExecute=false, CreateNoWindow=true,
        RedirectStandardOutput=true, RedirectStandardError=true };
    foreach (var value in new[] { "-v", "error", "-i", path, "-vf", $"format=gray,crop={width}:1:0:{height / 2}", "-vsync", "0", "-f", "rawvideo", "pipe:1" })
        start.ArgumentList.Add(value);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Decoder did not start.");
    var errors = process.StandardError.ReadToEndAsync();
    var row = new byte[width];
    int count=0, previous=-1;
    while (true)
    {
        var read=0;
        while (read<width)
        {
            var part=await process.StandardOutput.BaseStream.ReadAsync(row.AsMemory(read));
            if (part==0) break;
            read+=part;
        }
        if (read==0) break;
        if (read!=width) throw new InvalidOperationException("Decoded marker row is incomplete.");
        var marker=Array.FindIndex(row, pixel=>pixel>220);
        if (marker<0) throw new InvalidOperationException("Decoded frame lost the known motion marker.");
        if (marker!=previous) count++;
        previous=marker;
    }
    await process.WaitForExitAsync();
    if (process.ExitCode!=0) throw new InvalidOperationException("Decoder failed: "+await errors);
    return count;
}

static string CreateTone(string output)
{
    var path=Path.Combine(output,"acceptance-tone-"+Guid.NewGuid().ToString("N")+".wav");
    const int rate=48000, channels=2, samples=rate;
    using var writer=new BinaryWriter(File.Create(path));
    writer.Write("RIFF"u8); writer.Write(36+samples*channels*2); writer.Write("WAVEfmt "u8);
    writer.Write(16); writer.Write((short)1); writer.Write((short)channels); writer.Write(rate);
    writer.Write(rate*channels*2); writer.Write((short)(channels*2)); writer.Write((short)16);
    writer.Write("data"u8); writer.Write(samples*channels*2);
    for(var i=0;i<samples;i++)
    {
        var value=(short)(500*Math.Sin(2*Math.PI*440*i/rate));
        writer.Write(value); writer.Write(value);
    }
    return path;
}

[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetProcessDpiAwarenessContext(IntPtr value);

/// <summary>全屏动画窗口，为交付帧率基准提供稳定的高频变化画面。</summary>
internal sealed class MotionSourceForm : Forms.Form
{
    protected override bool ShowWithoutActivation => true;
    private readonly Forms.Timer _timer = new() { Interval = 1 };
    private int _tick;
    private int _paints;
    private readonly Rectangle _motionArea;
    public int PaintCount => Volatile.Read(ref _paints);

    public MotionSourceForm(Rectangle bounds, Rectangle? motionArea = null)
    {
        FormBorderStyle = Forms.FormBorderStyle.None;
        StartPosition = Forms.FormStartPosition.Manual;
        Bounds = bounds;
        var region = motionArea ?? bounds;
        _motionArea = new(region.X - bounds.X, region.Y - bounds.Y, region.Width, region.Height);
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.Black;
        _timer.Tick += (_, _) =>
        {
            _tick++;
            Invalidate();
        };
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        // 整帧渐变，保证相邻两次绘制的每个采样点都不同；
        // 只画几根竖条会让基准的哈希采样发生别名，读不到真实刷新率。
        var graphics = e.Graphics;
        var phase = _tick % 256;
        var area = new Rectangle(0, 0, Math.Max(2, ClientSize.Width), Math.Max(2, ClientSize.Height));
        graphics.Clear(Color.FromArgb(phase, 255 - phase, 64));
        graphics.FillRectangle(Brushes.White, _motionArea.X + _tick * 11 % Math.Max(1, _motionArea.Width - 120),
            _motionArea.Y + _motionArea.Height / 3, 120, _motionArea.Height / 3);
        Interlocked.Increment(ref _paints);
    }
}
