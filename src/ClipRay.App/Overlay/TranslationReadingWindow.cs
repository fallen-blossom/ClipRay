using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace ClipRay.App.Overlay;

/// <summary>An explicitly opened, local-only reader; never a replacement for the overlay.</summary>
public sealed class TranslationReadingWindow : Window
{
    private readonly TextBox _text;
    public TranslationReadingWindow(string text, bool english)
    {
        Title = english ? "Translations kept off the image" : "未原位显示的译文";
        Width = 460; Height = 340; MinWidth = 280; MinHeight = 180;
        Background = new SolidColorBrush(Color.FromRgb(18, 22, 32));
        Foreground = Brushes.White;
        ShowInTaskbar = false;
        _text = new TextBox
        {
            Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 16, Padding = new Thickness(12),
            Margin = new Thickness(12), Background = Background, Foreground = Foreground,
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 57, 74)), BorderThickness = new Thickness(1),
            SelectionBrush = new SolidColorBrush(Color.FromRgb(105, 91, 145)), SelectionOpacity = .55,
        };
        System.Windows.Automation.AutomationProperties.SetName(_text, Title);
        Content = _text;
        PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { Close(); args.Handled = true; } };
        Loaded += (_, _) => _text.Focus();
    }

    public void UpdateText(string text)
    {
        if (_text.Text != text) _text.Text = text;
    }

    public bool ExcludeFromCapture()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        return SetWindowDisplayAffinity(handle, 0x11);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
}
