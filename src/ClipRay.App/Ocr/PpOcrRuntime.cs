using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using ClipRay.App.Infrastructure;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Ocr;

internal interface IOcrWarmup
{
    Task WarmupAsync(CancellationToken cancellationToken);
}

internal sealed class PpOcrRuntime : IOcrRuntime, IOcrWarmup, IDisposable
{
    private const string WarmupRequest = "{\"warmup\":true}";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;
    private bool _disposed;

    public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (_disposed) throw new ObjectDisposedException(nameof(PpOcrRuntime));
        cancellationToken.ThrowIfCancellationRequested();
        var (python, script, models) = ResolvePaths();
        if (!File.Exists(python) || !File.Exists(script) || !Directory.Exists(models))
            throw new OcrUnavailableException("Local PP-OCR model or runtime is unavailable.");

        var png = await OcrBitmapEncoder.EncodePngAsync(bitmap, null, cancellationToken).ConfigureAwait(false);
        var input = JsonSerializer.Serialize(new { image = Convert.ToBase64String(png) });
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProcess(python, script, models);
            cancellationToken.ThrowIfCancellationRequested();
            await _input!.WriteLineAsync(input).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
            var output = await _output!.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output)) throw new OcrUnavailableException("Local PP-OCR sidecar exited; Windows OCR fallback will be used.");
            using var json = JsonDocument.Parse(output);
            if (json.RootElement.TryGetProperty("error", out _))
                throw new OcrUnavailableException("Local PP-OCR failed; Windows OCR fallback will be used.");
            return json.RootElement.GetProperty("words").EnumerateArray()
                .Select(item => new OcrWord(
                    item.GetProperty("text").GetString()!,
                    new PixelRect(
                        item.GetProperty("x").GetDouble(),
                        item.GetProperty("y").GetDouble(),
                        item.GetProperty("width").GetDouble(),
                        item.GetProperty("height").GetDouble())))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            StopProcess();
            throw;
        }
        catch
        {
            StopProcess();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WarmupAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        var (python, script, models) = ResolvePaths();
        if (!File.Exists(python) || !File.Exists(script) || !Directory.Exists(models)) return;

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                EnsureProcess(python, script, models);
                await _input!.WriteLineAsync(WarmupRequest).ConfigureAwait(false);
                await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
                var output = await _output!.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (!IsReadyResponse(output))
                    throw new OcrUnavailableException("Local PP-OCR sidecar did not finish loading.");
            }
            catch
            {
                StopProcess();
                throw;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { /* the foreground request will surface the fallback status */ }
    }

    private static bool IsReadyResponse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        try
        {
            using var json = JsonDocument.Parse(output);
            return json.RootElement.TryGetProperty("ready", out var ready)
                && ready.ValueKind == JsonValueKind.True
                && ready.GetBoolean();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static (string Python, string Script, string Models) ResolvePaths()
    {
        var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
        return (
            Path.Combine(root, "artifacts", "runtimes", "paddleocr", "Scripts", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "Ocr", "ppocr_sidecar.py"),
            Path.Combine(root, "artifacts", "models", "paddleocr"));
    }

    private void EnsureProcess(string python, string script, string models)
    {
        if (_process is { HasExited: false }) return;
        StopProcess();
        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("--models");
        startInfo.ArgumentList.Add(models);
        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new OcrUnavailableException("Local PP-OCR sidecar could not start; Windows OCR fallback will be used.");
        }

        _process = process;
        _input = process.StandardInput;
        _output = process.StandardOutput;
        _ = process.StandardError.ReadToEndAsync();
    }

    private void StopProcess()
    {
        _input?.Dispose();
        _output?.Dispose();
        _input = null;
        _output = null;
        if (_process is { } process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch { /* sidecar cleanup must not mask the OCR failure */ }
            process.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Wait();
        try { StopProcess(); }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}

internal sealed class FastFirstOcrRuntime(IOcrRuntime fast, IOcrRuntime fallback) : IOcrRuntime, IOcrWarmup, IDisposable, IAsyncDisposable
{
    public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        IReadOnlyList<OcrWord> words = [];
        try
        {
            words = await fast.RecognizeAsync(bitmap, cancellationToken).ConfigureAwait(false);
            if (!OcrQualityPolicy.NeedsReview(words, bitmap.Width, bitmap.Height)) return words;
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        try { return OcrQualityPolicy.Choose(words, await fallback.RecognizeAsync(bitmap, cancellationToken).ConfigureAwait(false)); }
        catch (OperationCanceledException) { throw; }
        catch when (words.Count > 0) { return words; }
    }

    public void Dispose()
    {
        _ = DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try { await OcrRuntimeRelease.ReleaseAsync(fast).ConfigureAwait(false); }
        finally { await OcrRuntimeRelease.ReleaseAsync(fallback).ConfigureAwait(false); }
    }

    public Task WarmupAsync(CancellationToken cancellationToken) =>
        fast is IOcrWarmup warmup ? warmup.WarmupAsync(cancellationToken) : Task.CompletedTask;
}
