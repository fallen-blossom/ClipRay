using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Settings;
using System.Globalization;

namespace ClipRay.App.Translation;

public partial class TranslationWindow : Window
{
    private readonly Func<TranslationRequest, TranslationRouteKind, CancellationToken, Task<TranslationResult>> _translate;
    private readonly UiOperationOwner _operations = new();
    private readonly string _interfaceLanguage;
    private readonly IReadOnlyDictionary<TranslationRouteKind, TranslationFileCheck?> _routeFileChecks;
    private ComboBoxItem? _nonMachineSource;
    private ComboBoxItem? _nonMachineTarget;
    private bool _machineLanguageOptionsActive;
    private bool _showingMissingRoute;
    private bool _closed;

    public event EventHandler<LiveTranslationStartRequest>? LiveTranslationRequested;

    public TranslationWindow(IDeepSeekTranslationProvider provider, string? credentialName, string model, string interfaceLanguage = "zh-CN")
        : this(
            (request, _, cancellationToken) => string.IsNullOrWhiteSpace(credentialName)
                ? Task.FromResult(new TranslationResult(false, string.Empty, "credential-missing"))
                : provider.TranslateAsync(CloneWithModel(request, model), credentialName, cancellationToken),
            TranslationRouteKind.DeepSeekFlash,
            interfaceLanguage)
    {
    }

    public TranslationWindow(
        Func<TranslationRequest, TranslationRouteKind, CancellationToken, Task<TranslationResult>> translate,
        TranslationRouteKind initialRoute = TranslationRouteKind.LocalLmt,
        string interfaceLanguage = "zh-CN",
        Func<TranslationRouteKind, TranslationFileCheck?>? fileCheck = null)
    {
        _translate = translate;
        _interfaceLanguage = AppInterfaceLanguage.Normalize(interfaceLanguage);
        _routeFileChecks = TranslationRouteCatalog.Options.ToDictionary(option => option.Kind,
            option => fileCheck?.Invoke(option.Kind));
        InitializeComponent();
        SettingsText.SetLanguage(this, _interfaceLanguage);
        Title = T("ClipRay 翻译", "ClipRay Translation");
        RouteBox.ItemsSource = TranslationRouteCatalog.Options
            .Where(option => option.Kind == initialRoute || !RouteMissing(option.Kind))
            .Select(option => option with
        {
            DisplayName = option.Kind switch
            {
                TranslationRouteKind.LocalLmt => T(option.DisplayName, "Local LMT (private)"),
                TranslationRouteKind.Machine => T(option.DisplayName, "Local machine translation (offline)"),
                _ => option.DisplayName,
            },
        }).ToArray();
        RouteBox.SelectedItem = ((TranslationRouteOption[])RouteBox.ItemsSource).First(option => option.Kind == TranslationRouteCatalog.Get(initialRoute).Kind);
        SetAccessibleNames();
        UpdateRouteStatus();
        Closed += (_, _) =>
        {
            _closed = true;
            _operations.Close();
        };
    }

    public UiOperation BeginOperation(CancellationToken cancellationToken = default) => _operations.Begin(cancellationToken);

    private string T(string chinese, string english) => AppInterfaceLanguage.Text(_interfaceLanguage, chinese, english);

    private void SetAccessibleNames()
    {
        System.Windows.Automation.AutomationProperties.SetName(SourceLanguageBox, T("源语言", "Source language"));
        System.Windows.Automation.AutomationProperties.SetName(TargetLanguageBox, T("目标语言", "Target language"));
        System.Windows.Automation.AutomationProperties.SetName(RouteBox, T("本次模型", "Model for this session"));
        System.Windows.Automation.AutomationProperties.SetName(InputText, T("原文", "Source text"));
        System.Windows.Automation.AutomationProperties.SetName(ResultText, T("译文", "Translation result"));
    }

    public bool TrySetResult(UiOperation operation, string input, TranslationResult result)
    {
        if (_closed || !_operations.IsCurrent(operation.Version)) return false;
        InputText.Text = input;
        ResultText.Text = result.Text;
        var providerStatus = _interfaceLanguage == "en" ? TranslationStatusText.EnglishProviderStatus(result.Status) : TranslationStatusText.LocalizeProviderStatus(result.Status);
        var costStatus = _interfaceLanguage == "en" ? EnglishCostEstimate(result.CostEstimate) : TranslationStatusText.FormatCostEstimate(result.CostEstimate);
        ProgressStatus.Text = costStatus.Length == 0 ? providerStatus : $"{providerStatus} {costStatus}";
        ProgressStatus.ToolTip = result.CostEstimate is not null
            ? T(TranslationStatusText.FormatDeepSeekTooltip(result.CostEstimate), "Cost is estimated from API token usage; check the DeepSeek bill for the final amount.")
            : result.IsSuccess ? T("翻译已完成。", "Translation completed.") : T("翻译失败时不会显示私密诊断。", "Private diagnostics are hidden if translation fails.");
        AnimateTranslateButton(result.IsSuccess ? 1.035 : 0.975);
        return true;
    }

    private TranslationRouteKind SelectedRoute =>
        (RouteBox.SelectedItem as TranslationRouteOption)?.Kind ?? TranslationRouteKind.LocalLmt;

    private bool RouteMissing(TranslationRouteKind route) =>
        _routeFileChecks.TryGetValue(route, out var check) && check?.MissingFiles.Count > 0;

    private async void OnTranslate(object sender, RoutedEventArgs e)
    {
        if (RouteMissing(SelectedRoute)) return;
        if (string.IsNullOrWhiteSpace(InputText.Text))
        {
            ProgressStatus.Text = T("请输入需要翻译的文本。", "Enter text to translate.");
            return;
        }

        var operation = BeginOperation();
        TranslateButton.IsEnabled = false;
        TranslateButton.Content = T("翻译中…", "Translating…");
        ProgressStatus.Text = T("正在翻译…", "Translating…");
        ProgressStatus.ToolTip = T("正在使用本次选择的模型；不会修改默认设置。", "Using the selected model for this session. Your defaults stay unchanged.");
        AnimateTranslateButton(0.965);
        try
        {
            var request = new TranslationRequest(
                SelectedLanguage(SourceLanguageBox),
                SelectedLanguage(TargetLanguageBox),
                InputText.Text);
            TrySetResult(operation, InputText.Text, await _translate(request, SelectedRoute, operation.Token));
        }
        catch (OperationCanceledException)
        {
            if (_operations.IsCurrent(operation.Version)) ProgressStatus.Text = T("已取消", "Cancelled");
        }
        catch
        {
            if (_operations.IsCurrent(operation.Version)) ProgressStatus.Text = T("翻译失败，未显示任何私密诊断。", "Translation failed. Private diagnostics are hidden.");
        }
        finally
        {
            if (_operations.IsCurrent(operation.Version))
            {
                TranslateButton.IsEnabled = !RouteMissing(SelectedRoute);
                TranslateButton.Content = T("开始翻译", "Translate");
            }
        }
    }

    private void AnimateTranslateButton(double emphasizedScale)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        TranslateButtonScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(emphasizedScale, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
        TranslateButtonScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(emphasizedScale, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
    }

    private void OnRouteChanged(object sender, SelectionChangedEventArgs e) => UpdateRouteStatus();

    private void UpdateRouteStatus()
    {
        if (ProviderStatus is null || RouteBox is null) return;
        UpdateLanguageOptions();
        if (RouteMissing(SelectedRoute))
        {
            var missing = _routeFileChecks[SelectedRoute]!;
            var details = T($"本地翻译缺少文件：{string.Join(" · ", missing.MissingFiles)}。补齐后重新打开翻译窗口；原文不会自动发送到云端。",
                $"Local translation files missing: {string.Join(" · ", missing.MissingFiles)}. Restore them and reopen Translation; source text will not be sent to the cloud automatically.");
            ProviderStatus.Text = T("本地翻译文件缺失", "Local translation files missing");
            ProviderStatus.ToolTip = details;
            ProgressStatus.Text = details;
            TranslateButton.IsEnabled = LiveTranslateButton.IsEnabled = false;
            _showingMissingRoute = true;
            return;
        }
        if (_showingMissingRoute) ProgressStatus.Text = T("就绪", "Ready");
        _showingMissingRoute = false;
        ProviderStatus.ToolTip = null;
        TranslateButton.IsEnabled = LiveTranslateButton.IsEnabled = true;
        ProviderStatus.Text = SelectedRoute switch
        {
            TranslationRouteKind.LocalLmt => T("本地 LMT", "Local LMT"),
            TranslationRouteKind.Machine => T("本地机翻 · 仅英语到简体中文", "Local machine translation · English to Simplified Chinese only"),
            _ => TranslationRouteCatalog.Get(SelectedRoute).ShortName,
        };
    }

    private void UpdateLanguageOptions()
    {
        if (SourceLanguageBox is null || TargetLanguageBox is null) return;
        var machine = SelectedRoute == TranslationRouteKind.Machine;
        if (machine == _machineLanguageOptionsActive) return;
        if (machine)
        {
            _nonMachineSource = SourceLanguageBox.SelectedItem as ComboBoxItem;
            _nonMachineTarget = TargetLanguageBox.SelectedItem as ComboBoxItem;
        }
        foreach (var item in SourceLanguageBox.Items.OfType<ComboBoxItem>())
        {
            var allowed = !machine || item.Tag is "auto" or "English" or "简体中文";
            item.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
            item.IsEnabled = allowed;
        }
        foreach (var item in TargetLanguageBox.Items.OfType<ComboBoxItem>())
        {
            var allowed = !machine || Equals(item.Tag, "简体中文");
            item.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
            item.IsEnabled = allowed;
        }
        if (machine)
        {
            if (SourceLanguageBox.SelectedItem is ComboBoxItem { IsEnabled: false }) SourceLanguageBox.SelectedIndex = 0;
            TargetLanguageBox.SelectedIndex = 0;
        }
        else
        {
            if (_nonMachineSource is not null) SourceLanguageBox.SelectedItem = _nonMachineSource;
            if (_nonMachineTarget is not null) TargetLanguageBox.SelectedItem = _nonMachineTarget;
        }
        _machineLanguageOptionsActive = machine;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!_closed && !string.IsNullOrEmpty(ResultText.Text)) System.Windows.Clipboard.SetText(ResultText.Text);
    }

    private void OnLiveTranslate(object sender, RoutedEventArgs e)
    {
        if (!RouteMissing(SelectedRoute))
            LiveTranslationRequested?.Invoke(this, new LiveTranslationStartRequest(null, SelectedRoute, SelectedLanguage(TargetLanguageBox)));
    }

    private static string SelectedLanguage(System.Windows.Controls.ComboBox box) =>
        box.SelectedItem is ComboBoxItem { Tag: string code } ? code : NormalizeLanguage(box.Text);

    private static string EnglishCostEstimate(DeepSeekCostEstimate? estimate) => estimate is null
        ? string.Empty
        : $"Estimated cost: ≈ ¥{estimate.EstimatedCny.ToString("0.######", CultureInfo.InvariantCulture)}";

    private static TranslationRequest CloneWithModel(TranslationRequest request, string model) => new(
        request.SourceLanguage,
        request.TargetLanguage,
        request.OcrText,
        model,
        request.TextKind,
        request.Scene,
        request.TranslationHint);

    private static string NormalizeLanguage(string value) => string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim() switch
    {
        "自动检测" => "auto",
        var language => language,
    };
}

public readonly record struct UiOperation(long Version, CancellationToken Token);

public sealed class UiOperationOwner : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _current;
    private long _version;
    private bool _closed;

    public UiOperation Begin(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_closed) throw new ObjectDisposedException(nameof(UiOperationOwner));
            _current?.Cancel();
            _current?.Dispose();
            _current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return new UiOperation(++_version, _current.Token);
        }
    }

    public bool IsCurrent(long version)
    {
        lock (_sync) return !_closed && version == _version && _current is { IsCancellationRequested: false };
    }

    public void CancelPending()
    {
        lock (_sync)
        {
            if (_closed) return;
            _version++;
            _current?.Cancel();
            _current?.Dispose();
            _current = null;
        }
    }

    public void Close()
    {
        lock (_sync)
        {
            if (_closed) return;
            _closed = true;
            _version++;
            _current?.Cancel();
            _current?.Dispose();
            _current = null;
        }
    }

    public void Dispose() => Close();
}
