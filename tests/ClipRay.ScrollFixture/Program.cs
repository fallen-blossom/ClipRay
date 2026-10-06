using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ClipRay.Acceptance.Testing;

namespace ClipRay.ScrollFixture;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var root = FindWorkspace(AppContext.BaseDirectory);
        var catalog = AcceptanceCatalog.Load(Path.Combine(root, "tests", "ClipRay.Acceptance", "cases"));
        var scenario = catalog.Cases.Single(static item => item.Id == "scroll-multilingual-40-lines-01");
        var lines = scenario.Fields
            .Where(static item => item.Key.StartsWith("content_line.", StringComparison.Ordinal))
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(static item => item.Value)
            .ToArray();
        if (lines.Length < int.Parse(scenario.GetRequired("minimum_content_lines"), System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidDataException("The scroll fixture requires every declared content line.");
        if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"SCROLL_FIXTURE_READY lines={lines.Length}");
            return;
        }

        var viewer = CreateViewer(lines);
        var window = CreateWindow(viewer);
        var application = new Application();
        if (args.Contains("--scroll-probe", StringComparer.OrdinalIgnoreCase))
        {
            ProbeScrollableContent(window, viewer, lines.Length);
            return;
        }

        application.Run(window);
    }

    private static ScrollViewer CreateViewer(IReadOnlyList<string> lines)
    {
        var content = new StackPanel { Margin = new Thickness(28), Background = Brushes.White };
        content.Children.Add(new TextBlock { Text = "ClipRay 40-line multilingual scrolling fixture", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
        for (var index = 0; index < lines.Count; index++)
        {
            content.Children.Add(new TextBlock { Text = $"{index + 1:D2}. {lines[index]}", FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        }

        var viewer = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(viewer, "ClipRayLongCaptureScrollTarget");
        return viewer;
    }

    private static Window CreateWindow(ScrollViewer viewer) => new()
    {
        Title = "ClipRay Scroll Fixture",
        Width = 560,
        Height = 640,
        Content = viewer,
        Background = Brushes.White,
    };

    private static void ProbeScrollableContent(Window window, ScrollViewer viewer, int lineCount)
    {
        window.Show();
        window.UpdateLayout();
        var range = viewer.ScrollableHeight;
        viewer.ScrollToVerticalOffset(Math.Min(160, range));
        window.UpdateLayout();
        var offset = viewer.VerticalOffset;
        window.Close();
        if (range <= 0 || offset <= 0)
            throw new InvalidOperationException("The long capture fixture did not expose a scrollable viewport.");
        Console.WriteLine($"SCROLL_FIXTURE_SCROLLABLE lines={lineCount} range={range:0.##} offset={offset:0.##}");
    }

    private static string FindWorkspace(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ClipRay.sln"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("ClipRay workspace was not found.");
    }
}
