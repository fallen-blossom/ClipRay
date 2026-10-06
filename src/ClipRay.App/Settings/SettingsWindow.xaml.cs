using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipRay.App.Infrastructure;
using ClipRay.App.Cleanup;
using ClipRay.App.Security;
using ClipRay.App.Translation;
using Forms = System.Windows.Forms;

namespace ClipRay.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsApplyCoordinator _applyCoordinator;
    private readonly ICredentialVault _credentialVault;
    private readonly SettingsConnectionTester _connectionTester;
    private readonly Func<string, CancellationToken, Task<TranslationResult>> _localProviderTest;
    private readonly Func<DeepSeekCostSummary> _loadDeepSeekCostSummary;
    private readonly Func<DeepSeekCostSummary> _resetDeepSeekCostSummary;
    private readonly Func<CleanupScheduleRegistrationStatus> _loadCleanupScheduleStatus;
    private readonly Func<bool> _loadStartupStatus;
    private readonly InstalledUninstallState _installedUninstall;
    private readonly UiOperationOwner _connectionOperations = new();
    private readonly Dictionary<System.Windows.Controls.TextBox, GlobalHotkey?> _hotkeyValues = [];
    private AppSettings _settings;
    private string? _selectedCredentialName;
    private SettingsDraftState? _draftState;
    private string _savedNonLanguageDraft = "";
    private bool _lastApplyInterfaceOnly;
    private CleanupRunStatus? _lastCleanupRun;
    private CleanupScheduleRegistrationStatus? _lastCleanupSchedule;
    private bool _lastStartupEnabled;
    private DeepSeekCostSummary? _lastCostSummary;
    private string? _credentialSuffix;
    private string _localStatusChinese = "离线短进程，不开放端口";
    private string _localStatusEnglish = "Offline process; no listening port";
    private ComboBoxItem? _nonMachineTranslationTarget;
    private bool _machineTargetRestrictionActive;
    public bool DiscardDraftOnClose { get; set; }

    public string ApplicationVersion => Ui(
        $"版本 {VersionNumber}", $"Version {VersionNumber}");

    private static string VersionNumber =>
        typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.6";

    public SettingsWindow(
        AppSettings initialSettings,
        string? loadWarning,
        SettingsApplyCoordinator applyCoordinator,
        ICredentialVault credentialVault,
        Func<IDeepSeekTranslationProvider> providerFactory,
        Func<string, CancellationToken, Task<TranslationResult>> localProviderTest,
        DeepSeekCostSummary deepSeekCostSummary,
        Func<DeepSeekCostSummary> loadDeepSeekCostSummary,
        Func<DeepSeekCostSummary> resetDeepSeekCostSummary,
        Func<CleanupScheduleRegistrationStatus>? loadCleanupScheduleStatus = null,
        Func<bool>? loadStartupStatus = null)
    {
        InitializeComponent();
        _applyCoordinator = applyCoordinator;
        _credentialVault = credentialVault;
        _connectionTester = new SettingsConnectionTester(providerFactory);
        _localProviderTest = localProviderTest;
        _loadDeepSeekCostSummary = loadDeepSeekCostSummary;
        _resetDeepSeekCostSummary = resetDeepSeekCostSummary;
        _settings = initialSettings;
        _loadCleanupScheduleStatus = loadCleanupScheduleStatus
            ?? (() => new WindowsCleanupScheduleRegistration().ReadStatus(
                CleanupScheduleOptions.FromSettings(_settings),
                Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 ClipRay 可执行文件路径。")));
        _loadStartupStatus = loadStartupStatus
            ?? (() => StartupRegistration.IsEnabled(
                Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 ClipRay 可执行文件路径。")));
        _installedUninstall = InstalledUninstallLauncher.Resolve(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        UninstallButton.Visibility = _installedUninstall.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        _selectedCredentialName = CredentialVault.FixedDeepSeekSlot;
        System.Windows.DataObject.AddPastingHandler(CleanupDailyTimeBox, CleanupDailyTimeBox_Pasting);
        _lastCostSummary = deepSeekCostSummary;
        LoadControls();
        InitializeDevelopmentLocalModels();
        RefreshCredentials();
        InterfaceLanguageBox.SelectedIndex = _settings.InterfaceLanguageFollowsWindows
            ? 0 : AppInterfaceLanguage.Normalize(_settings.InterfaceLanguage) == "en" ? 2 : 1;
        _savedNonLanguageDraft = BuildNonLanguageDraftFingerprint();
        ApplyInterfaceLanguage();
        _draftState = new SettingsDraftState(BuildDraftFingerprint());
        WireDraftChangeTracking();
        UpdatePrimaryActionState();
        if (!string.IsNullOrWhiteSpace(loadWarning))
            ShowStatus(SettingsLoadWarningText.ForInterface(loadWarning, _settings.InterfaceLanguage), success: false);
        Loaded += (_, _) => StartSignatureMotion();
        Closing += (_, args) =>
        {
            if (DiscardDraftOnClose || _draftState?.Action != SettingsPrimaryAction.Save) return;
            var decision = System.Windows.MessageBox.Show(this,
                Ui("设置尚未保存。选择“是”保存并关闭，“否”放弃修改，“取消”返回继续编辑。", "Unsaved settings. Choose Yes to save and close, No to discard changes, or Cancel to keep editing."), Ui("未保存的设置", "Unsaved settings"),
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            args.Cancel = !_draftState.CanClose(decision switch
            {
                MessageBoxResult.Yes => SettingsDraftCloseChoice.Save,
                MessageBoxResult.No => SettingsDraftCloseChoice.Discard,
                _ => SettingsDraftCloseChoice.Cancel,
            }, TryApplyCurrentSettings);
        };
        Closed += (_, _) =>
        {
            StopSignatureMotion();
            _connectionOperations.Close();
        };
    }

    public event EventHandler<AppSettings>? SettingsApplied;

    public event EventHandler<bool>? HotkeyCaptureFocusChanged;

    public void ShowLocalProviderPreview()
    {
        SelectLocalProviderOption(LocalLmtModelPathBox.Text);
        UpdateModelAvailability();
        Dispatcher.BeginInvoke(() => SettingsScroll.ScrollToVerticalOffset(430));
    }

    public void ShowSaveSuccessPreview() => ShowStatus(Ui("设置已保存并立即生效。", "Settings saved and applied."), success: true);

    private void LoadControls()
    {
        ScreenshotDirectoryBox.Text = _settings.ScreenshotDirectory;
        LongCaptureDirectoryBox.Text = _settings.LongCaptureDirectory;
        RecordingDirectoryBox.Text = _settings.RecordingDirectory;
        CopyToClipboardBox.IsChecked = _settings.CopyToClipboard;
        BorderFeedbackBox.IsChecked = _settings.ShowBorderFeedback;
        LiveStopControlBox.IsChecked = _settings.ShowLiveTranslationStopControl;
        LiveRecordingOverlayBox.IsChecked = _settings.ShowLiveTranslationInRecordings;
        StartupBox.IsChecked = _settings.StartWithWindows;
        CleanupScheduleBox.IsChecked = _settings.CleanupScheduleEnabled;
        CleanupRetentionDaysBox.Text = _settings.CleanupRetentionDays.ToString(CultureInfo.InvariantCulture);
        CleanupDailyTimeBox.Text = _settings.CleanupDailyTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        SelectComboValue(CleanupFrequencyBox, CleanupFrequencyDisplay(_settings.CleanupScheduleFrequency));
        SelectComboValue(CleanupWeeklyDayBox, CleanupWeeklyDayDisplay(_settings.CleanupWeeklyDay));
        UpdateCleanupFrequencyVisibility();
        RefreshCleanupStatus();
        SetHotkeyValue(RegionHotkeyBox, _settings.RegionCaptureHotkey);
        SetHotkeyValue(RegionSecondaryHotkeyBox, _settings.RegionCaptureSecondaryHotkey);
        SetHotkeyValue(CurrentHotkeyBox, _settings.CurrentScreenHotkey);
        SetHotkeyValue(CurrentSecondaryHotkeyBox, _settings.CurrentScreenSecondaryHotkey);
        SetHotkeyValue(TaskbarFreeCurrentHotkeyBox, _settings.TaskbarFreeCurrentScreenHotkey);
        SetHotkeyValue(TaskbarFreeCurrentSecondaryHotkeyBox, _settings.TaskbarFreeCurrentScreenSecondaryHotkey);
        SetHotkeyValue(DesktopHotkeyBox, _settings.VirtualDesktopHotkey);
        SetHotkeyValue(DesktopSecondaryHotkeyBox, _settings.VirtualDesktopSecondaryHotkey);
        SetHotkeyValue(StartRecordingHotkeyBox, _settings.StartRecordingHotkey);
        SetHotkeyValue(StartRecordingSecondaryHotkeyBox, _settings.StartRecordingSecondaryHotkey);
        SetHotkeyValue(StopRecordingHotkeyBox, _settings.StopRecordingHotkey);
        SetHotkeyValue(StopRecordingSecondaryHotkeyBox, _settings.StopRecordingSecondaryHotkey);
        SetHotkeyValue(StartLiveTranslationHotkeyBox, _settings.StartLiveTranslationHotkey);
        SetHotkeyValue(StartLiveTranslationSecondaryHotkeyBox, _settings.StartLiveTranslationSecondaryHotkey);
        SetHotkeyValue(StopLiveTranslationHotkeyBox, _settings.StopLiveTranslationHotkey);
        SetHotkeyValue(StopLiveTranslationSecondaryHotkeyBox, _settings.StopLiveTranslationSecondaryHotkey);
        SetHotkeyValue(ConfirmCaptureHotkeyBox, _settings.ConfirmCaptureHotkey);
        SetHotkeyValue(ConfirmCaptureSecondaryHotkeyBox, _settings.ConfirmCaptureSecondaryHotkey);
        SetHotkeyValue(CancelCaptureHotkeyBox, _settings.CancelCaptureHotkey);
        SetHotkeyValue(CancelCaptureSecondaryHotkeyBox, _settings.CancelCaptureSecondaryHotkey);
        SetHotkeyValue(StopLongCaptureHotkeyBox, _settings.StopLongCaptureHotkey);
        SetHotkeyValue(StopLongCaptureSecondaryHotkeyBox, _settings.StopLongCaptureSecondaryHotkey);
        WireHotkeyCaptureFocus();
        LocalLmtModelPathBox.Text = LocalLmtOptions.Discover(_settings.LocalLmtModelPath).ModelPath;
        SelectProviderChoice(_settings.Provider, LocalLmtModelPathBox.Text);
        SelectComboValue(LiveProviderBox, _settings.LiveTranslationProvider);
        SelectComboValue(ModelBox, _settings.DeepSeekModel);
        SelectComboValue(TranslationTargetLanguageBox, _settings.TranslationTargetLanguage);
        MachineFallbackBox.IsChecked = _settings.MachineTranslationFallbackEnabled;
        UpdateLocalModelPresentation();
        UpdateModelAvailability();
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => ExecutePrimaryAction();

    private bool ExecutePrimaryAction()
    {
        if (_draftState?.Action == SettingsPrimaryAction.Refresh)
        {
            return TryRefreshRuntimeData();
        }

        return TryApplyCurrentSettings();
    }

    private bool TryApplyCurrentSettings()
    {
        try
        {
            SaveSettingsDraft();
            if (!_lastApplyInterfaceOnly) RefreshRuntimeData();
            ShowStatus(Ui("设置已保存并立即生效。", "Settings saved and applied."), success: true);
            return true;
        }
        catch (Exception exception)
        {
            ShowStatus(Ui($"无法应用设置：{exception.Message}",
                "Could not apply settings. Review the values and try again."), success: false);
            return false;
        }
    }

    private void SaveSettingsDraft()
    {
        ApplyCurrentSettings();
        _draftState?.MarkSaved(BuildDraftFingerprint());
        _savedNonLanguageDraft = BuildNonLanguageDraftFingerprint();
        ApplyInterfaceLanguage();
        UpdatePrimaryActionState();
    }

    private void SettingsWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = HotkeyCapturePolicy.ResolveEventKey(
            e.Key,
            e.SystemKey,
            e.ImeProcessedKey,
            e.DeadCharProcessedKey);
        if (key != Key.S || Keyboard.Modifiers != ModifierKeys.Control) return;
        ExecutePrimaryAction();
        e.Handled = true;
    }

    private bool TryRefreshRuntimeData()
    {
        try
        {
            RefreshRuntimeData();
            ShowInfoStatus(Ui("运行数据已刷新。DeepSeek 费用、清理状态和凭据列表均为最新状态。",
                "Runtime data refreshed: DeepSeek costs, cleanup status and credentials are up to date."));
            return true;
        }
        catch (Exception exception)
        {
            ShowStatus(Ui($"无法刷新运行数据：{exception.Message}", "Could not refresh runtime data. Try again."), success: false);
            return false;
        }
    }

    private void RefreshRuntimeData()
    {
        var summary = _loadDeepSeekCostSummary();
        _lastCostSummary = summary;
        RenderCostSummary();
        RefreshCleanupStatus();
        RefreshCredentials();
    }

    private async void SaveCredential_Click(object sender, RoutedEventArgs e)
    {
        UiOperation? operation = null;
        try
        {
            const string name = CredentialVault.FixedDeepSeekSlot;
            const string provider = "DeepSeek";
            var candidate = CredentialValueBox.Password;
            CredentialSecretPolicy.ValidateCandidate(provider, candidate);
            operation = _connectionOperations.Begin();
            SaveCredentialButton.IsEnabled = false;
            ShowInfoStatus(Ui("正在验证候选凭据；验证成功前不会保存或替换。",
                "Checking the new credential; the saved key will stay unchanged until verification succeeds."));
            var verified = await _connectionTester.TestCandidateAsync(
                candidate,
                SelectedValue(ModelBox),
                operation.Value.Token);
            if (!_connectionOperations.IsCurrent(operation.Value.Version)) return;
            if (!verified.IsSuccess)
            {
                ShowStatus(
                    $"{ProviderStatus(verified.Status)} {Ui("Windows Credential Manager 中的原凭据未变化。", "The existing Windows Credential Manager key was not changed.")}",
                    success: false);
                return;
            }

            _credentialVault.Save(name, provider, candidate);
            _selectedCredentialName = name;
            ObserveSettingsDraft();
            CredentialValueBox.Clear();
            RefreshCredentials();
            ShowStatus(Ui("凭据已验证并安全保存；API Key 输入框已清空。",
                "Credential verified and saved; the API key field was cleared."), success: true);
        }
        catch (OperationCanceledException)
        {
            // Closing or superseding Settings owns cancellation and suppresses late UI updates.
        }
        catch (Exception exception)
        {
            var english = operation is null && exception is ArgumentException
                ? "Enter a DeepSeek API key beginning with sk- and containing no spaces. The saved key was not changed."
                : "Could not save the credential; the existing key was not replaced.";
            ShowStatus(Ui($"无法保存凭据：{exception.Message}", english), success: false);
        }
        finally
        {
            if (SaveCredentialButton is not null && (operation is null || _connectionOperations.IsCurrent(operation.Value.Version))) SaveCredentialButton.IsEnabled = true;
        }
    }

    private void DeleteCredential_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            const string name = CredentialVault.FixedDeepSeekSlot;
            _connectionOperations.CancelPending();
            var confirmed = CredentialDeletionPolicy.Confirm(name, (message, title) =>
                System.Windows.MessageBox.Show(
                    this,
                    message,
                    title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) == MessageBoxResult.Yes, _settings.InterfaceLanguage);
            if (!confirmed) return;

            _credentialVault.Delete(name);
            if (string.Equals(_selectedCredentialName, name, StringComparison.OrdinalIgnoreCase))
            {
                _selectedCredentialName = null;
                ObserveSettingsDraft();
            }
            CredentialValueBox.Clear();
            RefreshCredentials();
            ShowStatus(CredentialDeletionPolicy.Success(name, _settings.InterfaceLanguage), success: true);
        }
        catch (Exception exception)
        {
            ShowStatus(Ui($"无法删除凭据：{exception.Message}", "Could not delete the local credential."), success: false);
        }
    }

    private void ResetDeepSeekCost_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(
                this,
                Ui("确认清除本机累计的 DeepSeek 费用统计吗？此操作不可恢复，但不会影响 DeepSeek 账户账单。",
                    "Clear the DeepSeek cost statistics saved on this device? This cannot be undone and does not affect your DeepSeek account bill."),
                Ui("清除本机费用统计", "Clear local cost statistics"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var summary = _resetDeepSeekCostSummary();
            _lastCostSummary = summary;
            RenderCostSummary();
            ShowStatus(Ui("已清除本机 DeepSeek 费用统计。", "Local DeepSeek cost statistics cleared."), success: true);
        }
        catch (Exception exception)
        {
            ShowStatus(Ui($"无法清除本机费用统计：{exception.Message}", "Could not clear local cost statistics."), success: false);
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        UiOperation? operation = null;
        string? provider = null;
        try
        {
            SaveSettingsDraft();
            operation = _connectionOperations.Begin();
            provider = SelectedProvider();
            if (provider is "Local LMT" or "Machine")
            {
                LocalTrialResultBox.Clear();
                SetLocalStatus("正在验证并试译；本次尚无结果。", "Validating and testing; no result yet.");
            }
            ShowInfoStatus(provider is "Local LMT" or "Machine"
                ? Ui("正在验证本地模型并试译…", "Validating the local model and testing translation…")
                : Ui("正在测试 DeepSeek 连接…", "Testing the DeepSeek connection…"));
            TestProviderButton.IsEnabled = false;
            var result = await SettingsProviderTestRouter.TestAsync(
                provider,
                token => _localProviderTest(LocalTrialInputBox.Text, token),
                token => _connectionTester.TestAsync(
                    CredentialVault.FixedDeepSeekSlot,
                    SelectedValue(ModelBox),
                    token),
                operation.Value.Token);
            if (_connectionOperations.IsCurrent(operation.Value.Version))
            {
                ShowStatus(
                    result.IsSuccess && provider is "Local LMT" or "Machine"
                        ? $"{ProviderStatus(result.Status)} {Ui("试译：", "Test translation: ")}{result.Text}"
                        : ProviderStatus(result.Status),
                    result.IsSuccess);
                if (provider is "Local LMT" or "Machine")
                {
                    SetLocalStatus(result.IsSuccess ? "模型验证与离线试译成功" : "本地试译失败，请查看结果中的诊断。",
                        result.IsSuccess ? "Model validation and offline test succeeded" : "Local test failed; see the result diagnostics.");
                    LocalTrialResultBox.Text = result.IsSuccess ? result.Text : result.Status;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Closing or superseding Settings owns cancellation and suppresses late UI updates.
            if (operation is not null && _connectionOperations.IsCurrent(operation.Value.Version))
            {
                ShowStatus(Ui("本次测试已取消，可以重试。", "This test was cancelled. You can retry."), success: false);
                if (provider is "Local LMT" or "Machine")
                {
                    SetLocalStatus("本次试译已取消，可以重试。", "This translation test was cancelled. You can retry.");
                    LocalTrialResultBox.Clear();
                }
            }
        }
        catch
        {
            if (operation is null)
            {
                ShowStatus(Ui("无法保存测试设置；未启动翻译测试。", "Could not save test settings; translation test was not started."), success: false);
            }
            else if (_connectionOperations.IsCurrent(operation.Value.Version))
            {
                ShowStatus(Ui("连接测试失败，未显示任何私密诊断。", "Connection test failed. Private diagnostics are hidden."), success: false);
                if (provider is "Local LMT" or "Machine")
                {
                    SetLocalStatus("本地试译未完成；可再次点击验证并试译。", "Local test did not complete; run Test translation again to retry.");
                    LocalTrialResultBox.Text = Ui("本次没有有效译文，私密诊断未显示。", "No valid translation from this attempt. Private diagnostics are hidden.");
                }
            }
        }
        finally
        {
            if (TestProviderButton is not null && (operation is null || _connectionOperations.IsCurrent(operation.Value.Version))) TestProviderButton.IsEnabled = true;
        }
    }

    private void ChooseScreenshotFolder_Click(object sender, RoutedEventArgs e) => ChooseOutputFolder(
        ScreenshotDirectoryBox,
        Ui("选择 ClipRay 截图保存文件夹", "Choose the ClipRay screenshot folder"));

    private void ChooseRecordingFolder_Click(object sender, RoutedEventArgs e) => ChooseOutputFolder(
        RecordingDirectoryBox,
        Ui("选择 ClipRay 录屏保存文件夹", "Choose the ClipRay recording folder"));

    private void ChooseLongCaptureFolder_Click(object sender, RoutedEventArgs e) => ChooseOutputFolder(
        LongCaptureDirectoryBox,
        Ui("选择 ClipRay 长截图保存文件夹", "Choose the ClipRay scrolling capture folder"));

    private void ChooseOutputFolder(System.Windows.Controls.TextBox target, string description)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description,
            SelectedPath = target.Text,
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private void OpenScreenshotFolder_Click(object sender, RoutedEventArgs e) => OpenOutputFolder(ScreenshotDirectoryBox);

    private void OpenLongCaptureFolder_Click(object sender, RoutedEventArgs e) => OpenOutputFolder(LongCaptureDirectoryBox);

    private void OpenRecordingFolder_Click(object sender, RoutedEventArgs e) => OpenOutputFolder(RecordingDirectoryBox);

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var choice = System.Windows.MessageBox.Show(
            this,
            Ui("将打开 ClipRay 卸载程序。截图、长截图和录屏默认保留；是否移入回收站可在卸载确认页选择。",
                "ClipRay's uninstaller will open. Screenshots, scrolling captures and recordings are kept by default; the uninstall confirmation lets you choose whether to move them to the Recycle Bin."),
            Ui("卸载 ClipRay", "Uninstall ClipRay"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes) return;

        try
        {
            InstalledUninstallLauncher.Launch(_installedUninstall);
            Close();
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            ShowStatus(Ui($"无法启动卸载程序：{exception.Message}", "Could not start the uninstaller."), success: false);
        }
    }

    private void OpenOutputFolder(System.Windows.Controls.TextBox target)
    {
        try
        {
            Directory.CreateDirectory(target.Text);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetFullPath(target.Text)}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            ShowStatus(Ui($"无法打开文件夹：{exception.Message}", "Could not open the folder."), success: false);
        }
    }

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _connectionOperations.CancelPending();
        if (TestProviderButton is not null) TestProviderButton.IsEnabled = true;
        if (SaveCredentialButton is not null) SaveCredentialButton.IsEnabled = true;
        if (SelectedLocalModelOption() is { IsInstalled: true } option)
        {
            LocalLmtModelPathBox.Text = option.Path;
            UpdateLocalModelPresentation();
            SetLocalStatus("已切换本地模型；保存后生效，可先验证并试译。", "Model selection changed; save to apply, or run a test.");
        }

        UpdateModelAvailability();
    }

    private void CleanupDailyTimeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !CleanupTimeInputPolicy.IsCandidateText(BuildTimeCandidate(e.Text));
    }

    private void CleanupDailyTimeBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(System.Windows.DataFormats.UnicodeText)
            || e.SourceDataObject.GetData(System.Windows.DataFormats.UnicodeText) is not string text
            || !CleanupTimeInputPolicy.IsCandidateText(BuildTimeCandidate(text)))
        {
            e.CancelCommand();
        }
    }

    private void CleanupDailyTimeBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        NormalizeCleanupTime(showError: true);
        e.Handled = true;
    }

    private void CleanupDailyTimeBox_LostFocus(object sender, RoutedEventArgs e) =>
        NormalizeCleanupTime(showError: false);

    private void OpenTutorial_Click(object sender, RoutedEventArgs e)
    {
        var tutorialPath = Path.Combine(AppContext.BaseDirectory, "docs", "ClipRay-使用教程.html");
        if (!File.Exists(tutorialPath))
        {
            ShowStatus(Ui("教程文档缺失，请重新安装或更新 ClipRay。",
                "The bundled user guide is missing. Reinstall or update ClipRay."), success: false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(tutorialPath) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowStatus(Ui($"无法打开教程文档：{exception.Message}", "Could not open the user guide."), success: false);
        }
    }

    private string BuildTimeCandidate(string insertedText) => string.Concat(
        CleanupDailyTimeBox.Text.AsSpan(0, CleanupDailyTimeBox.SelectionStart),
        insertedText,
        CleanupDailyTimeBox.Text.AsSpan(CleanupDailyTimeBox.SelectionStart + CleanupDailyTimeBox.SelectionLength));

    private bool NormalizeCleanupTime(bool showError)
    {
        try
        {
            CleanupDailyTimeBox.Text = CleanupTimeInputPolicy.Normalize(CleanupDailyTimeBox.Text);
            return true;
        }
        catch (InvalidOperationException exception)
        {
            if (showError) ShowStatus(Ui(exception.Message, "Enter a valid cleanup time in HH:mm format."), success: false);
            return false;
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box) return;
        e.Handled = true;
        var key = HotkeyCapturePolicy.ResolveEventKey(
            e.Key,
            e.SystemKey,
            e.ImeProcessedKey,
            e.DeadCharProcessedKey);
        var secondary = IsSecondaryHotkeyBox(box.Name);
        if (secondary && key is Key.Back or Key.Delete)
        {
            SetHotkeyValue(box, null);
            ShowInfoStatus(Ui("备用快捷键已清空；保存后生效。", "Alternate shortcut cleared; save to apply."));
            return;
        }
        if (key == Key.Back)
        {
            SetHotkeyValue(box, DefaultHotkeyFor(box.Name));
            ShowInfoStatus(Ui($"{box.Text}（已恢复默认，保存后生效）", $"{box.Text} restored to default; save to apply."));
            return;
        }

        try
        {
            var hotkey = HotkeyCapturePolicy.Capture(key, HotkeyCapturePolicy.ReadCurrentModifiers());
            SetHotkeyValue(box, hotkey);
            ShowInfoStatus(hotkey.Control || hotkey.Shift || hotkey.Alt || hotkey.Windows
                ? Ui($"已识别 {box.Text}；保存后生效。", $"Captured {box.Text}; save to apply.")
                : Ui($"已识别 {box.Text}；部分全屏游戏会占用单键，建议使用组合键。保存后生效。",
                    $"Captured {box.Text}. Some full-screen games use single keys; consider a key combination. Save to apply."));
        }
        catch (ArgumentException exception)
        {
            ShowStatus(Ui(exception.Message, "That shortcut is unavailable. Choose another combination."), success: false);
        }
    }

    private void InitializeDevelopmentLocalModels()
    {
        var selectedPath = LocalLmtModelPathBox.Text;
        ComboBoxItem? selectedItem = null;
        var options = LocalLmtModelCatalog.ResolveProviderModels(
                     AppContext.BaseDirectory,
                     WorkspaceLocator.TryFind(AppContext.BaseDirectory)).ToList();
        if (_settings.Provider == "Local LMT" && !options.Any(option =>
                string.Equals(option.Path, selectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new LocalLmtDevelopmentModelOption(
                LocalLmtModelCatalog.ProviderChoiceNameFor(selectedPath), selectedPath, File.Exists(selectedPath)));
        }
        foreach (var option in options)
        {
            var item = new ComboBoxItem
            {
                Tag = option,
                IsEnabled = option.IsInstalled,
            };
            var modelName = LocalLmtModelCatalog.ProviderChoiceNameFor(option.Path);
            SettingsText.SetChinese(item, $"{modelName} · {(option.IsInstalled ? "已找到模型文件，运行未验证" : "未找到模型文件")}");
            SettingsText.SetEnglish(item, $"{LocalLmtModelCatalog.ProviderChoiceNameFor(option.Path, "en")} · {(option.IsInstalled ? "File found; runtime unverified" : "Model file missing")}");
            ProviderBox.Items.Add(item);
            if (string.Equals(option.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                selectedItem = item;
            }
        }

        if (string.Equals(_settings.Provider, "Local LMT", StringComparison.Ordinal) && selectedItem is not null)
        {
            ProviderBox.SelectedItem = selectedItem;
        }
    }

    private LocalLmtDevelopmentModelOption? SelectedLocalModelOption() =>
        ProviderBox.SelectedItem is ComboBoxItem { Tag: LocalLmtDevelopmentModelOption option }
            ? option
            : null;

    private void SelectProviderChoice(string provider, string? preferredLocalModelPath = null)
    {
        if (!string.Equals(provider, "Local LMT", StringComparison.Ordinal))
        {
            SelectComboValue(ProviderBox, provider);
            return;
        }

        SelectLocalProviderOption(preferredLocalModelPath);
    }

    private void SelectLocalProviderOption(string? preferredLocalModelPath)
    {
        var localOptions = ProviderBox.Items.OfType<ComboBoxItem>()
            .Where(item => item.Tag is LocalLmtDevelopmentModelOption);
        var selected = localOptions.FirstOrDefault(item =>
            string.Equals(
                ((LocalLmtDevelopmentModelOption)item.Tag).Path,
                preferredLocalModelPath,
                StringComparison.OrdinalIgnoreCase));

        if (selected is not null) ProviderBox.SelectedItem = selected;
    }

    private string SelectedProvider()
    {
        if (SelectedLocalModelOption() is not null) return "Local LMT";
        return ProviderBox.SelectedItem is ComboBoxItem { Tag: string tag } && tag == "Machine"
            ? "Machine"
            : "DeepSeek";
    }

    private string SelectedLiveProvider() =>
        LiveProviderBox.SelectedItem is ComboBoxItem { Tag: string tag } && tag == "DeepSeek"
            ? "DeepSeek"
            : "Machine";

    private void UpdateLocalModelPresentation() =>
        LocalModelNameText.Text = LocalLmtModelCatalog.DisplayNameFor(LocalLmtModelPathBox.Text, _settings.InterfaceLanguage);

    private void UpdateModelAvailability()
    {
        if (ModelBox is not null && ProviderBox is not null && LiveProviderBox is not null)
        {
            var provider = SelectedProvider();
            UpdateTranslationTargetAvailability(provider == "Machine");
            var deepSeek = provider == "DeepSeek" || SelectedLiveProvider() == "DeepSeek";
            var localLmt = provider == "Local LMT";
            ModelBox.IsEnabled = deepSeek;
            ModelBox.Visibility = deepSeek ? Visibility.Visible : Visibility.Collapsed;
            CredentialValueBox.IsEnabled = deepSeek;
            LocalModelPanel.Visibility = localLmt ? Visibility.Visible : Visibility.Collapsed;
            CredentialCard.Visibility = deepSeek ? Visibility.Visible : Visibility.Collapsed;
            DeepSeekCostPanel.Visibility = deepSeek ? Visibility.Visible : Visibility.Collapsed;
            TestProviderButton.Content = provider == "DeepSeek" ? Ui("验证连接", "Test connection") : Ui("验证并试译", "Test translation");
        }
    }

    private void UpdateTranslationTargetAvailability(bool machine)
    {
        if (TranslationTargetLanguageBox is null || machine == _machineTargetRestrictionActive) return;
        if (machine) _nonMachineTranslationTarget = TranslationTargetLanguageBox.SelectedItem as ComboBoxItem;
        foreach (var item in TranslationTargetLanguageBox.Items.OfType<ComboBoxItem>())
        {
            var allowed = !machine || Equals(item.Tag, "简体中文");
            item.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
            item.IsEnabled = allowed;
        }
        if (machine) SelectComboValue(TranslationTargetLanguageBox, "简体中文");
        else if (_nonMachineTranslationTarget is not null)
            TranslationTargetLanguageBox.SelectedItem = _nonMachineTranslationTarget;
        _machineTargetRestrictionActive = machine;
    }

    private void WireDraftChangeTracking()
    {
        System.Windows.Controls.TextBox[] textBoxes =
        [
            ScreenshotDirectoryBox,
            LongCaptureDirectoryBox,
            RecordingDirectoryBox,
            CleanupRetentionDaysBox,
            CleanupDailyTimeBox,
            RegionHotkeyBox,
            RegionSecondaryHotkeyBox,
            CurrentHotkeyBox,
            CurrentSecondaryHotkeyBox,
            TaskbarFreeCurrentHotkeyBox,
            TaskbarFreeCurrentSecondaryHotkeyBox,
            DesktopHotkeyBox,
            DesktopSecondaryHotkeyBox,
            StartRecordingHotkeyBox,
            StartRecordingSecondaryHotkeyBox,
            StopRecordingHotkeyBox,
            StopRecordingSecondaryHotkeyBox,
            StartLiveTranslationHotkeyBox,
            StartLiveTranslationSecondaryHotkeyBox,
            StopLiveTranslationHotkeyBox,
            StopLiveTranslationSecondaryHotkeyBox,
            ConfirmCaptureHotkeyBox,
            ConfirmCaptureSecondaryHotkeyBox,
            CancelCaptureHotkeyBox,
            CancelCaptureSecondaryHotkeyBox,
            StopLongCaptureHotkeyBox,
            StopLongCaptureSecondaryHotkeyBox,
            LocalLmtModelPathBox,
        ];
        foreach (var box in textBoxes) box.TextChanged += (_, _) => ObserveSettingsDraft();

        System.Windows.Controls.CheckBox[] checkBoxes =
        [
            CopyToClipboardBox,
            BorderFeedbackBox,
            LiveStopControlBox,
            LiveRecordingOverlayBox,
            StartupBox,
            CleanupScheduleBox,
            MachineFallbackBox,
        ];
        foreach (var box in checkBoxes)
        {
            box.Checked += (_, _) => ObserveSettingsDraft();
            box.Unchecked += (_, _) => ObserveSettingsDraft();
        }

        System.Windows.Controls.ComboBox[] comboBoxes =
        [
            InterfaceLanguageBox,
            CleanupFrequencyBox,
            CleanupWeeklyDayBox,
            ProviderBox,
            LiveProviderBox,
            ModelBox,
            TranslationTargetLanguageBox,
        ];
        foreach (var box in comboBoxes) box.SelectionChanged += (_, _) => ObserveSettingsDraft();
    }

    private void ObserveSettingsDraft()
    {
        if (_draftState is null) return;
        _draftState.Observe(BuildDraftFingerprint());
        UpdatePrimaryActionState();
    }

    private bool FollowsWindowsLanguage() => (InterfaceLanguageBox.SelectedItem as ComboBoxItem)?.Tag as string == "auto";
    private string SelectedInterfaceLanguage() => FollowsWindowsLanguage()
        ? AppInterfaceLanguage.ForWindowsUiCulture(CultureInfo.CurrentUICulture.Name)
        : (InterfaceLanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "zh-CN";
    private string Ui(string chinese, string english) => AppInterfaceLanguage.Text(_settings.InterfaceLanguage, chinese, english);
    private string ProviderStatus(string status) => Ui(TranslationStatusText.LocalizeProviderStatus(status),
        TranslationStatusText.EnglishProviderStatus(status));
    private void ApplyInterfaceLanguage()
    {
        SettingsText.SetLanguage(this, _settings.InterfaceLanguage);
        UpdateLocalModelPresentation();
        UpdateModelAvailability();
        RenderCleanupStatus();
        RenderCostSummary();
        RenderCredentialStatus();
        LocalModelStatusText.Text = Ui(_localStatusChinese, _localStatusEnglish);
        foreach (var entry in _hotkeyValues)
            entry.Key.Text = entry.Value is null ? Ui("未设置", "Not set") : HotkeyDisplayFormatter.Format(entry.Value);
        Title = SettingsHeading.Text = Ui("ClipRay 设置", "ClipRay Settings");
        VersionText.Text = ApplicationVersion;
        SettingsSubtitle.Text = Ui("截图、快捷键、翻译与本地模型", "Capture, shortcuts, translation and local models");
        OutputNavigation.Content = Ui("输出与维护", "Output and maintenance");
        StartupNavigation.Content = Ui("启动", "Startup");
        HotkeysNavigation.Content = Ui("快捷键", "Shortcuts");
        TranslationNavigation.Content = Ui("翻译", "Translation");
        CloseSettingsButton.Content = Ui("关闭", "Close");
        UninstallButton.Content = Ui("卸载 ClipRay", "Uninstall ClipRay");
        TutorialButton.Content = Ui("查看使用教程 ↗", "User guide ↗");
        TutorialButton.ToolTip = Ui("打开随 ClipRay 安装的离线使用教程", "Open the bundled offline user guide (Chinese)");
        UpdateAccessibleNames();
        UpdatePrimaryActionState();
    }

    private void UpdateAccessibleNames()
    {
        void Name(DependencyObject element, string chinese, string english) =>
            System.Windows.Automation.AutomationProperties.SetName(element, Ui(chinese, english));

        Name(InterfaceLanguageBox, "界面语言", "Interface language");
        Name(ScreenshotDirectoryBox, "截图保存目录", "Screenshot save folder");
        Name(LongCaptureDirectoryBox, "长截图保存目录", "Scrolling capture save folder");
        Name(RecordingDirectoryBox, "录屏保存目录", "Recording save folder");
        Name(CleanupRetentionDaysBox, "文件保留天数", "File retention days");
        Name(CleanupFrequencyBox, "自动清理频率", "Scheduled cleanup frequency");
        Name(CleanupWeeklyDayBox, "自动清理星期", "Scheduled cleanup weekday");
        Name(CleanupDailyTimeBox, "自动清理执行时间", "Scheduled cleanup time");
        Name(ProviderBox, "区域翻译方式", "Region translation provider");
        Name(ModelBox, "区域翻译模型", "Region translation model");
        Name(LiveProviderBox, "实时翻译方式", "Live translation provider");
        Name(TranslationTargetLanguageBox, "截图翻译目标语言", "Capture translation target language");
        Name(LocalLmtModelPathBox, "本地模型文件路径", "Local model file path");
        Name(LocalTrialInputBox, "试译文本", "Test translation input");
        Name(LocalTrialResultBox, "试译结果", "Test translation result");
        Name(CredentialValueBox, "DeepSeek API 密钥", "DeepSeek API key");

        foreach (var (primary, alternate, chinese, english) in new (System.Windows.Controls.TextBox, System.Windows.Controls.TextBox, string, string)[]
        {
            (RegionHotkeyBox, RegionSecondaryHotkeyBox, "区域截图", "Capture region"),
            (CurrentHotkeyBox, CurrentSecondaryHotkeyBox, "全屏截图不含任务栏", "Capture screen without taskbar"),
            (TaskbarFreeCurrentHotkeyBox, TaskbarFreeCurrentSecondaryHotkeyBox, "全屏截图含任务栏", "Capture screen with taskbar"),
            (DesktopHotkeyBox, DesktopSecondaryHotkeyBox, "完整虚拟桌面截图", "Capture all screens"),
            (StartRecordingHotkeyBox, StartRecordingSecondaryHotkeyBox, "开始录屏", "Start recording"),
            (StopRecordingHotkeyBox, StopRecordingSecondaryHotkeyBox, "停止录屏", "Stop recording"),
            (ConfirmCaptureHotkeyBox, ConfirmCaptureSecondaryHotkeyBox, "确认截图", "Confirm capture"),
            (CancelCaptureHotkeyBox, CancelCaptureSecondaryHotkeyBox, "取消截图", "Cancel capture"),
            (StopLongCaptureHotkeyBox, StopLongCaptureSecondaryHotkeyBox, "停止长截图", "Stop scrolling capture"),
            (StartLiveTranslationHotkeyBox, StartLiveTranslationSecondaryHotkeyBox, "开始实时翻译", "Start live translation"),
            (StopLiveTranslationHotkeyBox, StopLiveTranslationSecondaryHotkeyBox, "停止实时翻译", "Stop live translation"),
        })
        {
            Name(primary, $"{chinese}主快捷键", $"{english} primary shortcut");
            Name(alternate, $"{chinese}备用快捷键", $"{english} alternate shortcut");
        }

        Name(ChooseScreenshotFolderButton, "选择截图保存目录", "Browse for screenshot save folder");
        Name(OpenScreenshotFolderButton, "打开截图保存目录", "Open screenshot save folder");
        Name(ChooseLongCaptureFolderButton, "选择长截图保存目录", "Browse for scrolling capture save folder");
        Name(OpenLongCaptureFolderButton, "打开长截图保存目录", "Open scrolling capture save folder");
        Name(ChooseRecordingFolderButton, "选择录屏保存目录", "Browse for recording save folder");
        Name(OpenRecordingFolderButton, "打开录屏保存目录", "Open recording save folder");
    }
    private string BuildDraftFingerprint() => FollowsWindowsLanguage() + "\u001E" + SelectedInterfaceLanguage() + "\u001E" + BuildNonLanguageDraftFingerprint();
    private string BuildNonLanguageDraftFingerprint() => string.Join(
        '\u001F',
        ScreenshotDirectoryBox.Text,
        LongCaptureDirectoryBox.Text,
        RecordingDirectoryBox.Text,
        CopyToClipboardBox.IsChecked == true,
        BorderFeedbackBox.IsChecked == true,
        LiveStopControlBox.IsChecked == true,
        LiveRecordingOverlayBox.IsChecked == true,
        StartupBox.IsChecked == true,
        CleanupScheduleBox.IsChecked == true,
        MachineFallbackBox.IsChecked == true,
        CleanupRetentionDaysBox.Text,
        CleanupDailyTimeBox.Text,
        SelectedValue(CleanupFrequencyBox),
        SelectedValue(CleanupWeeklyDayBox),
        DraftHotkey(RegionHotkeyBox),
        DraftHotkey(RegionSecondaryHotkeyBox),
        DraftHotkey(CurrentHotkeyBox),
        DraftHotkey(CurrentSecondaryHotkeyBox),
        DraftHotkey(TaskbarFreeCurrentHotkeyBox),
        DraftHotkey(TaskbarFreeCurrentSecondaryHotkeyBox),
        DraftHotkey(DesktopHotkeyBox),
        DraftHotkey(DesktopSecondaryHotkeyBox),
        DraftHotkey(StartRecordingHotkeyBox),
        DraftHotkey(StartRecordingSecondaryHotkeyBox),
        DraftHotkey(StopRecordingHotkeyBox),
        DraftHotkey(StopRecordingSecondaryHotkeyBox),
        DraftHotkey(StartLiveTranslationHotkeyBox),
        DraftHotkey(StartLiveTranslationSecondaryHotkeyBox),
        DraftHotkey(StopLiveTranslationHotkeyBox),
        DraftHotkey(StopLiveTranslationSecondaryHotkeyBox),
        DraftHotkey(ConfirmCaptureHotkeyBox),
        DraftHotkey(ConfirmCaptureSecondaryHotkeyBox),
        DraftHotkey(CancelCaptureHotkeyBox),
        DraftHotkey(CancelCaptureSecondaryHotkeyBox),
        DraftHotkey(StopLongCaptureHotkeyBox),
        DraftHotkey(StopLongCaptureSecondaryHotkeyBox),
        SelectedProvider(),
        SelectedLiveProvider(),
        SelectedValue(ModelBox),
        SelectedValue(TranslationTargetLanguageBox),
        LocalLmtModelPathBox.Text,
        CredentialVault.FixedDeepSeekSlot);

    private string DraftHotkey(System.Windows.Controls.TextBox box) =>
        _hotkeyValues.TryGetValue(box, out var hotkey) ? hotkey?.ToString() ?? string.Empty : string.Empty;

    private void UpdatePrimaryActionState()
    {
        if (_draftState is null) return;
        var save = _draftState.Action == SettingsPrimaryAction.Save;
        SaveSettingsButton.Content = save ? Ui("保存设置", "Save settings") : Ui("立即刷新", "Refresh now");
        SaveSettingsButton.Style = (Style)FindResource(save ? "PrimaryButton" : "RefreshButton");
        SaveSettingsButton.ToolTip = save
            ? Ui("保存当前修改（Ctrl+S）", "Save changes (Ctrl+S)")
            : Ui("刷新 DeepSeek 费用、清理状态和凭据列表（Ctrl+S）", "Refresh DeepSeek costs, cleanup status and credentials (Ctrl+S)");
        System.Windows.Automation.AutomationProperties.SetName(SaveSettingsButton, SaveSettingsButton.Content.ToString()!);
    }

    private void RefreshCleanupStatus()
    {
        var dataRoot = ApplicationPaths.Create(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)).DataRoot;
        var cleanupStatusPath = Path.Combine(
            dataRoot,
            "cleanup-status.json");
        _lastCleanupRun = new CleanupRunStatusStore(cleanupStatusPath).Load();
        _lastCleanupSchedule = _loadCleanupScheduleStatus();
        _lastStartupEnabled = _loadStartupStatus();
        RenderCleanupStatus();
    }

    private void RenderCleanupStatus()
    {
        if (_lastCleanupSchedule is null) return;
        CleanupLastRunText.Text = CleanupRunStatusFormatter.Format(_lastCleanupRun, interfaceLanguage: _settings.InterfaceLanguage);
        CleanupScheduleStateText.Text = CleanupScheduleStatusFormatter.Format(
            _settings.CleanupScheduleEnabled,
            _lastCleanupSchedule, _settings.InterfaceLanguage);
        StartupStateText.Text = StartupStatusFormatter.Format(
            _settings.StartWithWindows,
            _lastStartupEnabled, _settings.InterfaceLanguage);
    }

    private void ApplyCurrentSettings()
    {
        if (BuildNonLanguageDraftFingerprint() == _savedNonLanguageDraft
            && (SelectedInterfaceLanguage() != _settings.InterfaceLanguage
                || FollowsWindowsLanguage() != _settings.InterfaceLanguageFollowsWindows))
        {
            var languageOnly = _settings with
            {
                InterfaceLanguage = SelectedInterfaceLanguage(),
                InterfaceLanguageFollowsWindows = FollowsWindowsLanguage(),
            };
            _applyCoordinator.Apply(_settings, languageOnly);
            _lastApplyInterfaceOnly = true;
            _settings = languageOnly;
            SettingsApplied?.Invoke(this, languageOnly);
            return;
        }
        _lastApplyInterfaceOnly = false;
        CleanupDailyTimeBox.Text = CleanupTimeInputPolicy.Normalize(CleanupDailyTimeBox.Text);
        var updated = new AppSettings
        {
            RegionCaptureHotkey = GetRequiredHotkeyValue(RegionHotkeyBox),
            RegionCaptureSecondaryHotkey = GetOptionalHotkeyValue(RegionSecondaryHotkeyBox),
            CurrentScreenHotkey = GetRequiredHotkeyValue(CurrentHotkeyBox),
            CurrentScreenSecondaryHotkey = GetOptionalHotkeyValue(CurrentSecondaryHotkeyBox),
            TaskbarFreeCurrentScreenHotkey = GetRequiredHotkeyValue(TaskbarFreeCurrentHotkeyBox),
            TaskbarFreeCurrentScreenSecondaryHotkey = GetOptionalHotkeyValue(TaskbarFreeCurrentSecondaryHotkeyBox),
            VirtualDesktopHotkey = GetRequiredHotkeyValue(DesktopHotkeyBox),
            VirtualDesktopSecondaryHotkey = GetOptionalHotkeyValue(DesktopSecondaryHotkeyBox),
            StartRecordingHotkey = GetRequiredHotkeyValue(StartRecordingHotkeyBox),
            StartRecordingSecondaryHotkey = GetOptionalHotkeyValue(StartRecordingSecondaryHotkeyBox),
            StopRecordingHotkey = GetRequiredHotkeyValue(StopRecordingHotkeyBox),
            StopRecordingSecondaryHotkey = GetOptionalHotkeyValue(StopRecordingSecondaryHotkeyBox),
            StartLiveTranslationHotkey = GetRequiredHotkeyValue(StartLiveTranslationHotkeyBox),
            StartLiveTranslationSecondaryHotkey = GetOptionalHotkeyValue(StartLiveTranslationSecondaryHotkeyBox),
            StopLiveTranslationHotkey = GetRequiredHotkeyValue(StopLiveTranslationHotkeyBox),
            StopLiveTranslationSecondaryHotkey = GetOptionalHotkeyValue(StopLiveTranslationSecondaryHotkeyBox),
            ConfirmCaptureHotkey = GetRequiredHotkeyValue(ConfirmCaptureHotkeyBox),
            ConfirmCaptureSecondaryHotkey = GetOptionalHotkeyValue(ConfirmCaptureSecondaryHotkeyBox),
            CancelCaptureHotkey = GetRequiredHotkeyValue(CancelCaptureHotkeyBox),
            CancelCaptureSecondaryHotkey = GetOptionalHotkeyValue(CancelCaptureSecondaryHotkeyBox),
            StopLongCaptureHotkey = GetRequiredHotkeyValue(StopLongCaptureHotkeyBox),
            StopLongCaptureSecondaryHotkey = GetOptionalHotkeyValue(StopLongCaptureSecondaryHotkeyBox),
            ScreenshotDirectory = Path.GetFullPath(ScreenshotDirectoryBox.Text),
            LongCaptureDirectory = Path.GetFullPath(LongCaptureDirectoryBox.Text),
            RecordingDirectory = Path.GetFullPath(RecordingDirectoryBox.Text),
            CopyToClipboard = CopyToClipboardBox.IsChecked == true,
            ShowBorderFeedback = BorderFeedbackBox.IsChecked == true,
            ShowLiveTranslationStopControl = LiveStopControlBox.IsChecked == true,
            ShowLiveTranslationInRecordings = LiveRecordingOverlayBox.IsChecked == true,
            StartWithWindows = StartupBox.IsChecked == true,
            CleanupScheduleEnabled = CleanupScheduleBox.IsChecked == true,
            CleanupRetentionDays = SettingsInputParser.ParseRetentionDays(CleanupRetentionDaysBox.Text),
            CleanupDailyTime = SettingsInputParser.ParseDailyTime(CleanupDailyTimeBox.Text),
            CleanupScheduleFrequency = ParseCleanupFrequency(SelectedValue(CleanupFrequencyBox)),
            CleanupWeeklyDay = ParseCleanupWeeklyDay(SelectedValue(CleanupWeeklyDayBox)),
            Provider = SelectedProvider(),
            HasExplicitProviderChoice = true,
            LiveTranslationProvider = SelectedLiveProvider(),
            DeepSeekModel = SelectedValue(ModelBox),
            InterfaceLanguage = SelectedInterfaceLanguage(),
            InterfaceLanguageFollowsWindows = FollowsWindowsLanguage(),
            TranslationTargetLanguage = SelectedValue(TranslationTargetLanguageBox),
            MachineTranslationFallbackEnabled = MachineFallbackBox.IsChecked == true,
            LocalLmtModelPath = NullIfWhiteSpace(LocalLmtModelPathBox.Text),
            CredentialName = CredentialVault.FixedDeepSeekSlot,
        };
        updated.Validate();
        _applyCoordinator.Apply(_settings, updated);
        _settings = updated;
        SettingsApplied?.Invoke(this, updated);
    }

    private void SetHotkeyValue(System.Windows.Controls.TextBox box, GlobalHotkey? hotkey)
    {
        _hotkeyValues[box] = hotkey;
        box.Text = hotkey is null ? Ui("未设置", "Not set") : HotkeyDisplayFormatter.Format(hotkey);
    }

    private void WireHotkeyCaptureFocus()
    {
        foreach (var box in _hotkeyValues.Keys)
        {
            box.GotKeyboardFocus += (_, _) => HotkeyCaptureFocusChanged?.Invoke(this, true);
            box.LostKeyboardFocus += (_, _) => HotkeyCaptureFocusChanged?.Invoke(this, false);
        }
    }

    private GlobalHotkey GetRequiredHotkeyValue(System.Windows.Controls.TextBox box) =>
        _hotkeyValues.TryGetValue(box, out var hotkey) && hotkey is not null
            ? hotkey
            : throw new InvalidOperationException($"快捷键字段尚未初始化: {box.Name}");

    private GlobalHotkey? GetOptionalHotkeyValue(System.Windows.Controls.TextBox box) =>
        _hotkeyValues.TryGetValue(box, out var hotkey)
            ? hotkey
            : throw new InvalidOperationException($"备用快捷键字段尚未初始化: {box.Name}");

    private void SetLocalStatus(string chinese, string english)
    {
        _localStatusChinese = chinese;
        _localStatusEnglish = english;
        LocalModelStatusText.Text = Ui(chinese, english);
    }

    private void RenderCostSummary()
    {
        if (_lastCostSummary is null) return;
        DeepSeekCostSummaryText.Text = DeepSeekCostSummaryFormatter.Format(_lastCostSummary, _settings.InterfaceLanguage);
        DeepSeekCostSummaryDetailText.Text = DeepSeekCostSummaryFormatter.Detail(_lastCostSummary, interfaceLanguage: _settings.InterfaceLanguage);
    }

    private void RefreshCredentials()
    {
        var slot = _credentialVault.List().FirstOrDefault(item =>
            string.Equals(item.Name, CredentialVault.FixedDeepSeekSlot, StringComparison.OrdinalIgnoreCase));
        _credentialSuffix = slot?.MaskedSuffix;
        RenderCredentialStatus();
    }

    private void RenderCredentialStatus() => CredentialSlotStatusText.Text = _credentialSuffix is null
        ? Ui("固定本机凭据槽：未保存", "Local credential slot: empty")
        : Ui($"固定本机凭据槽：已保存（{_credentialSuffix}）", $"Local credential slot: saved ({_credentialSuffix})");

    private static string SelectedValue(System.Windows.Controls.ComboBox box) =>
        box.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string value }
            ? value
            : (box.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? string.Empty;

    private static void SelectComboValue(System.Windows.Controls.ComboBox box, string value)
    {
        var item = box.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, value) || Equals(item.Content, value));
        if (item is not null) box.SelectedItem = item;
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CleanupWeeklyDayDisplay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "周一", DayOfWeek.Tuesday => "周二", DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四", DayOfWeek.Friday => "周五", DayOfWeek.Saturday => "周六",
        DayOfWeek.Sunday => "周日", _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };

    private static string CleanupFrequencyDisplay(CleanupScheduleFrequency frequency) => frequency switch
    {
        CleanupScheduleFrequency.Daily => "每天",
        CleanupScheduleFrequency.Weekly => "每周",
        _ => throw new ArgumentOutOfRangeException(nameof(frequency)),
    };

    private static CleanupScheduleFrequency ParseCleanupFrequency(string value) => value switch
    {
        "每天" => CleanupScheduleFrequency.Daily,
        "每周" => CleanupScheduleFrequency.Weekly,
        _ => throw new InvalidOperationException("自动清理执行频率无效。"),
    };

    private void CleanupFrequencyBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCleanupFrequencyVisibility();

    private void UpdateCleanupFrequencyVisibility()
    {
        if (CleanupFrequencyBox is null || CleanupWeeklyDayBox is null) return;
        CleanupWeeklyDayBox.Visibility = SelectedValue(CleanupFrequencyBox) == "每周"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ShowInfoStatus(string message) => ShowStatus(message, success: null);

    private void ShowStatus(string message, bool? success)
    {
        StatusText.Text = message;
        StatusIcon.Data = Geometry.Parse(success switch
        {
            true => "M 2,9 L 7,14 L 16,3",
            false => "M 3,3 L 15,15 M 15,3 L 3,15",
            null => "M 9,2 A 7,7 0 1 1 8.9,2 M 9,6 L 9,10 M 9,13 L 9,14",
        });
        if (success == true)
        {
            var rainbow = CreateStatusRainbowBrush(0.95);
            StatusIcon.Stroke = rainbow;
            StatusPanel.BorderBrush = rainbow;
            StatusPanel.Background = CreateStatusRainbowBrush(0.16);
        }
        else
        {
            var foreground = success == false ? "#FF7B88" : "#75CFFF";
            var border = success == false ? "#934953" : "#3E6E91";
            var background = success == false ? "#351E24" : "#172A3A";
            StatusIcon.Stroke = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(foreground));
            StatusPanel.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(border));
            StatusPanel.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(background));
        }
        StatusPanel.BeginAnimation(OpacityProperty, null);
        StatusPanel.Opacity = 1;
        if (!SystemParameters.ClientAreaAnimation) return;

        if (success == true) AnimateRainbowSuccess();

        StatusPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        if (StatusPanel.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut },
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut },
            });
        }
    }

    private static LinearGradientBrush CreateStatusRainbowBrush(double opacity)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0.5),
            EndPoint = new System.Windows.Point(1, 0.5),
            Opacity = opacity,
            RelativeTransform = new TranslateTransform(0.65, 0),
        };
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0xFF, 0x4D, 0x6D), 0));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0xFF, 0xC8, 0x57), 0.24));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0x45, 0xD6, 0xA0), 0.5));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0x4D, 0x8D, 0xFF), 0.74));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0xB2, 0x6D, 0xFF), 1));
        return brush;
    }

    private void AnimateRainbowSuccess()
    {
        AnimateRainbowBrush(StatusPanel.BorderBrush);
        AnimateRainbowBrush(StatusPanel.Background);
        if (StatusPanel.RenderTransform is not ScaleTransform scale) return;
        var pulse = new DoubleAnimationUsingKeyFrames();
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0.94, KeyTime.FromPercent(0)));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.025, KeyTime.FromPercent(0.55), new CubicEase { EasingMode = EasingMode.EaseOut }));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
        pulse.Duration = TimeSpan.FromMilliseconds(520);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private static void AnimateRainbowBrush(System.Windows.Media.Brush brush)
    {
        if (brush is not LinearGradientBrush { RelativeTransform: TranslateTransform sweep }) return;
        sweep.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.65, -0.65, TimeSpan.FromMilliseconds(920))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    private static DayOfWeek ParseCleanupWeeklyDay(string value) => value switch
    {
        "周一" => DayOfWeek.Monday, "周二" => DayOfWeek.Tuesday, "周三" => DayOfWeek.Wednesday,
        "周四" => DayOfWeek.Thursday, "周五" => DayOfWeek.Friday, "周六" => DayOfWeek.Saturday,
        "周日" => DayOfWeek.Sunday, _ => throw new InvalidOperationException("自动清理每周执行日无效。"),
    };

    private static bool IsSecondaryHotkeyBox(string controlName) => controlName.EndsWith(
        "SecondaryHotkeyBox",
        StringComparison.Ordinal);

    private static GlobalHotkey DefaultHotkeyFor(string controlName) => controlName switch
    {
        nameof(RegionHotkeyBox) => GlobalHotkey.Parse("Ctrl+Win+A"),
        nameof(CurrentHotkeyBox) => GlobalHotkey.Parse("Insert"),
        nameof(TaskbarFreeCurrentHotkeyBox) => GlobalHotkey.Parse("Ctrl+Insert"),
        nameof(DesktopHotkeyBox) => GlobalHotkey.Parse("Alt+OemQuestion"),
        nameof(StartRecordingHotkeyBox) => GlobalHotkey.Parse("Ctrl+Win+Z"),
        nameof(StopRecordingHotkeyBox) => GlobalHotkey.Parse("F8"),
        nameof(StartLiveTranslationHotkeyBox) => GlobalHotkey.Parse("Ctrl+Win+T"),
        nameof(StopLiveTranslationHotkeyBox) => GlobalHotkey.Parse("Ctrl+Win+Y"),
        nameof(ConfirmCaptureHotkeyBox) => GlobalHotkey.Parse("Enter"),
        nameof(CancelCaptureHotkeyBox) => GlobalHotkey.Parse("Escape"),
        nameof(StopLongCaptureHotkeyBox) => GlobalHotkey.Parse("Escape"),
        _ => throw new ArgumentOutOfRangeException(nameof(controlName)),
    };

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SectionNavigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name } && FindName(name) is FrameworkElement section)
            section.BringIntoView();
    }

    private void StartSignatureMotion()
    {
        if (!SignatureMotionPolicy.ShouldAnimate(SystemParameters.ClientAreaAnimation)) return;
        if (SignatureBadge.BorderBrush is not LinearGradientBrush brush) return;
        var rotation = new RotateTransform(0, 0.5, 0.5);
        brush.RelativeTransform = rotation;
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(10))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    private void StopSignatureMotion()
    {
        if (SignatureBadge.BorderBrush is LinearGradientBrush brush
            && brush.RelativeTransform is RotateTransform rotation)
        {
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        }
        if (SignatureBadge.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }

    private void OnSignatureMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => AnimateSignatureScale(1.03);

    private void OnSignatureMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => AnimateSignatureScale(1);

    private void AnimateSignatureScale(double target)
    {
        if (!SignatureMotionPolicy.ShouldAnimate(SystemParameters.ClientAreaAnimation)
            || SignatureBadge.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(167);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, duration) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, duration) { EasingFunction = easing });
    }
}

public static class SignatureMotionPolicy
{
    public static bool ShouldAnimate(bool clientAreaAnimation) => clientAreaAnimation;
}

public static class SettingsInputParser
{
    public static int ParseRetentionDays(string text)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            || days is < 1 or > 3650)
        {
            throw new InvalidOperationException("文件保留天数必须是 1 到 3650 之间的整数。");
        }
        return days;
    }

    public static TimeOnly ParseDailyTime(string text)
    {
        if (!TimeOnly.TryParseExact(
                text.Trim(),
                ["H:mm", "HH:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            throw new InvalidOperationException("每天执行时间必须使用 24 小时格式，例如 03:00 或 18:30。");
        }
        return time;
    }
}

public sealed class SettingsConnectionTester(Func<IDeepSeekTranslationProvider> providerFactory)
{
    public Task<TranslationResult> TestAsync(string credentialName, string model, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialName);
        return providerFactory().TestConnectionAsync(credentialName, model, cancellationToken);
    }

    public Task<TranslationResult> TestCandidateAsync(string apiKey, string model, CancellationToken cancellationToken) =>
        providerFactory().TestCandidateConnectionAsync(apiKey, model, cancellationToken);
}
