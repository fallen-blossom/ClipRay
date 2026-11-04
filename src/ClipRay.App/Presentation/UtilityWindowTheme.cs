using System.ComponentModel;
using System.Windows;
using Window = System.Windows.Window;

namespace ClipRay.App.Presentation;

/// <summary>Prepares only the app-owned utility windows that opt into WPF Fluent resources.</summary>
internal static class UtilityWindowTheme
{
    internal static void Prepare()
    {
        // WPF Fluent otherwise requests a Mica backdrop. Keep these small utility
        // windows opaque and inexpensive; transparent capture overlays do not opt in.
        AppContext.SetSwitch("Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop", true);
    }

    internal static void ApplyPalette(Window window)
    {
        var palette = new ResourceDictionary
        {
            Source = new Uri("/ClipRay.App;component/Presentation/UtilityWindowPalette.xaml", UriKind.Relative),
        };
        var closed = false;
        var watching = false;
        void Refresh()
        {
            if (closed) return;
            var dictionaries = window.Resources.MergedDictionaries;
            if (SystemParameters.HighContrast) dictionaries.Remove(palette);
            else if (dictionaries.LastOrDefault() != palette)
            {
                dictionaries.Remove(palette);
                dictionaries.Add(palette);
            }
        }
        PropertyChangedEventHandler changed = (_, args) =>
        {
            if (args.PropertyName != nameof(SystemParameters.HighContrast) || closed) return;
            if (window.Dispatcher.CheckAccess()) Refresh();
            else if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.InvokeAsync(Refresh);
        };
        Refresh();
        RoutedEventHandler loaded = (_, _) =>
        {
            if (closed || watching) return;
            Refresh();
            watching = true;
            SystemParameters.StaticPropertyChanged += changed;
        };
        window.Loaded += loaded;
        window.Closed += (_, _) =>
        {
            closed = true;
            window.Loaded -= loaded;
            if (watching) SystemParameters.StaticPropertyChanged -= changed;
        };
    }
}
