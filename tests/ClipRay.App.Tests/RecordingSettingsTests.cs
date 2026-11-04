using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using ClipRay.App.Recording;
using ClipRay.App.Settings;
using static BackgroundTask3Tests;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;

internal static class RecordingSettingsTests
{
    public static void TimingAndPersistence()
    {
        var legacy = JsonSerializer.Deserialize<AppSettings>("{}")!;
        if (legacy.RecordingFramesPerSecond != 60 || legacy.RecordingQuality != RecordingQuality.Balanced
            || !legacy.RecordingIncludeCursor || !legacy.RecordingIncludeSystemAudio)
            throw new Exception("Legacy settings lost recording defaults.");
        var settings = AppSettings.CreateDefault(Path.GetTempPath()) with
        {
            RecordingFramesPerSecond = 24, RecordingQuality = RecordingQuality.High,
            RecordingIncludeCursor = false, RecordingIncludeSystemAudio = false,
        };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        if (restored.CreateRecordingVideoOptions() != settings.CreateRecordingVideoOptions())
            throw new Exception("Recording options did not survive storage.");
        var options = restored.CreateRecordingVideoOptions();
        options.Validate();
        var jittered = new RecordingFramePacer(60, 1_000_000);
        var accepted = 0;
        for (var index = 0; index < 120; index++)
            if (jittered.ShouldCapture(index * 16_667L + (index % 2 == 0 ? 500 : -500))) accepted++;
        if (accepted < 118) throw new Exception("Normal present jitter dropped otherwise valid 60Hz frames.");
        if (options.SampleDuration100Nanoseconds != 10_000_000L / 24)
            throw new Exception("Selected frame rate and sample duration disagree.");
        if (RecordingBitratePolicy.ForFrameSize(1920, 1080, 60, RecordingQuality.High) != 20_000_000)
            throw new Exception("Original high bitrate recording is unavailable.");
        if (RecordingBitratePolicy.ForFrameSize(1920, 1080, 60, RecordingQuality.Compact)
            >= RecordingBitratePolicy.ForFrameSize(1920, 1080, 60, RecordingQuality.Balanced))
            throw new Exception("Smaller-file recording did not reduce the target bitrate.");
    }
}

internal static partial class BackgroundTask4Tests
{
    internal static void RecordingSettingsWindowRoundTrip()
    {
        var root = TempRoot();
        var initial = Settings(root);
        var store = new MemoryStore();
        var window = Window(initial, store);
        try
        {
            var presets = Control<ComboBox>(window, "RecordingPresetBox");
            presets.SelectedIndex = 1;
            Click(window, "SaveSettingsButton");
            Check(store.Saved?.RecordingFramesPerSecond == 60 && store.Saved.RecordingQuality == RecordingQuality.High,
                "Settings did not save the original high quality recording preset.");
            CloseFixture(window);
            window = Window(store.Saved!, store);
            Check((Control<ComboBox>(window, "RecordingPresetBox").SelectedItem as ComboBoxItem)?.Tag as string == "High",
                "Reopened settings did not retain the selected preset.");
            Control<ComboBox>(window, "RecordingFrameRateBox").SelectedIndex = 2;
            Control<CheckBox>(window, "RecordingCursorBox").IsChecked = false;
            Control<CheckBox>(window, "RecordingSystemAudioBox").IsChecked = false;
            Check((Control<ComboBox>(window, "RecordingPresetBox").SelectedItem as ComboBoxItem)?.Tag as string == "Custom",
                "A changed frame rate remained mislabeled as a preset.");
            Click(window, "SaveSettingsButton");
            Check(store.Saved!.RecordingFramesPerSecond == 30 && !store.Saved.RecordingIncludeCursor && !store.Saved.RecordingIncludeSystemAudio,
                "Settings did not commit custom frame rate, pointer and audio choices.");
            var path = Path.Combine(root, "recording-settings.json");
            var actualStore = new SettingsStore(path, root);
            actualStore.Save(store.Saved);
            var reloaded = actualStore.Load();
            Check(reloaded.CreateRecordingVideoOptions() == store.Saved.CreateRecordingVideoOptions(),
                "Persisted recording choices changed when reloaded.");
            Check(reloaded.ScreenshotDirectory == initial.ScreenshotDirectory && reloaded.Provider == initial.Provider,
                "Recording preferences changed unrelated output or translation settings.");
            window.ApplyRecordingPreferences(reloaded with { RecordingFramesPerSecond=60, RecordingQuality=RecordingQuality.Balanced });
            Check(!window.HasPendingSettingsDraft && Control<ComboBox>(window,"RecordingFrameRateBox").SelectedIndex==3,
                "A CLI preference refresh left the settings window stale or dirty.");
            Control<ComboBox>(window,"RecordingFrameRateBox").SelectedIndex=2;
            var draftProtected=false;
            try { window.ApplyRecordingPreferences(reloaded); } catch(InvalidOperationException) { draftProtected=true; }
            Check(draftProtected && window.HasPendingSettingsDraft,"A CLI refresh discarded the user's unsaved settings draft.");
        }
        finally { CloseFixture(window); }
    }
}
