using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
using ClipRay.App.Infrastructure;
using ClipRay.App.Recording;
using ClipRay.App.Settings;

namespace ClipRay.App;

public partial class App
{
    private AppCliServer? _cliServer;

    private async void RunCliAsync(string[] args)
    {
        AppCliResponse response;
        try
        {
            var executable = Path.GetFullPath(Environment.ProcessPath ?? throw new IOException("Executable identity unavailable."));
            if (!AppCliProtocol.TryParse(args,executable,out var request,out var error) || request is null)
                response = new(1,Guid.Empty,false,error ?? "invalid-arguments",2);
            else if (request.Command == "help")
                response = new(1,request.RequestId,true,"help",0,new { Usage = AppCliProtocol.HelpText });
            else if (InstallerUpgradeGuard.IsActive())
                response = new(1,request.RequestId,false,"update-in-progress",3);
            else
            {
                var paths = ApplicationPaths.Create(AppContext.BaseDirectory,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                response = await AppCliProtocol.SendAsync(AppActivationPolicy.CurrentUserScope(paths.ProfileId),request,CancellationToken.None);
            }
        }
        catch (Exception error)
        {
            response = new(1,Guid.Empty,false,"cli-failed",1,new { ErrorType = error.GetType().Name, HResult = $"0x{unchecked((uint)error.HResult):X8}" });
        }
        // Standard handles are supplied by a shell pipeline or redirected process. No console window is allocated.
        try { Console.Out.WriteLine(JsonSerializer.Serialize(response)); }
        finally { Shutdown(response.ExitCode); }
    }

    private void StartCliServer()
    {
        if (_paths is null) return;
        var executable = Path.GetFullPath(Environment.ProcessPath ?? throw new IOException("Executable identity unavailable."));
        _cliServer = new(AppActivationPolicy.CurrentUserScope(_paths.ProfileId),executable,
            request => Dispatcher.InvokeAsync(() => HandleCliAsync(request)).Task.Unwrap());
        _cliServer.Start();
    }

    internal async Task<AppCliResponse> HandleCliAsync(AppCliRequest request)
    {
        AppCliResponse Reply(bool success,string status,int exitCode=0,object? data=null) => new(1,request.RequestId,success,status,exitCode,data);
        if (_translationClosing || _recordingAdmissionClosed) return Reply(false,"application-retiring",3);
        try
        {
            switch (request.Command)
            {
                case "help": return Reply(true,"help",data:new { Usage = AppCliProtocol.HelpText });
                case "settings-open":
                    OpenSettings();
                    return _settingsWindow is null ? Reply(false,"settings-unavailable",1)
                        : Reply(true,"settings-open",data:new {
                            ProcessId = Environment.ProcessId,
                            WindowHandle = new System.Windows.Interop.WindowInteropHelper(_settingsWindow).Handle.ToInt64()
                        });
                case "translation-open":
                    var translationWindow = OpenTranslationWindow();
                    return Reply(true,"translation-open",data:new {
                        ProcessId = Environment.ProcessId,
                        WindowHandle = new System.Windows.Interop.WindowInteropHelper(translationWindow).Handle.ToInt64()
                    });
                case "settings-close":
                    if (_settingsWindow?.HasPendingSettingsDraft == true) return Reply(false,"settings-draft-open",4);
                    _settingsWindow?.Close();
                    return Reply(true,"settings-closed");
                case "status": return Reply(true,"ready",data:new
                {
                    Version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    ProcessId = Environment.ProcessId,
                    Recording = CliRecordingState(),
                });
                case "recording-settings":
                {
                    var current = _settings ?? throw new InvalidOperationException("Settings are not ready.");
                    var changed = request.FramesPerSecond is not null || request.Quality is not null
                        || request.IncludeCursor is not null || request.IncludeSystemAudio is not null;
                    if (changed)
                    {
                        if (_settingsWindow?.HasPendingSettingsDraft == true) return Reply(false,"settings-draft-open",4);
                        var store = _settingsStore ?? throw new InvalidOperationException("Settings store is not ready.");
                        if (store.Load() != current) return Reply(false,"settings-changed-on-disk",4);
                        var updated = current with
                        {
                            RecordingFramesPerSecond = request.FramesPerSecond ?? current.RecordingFramesPerSecond,
                            RecordingQuality = request.Quality is null ? current.RecordingQuality : Enum.Parse<RecordingQuality>(request.Quality,true),
                            RecordingIncludeCursor = request.IncludeCursor ?? current.RecordingIncludeCursor,
                            RecordingIncludeSystemAudio = request.IncludeSystemAudio ?? current.RecordingIncludeSystemAudio,
                        };
                        updated.Validate();
                        store.Save(updated);
                        ApplySettings(updated,registerHotkeys:false);
                        _settingsWindow?.ApplyRecordingPreferences(updated);
                        current = updated;
                    }
                    return Reply(true,changed ? "settings-saved" : "settings",data:CliRecordingOptions(current.CreateRecordingVideoOptions()));
                }
                case "recording-start":
                {
                    if (_overlay is not null || _longCaptureCancellation is not null) return Reply(false,"capture-ui-busy",4);
                    var region = request.Region;
                    if (region is not { Length:4 } || region[2] is <2 or >16384 || region[3] is <2 or >16384)
                        return Reply(false,"invalid-region",2);
                    var settings = _settings ?? throw new InvalidOperationException("Settings are not ready.");
                    var output = request.OutputDirectory ?? settings.RecordingDirectory;
                    if (!Path.IsPathFullyQualified(output)) return Reply(false,"output-must-be-absolute",2);
                    var started = await RecordingController.StartAsync(new(new Rectangle(region[0],region[1],region[2],region[3])),
                        new(output,!request.NoCopy && settings.CopyToClipboard,settings.InterfaceLanguage,
                            settings.CreateRecordingVideoOptions(),Interactive:request.ShowUi),_=>{},()=>{},
                        () => !_translationClosing && !_recordingAdmissionClosed);
                    return Reply(started,started ? RecordingController.State : "recording-start-failed",started?0:1,CliRecordingState());
                }
                case "recording-stop":
                {
                    var controller = RecordingController;
                    if (request.RunId is null || (controller.ActiveRunId != request.RunId && controller.LastRunId != request.RunId))
                        return Reply(false,"recording-run-mismatch",4);
                    // A repeated stop of the previous run must never stop a newer recording.
                    if (controller.ActiveRunId is { } active && active != request.RunId)
                        return Reply(false,"recording-run-mismatch",4);
                    var report = await controller.StopAsync();
                    if (report is { PhysicalCompleted:false }) return Reply(true,"saving",4,CliRecordingState());
                    return Reply(report?.Saved == true,report?.Saved == true ? "saved" : "recording-save-failed",report?.Saved==true?0:1,CliRecordingState());
                }
                default: return Reply(false,"unknown-command",2);
            }
        }
        catch (Exception error)
        {
            return Reply(false,"command-failed",1,new { ErrorType = error.GetType().Name,HResult = $"0x{unchecked((uint)error.HResult):X8}" });
        }
    }

    private static object CliRecordingOptions(RecordingVideoOptions options) => new
    {
        options.FramesPerSecond, Quality = options.Quality.ToString(), options.IncludeCursor,options.IncludeSystemAudio,
        TargetBitrate1080p = RecordingBitratePolicy.ForFrameSize(1920,1080,options.FramesPerSecond,options.Quality),
    };

    private object CliRecordingState()
    {
        var controller = RecordingController;
        var report = controller.LastReport;
        return new
        {
            controller.State,controller.ActiveRunId,controller.LastRunId,
            Options = controller.ActiveOptions is { } active ? CliRecordingOptions(active.VideoOptions) : null,
            ActivePath = controller.ActivePaths?.FinalPath,
            ActivePartialPath = controller.ActivePaths?.PartialPath,
            LastResult = report is null ? null : new
            {
                RunId = controller.LastRunId, Path = report.Paths?.FinalPath,PartialPath = report.Paths?.PartialPath,
                report.Saved,report.PhysicalCompleted,report.ResourcesReleased,
                FaultStage = report.FirstFault?.Stage,
                FaultHResult = report.FirstFault is { } fault ? $"0x{unchecked((uint)fault.Error.HResult):X8}" : null,
                controller.DeliveryErrors,
            },
        };
    }
}
