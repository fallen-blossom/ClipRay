using System.IO;
using ClipRay.App.Capture;
using ClipRay.App.Settings;
using Forms = System.Windows.Forms;

namespace ClipRay.App;

internal static class RecentCaptureTrayMenu
{
    internal static void Populate(Forms.ToolStripMenuItem menu, IReadOnlyList<RecentCaptureItem> items,
        string language, Action<RecentCaptureItem> locate, Action<RecentCaptureItem> copy)
    {
        string T(string chinese, string english) => AppInterfaceLanguage.Text(language, chinese, english);
        menu.DropDownItems.Clear();
        if (items.Count == 0)
        {
            menu.DropDownItems.Add(T("暂无已保存的截取", "No saved captures")).Enabled = false;
            return;
        }
        var copies = new Forms.ToolStripMenuItem(T("重新复制", "Copy again")) { Name = "recentCaptureCopies" };
        foreach (var item in items)
        {
            var kind = item.Kind switch
            {
                RecentCaptureKind.Recording => T("视频", "Video"),
                RecentCaptureKind.LongCapture => T("长截图", "Scrolling capture"),
                _ => T("截图", "Screenshot"),
            };
            var label = $"{item.LastWriteTimeUtc.ToLocalTime():MM-dd HH:mm:ss} · {kind}";
            var filename = Path.GetFileName(item.Path);
            var entry = new Forms.ToolStripMenuItem(label)
            {
                Name = "recentCaptureLocate",
                ToolTipText = T($"在文件管理器中显示：{filename}", $"Show in File Explorer: {filename}"),
            };
            entry.Click += (_, _) => locate(item);
            menu.DropDownItems.Add(entry);
            var copyEntry = new Forms.ToolStripMenuItem(label) { Name = "recentCaptureCopy", ToolTipText = filename };
            copyEntry.Click += (_, _) => copy(item);
            copies.DropDownItems.Add(copyEntry);
        }
        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        menu.DropDownItems.Add(copies);
        TrayMenuTheme.Apply(menu.DropDown);
        TrayMenuTheme.Apply(copies.DropDown);
    }
}
