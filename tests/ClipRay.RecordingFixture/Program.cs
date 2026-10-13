using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using ClipRay.App.Recording;
using NAudio.Wave;
using Vortice.MediaFoundation;
using Forms = System.Windows.Forms;

_ = SetProcessDpiAwarenessContext(new IntPtr(-4));
var workspace = FindWorkspace(AppContext.BaseDirectory);
var output = Path.Combine(workspace, "artifacts", "recording-fixture");
Directory.CreateDirectory(output);
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
    MotionSourceForm? motion = null;
    var motionReady = new ManualResetEventSlim();
    var motionThread = new Thread(() =>
    {
        motion = new MotionSourceForm(primary);
        motionReady.Set();
        Forms.Application.Run(motion);
    })
    { IsBackground = true };
    motionThread.SetApartmentState(ApartmentState.STA);
    motionThread.Start();
    if (!motionReady.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("动画录制源窗口没有启动。");
    await Task.Delay(500);
    var probePaths = RecordingOutputPaths.Create(output, DateTime.Now);
    await using var probeRecorder = new MediaFoundationRecorder(probePaths, crop.Width, crop.Height, new AudioCaptureFormat(48_000, 2, 32, true));
    await probeRecorder.StartAsync(CancellationToken.None);
    var delivered = 0;
    var hashes = new List<ulong>();
    var writeMilliseconds = new List<double>();
    var gate = new object();
    await using var probeSource = new GraphicsCaptureSource(mapping, RecordingTimeline.StartNow());
    var captureWatch = Stopwatch.StartNew();
    await probeSource.StartAsync(async sample =>
    {
        Interlocked.Increment(ref delivered);
        var hash = Fnv(sample.BgraPixels);
        var writeWatch = Stopwatch.GetTimestamp();
        await probeRecorder.WriteVideoAsync(sample);
        var elapsedMs = (Stopwatch.GetTimestamp() - writeWatch) * 1000.0 / Stopwatch.Frequency;
        lock (gate)
        {
            hashes.Add(hash);
            writeMilliseconds.Add(elapsedMs);
        }
    }, CancellationToken.None);
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    var captureSeconds = captureWatch.Elapsed.TotalSeconds;
    var drainWatch = Stopwatch.StartNew();
    await probeSource.StopAsync();
    var drain = drainWatch.Elapsed;
    var recorderStopWatch = Stopwatch.StartNew();
    await probeRecorder.StopAsync();
    var recorderStop = recorderStopWatch.Elapsed;
    motion?.BeginInvoke(new Action(Forms.Application.ExitThread));
    var distinct = hashes.Distinct().Count();
    var deliveredFps = delivered / captureSeconds;
    var distinctFps = distinct / captureSeconds;
    var writeMax = writeMilliseconds.DefaultIfEmpty(0).Max();
    var probeWatch = Stopwatch.StartNew();
    var probedDuration = RecordingMediaProbe.TryReadDuration(probePaths.FinalPath);
    var probeElapsed = probeWatch.Elapsed;
    Console.WriteLine($"RATE_PROBE crop={crop.Width}x{crop.Height} seconds={captureSeconds:F2} delivered={delivered} deliveredFps={deliveredFps:F2} distinct={distinct} distinctFps={distinctFps:F2}");
    Console.WriteLine($"RATE_PROBE writeMeanMs={writeMilliseconds.DefaultIfEmpty(0).Average():F2} writeMaxMs={writeMax:F2} drainMs={drain.TotalMilliseconds:F0} recorderStopMs={recorderStop.TotalMilliseconds:F0} probeMs={probeElapsed.TotalMilliseconds:F0} fileDuration={probedDuration?.TotalSeconds:F2}");
    Console.WriteLine($"STOP_BREAKDOWN videoStopMs={drain.TotalMilliseconds:F0} recorderStopMs={recorderStop.TotalMilliseconds:F0} probeMs={probeElapsed.TotalMilliseconds:F0} totalStopMs={(drain + recorderStop + probeElapsed).TotalMilliseconds:F0}");
    Console.WriteLine($"RATE_PROBE file={probePaths.FinalPath}");
    // Daily 30fps candidate: 90% unique-frame delivery; real-device acceptance is still pending.
    var pass = deliveredFps >= 27 && distinctFps >= 27 && writeMax < 200 && drain.TotalMilliseconds < 1500
        && probedDuration is not null && Math.Abs(probedDuration.Value.TotalSeconds - captureSeconds) < 1.5;
    Console.WriteLine(pass ? "RATE_PROBE result=PASS" : "RATE_PROBE result=FAIL");
    return pass ? 0 : 3;
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

[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetProcessDpiAwarenessContext(IntPtr value);

/// <summary>全屏动画窗口，为交付帧率基准提供稳定的高频变化画面。</summary>
internal sealed class MotionSourceForm : Forms.Form
{
    private readonly Forms.Timer _timer = new() { Interval = 15 };
    private int _tick;

    public MotionSourceForm(Rectangle bounds)
    {
        FormBorderStyle = Forms.FormBorderStyle.None;
        StartPosition = Forms.FormStartPosition.Manual;
        Bounds = bounds;
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
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            area,
            Color.FromArgb(phase, 255 - phase, 64),
            Color.FromArgb(255 - phase, 64, phase),
            System.Drawing.Drawing2D.LinearGradientMode.ForwardDiagonal);
        graphics.FillRectangle(brush, area);
        graphics.FillRectangle(Brushes.White, _tick * 11 % Math.Max(1, ClientSize.Width - 120), ClientSize.Height / 3, 120, ClientSize.Height / 3);
    }
}
