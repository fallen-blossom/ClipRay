using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ClipRay.App.Translation;
using ClipRay.App.Overlay;

namespace ClipRay.App.LiveTranslation;

public partial class LiveTranslationOverlayWindow : Window
{
    private readonly System.Drawing.Rectangle _region;
    private readonly bool _excludeFromCaptureRequested;
    private bool _displayAffinityApplied;
    private bool _hiddenForCapture;
    private long _appliedGeneration;

    public LiveTranslationOverlayWindow(
        System.Drawing.Rectangle region,
        bool showBorder = true,
        bool showInRecordings = false)
    {
        _region = region;
        _excludeFromCaptureRequested = !showInRecordings;
        InitializeComponent();
        SelectionBorder.Visibility = showBorder ? Visibility.Visible : Visibility.Collapsed;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => ReassertPhysicalBounds();
    }

    public bool DisplayAffinityApplied => _displayAffinityApplied;
    internal bool ExcludeFromCaptureRequested => _excludeFromCaptureRequested;
    internal bool IsSelectionBorderVisible => SelectionBorder.Visibility == Visibility.Visible;
    internal System.Windows.Media.Imaging.BitmapSource? RenderedImage =>
        TranslationCanvas.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault()?.Source
            as System.Windows.Media.Imaging.BitmapSource;

    public void Clear()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(Clear); return; }
        TranslationCanvas.Children.Clear();
    }

    public bool Apply(LiveTranslationFrame frame) => Apply(frame, 0);

    public bool Apply(LiveTranslationFrame frame, long generation)
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(() => Apply(frame, generation));
        }
        if (!LiveTranslationViewportResultPolicy.CanPresent(frame.ResultState)) return false;

        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var bottomRight = fromDevice.Transform(new System.Windows.Point(_region.Width, _region.Height));
        var rendered = new System.Windows.Controls.Image
        {
            Width = bottomRight.X,
            Height = bottomRight.Y,
            Source = TranslatedSelectionRenderer.RenderOverlay(_region.Width, _region.Height, frame.LayoutItems),
            Stretch = Stretch.Fill,
        };
        // Render the replacement before retiring the visible image.
        TranslationCanvas.Children.Clear();
        TranslationCanvas.Children.Add(rendered);
        ReassertPhysicalBounds();
        _appliedGeneration = generation;
        return true;
    }

    public Task<bool> AcknowledgePresentationAsync(long generation)
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.InvokeAsync(() => AcknowledgePresentationAsync(generation)).Task.Unwrap();
        return ObserveRenderCycleAsync(generation);
    }

    private async Task<bool> ObserveRenderCycleAsync(long generation)
    {
        var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (_appliedGeneration != generation)
            {
                rendered.TrySetResult(false);
                return;
            }
            // The overlay is hidden briefly for each source capture. A WPF
            // render callback during that interval is not a visible frame;
            // wait for the next callback after capture restores the window.
            if (IsVisible && RenderedImage is not null)
                rendered.TrySetResult(true);
        };
        CompositionTarget.Rendering += handler;
        try
        {
            await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Render);
            return await Task.WhenAny(rendered.Task, Task.Delay(1500)) == rendered.Task
                && await rendered.Task;
        }
        finally
        {
            CompositionTarget.Rendering -= handler;
        }
    }

    public void BeforeCapture()
    {
        if (!LiveTranslationCaptureVisibilityPolicy.MustHideDuringCapture(_displayAffinityApplied)) return;
        _hiddenForCapture = IsVisible;
        if (_hiddenForCapture) Hide();
    }

    public void AfterCapture()
    {
        if (!_hiddenForCapture) return;
        _hiddenForCapture = false;
        Show();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        extendedStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        _ = SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(extendedStyle));
        ReassertPhysicalBounds();
        _displayAffinityApplied = _excludeFromCaptureRequested &&
            SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
    }

    internal void ReassertPhysicalBounds()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ReassertPhysicalBounds);
            return;
        }
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        _ = SetWindowPos(
            handle,
            HWND_TOPMOST,
            _region.X,
            _region.Y,
            _region.Width,
            _region.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private static System.Windows.Media.Color ToMediaColor(System.Drawing.Color color) =>
        System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
}
