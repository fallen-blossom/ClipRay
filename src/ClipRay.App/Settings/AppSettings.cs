using ClipRay.App.Infrastructure;
using System.Globalization;
using System.IO;

namespace ClipRay.App.Settings;

public enum CleanupScheduleFrequency { Daily, Weekly }

public sealed record AppSettings
{
    public GlobalHotkey RegionCaptureHotkey { get; init; } = GlobalHotkey.Parse("Ctrl+Win+A");

    public GlobalHotkey CurrentScreenHotkey { get; init; } = GlobalHotkey.Parse("Insert");

    // Legacy JSON property name retained; this binding now includes the taskbar.
    public GlobalHotkey TaskbarFreeCurrentScreenHotkey { get; init; } = GlobalHotkey.Parse("Ctrl+Insert");

    public GlobalHotkey VirtualDesktopHotkey { get; init; } = GlobalHotkey.Parse("Alt+OemQuestion");

    public GlobalHotkey StartRecordingHotkey { get; init; } = GlobalHotkey.Parse("Ctrl+Win+Z");

    public GlobalHotkey StopRecordingHotkey { get; init; } = GlobalHotkey.Parse("F8");

    public GlobalHotkey StartLiveTranslationHotkey { get; init; } = GlobalHotkey.Parse("Ctrl+Win+T");

    public GlobalHotkey StopLiveTranslationHotkey { get; init; } = GlobalHotkey.Parse("Ctrl+Win+Y");

    public GlobalHotkey? StartLiveTranslationSecondaryHotkey { get; init; }

    public GlobalHotkey? StopLiveTranslationSecondaryHotkey { get; init; }

    public GlobalHotkey ConfirmCaptureHotkey { get; init; } = GlobalHotkey.Parse("Enter");

    public GlobalHotkey CancelCaptureHotkey { get; init; } = GlobalHotkey.Parse("Escape");

    public GlobalHotkey StopLongCaptureHotkey { get; init; } = GlobalHotkey.Parse("Escape");

    public GlobalHotkey? RegionCaptureSecondaryHotkey { get; init; }

    public GlobalHotkey? CurrentScreenSecondaryHotkey { get; init; }

    public GlobalHotkey? TaskbarFreeCurrentScreenSecondaryHotkey { get; init; }

    public GlobalHotkey? VirtualDesktopSecondaryHotkey { get; init; }

    public GlobalHotkey? StartRecordingSecondaryHotkey { get; init; }

    public GlobalHotkey? StopRecordingSecondaryHotkey { get; init; }

    public GlobalHotkey? ConfirmCaptureSecondaryHotkey { get; init; }

    public GlobalHotkey? CancelCaptureSecondaryHotkey { get; init; }

    public GlobalHotkey? StopLongCaptureSecondaryHotkey { get; init; }

    public string ScreenshotDirectory { get; init; } = string.Empty;

    public string LongCaptureDirectory { get; init; } = string.Empty;

    public string RecordingDirectory { get; init; } = string.Empty;

    // Written only when the complete layout is known to be the generated default.
    // Unmarked legacy folders remain ambiguous, even when their names look familiar.
    public string? DefaultOutputLayoutRoot { get; init; }

    public bool CopyToClipboard { get; init; } = true;

    public bool ShowBorderFeedback { get; init; } = true;

    public bool ShowLiveTranslationStopControl { get; init; } = true;

    public bool ShowLiveTranslationBorder { get; init; }

    public bool ShowLiveTranslationInRecordings { get; init; }

    public bool StartWithWindows { get; init; }

    public bool CleanupScheduleEnabled { get; init; }

    public int CleanupRetentionDays { get; init; } = 30;

    public TimeOnly CleanupDailyTime { get; init; } = new(21, 0);

    public CleanupScheduleFrequency CleanupScheduleFrequency { get; init; } = CleanupScheduleFrequency.Daily;

    public DayOfWeek CleanupWeeklyDay { get; init; } = DayOfWeek.Monday;

    public string Provider { get; init; } = "Machine";

    public bool HasExplicitProviderChoice { get; init; }

    public string LiveTranslationProvider { get; init; } = "Machine";

    public string DeepSeekModel { get; init; } = "deepseek-flash";

    public string InterfaceLanguage { get; init; } = "zh-CN";

    public bool InterfaceLanguageFollowsWindows { get; init; }

    public string TranslationTargetLanguage { get; init; } = "简体中文";

    public bool MachineTranslationFallbackEnabled { get; init; }

    public string? LocalLmtModelPath { get; init; }

    public string? CredentialName { get; init; }

    public static AppSettings CreateDefault(string workspaceRoot, string? windowsUiCultureName = null)
    {
        var directories = OutputDirectoryLayout.Create(workspaceRoot);
        return new AppSettings
        {
            ScreenshotDirectory = directories.Screenshots,
            LongCaptureDirectory = directories.LongCaptures,
            RecordingDirectory = directories.Recordings,
            DefaultOutputLayoutRoot = Path.GetFullPath(workspaceRoot),
            MachineTranslationFallbackEnabled = true,
            InterfaceLanguage = AppInterfaceLanguage.ForWindowsUiCulture(windowsUiCultureName ?? CultureInfo.CurrentUICulture.Name),
            InterfaceLanguageFollowsWindows = true,
        };
    }

    public AppSettings WithMissingDefaults(string workspaceRoot, string? windowsUiCultureName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var directories = OutputDirectoryLayout.Create(workspaceRoot);
        var rebaseDefaultLayout = OutputDirectoryLayout.HasConfirmedDefaultLayout(this);
        var allMissing = string.IsNullOrWhiteSpace(ScreenshotDirectory)
            && string.IsNullOrWhiteSpace(LongCaptureDirectory) && string.IsNullOrWhiteSpace(RecordingDirectory);
        var settings = this with
        {
            InterfaceLanguage = InterfaceLanguageFollowsWindows
                ? AppInterfaceLanguage.ForWindowsUiCulture(windowsUiCultureName ?? CultureInfo.CurrentUICulture.Name)
                : AppInterfaceLanguage.Normalize(InterfaceLanguage),
            ScreenshotDirectory = rebaseDefaultLayout || string.IsNullOrWhiteSpace(ScreenshotDirectory)
                ? directories.Screenshots
                : ScreenshotDirectory,
            LongCaptureDirectory = rebaseDefaultLayout || string.IsNullOrWhiteSpace(LongCaptureDirectory)
                ? directories.LongCaptures
                : LongCaptureDirectory,
            RecordingDirectory = rebaseDefaultLayout || string.IsNullOrWhiteSpace(RecordingDirectory)
                ? directories.Recordings
                : RecordingDirectory,
            DefaultOutputLayoutRoot = rebaseDefaultLayout || allMissing ? Path.GetFullPath(workspaceRoot) : null,
            Provider = !HasExplicitProviderChoice
                && TranslationTargetLanguage == "简体中文"
                && Provider is "DeepSeek" or "Local LMT"
                ? "Machine"
                : Provider,
        };
        return settings;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ScreenshotDirectory))
        {
            throw new InvalidOperationException("截图目录不能为空。");
        }

        if (string.IsNullOrWhiteSpace(RecordingDirectory))
        {
            throw new InvalidOperationException("录屏目录不能为空。");
        }

        if (string.IsNullOrWhiteSpace(LongCaptureDirectory))
        {
            throw new InvalidOperationException("长截图目录不能为空。");
        }

        if (CleanupRetentionDays is < 1 or > 3650)
        {
            throw new InvalidOperationException("自动清理保留天数必须在 1 到 3650 天之间。");
        }

        if (!Enum.IsDefined(CleanupScheduleFrequency))
        {
            throw new InvalidOperationException("自动清理执行频率无效。");
        }

        if (!Enum.IsDefined(CleanupWeeklyDay))
        {
            throw new InvalidOperationException("自动清理每周执行日无效。");
        }

        if (Provider is not ("DeepSeek" or "Local LMT" or "Machine"))
        {
            throw new InvalidOperationException("Provider 必须是 DeepSeek、Local LMT 或 Machine。");
        }

        if (LiveTranslationProvider is not ("DeepSeek" or "Machine"))
        {
            throw new InvalidOperationException("LiveTranslationProvider 必须是 DeepSeek 或 Machine。");
        }

        if (Provider == "Machine" && TranslationTargetLanguage != "简体中文")
        {
            throw new InvalidOperationException("Machine 当前仅支持 English/auto → 简体中文。");
        }

        if (DeepSeekModel != "deepseek-flash")
        {
            throw new InvalidOperationException("DeepSeek model 值无效。");
        }

        if (TranslationTargetLanguage is not ("简体中文" or "English" or "日本語" or "Français" or "Русский"))
        {
            throw new InvalidOperationException("Translation target language is invalid.");
        }

        ValidateHotkeys();
    }

    public void ValidateHotkeys()
    {
        var globalHotkeys = new[]
        {
            HotkeyBindingPolicy.Active(RegionCaptureHotkey, RegionCaptureSecondaryHotkey),
            HotkeyBindingPolicy.Active(CurrentScreenHotkey, CurrentScreenSecondaryHotkey),
            HotkeyBindingPolicy.Active(TaskbarFreeCurrentScreenHotkey, TaskbarFreeCurrentScreenSecondaryHotkey),
            HotkeyBindingPolicy.Active(VirtualDesktopHotkey, VirtualDesktopSecondaryHotkey),
            HotkeyBindingPolicy.Active(StartRecordingHotkey, StartRecordingSecondaryHotkey),
            HotkeyBindingPolicy.Active(StopRecordingHotkey, StopRecordingSecondaryHotkey),
            HotkeyBindingPolicy.Active(StartLiveTranslationHotkey, StartLiveTranslationSecondaryHotkey),
            HotkeyBindingPolicy.Active(StopLiveTranslationHotkey, StopLiveTranslationSecondaryHotkey),
        }.SelectMany(static hotkeys => hotkeys).ToArray();
        if (globalHotkeys.Distinct().Count() != globalHotkeys.Length)
        {
            throw new InvalidOperationException("全局快捷键不能冲突。");
        }
        var stopLongCaptureHotkeys = HotkeyBindingPolicy.Active(
            StopLongCaptureHotkey,
            StopLongCaptureSecondaryHotkey);
        if (stopLongCaptureHotkeys.Distinct().Count() != stopLongCaptureHotkeys.Count)
        {
            throw new InvalidOperationException("停止长截图的主快捷键与备用快捷键不能重复。");
        }
        if (stopLongCaptureHotkeys.Any(globalHotkeys.Contains))
        {
            throw new InvalidOperationException("停止长截图快捷键不能与全局快捷键冲突。");
        }

        var overlayHotkeys = new[]
        {
            HotkeyBindingPolicy.Active(ConfirmCaptureHotkey, ConfirmCaptureSecondaryHotkey),
            HotkeyBindingPolicy.Active(CancelCaptureHotkey, CancelCaptureSecondaryHotkey),
        }.SelectMany(static hotkeys => hotkeys).ToArray();
        if (overlayHotkeys.Distinct().Count() != overlayHotkeys.Length)
        {
            throw new InvalidOperationException("截图界面快捷键不能冲突。");
        }
        if (overlayHotkeys.Any(globalHotkeys.Contains))
        {
            throw new InvalidOperationException("截图界面快捷键不能与全局快捷键冲突。");
        }
        if (globalHotkeys.Concat(overlayHotkeys).Concat(stopLongCaptureHotkeys)
            .Any(HotkeyCapturePolicy.IsReservedForEditing))
        {
            throw new InvalidOperationException("Ctrl+Z 和 Ctrl+Shift+Z 已固定用于撤回与重做，不能绑定到其他操作。");
        }

        foreach (var hotkey in globalHotkeys.Concat(overlayHotkeys).Concat(stopLongCaptureHotkeys))
        {
            _ = hotkey.ToVirtualKey();
        }
    }
}
