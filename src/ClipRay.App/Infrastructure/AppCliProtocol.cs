using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipRay.App.Infrastructure;

public sealed record AppCliRequest(int Protocol, Guid RequestId, string ExecutablePath, string Command,
    int? FramesPerSecond = null, string? Quality = null, bool? IncludeCursor = null, bool? IncludeSystemAudio = null,
    int[]? Region = null, string? OutputDirectory = null, bool NoCopy = false, bool ShowUi = false, Guid? RunId = null);

public sealed record AppCliResponse(int Protocol, Guid RequestId, bool Success, string Status, int ExitCode, object? Data = null);

public static class AppCliProtocol
{
    public const int Version = 1;
    public const int MaximumRequestCharacters = 16 * 1024;
    internal const int MaximumResponseCharacters = 64 * 1024;
    private static readonly UTF8Encoding WireEncoding = new(false, true);
    public const string HelpText = """
        ClipRay 本地 CLI（仅控制同用户、同一安装路径的运行实例）
          --cli help
          --cli status
          --cli settings-close
          --cli settings-open
          --cli translation-open
          --cli recording-settings [--fps 15|24|30|60] [--quality Compact|Balanced|High]
                                   [--cursor true|false] [--audio true|false]
          --cli recording-start --region X Y W H [--output ABS] [--no-copy] [--show-ui]
          --cli recording-stop --run-id GUID
        所有命令均可附加 --request-id GUID。recording-settings 不带设置项时只读。
        设置/开始请求使用固定 RequestId 可查询同次请求结果；不得换 ID 自动重试结果未知的写请求。
        停止请求按 RunId 幂等处理，不占写结果缓存；保存中应查询 status。
        退出码：0 成功；1 操作失败；2 参数或协议无效；3 实例未运行/无法连接；4 请求未发送或暂不可接受；5 请求结果未知。
        """;

    public static bool IsRequested(string[] args) => args.Any(value => string.Equals(value, "--cli", StringComparison.OrdinalIgnoreCase));

    public static bool TryParse(string[] args, string exactExecutablePath, out AppCliRequest? request, out string? error)
    {
        request = null;
        error = null;
        if (args.Length < 2 || !string.Equals(args[0], "--cli", StringComparison.OrdinalIgnoreCase))
            return Fail("必须使用 --cli 后接一个命令；不能混用其它启动参数。", out error);
        var command = args[1].ToLowerInvariant();
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var id = Guid.NewGuid();
        int? fps = null;
        string? quality = null, output = null;
        bool? cursor = null, audio = null;
        int[]? region = null;
        Guid? runId = null;
        var noCopy = false;
        var showUi = false;
        for (var index = 2; index < args.Length; index++)
        {
            var flag = args[index].ToLowerInvariant();
            if (!flags.Add(flag)) return Fail("同一个选项不能重复指定。", out error);
            if (!AllowedFlag(command, flag)) return Fail("选项未知，或不适用于当前命令。", out error);
            if (flag == "--no-copy") { noCopy = true; continue; }
            if (flag == "--show-ui") { showUi = true; continue; }
            if (flag == "--region")
            {
                if (index + 4 >= args.Length) return Fail("--region 需要四个整数 X Y W H。", out error);
                region = new int[4];
                for (var part = 0; part < 4; part++)
                    if (!int.TryParse(args[++index], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out region[part]))
                        return Fail("--region 需要四个整数 X Y W H。", out error);
                continue;
            }
            if (++index >= args.Length) return Fail("选项缺少值。", out error);
            var value = args[index];
            switch (flag)
            {
                case "--request-id":
                    if (!Guid.TryParse(value, out id) || id == Guid.Empty) return Fail("--request-id 需要非空 GUID。", out error);
                    break;
                case "--run-id":
                    if (!Guid.TryParse(value, out var run) || run == Guid.Empty) return Fail("--run-id 需要非空 GUID。", out error);
                    runId = run;
                    break;
                case "--fps":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedFps))
                        return Fail("--fps 必须为 15、24、30 或 60。", out error);
                    fps = parsedFps;
                    break;
                case "--quality": quality = value; break;
                case "--cursor":
                    if (!bool.TryParse(value, out var parsedCursor)) return Fail("--cursor 只接受 true 或 false。", out error);
                    cursor = parsedCursor;
                    break;
                case "--audio":
                    if (!bool.TryParse(value, out var parsedAudio)) return Fail("--audio 只接受 true 或 false。", out error);
                    audio = parsedAudio;
                    break;
                case "--output": output = value; break;
            }
        }
        var parsed = new AppCliRequest(Version, id, exactExecutablePath, command, fps, quality, cursor, audio,
            region, output, noCopy, showUi, runId);
        return TryNormalize(parsed, exactExecutablePath, out request, out error);
    }

    public static bool Validate(AppCliRequest request, string exactExecutablePath, out string? error) =>
        TryNormalize(request, exactExecutablePath, out _, out error);

    public static string PipeName(string scope, string exe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (!TryExactPath(exe, out var path)) throw new ArgumentException("Executable path must be absolute.", nameof(exe));
        var identity = Encoding.UTF8.GetBytes(scope + "\0" + path!.ToUpperInvariant());
        return "ClipRay.Cli.v1." + Convert.ToHexString(SHA256.HashData(identity));
    }

    public static async Task<AppCliResponse> SendAsync(string scope, AppCliRequest request, CancellationToken token = default)
    {
        if (!TryNormalize(request, request.ExecutablePath, out var normalized, out var error))
            return Failure(request.RequestId, "invalid-request", 2, new { error });
        request = normalized!;
        if (request.Command == "help") return new(Version, request.RequestId, true, "help", 0, new { help = HelpText });
        var line = JsonSerializer.Serialize(request);
        if (line.Length > MaximumRequestCharacters) return Failure(request.RequestId, "request-too-large", 2);
        using var client = new NamedPipeClientStream(".", PipeName(scope, request.ExecutablePath), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(2_000, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransportError(exception))
        {
            return Failure(request.RequestId, token.IsCancellationRequested ? "cancelled-before-send" : "not-running",
                token.IsCancellationRequested ? 4 : 3, Diagnostic(exception));
        }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        operation.CancelAfter(TimeSpan.FromSeconds(20));
        var transmissionStarted = false;
        try
        {
            operation.Token.ThrowIfCancellationRequested();
            using var writer = CreateWriter(client);
            using var reader = CreateReader(client);
            // A failed/partially completed write can already have reached the server. From
            // this point every transport failure is conservatively an unknown outcome.
            transmissionStarted = true;
            await writer.WriteLineAsync(line.AsMemory(), operation.Token).ConfigureAwait(false);
            var responseLine = await ReadLineLimitedAsync(reader, MaximumResponseCharacters, operation.Token).ConfigureAwait(false);
            var response = responseLine is null ? null : JsonSerializer.Deserialize<AppCliResponse>(responseLine);
            if (response is null || response.Protocol != Version || response.RequestId != request.RequestId
                || string.IsNullOrWhiteSpace(response.Status))
                return Failure(request.RequestId, "request-outcome-unknown", 5, new { errorType = "InvalidResponse" });
            return response;
        }
        catch (Exception exception) when (IsTransportError(exception) || exception is JsonException or InvalidDataException or DecoderFallbackException)
        {
            return Failure(request.RequestId, transmissionStarted ? "request-outcome-unknown" : "cancelled-before-send",
                transmissionStarted ? 5 : 4, Diagnostic(exception));
        }
    }

    internal static bool IsMutation(AppCliRequest request) => request.Command is "recording-start" or "recording-stop" or "settings-close" or "settings-open" or "translation-open"
        || request.Command == "recording-settings" && HasSettings(request);

    internal static bool TryNormalize(AppCliRequest request, string exactExecutablePath,
        out AppCliRequest? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (request.Protocol != Version || request.RequestId == Guid.Empty)
            return Fail("协议版本或 RequestId 无效。", out error);
        if (!TryExactPath(exactExecutablePath, out var expected) || !TryExactPath(request.ExecutablePath, out var actual)
            || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            return Fail("请求不属于此安装路径。", out error);
        var command = request.Command?.ToLowerInvariant();
        if (command is not ("help" or "status" or "settings-close" or "settings-open" or "translation-open" or "recording-settings" or "recording-start" or "recording-stop"))
            return Fail("命令未知。", out error);
        var settings = HasSettings(request);
        var start = request.Region is not null || request.OutputDirectory is not null || request.NoCopy || request.ShowUi;
        if (command != "recording-settings" && settings || command != "recording-start" && start
            || command != "recording-stop" && request.RunId is not null)
            return Fail("请求包含不适用于当前命令的选项。", out error);
        if (request.FramesPerSecond is not null and not (15 or 24 or 30 or 60))
            return Fail("录屏帧率必须为 15、24、30 或 60。", out error);
        string? quality = request.Quality;
        if (quality is not null)
        {
            quality = new[] { "Compact", "Balanced", "High" }.SingleOrDefault(value => string.Equals(value, quality, StringComparison.OrdinalIgnoreCase));
            if (quality is null) return Fail("录屏画质必须为 Compact、Balanced 或 High。", out error);
        }
        if (command == "recording-start")
        {
            if (request.Region is not { Length: 4 } region || region[2] <= 0 || region[3] <= 0
                || (long)region[0] + region[2] > int.MaxValue || (long)region[1] + region[3] > int.MaxValue)
                return Fail("recording-start 需要有效的 --region X Y W H；宽高必须大于零。", out error);
        }
        string? output = request.OutputDirectory;
        if (output is not null && !TryExactPath(output, out output)) return Fail("--output 需要绝对目录路径。", out error);
        if (command == "recording-stop" && (request.RunId is null || request.RunId == Guid.Empty))
            return Fail("recording-stop 需要非空 --run-id。", out error);
        // Freeze the only mutable field before it participates in the deduplication key or handler.
        normalized = request with { ExecutablePath = expected!, Command = command, Quality = quality,
            Region = request.Region?.ToArray(), OutputDirectory = output };
        return true;
    }

    private static bool HasSettings(AppCliRequest request) => request.FramesPerSecond is not null || request.Quality is not null
        || request.IncludeCursor is not null || request.IncludeSystemAudio is not null;

    private static bool AllowedFlag(string command, string flag) => flag == "--request-id" || (command switch
    {
        "recording-settings" => flag is "--fps" or "--quality" or "--cursor" or "--audio",
        "recording-start" => flag is "--region" or "--output" or "--no-copy" or "--show-ui",
        "recording-stop" => flag == "--run-id",
        _ => false
    });

    private static bool TryExactPath(string? path, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try { normalized = Path.GetFullPath(path); return true; }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static bool Fail(string message, out string? error) { error = message; return false; }
    internal static AppCliResponse Failure(Guid id, string status, int exitCode, object? data = null) => new(Version, id, false, status, exitCode, data);
    internal static object Diagnostic(Exception error) => new { errorType = error.GetType().Name, hresult = $"0x{unchecked((uint)error.HResult):X8}" };
    private static bool IsTransportError(Exception error) => error is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException;
    internal static StreamReader CreateReader(Stream stream) => new(stream, WireEncoding, false, 1024, leaveOpen: true);
    internal static StreamWriter CreateWriter(Stream stream) => new(stream, WireEncoding, 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

    internal static async Task<string?> ReadLineLimitedAsync(StreamReader reader, int maximumCharacters, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[1024];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) return text.Length == 0 ? null : text.ToString();
            var newline = Array.IndexOf(buffer, '\n', 0, count);
            var take = newline >= 0 ? newline : count;
            if (text.Length + take > maximumCharacters) throw new InvalidDataException("CLI line exceeds the protocol limit.");
            text.Append(buffer, 0, take);
            if (newline >= 0)
            {
                if (text.Length > 0 && text[^1] == '\r') text.Length--;
                return text.ToString();
            }
        }
    }
}

/// <summary>A separate same-user endpoint; it never forwards commands to the activation/installer pipe.</summary>
public sealed class AppCliServer : IDisposable
{
    private const int MaximumMutations = 128;
    private const int MaximumConnections = 16;
    private readonly string _pipeName, _executable;
    private readonly Func<AppCliRequest, Task<AppCliResponse>> _handler;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(MaximumConnections, MaximumConnections);
    private readonly HashSet<NamedPipeServerStream> _pipes = [];
    private readonly HashSet<Task> _connections = [];
    private readonly Dictionary<Guid, Mutation> _mutations = [];
    private Task? _listener;
    private bool _disposed;
    private sealed record Mutation(string Payload, Task<AppCliResponse> Result);
    private static readonly HashSet<string> RequestProperties = typeof(AppCliRequest).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

    public AppCliServer(string scope, string exe, Func<AppCliRequest, Task<AppCliResponse>> handler)
    {
        _pipeName = AppCliProtocol.PipeName(scope, exe);
        _executable = Path.GetFullPath(exe);
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_listener is not null) throw new InvalidOperationException("CLI server already started.");
            var first = CreatePipe();
            _pipes.Add(first);
            _listener = Task.Run(() => ListenAsync(first));
        }
    }

    private NamedPipeServerStream CreatePipe() => new(_pipeName, PipeDirection.InOut, MaximumConnections, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);

    private async Task ListenAsync(NamedPipeServerStream first)
    {
        NamedPipeServerStream? pending = first;
        while (!_stop.IsCancellationRequested)
        {
            var reserved = false;
            try
            {
                await _slots.WaitAsync(_stop.Token).ConfigureAwait(false);
                reserved = true;
                if (pending is null)
                {
                    pending = CreatePipe();
                    lock (_sync) _pipes.Add(pending);
                }
                await pending.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                var connection = ServeAsync(pending);
                lock (_sync) _connections.Add(connection);
                _ = connection.ContinueWith(completed => { lock (_sync) _connections.Remove(completed); },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                pending = null;
                reserved = false; // ServeAsync owns the connection slot and pipe now.
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or UnauthorizedAccessException)
            {
                if (pending is not null) { lock (_sync) _pipes.Remove(pending); pending.Dispose(); pending = null; }
                if (reserved) _slots.Release();
                // Do not spin or silently recreate an endpoint after an unexpected listener failure.
                break;
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        operation.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var reader = AppCliProtocol.CreateReader(pipe);
            using var writer = AppCliProtocol.CreateWriter(pipe);
            AppCliRequest? request = null;
            AppCliResponse response;
            try
            {
                var line = await AppCliProtocol.ReadLineLimitedAsync(reader, AppCliProtocol.MaximumRequestCharacters, operation.Token).ConfigureAwait(false);
                if (line is null) return;
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in document.RootElement.EnumerateObject())
                    if (!RequestProperties.Contains(property.Name) || !seen.Add(property.Name)) throw new JsonException();
                request = JsonSerializer.Deserialize<AppCliRequest>(line);
                if (request is null || !AppCliProtocol.TryNormalize(request, _executable, out var normalized, out var error))
                    response = AppCliProtocol.Failure(request?.RequestId ?? Guid.Empty, "invalid-request", 2);
                else response = await DispatchAsync(normalized!).WaitAsync(operation.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or DecoderFallbackException)
            {
                response = AppCliProtocol.Failure(request?.RequestId ?? Guid.Empty, "invalid-request", 2, AppCliProtocol.Diagnostic(error));
            }
            var serialized = JsonSerializer.Serialize(response);
            if (serialized.Length > AppCliProtocol.MaximumResponseCharacters)
                serialized = JsonSerializer.Serialize(AppCliProtocol.Failure(response.RequestId, "response-too-large", 1));
            await writer.WriteLineAsync(serialized.AsMemory(), operation.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or UnauthorizedAccessException)
        {
            // A disconnected/timed-out caller cannot receive a reply. Accepted mutations
            // keep running and remain in the cache; the client reports unknown outcome.
        }
        finally
        {
            lock (_sync) _pipes.Remove(pipe);
            pipe.Dispose();
            _slots.Release();
        }
    }

    private Task<AppCliResponse> DispatchAsync(AppCliRequest request)
    {
        var payload = JsonSerializer.Serialize(request);
        TaskCompletionSource<AppCliResponse>? completion = null;
        lock (_sync)
        {
            if (_mutations.TryGetValue(request.RequestId, out var existing))
                return existing.Payload == payload ? existing.Result
                    : Task.FromResult(AppCliProtocol.Failure(request.RequestId, "request-id-conflict", 2));
            // Stop is idempotent through the controller's RunId and shared completion task.
            // It must remain reachable even when replay protection has filled the write cache.
            if (AppCliProtocol.IsMutation(request) && request.Command != "recording-stop")
            {
                if (_mutations.Count >= MaximumMutations)
                    return Task.FromResult(AppCliProtocol.Failure(request.RequestId, "mutation-cache-full", 4));
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _mutations.Add(request.RequestId, new(payload, completion.Task));
            }
        }
        if (completion is null) return HandleSafelyAsync(request);
        _ = CompleteMutationAsync(request, completion);
        return completion.Task;
    }

    private async Task CompleteMutationAsync(AppCliRequest request, TaskCompletionSource<AppCliResponse> completion) =>
        completion.TrySetResult(await HandleSafelyAsync(request).ConfigureAwait(false));

    private async Task<AppCliResponse> HandleSafelyAsync(AppCliRequest request)
    {
        try
        {
            var response = await _handler(request).ConfigureAwait(false);
            if (response is null || response.Protocol != AppCliProtocol.Version || response.RequestId != request.RequestId)
                return AppCliProtocol.Failure(request.RequestId, "handler-invalid-response", 1);
            // Cache a value snapshot, not a mutable application/settings object whose fields
            // could change between the first reply and a later same-ID result lookup.
            response = response with { Data = response.Data is null ? null : JsonSerializer.SerializeToElement(response.Data) };
            if (JsonSerializer.Serialize(response).Length > AppCliProtocol.MaximumResponseCharacters)
                return AppCliProtocol.Failure(request.RequestId, "response-too-large", 1);
            return response;
        }
        catch (Exception error) { return AppCliProtocol.Failure(request.RequestId, "handler-error", 1, AppCliProtocol.Diagnostic(error)); }
    }

    public void Dispose()
    {
        NamedPipeServerStream[] pipes;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            pipes = _pipes.ToArray();
        }
        foreach (var pipe in pipes) pipe.Dispose();
        _ = FinishDisposeAsync();
    }

    private async Task FinishDisposeAsync()
    {
        try
        {
            if (_listener is not null) await _listener.ConfigureAwait(false);
            Task[] connections;
            lock (_sync) connections = _connections.ToArray();
            await Task.WhenAll(connections).ConfigureAwait(false);
        }
        finally { _slots.Dispose(); _stop.Dispose(); }
    }
}
