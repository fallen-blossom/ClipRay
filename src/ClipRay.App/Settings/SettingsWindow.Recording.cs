using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ClipRay.App.Recording;

namespace ClipRay.App.Settings;

public partial class SettingsWindow
{
    private bool _updatingRecordingChoices;
    internal bool HasPendingSettingsDraft => _draftState?.Action == SettingsPrimaryAction.Save;

    internal void ApplyRecordingPreferences(AppSettings settings)
    {
        if (HasPendingSettingsDraft) throw new InvalidOperationException("Settings have an unsaved draft.");
        _settings = settings;
        LoadRecordingControls();
        _savedNonLanguageDraft = BuildNonLanguageDraftFingerprint();
        _draftState = new SettingsDraftState(BuildDraftFingerprint());
        UpdatePrimaryActionState();
    }

    private void OpenCaptureBorderSettings_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-graphicscapturewithoutborder") { UseShellExecute = true }); }
        catch (Exception error) { ShowStatus(Ui($"无法打开 Windows 设置（0x{unchecked((uint)error.HResult):X8}）。", "Could not open Windows capture border settings."), success:false); }
    }

    private void LoadRecordingControls()
    {
        _updatingRecordingChoices = true;
        try
        {
            SelectComboValue(RecordingFrameRateBox, _settings.RecordingFramesPerSecond.ToString(CultureInfo.InvariantCulture));
            SelectComboValue(RecordingQualityBox, _settings.RecordingQuality.ToString());
            RecordingCursorBox.IsChecked = _settings.RecordingIncludeCursor;
            RecordingSystemAudioBox.IsChecked = _settings.RecordingIncludeSystemAudio;
        }
        finally { _updatingRecordingChoices = false; }
        RefreshRecordingChoices();
    }

    private void RecordingPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingRecordingChoices || RecordingFrameRateBox is null || RecordingQualityBox is null) return;
        var preset = SelectedValue(RecordingPresetBox);
        if (preset is "Balanced" or "High")
        {
            _updatingRecordingChoices = true;
            try
            {
                SelectComboValue(RecordingFrameRateBox, "60");
                SelectComboValue(RecordingQualityBox, preset);
            }
            finally { _updatingRecordingChoices = false; }
        }
        RefreshRecordingEstimate();
        ObserveSettingsDraft();
    }

    private void RecordingDetail_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingRecordingChoices) return;
        RefreshRecordingChoices();
        ObserveSettingsDraft();
    }

    private void RefreshRecordingChoices()
    {
        if (RecordingPresetBox is null || RecordingFrameRateBox is null || RecordingQualityBox is null) return;
        var quality = SelectedValue(RecordingQualityBox);
        _updatingRecordingChoices = true;
        try
        {
            SelectComboValue(RecordingPresetBox, SelectedValue(RecordingFrameRateBox) == "60" && quality is "Balanced" or "High"
                ? quality : "Custom");
        }
        finally { _updatingRecordingChoices = false; }
        RefreshRecordingEstimate();
    }

    private void RefreshRecordingEstimate()
    {
        if (RecordingEstimateText is null || RecordingQualityBox is null || RecordingFrameRateBox is null) return;
        if (!int.TryParse(SelectedValue(RecordingFrameRateBox), out var fps)
            || !Enum.TryParse<RecordingQuality>(SelectedValue(RecordingQualityBox), out var quality)) return;
        var bitrate = RecordingBitratePolicy.ForFrameSize(1920, 1080, fps, quality);
        var mbps = (bitrate / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture);
        var mbPerMinute = (bitrate * 60d / 8 / 1_000_000).ToString("0", CultureInfo.InvariantCulture);
        RecordingEstimateText.Text = Ui($"以 1080p 为例：目标 {mbps} Mbps，约 {mbPerMinute} MB/分钟（不含声音）；实际文件大小随画面内容变化。",
            $"At 1080p: target {mbps} Mbps, about {mbPerMinute} MB/minute before audio. Actual size varies with the content.");
    }

    private RecordingVideoOptions ReadRecordingOptions()
    {
        var options = new RecordingVideoOptions(
            int.Parse(SelectedValue(RecordingFrameRateBox), CultureInfo.InvariantCulture),
            Enum.Parse<RecordingQuality>(SelectedValue(RecordingQualityBox)),
            RecordingCursorBox.IsChecked == true, RecordingSystemAudioBox.IsChecked == true);
        options.Validate();
        return options;
    }

    private void SetRecordingAccessibleNames()
    {
        System.Windows.Automation.AutomationProperties.SetName(RecordingPresetBox, Ui("录屏方案", "Recording preset"));
        System.Windows.Automation.AutomationProperties.SetName(RecordingFrameRateBox, Ui("录屏目标帧率", "Recording target frame rate"));
        System.Windows.Automation.AutomationProperties.SetName(RecordingQualityBox, Ui("录屏画质与文件大小", "Recording quality and file size"));
    }
}
