using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Button = System.Windows.Controls.Button;

namespace ClipRay.App.Settings;

public partial class SettingsWindow
{
    private string _activeSettingsPage = "StartupSection";
    private readonly Dictionary<string, double> _settingsPageOffsets = new(StringComparer.Ordinal);
    internal string ActiveSettingsPage => _activeSettingsPage;

    internal void ShowSettingsPage(string name)
    {
        if (name is not ("StartupSection" or "OutputSection" or "RecordingSection"
            or "TranslationSection" or "HotkeysSection" or "MaintenanceSection" or "HelpSection"))
            throw new ArgumentOutOfRangeException(nameof(name));
        _settingsPageOffsets[_activeSettingsPage] = SettingsScroll.VerticalOffset;
        _activeSettingsPage = name;
        RefreshSettingsNavigation();
        SettingsScroll.UpdateLayout();
        SettingsScroll.ScrollToVerticalOffset(_settingsPageOffsets.GetValueOrDefault(name));
    }

    private void RefreshSettingsNavigation()
    {
        if (_settings is null || PageHeading is null || HelpSection is null) return;
        CleanupReasonsExpander.Header = Ui("查看保留与失败原因", "View kept files and failure reasons");
        foreach (var section in new FrameworkElement[] { StartupSection, OutputSection, RecordingSection,
            TranslationSection, HotkeysSection, MaintenanceSection, HelpSection })
            section.Visibility = section.Name == _activeSettingsPage ? Visibility.Visible : Visibility.Collapsed;
        var cloud = SelectedProvider() == "DeepSeek" || SelectedLiveProvider() == "DeepSeek";
        CredentialCard.Visibility = _activeSettingsPage == "TranslationSection" && cloud
            ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new Button[] { StartupNavigation, OutputNavigation, RecordingNavigation,
            TranslationNavigation, HotkeysNavigation, MaintenanceNavigation, HelpNavigation })
        {
            var current = button.Tag as string == _activeSettingsPage;
            Selector.SetIsSelected(button, current);
            System.Windows.Automation.AutomationProperties.SetItemStatus(button,
                current ? Ui("当前页", "Current page") : string.Empty);
        }
        (PageHeading.Text, PageDescription.Text) = _activeSettingsPage switch
        {
            "StartupSection" => (Ui("常规", "General"), Ui("选择界面语言和登录启动方式。", "Choose your language and sign-in behavior.")),
            "OutputSection" => (Ui("保存与复制", "Save and copy"), Ui("找到成果文件，选择完成后是否自动复制。", "Choose where captures are saved and what happens when they finish.")),
            "RecordingSection" => (Ui("录屏", "Recording"), Ui("按日常需要选择画质、帧率和声音。", "Choose quality, frame rate and sound for everyday recording.")),
            "TranslationSection" => (Ui("翻译与模型", "Translation"), Ui("选择翻译方式、目标语言，或试译一句话。", "Choose a provider and target language, or try a short translation.")),
            "HotkeysSection" => (Ui("快捷键", "Shortcuts"), Ui("点击输入框，再按下要使用的组合键。", "Select a shortcut field, then press the key combination you want.")),
            "MaintenanceSection" => (Ui("维护与清理", "Maintenance"), Ui("只清理已到期且确认保存完成的成果。", "Only expired captures with verified completion records are recycled.")),
            _ => (Ui("使用帮助", "Help"), Ui("查看操作步骤、常见问题和版本信息。", "Find practical steps, common questions and version information.")),
        };
    }

    private void RefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_draftState?.Action == SettingsPrimaryAction.Save) return;
        TryRefreshRuntimeData();
    }
}
