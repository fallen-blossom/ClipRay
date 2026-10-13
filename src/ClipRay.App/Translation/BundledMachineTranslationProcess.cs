using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using ClipRay.App.Infrastructure;

namespace ClipRay.App.Translation;

public sealed class BundledMachineTranslationProcess(
    string pythonPath,
    string workerPath,
    string modelPath,
    TimeSpan? workerTimeout = null,
    bool keepAlive = false,
    string? gpuLibraryDirectory = null) : IMachineTranslationProcess, IDisposable, IAsyncDisposable
{
    private readonly string? _gpuLibraryDirectory = gpuLibraryDirectory is null ? null : Path.GetFullPath(gpuLibraryDirectory);
    private readonly string _pythonPath = Path.GetFullPath(pythonPath);
    private readonly string _workerPath = Path.GetFullPath(workerPath);
    private readonly string _modelPath = Path.GetFullPath(modelPath);
    private readonly TimeSpan _workerTimeout = workerTimeout is null ? TimeSpan.FromMinutes(2)
        : workerTimeout.Value > TimeSpan.Zero ? workerTimeout.Value
        : throw new ArgumentOutOfRangeException(nameof(workerTimeout));

    private readonly SemaphoreSlim _workerGate = new(1, 1);
    private Process? _persistentProcess;
    private TranslationChildLifetime? _child;
    private bool _disposed;
    private readonly AsyncResourceLifetime _lifetime = new();
    public int ProcessStartCount { get; private set; }
    public MachineTranslationBackendStatus? LastBackendStatus { get; private set; }

    public async Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken cancellationToken) =>
        (await TranslateWithEvidenceAsync(request, cancellationToken).ConfigureAwait(false)).Texts;

    public async Task<MachineTranslationProcessResult> TranslateWithEvidenceAsync(
        MachineTranslationProcessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var admission = _lifetime.Enter();
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = stopped.Token;
        await _workerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await TranslateOwnedAsync(request, cancellationToken).ConfigureAwait(false); }
        finally { _workerGate.Release(); }
    }

    private async Task<MachineTranslationProcessResult> TranslateOwnedAsync(
        MachineTranslationProcessRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (keepAlive) return await TranslatePersistentAsync(request, cancellationToken).ConfigureAwait(false);
        await ResetWorkerAsync().ConfigureAwait(false);
        LastBackendStatus = null;
        EnsureRequiredPath(_pythonPath);
        EnsureRequiredPath(_workerPath);
        EnsureRequiredPath(_modelPath);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_workerTimeout);
        var workerToken = timeout.Token;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        var startInfo = new ProcessStartInfo
        {
            FileName = _pythonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        if (_gpuLibraryDirectory is not null) startInfo.Environment["CLIPRAY_CUDA_BIN"] = _gpuLibraryDirectory;
        startInfo.ArgumentList.Add(_workerPath);
        startInfo.ArgumentList.Add(_modelPath);

        var process = TranslationChildLifetime.Start(startInfo, () => new InvalidOperationException("machine-worker-start-failed"));
        ProcessStartCount++;
        // Drain concurrently without retaining worker stderr (which may contain user text).
        var stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        _child = TranslationChildLifetime.Own(process, stderrDrain);
        try
        {
            var payload = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            await WriteRequestAsync(process, payload, workerToken).ConfigureAwait(false);
            process.StandardInput.Close();

            var output = await process.StandardOutput.ReadLineAsync(workerToken).ConfigureAwait(false);
            await process.WaitForExitAsync(workerToken).ConfigureAwait(false);
            await stderrDrain.ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("machine-worker-failed");

            var response = JsonSerializer.Deserialize<WorkerResponse>(output, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            ThrowStartupFailure(response);
            if (process.ExitCode != 0) throw new InvalidOperationException("machine-worker-failed");
            if (response?.Texts is null || response.Texts.Any(static text => text is null))
                throw new InvalidOperationException("machine-worker-invalid-response");
            LastBackendStatus = response.Backend;
            return WithEvidence(response, process.StartInfo.Environment.TryGetValue("CLIPRAY_MT_DEVICE", out var requested) ? requested ?? "auto" : "auto");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("machine-worker-timeout");
        }
        finally
        {
            await ResetWorkerAsync().ConfigureAwait(false);
        }
    }

    private async Task<MachineTranslationProcessResult> TranslatePersistentAsync(
        MachineTranslationProcessRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_workerTimeout);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_persistentProcess is null || _persistentProcess.HasExited)
            {
                await ResetWorkerAsync().ConfigureAwait(false);
                EnsureRequiredPath(_pythonPath);
                EnsureRequiredPath(_workerPath);
                EnsureRequiredPath(_modelPath);
                var utf8 = new UTF8Encoding(false, true);
                var info = new ProcessStartInfo(_pythonPath)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardInputEncoding = utf8, StandardOutputEncoding = utf8, StandardErrorEncoding = utf8,
                };
                info.Environment["PYTHONIOENCODING"] = "utf-8";
                info.Environment["PYTHONUTF8"] = "1";
                if (_gpuLibraryDirectory is not null) info.Environment["CLIPRAY_CUDA_BIN"] = _gpuLibraryDirectory;
                info.ArgumentList.Add(_workerPath);
                info.ArgumentList.Add(_modelPath);
                info.ArgumentList.Add("--persistent");
                _persistentProcess = TranslationChildLifetime.Start(info, () => new InvalidOperationException("machine-worker-start-failed"));
                ProcessStartCount++;
                _child = TranslationChildLifetime.Own(_persistentProcess,
                    _persistentProcess.StandardError.BaseStream.CopyToAsync(Stream.Null));
            }
            var process = _persistentProcess;
            var payload = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await WriteRequestAsync(process, payload, timeout.Token).ConfigureAwait(false);
            var output = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("machine-worker-failed");
            var response = JsonSerializer.Deserialize<WorkerResponse>(output,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            ThrowStartupFailure(response);
            if (response?.Texts is null || response.Texts.Any(static text => text is null))
                throw new InvalidOperationException("machine-worker-invalid-response");
            LastBackendStatus = response.Backend;
            return WithEvidence(response, process.StartInfo.Environment.TryGetValue("CLIPRAY_MT_DEVICE", out var requested) ? requested ?? "auto" : "auto");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await ResetWorkerAsync().ConfigureAwait(false);
            throw new TimeoutException("machine-worker-timeout");
        }
        catch
        {
            await ResetWorkerAsync().ConfigureAwait(false);
            throw;
        }
    }

    private MachineTranslationProcessResult WithEvidence(WorkerResponse response, string requestedBackend)
    {
        TranslationExecutionEvidence? Describe(MachineTranslationBackendStatus? backend) => backend is null ? null
            : new("ctranslate2", Path.GetFileName(_modelPath), null, requestedBackend,
                backend.Verified ? $"{backend.Device} / {backend.ComputeType}" : null, backend.FallbackReason);
        // Older workers expose only current global status, which cannot identify
        // the backend that produced a cached text. Do not infer per-text evidence.
        return new(response.Texts, TextEvidence: response.TextBackends?.Select(Describe).ToArray());
    }
    private async Task ResetWorkerAsync()
    {
        LastBackendStatus = null;
        if (_child is { } child)
        {
            await child.RetireAsync().ConfigureAwait(false);
            _child = null;
            _persistentProcess = null;
        }
    }

    public void Dispose()
    {
        _ = DisposeAsync();
    }
    public ValueTask DisposeAsync() => new(_lifetime.RetireAsync(async () =>
    { _disposed = true; await ResetWorkerAsync().ConfigureAwait(false); }));

    private static void EnsureRequiredPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("Bundled machine translation component is unavailable.");
    }

    private static void ThrowStartupFailure(WorkerResponse? response)
    {
        if (response?.Error is "machine-dependency-unavailable" or "machine-runtime-load-failed")
            throw new MachineTranslationRuntimeException(response.Error);
    }

    private static async Task WriteRequestAsync(Process process, string payload, CancellationToken token)
    {
        try
        {
            await process.StandardInput.WriteLineAsync(payload.AsMemory(), token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Import failures can close stdin before a large request is written.
            // Recover only the fixed protocol diagnostic, within the same timeout.
            var line = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(line))
                ThrowStartupFailure(JsonSerializer.Deserialize<WorkerResponse>(line,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
            throw;
        }
    }

    private sealed record WorkerResponse(IReadOnlyList<string> Texts, MachineTranslationBackendStatus? Backend,
        string? Error = null, IReadOnlyList<MachineTranslationBackendStatus?>? TextBackends = null);
}

public sealed record MachineTranslationBackendStatus(string Device, string ComputeType, bool Verified,
    string? FallbackReason);
