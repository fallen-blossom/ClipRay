using System.IO;
using ClipRay.App.Infrastructure;
using ClipRay.App.Recording;
using Vortice.MediaFoundation;

internal static class CaptureRecordingRepairTests
{
    public static void RecordingPreviewProvidesScrubbing()
    {
        var duration = TimeSpan.FromSeconds(14.016667);
        var half = RecordingPreviewTransport.PositionFromFraction(0.5, duration);
        if (Math.Abs(half.TotalSeconds - duration.TotalSeconds / 2) > 0.001)
            throw new Exception("预览进度条没有把拖动值换算成成片中间位置。");
        if (RecordingPreviewTransport.PositionFromFraction(1.6, duration) != duration)
            throw new Exception("预览进度条没有把越界拖动收敛到成片末尾。");
        if (RecordingPreviewTransport.PositionFromFraction(-0.4, duration) != TimeSpan.Zero)
            throw new Exception("预览进度条没有把越界拖动收敛到成片开头。");
        if (RecordingPreviewTransport.PositionFromFraction(0.5, TimeSpan.Zero) != TimeSpan.Zero)
            throw new Exception("时长未知时预览进度条不应产生跳转位置。");
        var fraction = RecordingPreviewTransport.FractionFromPosition(TimeSpan.FromSeconds(7), duration);
        if (Math.Abs(fraction - 0.4994) > 0.001)
            throw new Exception("预览进度条没有把播放位置映回拖动值。");
        if (RecordingPreviewTransport.FractionFromPosition(TimeSpan.FromSeconds(30), duration) != 1d)
            throw new Exception("预览进度条没有把超出成片的播放位置收敛到末尾。");
        if (RecordingPreviewTransport.FormatPosition(TimeSpan.FromSeconds(3.4), duration) != "0:03 / 0:14")
            throw new Exception("预览时间显示格式不符合预期。");
        if (RecordingPreviewTransport.FormatPosition(TimeSpan.FromSeconds(3671), TimeSpan.FromSeconds(3671)) != "1:01:11 / 1:01:11")
            throw new Exception("超过一小时的预览时间显示没有进位。");

        var path = Path.Combine(Path.GetTempPath(), "clipray-preview-scrub-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(path, []);
        var garbage = Path.Combine(Path.GetTempPath(), "clipray-duration-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(garbage, [1, 2, 3, 4]);
        try
        {
            if (RecordingMediaProbe.TryReadDuration(garbage) is not null)
                throw new Exception("无效成片不应给出可拖动时长。");
            var preview = new RecordingPreviewWindow(path);
            var slider = (System.Windows.Controls.Slider?)typeof(RecordingPreviewWindow)
                .GetField("_seek", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(preview);
            if (slider is null) throw new Exception("预览窗口没有可拖动的进度条。");
            if (slider.Minimum != 0d) throw new Exception("预览进度条的下限不是成片开头。");
            if (slider.IsEnabled) throw new Exception("时长未知时预览进度条不应可拖动。");
            preview.Close();
        }
        finally
        {
            File.Delete(path);
            File.Delete(garbage);
        }
    }

    public static void RecordingPreviewDragSeeksAndResumes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "clipray-scrub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var paths = new RecordingOutputPaths(Path.Combine(directory, "clip.partial.mp4"), Path.Combine(directory, "clip.mp4"));
        try
        {
            var recorder = new MediaFoundationRecorder(paths, 320, 180, new AudioCaptureFormat(48_000, 2, 32, true));
            recorder.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            var step = RecordingFrameRatePolicy.SampleDuration100Nanoseconds;
            // 素材要足够长：2 秒素材播到头会触发循环重置，拖动断言会被 MediaEnded 覆盖。
            for (var index = 0; index < 6 * RecordingFrameRatePolicy.FramesPerSecond; index++)
            {
                var pixels = new byte[320 * 180 * 4];
                for (var pixel = 0; pixel < pixels.Length; pixel += 4)
                {
                    pixels[pixel] = (byte)(index * 4);
                    pixels[pixel + 3] = 255;
                }
                recorder.WriteVideoAsync(new VideoRecordingSample(pixels, 320, 180, index * step, step)).AsTask().GetAwaiter().GetResult();
            }
            recorder.StopAsync().GetAwaiter().GetResult();
            recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var probed = RecordingMediaProbe.TryReadDuration(paths.FinalPath);
            if (probed is null || Math.Abs(probed.Value.TotalSeconds - 6) > 0.3)
                throw new Exception("预览没有从成片本身读到时长：" + probed);

            var preview = new RecordingPreviewWindow(paths.FinalPath);
            var media = (System.Windows.Controls.MediaElement)typeof(RecordingPreviewWindow)
                .GetField("_media", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(preview)!;
            var slider = (System.Windows.Controls.Slider)typeof(RecordingPreviewWindow)
                .GetField("_seek", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(preview)!;
            var playing = typeof(RecordingPreviewWindow)
                .GetField("_playing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            preview.Show();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (!preview.LoadCompleted && DateTime.UtcNow < deadline) Pump(TimeSpan.FromMilliseconds(50));
            if (!preview.MediaOpenedSuccessfully) throw new Exception("预览窗口无法打开真实成片。");
            Pump(TimeSpan.FromMilliseconds(300));
            if (slider.Maximum < 5.7 || slider.Maximum > 6.3)
                throw new Exception(
                    "预览进度条没有跟随成片时长：" + slider.Maximum
                    + " natural=" + media.NaturalDuration
                    + " time=" + preview.TimeText);
            if (!slider.IsEnabled) throw new Exception("成片已载入但预览进度条仍不可拖动。");
            if (preview.TimeText is "0:00 / 0:00" or "")
                throw new Exception("预览时间显示没有跟随成片时长：" + preview.TimeText);

            // 同一进程里跑过前面的媒体用例后，WPF media engine 可能不再推进（Position 恒为 0）。
            // 这种情况下只校验拖动契约，真实播放推进交给前台验收。
            var engineStart = media.Position;
            Pump(TimeSpan.FromMilliseconds(600));
            var engineAdvances = (media.Position - engineStart).TotalSeconds > 0.1;

            var seekTarget = slider.Maximum * 0.5;
            slider.Value = seekTarget;
            slider.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
            });
            Pump(TimeSpan.FromMilliseconds(60));
            if ((bool)playing.GetValue(preview)!) throw new Exception("拖动预览进度条时视频仍在播放。");
            slider.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            });
            if (preview.LastScrubTarget is null
                || Math.Abs(preview.LastScrubTarget.Value.TotalSeconds - seekTarget) > 0.3)
                throw new Exception("拖动预览进度条没有换算出跳转位置：" + preview.LastScrubTarget);
            if (!engineAdvances)
            {
                Console.WriteLine($"SEEK_LIVE_SKIP position={media.Position.TotalSeconds:F2} target={seekTarget:F2}");
            }
            else
            {
                // MediaElement 的跳转是异步的，所以有界等待真实播放位置跟上。
                var seekDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (media.Position.TotalSeconds < seekTarget - 0.5 && DateTime.UtcNow < seekDeadline)
                    Pump(TimeSpan.FromMilliseconds(100));
                if (media.Position.TotalSeconds < seekTarget - 0.5)
                    throw new Exception($"拖动预览进度条没有跳转播放位置：{media.Position.TotalSeconds:F2} vs {seekTarget:F2}");
            }
            if (!(bool)playing.GetValue(preview)!) throw new Exception("拖动结束后视频没有恢复播放。");
            preview.Close();
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    public static void RecordingEncoderUsesHardwareTransformsWithNativeBackpressure()
    {
        MediaFactory.MFStartup();
        try
        {
            using var attributes = MediaFoundationRecorder.CreateSinkWriterAttributes();
            var hardwareTransforms = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");
            var lookup = attributes.GetUInt32(hardwareTransforms, out var enabled);
            if (!lookup.Success || enabled != 1u)
                throw new Exception("MP4 写入器没有请求 Media Foundation 硬件编码。");
            var throttling = new Guid("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
            var throttlingLookup = attributes.GetUInt32(throttling, out var throttlingValue);
            if (throttlingLookup.Success && throttlingValue != 0u)
                throw new Exception("Daily recording must retain default native backpressure.");
            var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
            var recorder = File.ReadAllText(Path.Combine(root, "src", "ClipRay.App", "Recording", "MediaFoundationRecorder.cs"));
            if (!recorder.Contains("MFCreateSinkWriterFromURL(_paths.PartialPath, null, attributes)", StringComparison.Ordinal))
                throw new Exception("MP4 写入器创建时丢弃了硬件编码属性。");
        }
        finally { MediaFactory.MFShutdown(); }
    }

    public static void RecordingCapturesTheCursor()
    {
        if (!RecordingCapturePolicy.IncludeCursor)
            throw new Exception("录屏光标策略被关闭，成片里看不到鼠标。");
        var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var capture = File.ReadAllText(Path.Combine(root, "src", "ClipRay.App", "Recording", "GraphicsCaptureSource.cs"));
        if (!capture.Contains("RecordingCapturePolicy.IncludeCursor", StringComparison.Ordinal)
            || !capture.Contains("_includeCursor = includeCursor", StringComparison.Ordinal)
            || !capture.Contains("IsCursorCaptureEnabled = _includeCursor", StringComparison.Ordinal))
            throw new Exception("屏幕捕获没有按策略把光标写入成片。");
    }

    private static void Pump(TimeSpan span)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = span };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    public static void RecordingPreviewDoesNotClaimUnperformedCopy()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipray-preview-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(path, []);
        try
        {
            var preview = new RecordingPreviewWindow(path);
            var media = (System.Windows.Controls.MediaElement)typeof(RecordingPreviewWindow)
                .GetField("_media", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(preview)!;
            media.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MediaElement.MediaOpenedEvent));
            if (!preview.Status.StartsWith("已保存：", StringComparison.Ordinal) || preview.Status.Contains("已保存并复制"))
                throw new Exception("Playback claims a clipboard copy that did not occur.");
            preview.Close();
        }
        finally { File.Delete(path); }
    }

    public static void PendingPreviewWindowOpensBeforeTheFileExists()
    {
        var missing = Path.Combine(Path.GetTempPath(), "clipray-pending-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            // 成片还没 finalize 时路径并不存在；等待态必须允许这种构造。
            new RecordingPreviewWindow(missing, pending: true).Close();
        }
        catch (Exception exception)
        {
            throw new Exception("等待态预览窗在成片写完之前无法创建：" + exception.GetType().Name);
        }

        try
        {
            new RecordingPreviewWindow(missing).Close();
            throw new Exception("成片缺失时普通预览窗本应拒绝创建。");
        }
        catch (FileNotFoundException) { }

        var path = Path.Combine(Path.GetTempPath(), "clipray-pending-real-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(path, []);
        try
        {
            var preview = new RecordingPreviewWindow(path, pending: true);
            if (!preview.IsPending) throw new Exception("等待态预览窗没有进入等待态。");
            if (!preview.Status.Contains("正在保存", StringComparison.Ordinal))
                throw new Exception("等待态预览窗没有提示正在保存：" + preview.Status);
            if (preview.SeekEnabled) throw new Exception("成片未就绪时预览进度条不应可拖动。");
            preview.Fail("测试：收尾失败");
            if (preview.IsPending || !preview.Status.Contains("测试：收尾失败", StringComparison.Ordinal))
                throw new Exception("收尾失败后预览窗没有切出等待态：" + preview.Status);
            preview.Close();
        }
        finally { File.Delete(path); }
    }

    public static void WgcMapsCropIntoPhysicalMonitor()
    {
        var monitor = new System.Drawing.Rectangle(-1920, -100, 1920, 1080);
        var workArea = new System.Drawing.Rectangle(-1920, -60, 1920, 1040);
        var mapping = RecordingGeometry.MapToMonitor(workArea, [monitor]);
        if (mapping.MonitorBounds != monitor || mapping.CropInMonitor != new System.Drawing.Rectangle(0, 40, 1920, 1040))
            throw new Exception("Working area does not map to the real monitor and taskbar offset.");
        var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(root, "src", "ClipRay.App", "Capture", "ScreenCaptureService.cs"));
        if (!source.Contains("RecordingGeometry.MapToMonitor(", StringComparison.Ordinal)
            || !source.Contains("Forms.Screen.AllScreens", StringComparison.Ordinal))
            throw new Exception("WGC treats the cropped working area as a physical monitor.");
    }

    public static void FullScreenHotkeysFollowOriginalContract()
    {
        var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(root, "src", "ClipRay.App", "App.xaml.cs"));
        if (!source.Contains("RegisterAll(_settings.CurrentScreenHotkey, _settings.CurrentScreenSecondaryHotkey, () =>\n            _ = RunCurrentMonitorCaptureAsync(), failures);".Replace("\n", Environment.NewLine))
            && !source.Contains("RegisterAll(_settings.CurrentScreenHotkey, _settings.CurrentScreenSecondaryHotkey, () =>\n            _ = RunCurrentMonitorCaptureAsync(), failures);"))
            throw new Exception("Insert's binding does not use the taskbar-free capture action.");
        var additional = source.IndexOf("RegisterAll(_settings.TaskbarFreeCurrentScreenHotkey", StringComparison.Ordinal);
        var end = source.IndexOf("RegisterAll(_settings.VirtualDesktopHotkey", additional, StringComparison.Ordinal);
        if (!source[additional..end].Contains("RunCurrentMonitorCaptureIncludingTaskbarAsync"))
            throw new Exception("Ctrl+Insert's binding does not include the taskbar.");
    }

    public static void FreeCaptureUsesWholeDesktopBounds()
    {
        var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(root, "src", "ClipRay.App", "App.xaml.cs"));
        var start = source.IndexOf("private void StartInteractiveCapture(", StringComparison.Ordinal);
        var end = source.IndexOf("_overlay = new SelectionOverlayWindow", start, StringComparison.Ordinal);
        if (!source[start..end].Contains("CaptureVirtualDesktop()"))
            throw new Exception("Free selection is restricted to the current monitor instead of the desktop including all taskbars.");
    }

    public static void FramePacingDoesNotLoseRefreshTicks()
    {
        var type = typeof(VideoRecordingSample).Assembly.GetType("ClipRay.App.Recording.RecordingFramePacer");
        if (type is null) throw new Exception("Recording resets the throttle deadline to each accepted refresh tick.");
        var pacer = Activator.CreateInstance(type, [30, 1_000_000L])!;
        var accepted = 0;
        for (var index = 0; index < 120; index++)
            if ((bool)type.GetMethod("ShouldCapture")!.Invoke(pacer, [(long)index * 16600])!) accepted++;
        if (accepted is < 59 or > 61) throw new Exception("30fps target collapsed on near-60Hz input: " + accepted);
    }

    public static void RecordingPipelineTargetsDailyFramesPerSecond()
    {
        if (BackgroundTask5Tests.Run(["--background-task-5-tests","daily-policy","clock-pacer"]) != 0)
            throw new Exception("Daily frame-rate/bitrate/timeline contract failed.");
        var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var capture = File.ReadAllText(Path.Combine(root,"src","ClipRay.App","Recording","GraphicsCaptureSource.cs"));
        var encoder = File.ReadAllText(Path.Combine(root,"src","ClipRay.App","Recording","MediaFoundationRecorder.cs"));
        if (!capture.Contains("RecordingFrameRatePolicy.FramesPerSecond") || !capture.Contains("RecordingFrameRatePolicy.SampleDuration100Nanoseconds")
            || !encoder.Contains("RecordingFrameRatePolicy.FramesPerSecond")) throw new Exception("Capture or encoder bypassed shared daily clock.");
    }

    public static void StopShowsPreviewBeforeFinalizeFinishes()
    {
        if (BackgroundTask5Tests.Run(["--background-task-5-tests","app-pending"]) != 0)
            throw new Exception("Actual stop failed pending preview/physical ownership checks.");
    }

    public static void EncoderQueueIsBoundedAndRunsOffCaptureThread()
    {
        var type = typeof(VideoRecordingSample).Assembly.GetType("ClipRay.App.Recording.RecordingVideoPump");
        if (type is null) throw new Exception("Capture can run encoding inline and accumulate an unbounded write chain.");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = new List<long>();
        var captureThread = Environment.CurrentManagedThreadId;
        var callbackThread = captureThread;
        var pump = Activator.CreateInstance(type, [(Func<VideoRecordingSample, ValueTask>)(async sample =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            written.Add(sample.Timestamp100Nanoseconds);
            entered.TrySetResult();
            await release.Task;
        })])!;
        var post = type.GetMethod("TryPost")!;
        post.Invoke(pump, [new VideoRecordingSample(new byte[16], 2, 2, 0, 1)]);
        entered.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        for (var index = 1; index <= 100; index++)
            post.Invoke(pump, [new VideoRecordingSample(new byte[16], 2, 2, index, 1)]);
        if ((int)type.GetProperty("PendingCount")!.GetValue(pump)! > 2) throw new Exception("Encoding queue is unbounded.");
        release.TrySetResult();
        ((Task)type.GetMethod("CompleteAsync")!.Invoke(pump, null)!).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        if (callbackThread == captureThread || written.Count > 3 || written[^1] != 100)
            throw new Exception("Encoder blocks capture or loses the newest buffered frame.");
    }
}
