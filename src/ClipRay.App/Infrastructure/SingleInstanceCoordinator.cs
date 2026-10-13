using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace ClipRay.App.Infrastructure;

public enum AppActivationCommand
{
    OpenSettings,
    StartInteractiveCapture,
    CaptureCurrentScreen,
}

public enum SingleInstanceStartResult
{
    Primary,
    Forwarded,
    SecondaryUnavailable,
}

public static class AppActivationPolicy
{
    private static readonly string[] MaintenanceArguments =
        ["--cleanup-scheduled", "--configure-install-cleanup", "--uninstall-cleanup", "--request-install-exit"];

    private static readonly string[] DebugPreviewArguments =
    [
        "--color-tools-preview",
        "--long-capture-live-preview",
        "--long-capture-preview",
        "--long-capture-runtime-probe",
        "--long-capture-runtime-ui-probe",
        "--live-logged-runtime-probe",
        "--modern-overlay-preview",
        "--overlay-preview",
        "--picker-preview",
        "--recording-overlay-preview",
        "--recording-preview-probe",
        "--settings-preview",
        "--settings-signature-preview",
        "--translation-focus-preview",
        "--translation-window-preview",
        "--tray-menu-preview",
    ];

    public static AppActivationCommand ForwardedCommand(IEnumerable<string> arguments)
    {
        var values = arguments.ToArray();
        if (Contains(values, "--interactive")) return AppActivationCommand.StartInteractiveCapture;
        if (Contains(values, "--quick-current")) return AppActivationCommand.CaptureCurrentScreen;
        return AppActivationCommand.OpenSettings;
    }

    public static bool ShouldBypassSingleInstance(IEnumerable<string> arguments, bool isDebugBuild)
    {
        var values = arguments.ToArray();
        if (MaintenanceArguments.Any(argument => Contains(values, argument))) return true;
        return isDebugBuild && DebugPreviewArguments.Any(argument => Contains(values, argument));
    }

    public static string CurrentUserScope(string profileId = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var userIdentity = WindowsIdentity.GetCurrent().User?.Value;
        return $"ClipRay.{(string.IsNullOrWhiteSpace(userIdentity) ? Environment.UserName : userIdentity)}.{profileId}";
    }

    private static bool Contains(IEnumerable<string> arguments, string expected) =>
        arguments.Contains(expected, StringComparer.OrdinalIgnoreCase);
}

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly object _serverGate = new();
    private Mutex? _mutex;
    private NamedPipeServerStream? _activeServer;
    private CancellationTokenSource? _stop;
    private Task? _listener;
    private bool _ownsMutex;
    private bool _disposed;

    public SingleInstanceCoordinator(string scope)
    {
        var safeScope = string.Concat(scope.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' ? character : '.'));
        _mutexName = $"Local\\{safeScope}.Mutex";
        _pipeName = InstallExitProtocol.PipeName(scope);
    }

    public event Action<AppActivationCommand>? ActivationRequested;
    public Func<InstallExitRequest, Task<InstallExitResponse>>? InstallExitRequested { get; set; }
    public event Action? InstallExitResponseCompleted;

    public SingleInstanceStartResult Start(
        AppActivationCommand command,
        TimeSpan? forwardTimeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mutex is not null) throw new InvalidOperationException("Single-instance coordination has already started.");

        _mutex = new Mutex(initiallyOwned: false, _mutexName);
        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        if (_ownsMutex)
        {
            _stop = new CancellationTokenSource();
            _listener = ListenAsync(_stop.Token);
            return SingleInstanceStartResult.Primary;
        }

        return TryForward(command, forwardTimeout ?? TimeSpan.FromSeconds(1))
            ? SingleInstanceStartResult.Forwarded
            : SingleInstanceStartResult.SecondaryUnavailable;
    }

    private bool TryForward(AppActivationCommand command, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                client.Connect(Math.Max(1, Math.Min(200, (int)(deadline - DateTime.UtcNow).TotalMilliseconds)));
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(command.ToString());
                return true;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }

            if (DateTime.UtcNow < deadline) Thread.Sleep(40);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                lock (_serverGate)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        server.Dispose();
                        break;
                    }
                    _activeServer = server;
                }
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server,leaveOpen:true);
                var value = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (value?.StartsWith('{') == true)
                {
                    InstallExitResponse? response = null;
                    try
                    {
                        var request = JsonSerializer.Deserialize<InstallExitRequest>(value);
                        if (request is not null && InstallExitRequested is not null)
                        {
                            response = await InstallExitRequested(request).ConfigureAwait(false);
                            using var writer = new StreamWriter(server,leaveOpen:true) { AutoFlush = true };
                            await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (JsonException) { }
                    catch (ArgumentException) { }
                    finally
                    {
                        // Retirement precedes the reply. Shutdown is scheduled only after the flush attempt.
                        if (response?.Status == "retired") InstallExitResponseCompleted?.Invoke();
                    }
                }
                else if (Enum.TryParse<AppActivationCommand>(value, ignoreCase: false, out var command) && Enum.IsDefined(command))
                {
                    ActivationRequested?.Invoke(command);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                if (cancellationToken.IsCancellationRequested) break;
                // 瞬时客户端断开不应终止主实例的激活监听。
            }
            finally
            {
                lock (_serverGate)
                {
                    if (ReferenceEquals(_activeServer, server)) _activeServer = null;
                }
                server?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop?.Cancel();
        lock (_serverGate)
        {
            _activeServer?.Dispose();
            _activeServer = null;
        }
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException error) when (error.InnerExceptions.All(static inner =>
                   inner is OperationCanceledException or ObjectDisposedException))
        {
        }
        _stop?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
