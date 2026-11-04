using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using ClipRay.App.Settings;

namespace ClipRay.App.Scrolling;

/// <summary>Owns visible feedback between capture completion and the result window.</summary>
internal sealed class ScrollCaptureProcessingWindow : Window, IDisposable
{
    private readonly TextBlock _status;
    private readonly string _language;
    private bool _completed;

    internal ScrollCaptureProcessingWindow(string language)
    {
        _language = language;
        Title = T("长截图处理中", "Processing scrolling capture");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowActivated = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1D, 0x27));
        var panel = new StackPanel { Margin = new Thickness(20) };
        _status = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(_status);
        panel.Children.Add(new System.Windows.Controls.ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap,
            Text = T("关闭此窗口只会隐藏进度，后台仍会继续处理；失败时保留截图材料。", "Closing this window hides progress. Processing continues in the background; captured pieces are kept on failure.") });
        Content = panel;
        SetStage("正在合成长截图…", "Composing scrolling capture…");
        Closing += (_, e) => { if (!_completed) { e.Cancel = true; Hide(); } };
    }

    private string T(string chinese, string english) => AppInterfaceLanguage.Text(_language, chinese, english);
    internal string Stage => _status.Text;
    internal void SetStage(string chinese, string english) => _status.Text = T(chinese, english);
    public void Dispose() { if (_completed) return; _completed = true; Close(); }
}
