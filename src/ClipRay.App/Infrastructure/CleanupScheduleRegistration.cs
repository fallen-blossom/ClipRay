using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ClipRay.App.Settings;

namespace ClipRay.App.Infrastructure;

public sealed record CleanupScheduleOptions(
    bool Enabled,
    CleanupScheduleFrequency Frequency,
    DayOfWeek WeeklyDay,
    TimeOnly DailyTime)
{
    public static CleanupScheduleOptions FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new CleanupScheduleOptions(
            settings.CleanupScheduleEnabled,
            settings.CleanupScheduleFrequency,
            settings.CleanupWeeklyDay,
            settings.CleanupDailyTime);
    }
}

public sealed record CleanupScheduledTaskDefinition(
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    DateTime FirstRunLocal,
    bool RunOnlyWhenUserIsLoggedOn,
    bool StartWhenAvailable,
    bool IgnoreNewInstances,
    bool RequiresElevation,
    bool LaunchesExecutableDirectlyWithoutShell)
{
    public const string TaskPath = @"\ClipRay Cleanup";
    public const string FolderPath = @"\";
    public const string TaskName = "ClipRay Cleanup";
    public const string LegacyFolderPath = @"\ClipRay";
    public const string LegacyTaskName = "Cleanup";

    public static CleanupScheduledTaskDefinition Build(
        CleanupScheduleOptions options,
        string executablePath,
        DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullExecutablePath = Path.GetFullPath(executablePath);
        var firstRun = options.Frequency == CleanupScheduleFrequency.Weekly
            ? NextWeeklyRun(options.WeeklyDay, options.DailyTime, nowLocal)
            : nowLocal.Date.Add(options.DailyTime.ToTimeSpan());
        if (options.Frequency == CleanupScheduleFrequency.Daily && firstRun <= nowLocal) firstRun = firstRun.AddDays(1);
        firstRun = DateTime.SpecifyKind(firstRun, DateTimeKind.Local);
        return new CleanupScheduledTaskDefinition(
            fullExecutablePath,
            "--cleanup-scheduled",
            Path.GetDirectoryName(fullExecutablePath)
                ?? throw new InvalidOperationException("无法确定 ClipRay 计划任务工作目录。"),
            firstRun,
            RunOnlyWhenUserIsLoggedOn: true,
            StartWhenAvailable: true,
            IgnoreNewInstances: true,
            RequiresElevation: false,
            LaunchesExecutableDirectlyWithoutShell: true);
    }

    private static DateTime NextWeeklyRun(DayOfWeek day, TimeOnly time, DateTime nowLocal)
    {
        var offset = ((int)day - (int)nowLocal.DayOfWeek + 7) % 7;
        var candidate = nowLocal.Date.AddDays(offset).Add(time.ToTimeSpan());
        return candidate <= nowLocal ? candidate.AddDays(7) : candidate;
    }
}

public sealed record CleanupScheduledTaskSnapshot(
    bool Enabled,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    CleanupScheduleFrequency Frequency,
    DayOfWeek? WeeklyDay,
    TimeOnly DailyTime)
{
    public bool Matches(CleanupScheduleOptions options, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var expectedExecutable = Path.GetFullPath(executablePath);
        var expectedWorkingDirectory = Path.GetDirectoryName(expectedExecutable)
            ?? throw new InvalidOperationException("无法确定 ClipRay 计划任务工作目录。");
        return Enabled
            && string.Equals(Path.GetFullPath(ExecutablePath), expectedExecutable, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Arguments.Trim(), "--cleanup-scheduled", StringComparison.Ordinal)
            && string.Equals(Path.GetFullPath(WorkingDirectory), expectedWorkingDirectory, StringComparison.OrdinalIgnoreCase)
            && Frequency == options.Frequency
            && DailyTime == options.DailyTime
            && (Frequency != CleanupScheduleFrequency.Weekly || WeeklyDay == options.WeeklyDay);
    }
}

public interface ICleanupScheduleRegistration
{
    void Set(CleanupScheduleOptions options, string executablePath);
}

public enum CleanupScheduleRegistrationState { Registered, Missing, Mismatched, Unavailable }

public sealed record CleanupScheduleRegistrationStatus(
    CleanupScheduleRegistrationState State,
    int? ErrorCode = null);

public static class CleanupScheduleStatusFormatter
{
    public static string Format(bool expectedEnabled, CleanupScheduleRegistrationStatus status, string interfaceLanguage = "zh-CN")
    {
        ArgumentNullException.ThrowIfNull(status);
        if (interfaceLanguage == "en")
        {
            return status.State switch
            {
                CleanupScheduleRegistrationState.Missing => expectedEnabled ? "Not created; save settings to retry" : "Disabled (no task)",
                CleanupScheduleRegistrationState.Registered => expectedEnabled ? "Created" : "Unexpected cleanup task still exists",
                CleanupScheduleRegistrationState.Mismatched => expectedEnabled ? "Configuration mismatch; save settings to repair" : "Unexpected cleanup task still exists",
                CleanupScheduleRegistrationState.Unavailable when status.ErrorCode is int code => $"Cannot read (error 0x{unchecked((uint)code):X8})",
                _ => "Cannot read",
            };
        }
        if (!expectedEnabled)
        {
            return status.State switch
            {
                CleanupScheduleRegistrationState.Missing => "未启用（任务不存在）",
                CleanupScheduleRegistrationState.Registered or CleanupScheduleRegistrationState.Mismatched =>
                    "异常：自动清理任务仍然存在",
                CleanupScheduleRegistrationState.Unavailable when status.ErrorCode is int errorCode =>
                    $"无法读取（错误 0x{unchecked((uint)errorCode):X8}）",
                _ => "无法读取",
            };
        }
        return status.State switch
        {
            CleanupScheduleRegistrationState.Registered => "已创建",
            CleanupScheduleRegistrationState.Missing => "未创建；请保存设置重试",
            CleanupScheduleRegistrationState.Mismatched => "配置不一致；请保存设置修复",
            CleanupScheduleRegistrationState.Unavailable when status.ErrorCode is int errorCode =>
                $"无法读取（错误 0x{unchecked((uint)errorCode):X8}）",
            _ => "无法读取",
        };
    }
}

public sealed class NoOpCleanupScheduleRegistration : ICleanupScheduleRegistration
{
    public static NoOpCleanupScheduleRegistration Instance { get; } = new();

    private NoOpCleanupScheduleRegistration() { }

    public void Set(CleanupScheduleOptions options, string executablePath) { }
}

public sealed class WindowsCleanupScheduleRegistration : ICleanupScheduleRegistration
{
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskRunLevelLeastPrivilege = 0;
    private const int TaskTriggerDaily = 2;
    private const int TaskTriggerWeekly = 3;
    private const int TaskActionExec = 0;
    private const int TaskInstancesIgnoreNew = 2;

    public void Set(CleanupScheduleOptions options, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        object? serviceObject = null;
        object? rootObject = null;
        object? legacyFolderObject = null;
        object? definitionObject = null;
        object? triggerObject = null;
        object? actionObject = null;
        object? registeredTaskObject = null;
        object? verifiedTaskObject = null;
        try
        {
            var serviceType = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
                ?? throw new PlatformNotSupportedException("Windows Task Scheduler 2.0 不可用。");
            serviceObject = Activator.CreateInstance(serviceType)
                ?? throw new InvalidOperationException("无法创建 Windows Task Scheduler 服务对象。");
            dynamic service = serviceObject;
            service.Connect();
            rootObject = service.GetFolder(@"\");
            dynamic root = rootObject;

            if (!options.Enabled)
            {
                TryDeleteTask(root, CleanupScheduledTaskDefinition.TaskName);
                if (TryGetFolder(service, CleanupScheduledTaskDefinition.LegacyFolderPath, out legacyFolderObject))
                    TryDeleteTask((dynamic)legacyFolderObject!, CleanupScheduledTaskDefinition.LegacyTaskName);
                if (ReadStatus(options, executablePath).State != CleanupScheduleRegistrationState.Missing)
                    throw new InvalidOperationException("Windows 自动清理任务删除后仍然存在。");
                return;
            }

            var task = CleanupScheduledTaskDefinition.Build(options, executablePath, DateTime.Now);
            definitionObject = service.NewTask(0);
            dynamic definition = definitionObject;
            definition.RegistrationInfo.Description = "按 ClipRay 设置清理超过保留天数的已完成截图、长截图和录屏。";
            definition.Principal.LogonType = TaskLogonInteractiveToken;
            definition.Principal.RunLevel = TaskRunLevelLeastPrivilege;
            definition.Settings.Enabled = true;
            definition.Settings.StartWhenAvailable = task.StartWhenAvailable;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = "PT10M";
            definition.Settings.MultipleInstances = TaskInstancesIgnoreNew;

            triggerObject = definition.Triggers.Create(options.Frequency == CleanupScheduleFrequency.Weekly
                ? TaskTriggerWeekly
                : TaskTriggerDaily);
            dynamic trigger = triggerObject;
            trigger.StartBoundary = task.FirstRunLocal.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            if (options.Frequency == CleanupScheduleFrequency.Weekly)
            {
                trigger.DaysOfWeek = 1 << (int)options.WeeklyDay;
                trigger.WeeksInterval = 1;
            }
            else
            {
                trigger.DaysInterval = 1;
            }
            trigger.Enabled = true;

            actionObject = definition.Actions.Create(TaskActionExec);
            dynamic action = actionObject;
            action.Path = task.ExecutablePath;
            action.Arguments = task.Arguments;
            action.WorkingDirectory = task.WorkingDirectory;

            registeredTaskObject = root.RegisterTaskDefinition(
                CleanupScheduledTaskDefinition.TaskName,
                definition,
                TaskCreateOrUpdate,
                null,
                null,
                TaskLogonInteractiveToken,
                null);
            var status = ReadStatus(options, executablePath);
            if (status.State != CleanupScheduleRegistrationState.Registered)
                throw new InvalidOperationException("Windows 自动清理任务写入后与设置不一致。");
        }
        finally
        {
            ReleaseComObject(verifiedTaskObject);
            ReleaseComObject(registeredTaskObject);
            ReleaseComObject(actionObject);
            ReleaseComObject(triggerObject);
            ReleaseComObject(definitionObject);
            ReleaseComObject(legacyFolderObject);
            ReleaseComObject(rootObject);
            ReleaseComObject(serviceObject);
        }
    }

    public CleanupScheduleRegistrationStatus ReadStatus()
        => ReadStatusCore(expectedOptions: null, executablePath: null);

    public CleanupScheduleRegistrationStatus ReadStatus(
        CleanupScheduleOptions options,
        string executablePath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return ReadStatusCore(options, executablePath);
    }

    private static CleanupScheduleRegistrationStatus ReadStatusCore(
        CleanupScheduleOptions? expectedOptions,
        string? executablePath)
    {
        object? serviceObject = null;
        object? rootObject = null;
        object? taskObject = null;
        object? definitionObject = null;
        object? actionsObject = null;
        object? actionObject = null;
        object? triggersObject = null;
        object? triggerObject = null;
        try
        {
            var serviceType = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
                ?? throw new PlatformNotSupportedException("Windows Task Scheduler 2.0 不可用。");
            serviceObject = Activator.CreateInstance(serviceType)
                ?? throw new InvalidOperationException("无法创建 Windows Task Scheduler 服务对象。");
            dynamic service = serviceObject;
            service.Connect();
            rootObject = service.GetFolder(@"\");
            dynamic root = rootObject;
            taskObject = root.GetTask(CleanupScheduledTaskDefinition.TaskName);
            if (expectedOptions is null || executablePath is null)
                return new CleanupScheduleRegistrationStatus(CleanupScheduleRegistrationState.Registered);

            dynamic task = taskObject;
            definitionObject = task.Definition;
            dynamic definition = definitionObject;
            actionsObject = definition.Actions;
            triggersObject = definition.Triggers;
            dynamic actions = actionsObject;
            dynamic triggers = triggersObject;
            if ((int)actions.Count != 1 || (int)triggers.Count != 1)
                return new CleanupScheduleRegistrationStatus(CleanupScheduleRegistrationState.Mismatched);
            actionObject = actions.Item(1);
            triggerObject = triggers.Item(1);
            dynamic action = actionObject;
            dynamic trigger = triggerObject;
            if ((int)action.Type != TaskActionExec)
                return new CleanupScheduleRegistrationStatus(CleanupScheduleRegistrationState.Mismatched);
            var triggerType = (int)trigger.Type;
            if (triggerType is not (TaskTriggerDaily or TaskTriggerWeekly))
                return new CleanupScheduleRegistrationStatus(CleanupScheduleRegistrationState.Mismatched);
            var frequency = triggerType == TaskTriggerWeekly
                ? CleanupScheduleFrequency.Weekly
                : CleanupScheduleFrequency.Daily;
            var start = DateTime.Parse((string)trigger.StartBoundary, CultureInfo.InvariantCulture);
            var weeklyDay = frequency == CleanupScheduleFrequency.Weekly
                ? ResolveWeeklyDay((int)trigger.DaysOfWeek)
                : null;
            var snapshot = new CleanupScheduledTaskSnapshot(
                (bool)task.Enabled,
                (string)action.Path,
                (string)action.Arguments,
                (string)action.WorkingDirectory,
                frequency,
                weeklyDay,
                TimeOnly.FromDateTime(start));
            return new CleanupScheduleRegistrationStatus(snapshot.Matches(expectedOptions, executablePath)
                ? CleanupScheduleRegistrationState.Registered
                : CleanupScheduleRegistrationState.Mismatched);
        }
        catch (Exception exception) when (IsTaskNotFound(exception))
        {
            return new CleanupScheduleRegistrationStatus(CleanupScheduleRegistrationState.Missing);
        }
        catch (Exception exception)
        {
            return new CleanupScheduleRegistrationStatus(
                CleanupScheduleRegistrationState.Unavailable,
                exception.HResult);
        }
        finally
        {
            ReleaseComObject(triggerObject);
            ReleaseComObject(triggersObject);
            ReleaseComObject(actionObject);
            ReleaseComObject(actionsObject);
            ReleaseComObject(definitionObject);
            ReleaseComObject(taskObject);
            ReleaseComObject(rootObject);
            ReleaseComObject(serviceObject);
        }
    }

    private static DayOfWeek? ResolveWeeklyDay(int daysOfWeek)
    {
        for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
        {
            if (daysOfWeek == 1 << (int)day) return day;
        }
        return null;
    }

    private static bool TryGetFolder(dynamic service, string path, out object? folder)
    {
        try
        {
            folder = service.GetFolder(path);
            return true;
        }
        catch (Exception exception) when (IsTaskNotFound(exception))
        {
            folder = null;
            return false;
        }
    }

    private static void TryDeleteTask(dynamic folder, string taskName)
    {
        try
        {
            folder.DeleteTask(taskName, 0);
        }
        catch (Exception exception) when (IsTaskNotFound(exception))
        {
            // 已经不存在等价于禁用成功。
        }
    }

    public static bool IsTaskNotFound(Exception exception) =>
        unchecked((uint)exception.HResult) == 0x80070002u;

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
