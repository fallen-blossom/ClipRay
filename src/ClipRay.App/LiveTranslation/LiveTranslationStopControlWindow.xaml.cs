using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ClipRay.App.Settings;
using Forms = System.Windows.Forms;

namespace ClipRay.App.LiveTranslation;

public partial class LiveTranslationStopControlWindow : Window
{
    private readonly System.Drawing.Rectangle _region;
    private readonly System.Drawing.Rectangle _bounds;
    private readonly string _interfaceLanguage;

    public LiveTranslationStopControlWindow(System.Drawing.Rectangle region, string interfaceLanguage = "zh-CN")
    {
        _region = region;
        _interfaceLanguage = AppInterfaceLanguage.Normalize(interfaceLanguage);
        var workArea = Forms.Screen.FromRectangle(_region).WorkingArea;
        CanPlaceOutside = LiveTranslationStopPlacement.TryPlaceOutside(
            _region, workArea, ControlPhysicalSize(region), out _bounds);
        InitializeComponent();
        StopButton.Content = T("停止翻译", "Stop translation");
        StopButton.ToolTip = T("双击 Esc 也可停止", "Press Esc twice to stop");
        EscapeHint.Text = T("双击 Esc 退出", "Press Esc twice to exit");
        RetryButton.Content = T("重试", "Retry");
        StatusText.Text = T("正在启动本地机翻…", "Starting local machine translation…");
        SourceInitialized += OnSourceInitialized;
    }

    private string T(string chinese, string english) => AppInterfaceLanguage.Text(_interfaceLanguage, chinese, english);

    private static System.Drawing.Size ControlPhysicalSize(System.Drawing.Rectangle region)
    {
        var monitor = MonitorFromPoint(new NativePoint(region.X, region.Y), 2);
        var scale = GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0 ? dpiX / 96d : 1d;
        return new System.Drawing.Size((int)Math.Ceiling(260 * scale), (int)Math.Ceiling(112 * scale));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    public event EventHandler? RequestStop;
    public event EventHandler? RequestRetry;

    public bool CanPlaceOutside { get; }

    private void OnStop(object sender, RoutedEventArgs e) => RequestStop?.Invoke(this, EventArgs.Empty);
    private void OnRetry(object sender, RoutedEventArgs e) => RequestRetry?.Invoke(this, EventArgs.Empty);
    public void ShowStatus(string text, bool canRetry = false)
    {
        StatusText.Text = _interfaceLanguage == "en" ? EnglishStatus(text) : text;
        RetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string EnglishStatus(string text)
    {
        var translations = new (string Chinese, string English)[]
        {
            ("正在识别并机翻…", "Recognizing and translating locally…"),
            ("正在识别并翻译…", "Recognizing and translating…"),
            ("已更新 · 等待画面变化", "Updated · waiting for screen changes"),
            ("部分翻译完成 · 未成功区域保留原文，稍后重试", "Partly translated · original text kept in failed regions; retrying soon"),
            ("已更新可用区域 · 未完成区域稍后重试", "Available regions updated · retrying unfinished regions soon"),
            ("暂未更新 · 后台恢复中", "No update yet · recovering in the background"),
            ("已停止：旧画面处理未能及时取消 · 请重启 ClipRay", "Stopped: earlier frame processing did not cancel in time · restart ClipRay"),
            ("会话已停止 · 后台任务尚未退出，暂不能重新开始", "Session stopped · background work is still exiting; restart is unavailable"),
            ("暂时没有最新原图，请稍后重试。", "No recent original image yet. Try again shortly."),
            ("再按一次 Esc 停止实时翻译", "Press Esc again to stop live translation"),
            ("Esc 未注册 · 请点击停止按钮或使用停止快捷键", "Esc is unavailable · use the Stop button or stop shortcut"),
            ("本地机翻", "Local machine translation"),
            ("本地 LMT", "Local LMT"),
            ("简体中文", "Simplified Chinese"),
        };
        foreach (var (chinese, english) in translations)
            text = text.Replace(chinese, english, StringComparison.Ordinal);
        return text;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!CanPlaceOutside) return;
        _ = SetWindowPos(handle, HWND_TOPMOST, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
