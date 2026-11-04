using System.Drawing;
using System.IO;
using ClipRay.App;
using ClipRay.App.Capture;
using ClipRay.App.Settings;
using Forms = System.Windows.Forms;

/// <summary>Uses only synthetic metadata/files and injected actions; never opens Explorer or touches the clipboard.</summary>
internal static class RecentCaptureTrayTests
{
    public static void Run()
    {
        VerifyPrimaryAndSecondaryActions();
        VerifyLocatorDispatchAndFailure();
        VerifyActualTraySettingsIconAndRecoveryRemoval();
        Console.WriteLine("PASS: recent capture locate/copy actions, locator safety/failures, settings gear and recovery entry removal");
    }

    private static void VerifyPrimaryAndSecondaryActions()
    {
        var items = new[]
        {
            new RecentCaptureItem(@"C:\fixture\截图 中文, sample.png", RecentCaptureKind.Screenshot, DateTime.UtcNow, 10),
            new RecentCaptureItem(@"C:\fixture\long.png", RecentCaptureKind.LongCapture, DateTime.UtcNow, 20),
            new RecentCaptureItem(@"C:\fixture\recording.mp4", RecentCaptureKind.Recording, DateTime.UtcNow, 30),
        };
        foreach (var language in new[] { "zh-CN", "en" })
        {
            using var menu = new Forms.ToolStripMenuItem();
            var located = new List<RecentCaptureItem>();
            var copied = new List<RecentCaptureItem>();
            RecentCaptureTrayMenu.Populate(menu, items, language, located.Add, copied.Add);
            var primary = menu.DropDownItems.OfType<Forms.ToolStripMenuItem>().Where(x => x.Name == "recentCaptureLocate").ToArray();
            Check(primary.Length == items.Length && primary.All(x => !x.HasDropDownItems), "Primary items must be direct clickable leaves.");
            for (var index = 0; index < primary.Length; index++) primary[index].PerformClick();
            Check(located.SequenceEqual(items) && copied.Count == 0, "Primary action must locate each exact item without copying.");
            var copies = menu.DropDownItems.OfType<Forms.ToolStripMenuItem>().Single(x => x.Name == "recentCaptureCopies");
            Check(copies.Text == (language == "en" ? "Copy again" : "重新复制"), "Copy submenu label is not localized.");
            Check(primary[0].ToolTipText!.Contains(language == "en" ? "Show in File Explorer" : "在文件管理器中显示", StringComparison.Ordinal), "Primary action tooltip is ambiguous.");
            foreach (var copy in copies.DropDownItems.OfType<Forms.ToolStripMenuItem>()) copy.PerformClick();
            Check(copied.SequenceEqual(items) && located.Count == items.Length, "Secondary action must copy each exact item without locating.");
            RecentCaptureTrayMenu.Populate(menu, [], language, located.Add, copied.Add);
            Check(menu.DropDownItems.Count == 1 && !menu.DropDownItems[0].Enabled
                && menu.DropDownItems[0].Text == (language == "en" ? "No saved captures" : "暂无已保存的截取"), "Empty state must be localized and disabled.");
        }
    }

    private static void VerifyLocatorDispatchAndFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipray-locate-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "中文 空格, literal & file.png");
            File.WriteAllText(path, "synthetic fixture only");
            string? selected = null;
            Check(SavedCaptureFileLocator.TrySelect(path, value => { selected = value; return true; }) == SavedCaptureLocationResult.Selected
                && selected == Path.GetFullPath(path), "Unicode, spaces and punctuation must reach the selector intact.");
            Check(SavedCaptureFileLocator.TrySelect(path, _ => false) == SavedCaptureLocationResult.ShellUnavailable, "Shell refusal must remain a failure.");
            Check(SavedCaptureFileLocator.TrySelect(path, _ => throw new IOException("fixture")) == SavedCaptureLocationResult.ShellUnavailable, "Shell exceptions must be reported.");
            var calls = 0;
            foreach (var unavailable in new[] { Path.Combine(root, "missing.png"), root, "relative.png", @"\\server\share\file.png", "" })
                Check(SavedCaptureFileLocator.TrySelect(unavailable, _ => { calls++; return true; }) == SavedCaptureLocationResult.FileUnavailable,
                    "Unavailable or nonlocal paths must fail before Shell dispatch.");
            Check(calls == 0, "Invalid paths reached the Shell selector.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void VerifyActualTraySettingsIconAndRecoveryRemoval()
    {
        foreach (var language in new[] { "zh-CN", "en" })
        {
            var app = BackgroundTask3LifetimeTests.FakeApp();
            BackgroundTask3LifetimeTests.Set(app, "_settings", AppSettings.CreateDefault(Path.GetTempPath(), language));
            using var menu = app.CreateTrayMenu();
            var settings = menu.Items.OfType<Forms.ToolStripMenuItem>().Single(x => x.Text == (language == "en" ? "Settings" : "设置"));
            Check(menu.ShowImageMargin && settings.Image is Bitmap { Width: 16, Height: 16 }, "Actual settings menu must show a normal-size icon margin and gear.");
            var image = (Bitmap)settings.Image!;
            Check(image.GetPixel(8, 8).A == 0 && Enumerable.Range(0, 16).Any(x => image.GetPixel(x, 4).A > 0), "Settings gear must have a visible body and hollow center.");
            Check(!menu.Items.OfType<Forms.ToolStripMenuItem>().Any(x => x.Text is "恢复长截图" or "Recover scrolling capture"), "Recovery entry must be absent from the actual tray.");
            AppInterfaceLanguage.UpdateTray(menu.Items, language == "en" ? "zh-CN" : "en");
            Check(settings.Text == (language == "en" ? "设置" : "Settings") && settings.Image == image, "Language changes must preserve the settings gear and mapping.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
