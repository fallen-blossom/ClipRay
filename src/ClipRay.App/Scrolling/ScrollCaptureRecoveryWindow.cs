using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ClipRay.App.Settings;

namespace ClipRay.App.Scrolling;

internal sealed class ScrollCaptureRecoveryWindow : Window
{
    public ScrollCaptureRecoveryWindow(ScrollCaptureRecoveryCandidate candidate, BitmapSource preview,string language)
    {
        string T(string chinese,string english)=>AppInterfaceLanguage.Text(language,chinese,english);
        Title=T("恢复长截图","Recover scrolling capture");
        Width=900; Height=700; MinWidth=450; MinHeight=350; WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel {Margin=new Thickness(12)};
        var details=new TextBlock {Text=$"{candidate.CreatedUtc!.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {candidate.Width}×{candidate.Height} · {candidate.FrameCount} "+T("帧","frames")
            +"\n"+T("关闭或取消仍保留原片段。","Closing or cancelling keeps the original pieces."),Margin=new Thickness(0,0,0,10)};
        DockPanel.SetDock(details,Dock.Top);root.Children.Add(details);
        var buttons=new StackPanel {Orientation=System.Windows.Controls.Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right};
        var cancel=new System.Windows.Controls.Button {Content=T("保留并关闭","Keep and close"),Padding=new Thickness(12,8,12,8),Margin=new Thickness(4),IsCancel=true};
        cancel.Click+=(_,_)=>Close();
        var save=new System.Windows.Controls.Button {Content=T("保存 PNG","Save PNG"),Padding=new Thickness(12,8,12,8),Margin=new Thickness(4),IsDefault=true};
        save.Click+=(_,_)=>DialogResult=true;
        buttons.Children.Add(cancel);buttons.Children.Add(save);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer {Content=new System.Windows.Controls.Image {Source=preview,Stretch=System.Windows.Media.Stretch.Uniform},
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        Content=root;
        PreviewKeyDown+=(_,e)=> {if(e.Key==Key.Escape){e.Handled=true;Close();}};
    }
}
