using System.Text.Json;
using System.IO;
using System.Globalization;
using ClipRay.App.Infrastructure;

namespace ClipRay.App.Settings;

public interface ISettingsStore
{
    AppSettings Load();

    SettingsLoadResult LoadRecoverable();

    void Save(AppSettings settings);
}

public sealed record SettingsLoadResult(AppSettings Settings, string? Warning, bool RequiresSave = false,
    bool PreserveOriginalFile = false);

public static class ExternalSettingsReconciler
{
    public static string? RepairCleanupSchedule(
        AppSettings settings,
        ICleanupScheduleRegistration registration,
        string executablePath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(registration);
        if (!settings.CleanupScheduleEnabled) return null;
        try
        {
            registration.Set(CleanupScheduleOptions.FromSettings(settings), executablePath);
            return null;
        }
        catch (Exception exception)
        {
            return $"自动清理计划尚未修复（错误 0x{unchecked((uint)exception.HResult):X8}）；请在设置中重试。";
        }
    }
}

public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly string _workspaceRoot;
    private readonly Func<string> _windowsUiCultureName;

    public SettingsStore(string path, string workspaceRoot, Func<string>? windowsUiCultureName = null)
    {
        _path = path;
        _workspaceRoot = workspaceRoot;
        _windowsUiCultureName = windowsUiCultureName ?? (() => CultureInfo.CurrentUICulture.Name);
    }

    public AppSettings Load()
    {
        return LoadResult().Settings;
    }

    public SettingsLoadResult LoadRecoverable()
    {
        try
        {
            return LoadResult();
        }
        catch (Exception exception) when (IsRecoverableContentError(exception))
        {
            return new SettingsLoadResult(
                AppSettings.CreateDefault(_workspaceRoot, _windowsUiCultureName()),
                "设置文件无效，已使用默认设置。原文件已保留，修正后可在设置中保存。", PreserveOriginalFile: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(AppSettings.CreateDefault(_workspaceRoot, _windowsUiCultureName()),
                $"无法读取设置文件（错误 0x{unchecked((uint)exception.HResult):X8}）；已使用默认设置，原文件未更改。", PreserveOriginalFile: true);
        }
    }

    private SettingsLoadResult LoadResult()
    {
        string json;
        try { json = File.ReadAllText(_path); }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        { return new SettingsLoadResult(AppSettings.CreateDefault(_workspaceRoot, _windowsUiCultureName()), null); }
        using var document = JsonDocument.Parse(json);
        var hasMachineFallbackChoice = document.RootElement.TryGetProperty(
            nameof(AppSettings.MachineTranslationFallbackEnabled),
            out _);
        var hasLiveTranslationProviderChoice = document.RootElement.TryGetProperty(
            nameof(AppSettings.LiveTranslationProvider),
            out _);
        var persisted = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("设置文件内容为空。");
        var settings = persisted.WithMissingDefaults(_workspaceRoot, _windowsUiCultureName());
        if (settings.ShowLiveTranslationBorder)
            settings = settings with { ShowLiveTranslationBorder = false };
        if (settings.DeepSeekModel is "deepseek-v4-flash" or "deepseek-v4-pro")
            settings = settings with { DeepSeekModel = "deepseek-flash" };
        if (!hasMachineFallbackChoice)
            settings = settings with { MachineTranslationFallbackEnabled = true };
        if (!hasLiveTranslationProviderChoice)
            settings = settings with { LiveTranslationProvider = "Machine" };
        var liveHotkeysAdjusted = false;
        if (!document.RootElement.TryGetProperty(nameof(AppSettings.StartLiveTranslationHotkey), out _)
            && HasOtherBinding(settings, settings.StartLiveTranslationHotkey, nameof(AppSettings.StartLiveTranslationHotkey)))
        {
            settings = settings with { StartLiveTranslationHotkey = AvailableLiveBinding(settings, "Ctrl+Win+Shift+T") };
            liveHotkeysAdjusted = true;
        }
        if (!document.RootElement.TryGetProperty(nameof(AppSettings.StopLiveTranslationHotkey), out _)
            && HasOtherBinding(settings, settings.StopLiveTranslationHotkey, nameof(AppSettings.StopLiveTranslationHotkey)))
        {
            settings = settings with { StopLiveTranslationHotkey = AvailableLiveBinding(settings, "Ctrl+Win+Shift+Y") };
            liveHotkeysAdjusted = true;
        }
        settings.Validate();
        return new SettingsLoadResult(settings,
            liveHotkeysAdjusted ? "实时翻译默认快捷键与旧配置冲突，已仅调整新增快捷键；原有设置保留。" : null,
            settings != persisted || !hasLiveTranslationProviderChoice);
    }

    private static IEnumerable<(string Name, GlobalHotkey Key)> Hotkeys(AppSettings settings) =>
        typeof(AppSettings).GetProperties()
            .Where(static property => property.PropertyType == typeof(GlobalHotkey))
            .Select(property => (property.Name, Value: property.GetValue(settings)))
            .Where(static item => item.Value is GlobalHotkey)
            .Select(static item => (item.Name, (GlobalHotkey)item.Value!));

    private static bool HasOtherBinding(AppSettings settings, GlobalHotkey key, string ownName) =>
        Hotkeys(settings).Any(item => item.Name != ownName && item.Key == key);

    private static GlobalHotkey AvailableLiveBinding(AppSettings settings, string preferred)
    {
        var taken = Hotkeys(settings).Select(static item => item.Key).ToHashSet();
        return new[] { preferred }.Concat(Enumerable.Range(1, 24).Select(static index => $"Ctrl+Alt+Win+Shift+F{index}"))
            .Select(GlobalHotkey.Parse).First(key => !taken.Contains(key));
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static bool IsRecoverableContentError(Exception exception) => exception is
        JsonException or
        InvalidDataException or
        InvalidOperationException or
        FormatException or
        ArgumentException or
        NotSupportedException;
}

public sealed class SettingsApplyCoordinator
{
    private readonly ISettingsStore _settingsStore;
    private readonly IStartupRegistration _startupRegistration;
    private readonly ICleanupScheduleRegistration _cleanupScheduleRegistration;
    private readonly string _executablePath;

    public SettingsApplyCoordinator(
        ISettingsStore settingsStore,
        IStartupRegistration startupRegistration,
        string executablePath)
        : this(
            settingsStore,
            startupRegistration,
            NoOpCleanupScheduleRegistration.Instance,
            executablePath)
    {
    }

    public SettingsApplyCoordinator(
        ISettingsStore settingsStore,
        IStartupRegistration startupRegistration,
        ICleanupScheduleRegistration cleanupScheduleRegistration,
        string executablePath)
    {
        _settingsStore = settingsStore;
        _startupRegistration = startupRegistration;
        _cleanupScheduleRegistration = cleanupScheduleRegistration;
        _executablePath = executablePath;
    }

    public void Apply(AppSettings prior, AppSettings updated)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(updated);
        updated.Validate();
        var startupChanged = prior.StartWithWindows != updated.StartWithWindows;
        var cleanupScheduleChanged = CleanupScheduleChanged(prior, updated);

        try
        {
            if (startupChanged)
                _startupRegistration.SetEnabled(updated.StartWithWindows, _executablePath);
        }
        catch (Exception primaryException)
        {
            throw new SettingsApplyException(
                "启动项更新失败；设置未更改。",
                primaryException,
                rollbackException: null);
        }

        try
        {
            if (cleanupScheduleChanged)
            {
                _cleanupScheduleRegistration.Set(CleanupScheduleOptions.FromSettings(updated), _executablePath);
            }
        }
        catch (Exception primaryException)
        {
            var rollbackException = RollBackExternalSettings(
                prior,
                includeCleanupSchedule: cleanupScheduleChanged,
                includeStartup: startupChanged);
            throw new SettingsApplyException(
                rollbackException is null
                    ? $"自动清理计划更新失败（错误 0x{unchecked((uint)primaryException.HResult):X8}）；设置未更改。"
                    : $"自动清理计划更新失败（错误 0x{unchecked((uint)primaryException.HResult):X8}），且系统设置回滚失败（错误 0x{unchecked((uint)rollbackException.HResult):X8}）；请重试。",
                primaryException,
                rollbackException);
        }

        try
        {
            _settingsStore.Save(updated);
        }
        catch (Exception primaryException)
        {
            var rollbackException = RollBackExternalSettings(
                prior,
                includeCleanupSchedule: cleanupScheduleChanged,
                includeStartup: startupChanged);
            if (rollbackException is not null)
                throw new SettingsApplyException(
                    rollbackException is AggregateException aggregate
                    && aggregate.InnerExceptions.Count == 1
                    && aggregate.InnerExceptions[0].Message.Contains("startup", StringComparison.OrdinalIgnoreCase)
                        ? "设置保存失败，且启动项回滚失败；请重试或手动恢复启动设置。"
                        : "设置保存失败，且系统设置回滚失败；请重试或手动恢复相关设置。",
                    primaryException,
                    rollbackException);

            throw new SettingsApplyException(
                "设置保存失败；启动项已恢复。",
                primaryException,
                rollbackException: null);
        }
    }

    private static bool CleanupScheduleChanged(AppSettings prior, AppSettings updated) =>
        prior.CleanupScheduleEnabled != updated.CleanupScheduleEnabled
        || updated.CleanupScheduleEnabled && (
            prior.CleanupDailyTime != updated.CleanupDailyTime
            || prior.CleanupScheduleFrequency != updated.CleanupScheduleFrequency
            || prior.CleanupWeeklyDay != updated.CleanupWeeklyDay);

    private Exception? RollBackExternalSettings(
        AppSettings prior,
        bool includeCleanupSchedule,
        bool includeStartup)
    {
        var failures = new List<Exception>();
        if (includeCleanupSchedule)
        {
            try
            {
                _cleanupScheduleRegistration.Set(CleanupScheduleOptions.FromSettings(prior), _executablePath);
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException("cleanup schedule rollback failure", exception));
            }
        }

        if (includeStartup)
        {
            try
            {
                _startupRegistration.SetEnabled(prior.StartWithWindows, _executablePath);
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException("startup rollback failure", exception));
            }
        }

        return failures.Count == 0 ? null : new AggregateException(failures);
    }
}

public sealed class SettingsApplyException : Exception
{
    public SettingsApplyException(string message, Exception primaryException, Exception? rollbackException)
        : base(message, primaryException)
    {
        PrimaryException = primaryException;
        RollbackException = rollbackException;
    }

    public Exception PrimaryException { get; }

    public Exception? RollbackException { get; }
}
