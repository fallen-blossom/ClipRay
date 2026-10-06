using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipRay.App.Settings;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace ClipRay.App.Recording;

public interface IRecordingFileClipboard
{
    void SetFile(string path);
}

public sealed class WindowsRecordingFileClipboard : IRecordingFileClipboard
{
    public void SetFile(string path)
    {
        var files = new StringCollection { Path.GetFullPath(path) };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetFileDropList(files);
                return;
            }
            catch (ExternalException) when (attempt < 4)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
        }
    }
}

public static class RecordingResultDelivery
{
    public static bool TryCopyFile(string path, IRecordingFileClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (!File.Exists(path)) return false;
        try
        {
            clipboard.SetFile(Path.GetFullPath(path));
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class RecordingPreviewWindow : Window
{
    private readonly string _interfaceLanguage;
    private readonly string _path;
    private readonly MediaElement _media;
    private readonly Slider _seek;
    private readonly TextBlock _time;
    private readonly TextBlock _status;
    private readonly System.Windows.Threading.DispatcherTimer _progress;
    private readonly StackPanel _transportActions;
    private TimeSpan _duration;
    private readonly DateTime _pendingSince;
    private bool _pending;
    private bool _closed;
    private bool _playing;
    private bool _copied;
    private bool _scrubbing;
    private bool _resumeAfterScrub;

    public bool LoadCompleted { get; private set; }
    public bool MediaOpenedSuccessfully { get; private set; }
    public string Status => _status.Text;
    public string TimeText => _time.Text;
    public double SeekMaximum => _seek.Maximum;
    public bool SeekEnabled => _seek.IsEnabled;
    public bool IsPending => _pending;

    /// <summary>最近一次拖动换算出的成片位置；null 表示还没有拖动过。</summary>
    public TimeSpan? LastScrubTarget { get; private set; }

    public RecordingPreviewWindow(
        string path,
        IRecordingFileClipboard? clipboard = null,
        bool initiallyCopied = false,
        bool pending = false,
        string interfaceLanguage = "zh-CN")
    {
        _interfaceLanguage = AppInterfaceLanguage.Normalize(interfaceLanguage);
        _copied = initiallyCopied;
        _path = Path.GetFullPath(path);
        // 编码收尾还没写完 moov 时成片不可播；先亮出窗口并置为等待态，
        // 由 AttachMedia 在 finalize 结束后接上媒体。
        _pending = pending;
        _pendingSince = DateTime.UtcNow;
        if (!_pending && !File.Exists(_path)) throw new FileNotFoundException("Recording preview file is missing.", _path);
        clipboard ??= new WindowsRecordingFileClipboard();
        if (!_pending) _duration = RecordingMediaProbe.TryReadDuration(_path) ?? TimeSpan.Zero;

        Title = T("ClipRay 录屏预览", "ClipRay Recording Preview");
        Width = 820;
        Height = 560;
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(MediaColor.FromRgb(0x0C, 0x10, 0x17));
        Foreground = MediaBrushes.White;

        _media = new MediaElement
        {
            Source = _pending ? null : new Uri(_path),
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Stop,
            Stretch = Stretch.Uniform,
            ScrubbingEnabled = true,
        };
        _seek = new Slider
        {
            Minimum = 0,
            Maximum = _duration.TotalSeconds,
            Value = 0,
            IsEnabled = false,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _seek.AddHandler(
            System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
            new System.Windows.Controls.Primitives.DragStartedEventHandler(OnScrubStarted));
        _seek.AddHandler(
            System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            new System.Windows.Controls.Primitives.DragCompletedEventHandler(OnScrubCompleted));
        _seek.ValueChanged += (_, _) =>
        {
            if (_scrubbing) UpdateTimeText();
        };
        _time = new TextBlock
        {
            Text = RecordingPreviewTransport.FormatPosition(TimeSpan.Zero, _duration),
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xA9, 0xB7, 0xCC)),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 96,
            TextAlignment = TextAlignment.Right,
        };
        _status = new TextBlock
        {
            Text = _pending ? T("正在保存录屏…", "Saving recording…") : T("正在载入录屏…", "Loading recording…"),
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0xA9, 0xB7, 0xCC)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _progress = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _progress.Tick += (_, _) =>
        {
            if (_pending)
            {
                var seconds = (DateTime.UtcNow - _pendingSince).TotalSeconds;
                _status.Text = T($"正在保存录屏…已用 {seconds:F1} 秒", $"Saving recording… {seconds:F1} s elapsed");
                return;
            }
            SyncTransport();
        };
        var toggle = CreateButton(T("暂停", "Pause"));
        toggle.Click += (_, _) =>
        {
            if (_playing)
            {
                _media.Pause();
                toggle.Content = T("播放", "Play");
            }
            else
            {
                _media.Play();
                toggle.Content = T("暂停", "Pause");
            }
            _playing = !_playing;
        };
        var copy = CreateButton(T("复制视频文件", "Copy video file"));
        copy.Click += (_, _) =>
        {
            var copied = RecordingResultDelivery.TryCopyFile(_path, clipboard);
            _copied |= copied;
            _status.Text = copied
                ? T("视频文件已复制到剪贴板", "Video file copied to clipboard")
                : T("剪贴板正被占用，请稍后重试", "Clipboard is busy. Try copying again shortly.");
        };
        var folder = CreateButton(T("打开所在位置", "Open file location"));
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_path}\"") { UseShellExecute = true });
        var close = CreateButton(T("关闭", "Close"));
        close.Click += (_, _) => Close();

        _transportActions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            IsEnabled = !_pending,
            Visibility = _pending ? Visibility.Collapsed : Visibility.Visible,
        };
        _transportActions.Children.Add(toggle);
        _transportActions.Children.Add(copy);
        _transportActions.Children.Add(folder);
        var controls = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        controls.Children.Add(_transportActions);
        controls.Children.Add(close);
        var footer = new Grid { Margin = new Thickness(14, 10, 14, 12) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_status);
        Grid.SetColumn(controls, 1);
        footer.Children.Add(controls);

        var transport = new Grid { Margin = new Thickness(14, 10, 14, 0) };
        transport.ColumnDefinitions.Add(new ColumnDefinition());
        transport.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        transport.Children.Add(_seek);
        Grid.SetColumn(_time, 1);
        transport.Children.Add(_time);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_media);
        Grid.SetRow(transport, 1);
        root.Children.Add(transport);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;

        Loaded += (_, _) =>
        {
            if (_media.Source is not null)
            {
                _media.Play();
                _playing = true;
            }
            _progress.Start();
        };
        _media.MediaOpened += (_, _) =>
        {
            LoadCompleted = true;
            MediaOpenedSuccessfully = true;
            _media.Play();
            _playing = true;
            _seek.IsEnabled = true;
            SyncTransport();
            _progress.Start();
            _status.Text = _copied
                ? T($"已保存并复制：{Path.GetFileName(_path)}", $"Saved and copied: {Path.GetFileName(_path)}")
                : T($"已保存：{Path.GetFileName(_path)}", $"Saved: {Path.GetFileName(_path)}");
        };
        _media.MediaEnded += (_, _) =>
        {
            _media.Position = TimeSpan.Zero;
            _media.Play();
            SyncTransport();
        };
        _media.MediaFailed += (_, _) =>
        {
            LoadCompleted = true;
            _status.Text = _copied
                ? T("系统播放器无法预览；视频文件已保留并复制到剪贴板",
                    "Windows could not preview the recording. The file was kept and copied to the clipboard.")
                : T("系统播放器无法预览；视频文件已保留，可重试复制",
                    "Windows could not preview the recording. The file was kept; you can retry copying it.");
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _progress.Stop();
            _media.Close();
        };
    }

    /// <summary>finalize 结束后把写好的成片接到等待态的预览窗上。</summary>
    public void AttachMedia(bool copied)
    {
        if (_closed || !_pending) return;
        _pending = false;
        _copied |= copied;
        _duration = RecordingMediaProbe.TryReadDuration(_path) ?? TimeSpan.Zero;
        _seek.Maximum = _duration.TotalSeconds;
        _transportActions.IsEnabled = true;
        _transportActions.Visibility = Visibility.Visible;
        _status.Text = T("正在载入录屏…", "Loading recording…");
        _media.Source = new Uri(_path);
        _media.Play();
        _playing = true;
        _progress.Start();
    }

    /// <summary>收尾失败时保留窗口，但不再把它当成一份可用的成片。</summary>
    public void Fail(string message)
    {
        if (_closed) return;
        _pending = false;
        _progress.Stop();
        _status.Text = message;
    }

    private string T(string chinese, string english) =>
        AppInterfaceLanguage.Text(_interfaceLanguage, chinese, english);

    private TimeSpan MediaDuration =>
        _duration > TimeSpan.Zero
            ? _duration
            : _media.NaturalDuration.HasTimeSpan ? _media.NaturalDuration.TimeSpan : TimeSpan.Zero;

    private void OnScrubStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs args)
    {
        _scrubbing = true;
        _resumeAfterScrub = _playing;
        _media.Pause();
        _playing = false;
        _progress.Stop();
    }

    private void OnScrubCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs args)
    {
        _scrubbing = false;
        var duration = MediaDuration;
        var fraction = _seek.Maximum > 0 ? _seek.Value / _seek.Maximum : 0d;
        var target = RecordingPreviewTransport.PositionFromFraction(fraction, duration);
        LastScrubTarget = target;
        if (duration > TimeSpan.Zero) _media.Position = target;
        UpdateTimeText(target);
        if (_resumeAfterScrub)
        {
            _media.Play();
            _playing = true;
            _progress.Start();
        }
        _resumeAfterScrub = false;
    }

    private void SyncTransport()
    {
        var duration = MediaDuration;
        if (duration <= TimeSpan.Zero) return;
        if (Math.Abs(_seek.Maximum - duration.TotalSeconds) > 0.001) _seek.Maximum = duration.TotalSeconds;
        if (!_scrubbing)
        {
            _seek.Value = RecordingPreviewTransport.FractionFromPosition(_media.Position, duration) * _seek.Maximum;
        }
        UpdateTimeText(_media.Position);
    }

    private void UpdateTimeText() => UpdateTimeText(RecordingPreviewTransport.PositionFromFraction(
        _seek.Maximum > 0 ? _seek.Value / _seek.Maximum : 0d,
        MediaDuration));

    private void UpdateTimeText(TimeSpan position) =>
        _time.Text = RecordingPreviewTransport.FormatPosition(position, MediaDuration);

    private static System.Windows.Controls.Button CreateButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(8, 0, 0, 0),
        Padding = new Thickness(12, 7, 12, 7),
        Foreground = MediaBrushes.White,
        Background = new SolidColorBrush(MediaColor.FromRgb(0x20, 0x29, 0x3A)),
        BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x3A, 0x48, 0x61)),
        Cursor = System.Windows.Input.Cursors.Hand,
    };
}
