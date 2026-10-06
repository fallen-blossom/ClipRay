using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClipRay.App.Infrastructure;

namespace ClipRay.App.Translation;

public static class LocalLmtLanguage
{
    public static string Normalize(string language) => language.Trim().ToLowerInvariant() switch
    {
        "简体中文" or "中文" or "zh" or "zh-cn" or "chinese" => "Chinese",
        "英文" or "英语" or "en" or "en-us" or "english" => "English",
        "ja" or "japanese" or "日语" => "Japanese",
        "ko" or "korean" or "韩语" => "Korean",
        "es" or "spanish" or "西班牙语" => "Spanish",
        "fr" or "french" or "法语" => "French",
        "de" or "german" or "德语" => "German",
        "ru" or "russian" or "俄语" => "Russian",
        "pt" or "portuguese" or "葡萄牙语" => "Portuguese",
        "it" or "italian" or "意大利语" => "Italian",
        "ar" or "arabic" or "阿拉伯语" => "Arabic",
        _ => language.Trim(),
    };

    public static string NormalizeSource(string language, string text)
    {
        if (!string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase)) return Normalize(language);
        if (text.Any(static value => value is >= '\u3040' and <= '\u30FF')) return "Japanese";
        if (text.Any(static value => value is >= '\uAC00' and <= '\uD7AF')) return "Korean";
        if (text.Any(static value => value is >= '\u0400' and <= '\u04FF')) return "Russian";
        var hanCount = text.Count(static value => value is >= '\u4E00' and <= '\u9FFF');
        var latinCount = text.Count(static value => value is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
        if (hanCount > 0 && hanCount >= latinCount) return "Chinese";
        return "English";
    }

    public static bool AreEquivalent(string sourceLanguage, string targetLanguage) =>
        string.Equals(Normalize(sourceLanguage), Normalize(targetLanguage), StringComparison.OrdinalIgnoreCase);
}

public sealed record LocalLmtOptions(
    string ModelPath,
    string LlamaCliPath,
    LocalLmtBackend PreferredBackend = LocalLmtBackend.Vulkan,
    string? ExpectedModelSha256 = null)
{
    public static readonly string Q4ModelSha256 = "9e9326981457f09e6cd66ea57aa9d879dc142edb226dee0feebf6bef317cf6e0";
    public static readonly string DefaultModelSha256 = "743c6cdc13294b5c470ec0e89e79d64f96ef0cab94ce1285c78da4dc0f56be5c";

    public static LocalLmtOptions Discover(
        string? configuredModelPath = null,
        string? installRootOverride = null)
    {
        var installRoot = string.IsNullOrWhiteSpace(installRootOverride)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(installRootOverride);
        var workspaceRoot = WorkspaceLocator.TryFind(installRoot);
        // An explicit selection must survive a missing file so readiness can report
        // that failure instead of silently running a different model.
        var model = !string.IsNullOrWhiteSpace(configuredModelPath) ? configuredModelPath : FirstExisting(
            Environment.GetEnvironmentVariable("CLIPRAY_LMT_MODEL_PATH"),
            Path.Combine(installRoot, "models", "local-lmt", "LMT-60-0.6B.Q4_K_M.gguf"),
            workspaceRoot is null ? null : Path.Combine(workspaceRoot, "artifacts", "models", "NiuTrans-LMT-60-0.6B", "LMT-60-0.6B.Q4_K_M.gguf"),
            Path.Combine(installRoot, "models", "local-lmt", "LMT-60-1.7B-Q4_K_M.gguf"),
            workspaceRoot is null ? null : Path.Combine(workspaceRoot, "artifacts", "models", "NiuTrans-LMT-60-1.7B", "LMT-60-1.7B-Q4_K_M.gguf"));
        var cli = FirstExisting(
            Environment.GetEnvironmentVariable("CLIPRAY_LLAMA_CLI_PATH"),
            Path.Combine(installRoot, "tools", "llama", "llama-cli.exe"),
            workspaceRoot is null ? null : Path.Combine(workspaceRoot, "artifacts", "tools", "llama-b8816-bin-win-vulkan-x64", "llama-cli.exe"),
            workspaceRoot is null ? null : Path.Combine(workspaceRoot, "artifacts", "tools", "llama-b8816-bin-win-cpu-x64", "llama-cli.exe"));
        return new LocalLmtOptions(
            model ?? string.Empty,
            cli ?? string.Empty,
            ExpectedModelSha256: LocalLmtModelCatalog.ExpectedSha256For(model));
    }

    private static string? FirstExisting(params string?[] candidates) => candidates.FirstOrDefault(static candidate =>
        !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
}

public sealed record LocalLmtModelDescriptor(string FileName, string DisplayName, string Sha256);
public sealed record LocalLmtDevelopmentModelOption(string DisplayName, string Path, bool IsInstalled);

public static class LocalLmtModelCatalog
{
    private static readonly LocalLmtModelDescriptor DefaultModel = new(
        "LMT-60-0.6B.Q4_K_M.gguf",
        "LMT-60 · 0.6B 参数 · Q4_K_M（默认）",
        LocalLmtOptions.DefaultModelSha256);

    private static readonly LocalLmtModelDescriptor FullModel = new(
        "LMT-60-1.7B-Q4_K_M.gguf",
        "LMT-60 · 1.7B 参数 · Q4_K_M（备用）",
        LocalLmtOptions.Q4ModelSha256);

    public static IReadOnlyList<LocalLmtModelDescriptor> DevelopmentModels { get; } =
        [DefaultModel, FullModel];

    public static string ExpectedSha256For(string? modelPath) =>
        Find(modelPath)?.Sha256 ?? string.Empty;

    public static string DisplayNameFor(string? modelPath, string interfaceLanguage = "zh-CN") =>
        Find(modelPath)?.DisplayName ?? (string.IsNullOrWhiteSpace(modelPath)
            ? interfaceLanguage == "en" ? "Local LMT · No model configured" : "Local LMT · 未配置模型"
            : interfaceLanguage == "en" ? $"Local LMT · Unrecognized model ({Path.GetFileName(modelPath)})" : $"Local LMT · 未识别模型（{Path.GetFileName(modelPath)}）");

    public static string ProviderChoiceNameFor(string? modelPath, string interfaceLanguage = "zh-CN") =>
        Find(modelPath) is not { } model ? DisplayNameFor(modelPath, interfaceLanguage)
        : string.Equals(model.FileName, DefaultModel.FileName, StringComparison.OrdinalIgnoreCase)
            ? interfaceLanguage == "en" ? "Local LMT · 0.6B (default)" : "Local LMT · 0.6B 参数（默认）"
            : interfaceLanguage == "en" ? "Local LMT · 1.7B (alternate)" : "Local LMT · 1.7B 参数（备用）";

    public static IReadOnlyList<LocalLmtDevelopmentModelOption> ResolveDevelopmentModels(string workspaceRoot) =>
        DevelopmentModels.Select(model =>
        {
            var folderName = model.FileName.StartsWith("LMT-60-0.6B", StringComparison.OrdinalIgnoreCase)
                ? "NiuTrans-LMT-60-0.6B"
                : "NiuTrans-LMT-60-1.7B";
            var path = Path.Combine(workspaceRoot, "artifacts", "models", folderName, model.FileName);
            return new LocalLmtDevelopmentModelOption(model.DisplayName, path, File.Exists(path));
        }).ToArray();

    public static IReadOnlyList<LocalLmtDevelopmentModelOption> ResolveProviderModels(
        string installRoot,
        string? workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        var installedRoot = Path.GetFullPath(installRoot);
        var developmentModels = string.IsNullOrWhiteSpace(workspaceRoot)
            ? []
            : ResolveDevelopmentModels(workspaceRoot);
        var options = new List<LocalLmtDevelopmentModelOption>();

        foreach (var model in DevelopmentModels)
        {
            var installedPath = Path.Combine(installedRoot, "models", "local-lmt", model.FileName);
            if (File.Exists(installedPath))
            {
                options.Add(new LocalLmtDevelopmentModelOption(model.DisplayName, installedPath, IsInstalled: true));
                continue;
            }

            var development = developmentModels.FirstOrDefault(option => string.Equals(
                Path.GetFileName(option.Path),
                model.FileName,
                StringComparison.OrdinalIgnoreCase));
            if (development is not null) options.Add(development);
        }

        return options;
    }

    private static LocalLmtModelDescriptor? Find(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath)) return null;
        var fileName = Path.GetFileName(modelPath);
        return DevelopmentModels.FirstOrDefault(model =>
            string.Equals(model.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }
}

public enum LocalLmtBackend { Vulkan, Cpu }

public sealed record LocalLmtSessionStart(
    string FileName,
    string Arguments,
    bool ClearAfterEachResponse = true);

public interface ILocalLmtSession : IDisposable
{
    bool IsAlive { get; }
    Task<string> TranslateAsync(string prompt, CancellationToken cancellationToken);
}

public interface ILocalLmtSessionFactory
{
    Task<ILocalLmtSession> StartAsync(LocalLmtSessionStart start, CancellationToken cancellationToken);
}

public interface ILocalLmtOneShotRunner
{
    Task<string> TranslateAsync(LocalLmtSessionStart start, string prompt, CancellationToken cancellationToken);
}

public interface ILocalLmtBatchRunner
{
    Task<IReadOnlyList<string>> TranslateAsync(
        LocalLmtSessionStart start,
        IReadOnlyList<string> prompts,
        CancellationToken cancellationToken);
}

public sealed class LocalLmtPromptFile : IDisposable
{
    private bool _disposed;

    private LocalLmtPromptFile(string path) => Path = path;

    public string Path { get; }

    public static LocalLmtPromptFile Create(string prompt, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var root = directory ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClipRay", "LocalLmt");
        Directory.CreateDirectory(root);
        var path = System.IO.Path.Combine(root, $"prompt-{Guid.NewGuid():N}.txt");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
        {
            stream.Write(Encoding.UTF8.GetBytes(prompt));
            stream.Flush(flushToDisk: true);
        }
        return new LocalLmtPromptFile(path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        File.Delete(Path);
    }
}

public sealed class LocalLmtOneShotRunner : ILocalLmtOneShotRunner
{
    public static LocalLmtSessionStart WithPromptFile(LocalLmtSessionStart start, string promptFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptFilePath);
        if (promptFilePath.Contains('"')) throw new ArgumentException("Prompt file path cannot contain a quote.", nameof(promptFilePath));
        return start with { Arguments = $"{start.Arguments} --file \"{promptFilePath}\"" };
    }

    public async Task<string> TranslateAsync(LocalLmtSessionStart start, string prompt, CancellationToken cancellationToken)
    {
        using var promptFile = LocalLmtPromptFile.Create(prompt);
        using var process = new Process { StartInfo = LocalLmtProcessSession.CreateStartInfo(WithPromptFile(start, promptFile.Path)) };
        if (!process.Start()) throw new LocalLmtProcessException(LocalLmtProcessFailureKind.StartupFailed);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            _ = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new LocalLmtProcessException(LocalLmtProcessFailureKind.SessionExited);
            var output = await stdout.ConfigureAwait(false);
            return RemoveCurrentPromptEcho(output, prompt);
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await process.WaitForExitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the original cancellation or translation failure.
                }
            }
        }
    }

    public static string RemoveCurrentPromptEcho(string output, string prompt)
    {
        var cleaned = output.Replace("\r", string.Empty, StringComparison.Ordinal);
        var promptIndex = cleaned.IndexOf(prompt, StringComparison.Ordinal);
        var response = promptIndex < 0
            ? cleaned
            : cleaned[(promptIndex + prompt.Length)..].TrimStart('\n');
        response = Regex.Replace(
            response,
            @"(?ms)\s*\[\s*Prompt:.*?\]\s*$",
            string.Empty,
            RegexOptions.CultureInvariant);
        response = Regex.Replace(
            response,
            @"(?im)^\s*\[end of text\]\s*$",
            string.Empty,
            RegexOptions.CultureInvariant);
        response = Regex.Replace(
            response,
            @"(?im)^\s*Exiting\.\.\.\s*$",
            string.Empty,
            RegexOptions.CultureInvariant);
        return response.Trim();
    }
}

public sealed class LocalLmtBatchRunner : ILocalLmtBatchRunner
{
    public async Task<IReadOnlyList<string>> TranslateAsync(
        LocalLmtSessionStart start,
        IReadOnlyList<string> prompts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        if (prompts.Count == 0) return [];

        // Wait for the actual interactive prompt before sending the first request.
        // With --file, llama-cli can emit a startup prompt/banner before it has
        // answered the file input, shifting every batch response by one item.
        using var session = await LocalLmtProcessSession.StartAsync(start, cancellationToken).ConfigureAwait(false);
        var responses = new List<string>(prompts.Count);
        for (var index = 0; index < prompts.Count; index++)
        {
            var oneLinePrompt = Regex.Replace(prompts[index], @"\s+", " ").Trim();
            responses.Add(await session.TranslateAsync(oneLinePrompt, cancellationToken).ConfigureAwait(false));
        }
        return responses;
    }
}

public sealed class LocalLmtProcessSessionFactory : ILocalLmtSessionFactory
{
    public Task<ILocalLmtSession> StartAsync(LocalLmtSessionStart start, CancellationToken cancellationToken) =>
        LocalLmtProcessSession.StartAsync(start, cancellationToken);
}

public sealed class LocalLmtProcessSession : ILocalLmtSession
{
    private static readonly Regex PromptStatisticsPattern = new(
        @"\s*\[\s*Prompt:.*?\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex SessionControlLinePattern = new(
        @"(?im)^\s*Chat history cleared\.\s*\n?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly Process _process;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Task _stderrDrain;
    private readonly bool _clearAfterEachResponse;
    private bool _disposed;

    private LocalLmtProcessSession(LocalLmtSessionStart start)
    {
        _clearAfterEachResponse = start.ClearAfterEachResponse;
        _process = new Process { StartInfo = CreateStartInfo(start), EnableRaisingEvents = true };
        if (!_process.Start()) throw new LocalLmtProcessException(LocalLmtProcessFailureKind.StartupFailed);
        _stderrDrain = _process.StandardError.ReadToEndAsync();
    }

    public bool IsAlive => !_disposed && !_process.HasExited;

    public static ProcessStartInfo CreateStartInfo(LocalLmtSessionStart start) => new(start.FileName)
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(false),
        StandardOutputEncoding = new UTF8Encoding(false),
        StandardErrorEncoding = new UTF8Encoding(false),
        CreateNoWindow = true,
        Arguments = start.Arguments,
    };

    public static async Task<ILocalLmtSession> StartAsync(
        LocalLmtSessionStart start,
        CancellationToken cancellationToken)
    {
        LocalLmtProcessSession? session = null;
        try
        {
            session = new LocalLmtProcessSession(start);
            _ = await session.ReadUntilPromptAsync(cancellationToken, LocalLmtProcessFailureKind.StartupFailed).ConfigureAwait(false);
            return session;
        }
        catch (OperationCanceledException)
        {
            session?.Dispose();
            throw;
        }
        catch (LocalLmtProcessException)
        {
            session?.Dispose();
            throw;
        }
        catch
        {
            session?.Dispose();
            throw new LocalLmtProcessException(LocalLmtProcessFailureKind.StartupFailed);
        }
    }

    public static async Task<(ILocalLmtSession Session, string InitialResponse)> StartWithInitialResponseAsync(
        LocalLmtSessionStart start,
        CancellationToken cancellationToken)
    {
        LocalLmtProcessSession? session = null;
        try
        {
            session = new LocalLmtProcessSession(start);
            var raw = await session.ReadUntilPromptAsync(
                cancellationToken,
                LocalLmtProcessFailureKind.StartupFailed).ConfigureAwait(false);
            return (session, CleanResponse(raw));
        }
        catch (OperationCanceledException)
        {
            session?.Dispose();
            throw;
        }
        catch (LocalLmtProcessException)
        {
            session?.Dispose();
            throw;
        }
        catch
        {
            session?.Dispose();
            throw new LocalLmtProcessException(LocalLmtProcessFailureKind.StartupFailed);
        }
    }

    public async Task<string> TranslateAsync(string prompt, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (prompt.Contains('\r') || prompt.Contains('\n'))
            throw new ArgumentException("A Local LMT interactive prompt must be one line.", nameof(prompt));

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsAlive) throw new LocalLmtProcessException(LocalLmtProcessFailureKind.SessionExited);
            await _process.StandardInput.WriteLineAsync(prompt.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            var raw = await ReadUntilPromptAsync(cancellationToken, LocalLmtProcessFailureKind.ResponseBoundaryLost).ConfigureAwait(false);
            if (_clearAfterEachResponse)
            {
                // 长生命周期测试 seam 仍可显式清理；生产短批次不会发送控制命令。
                await _process.StandardInput.WriteLineAsync("/clear".AsMemory(), cancellationToken).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                _ = await ReadUntilPromptAsync(cancellationToken, LocalLmtProcessFailureKind.ResetBoundaryLost).ConfigureAwait(false);
            }
            return CleanResponse(raw);
        }
        catch (OperationCanceledException)
        {
            Dispose();
            throw;
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        finally
        {
            _process.Dispose();
            _ = _stderrDrain.Exception;
        }
    }

    private async Task<string> ReadUntilPromptAsync(
        CancellationToken cancellationToken,
        LocalLmtProcessFailureKind failure)
    {
        var output = new StringBuilder();
        var buffer = new char[1];
        while (true)
        {
            var read = await _process.StandardOutput.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new LocalLmtProcessException(failure);
            }
            output.Append(buffer[0]);
            if (output.Length >= 3
                && output[^3] == '\n'
                && output[^2] == '>'
                && output[^1] == ' ')
            {
                output.Length -= 3;
                return output.ToString();
            }
        }
    }

    private static string CleanResponse(string raw)
    {
        var cleaned = raw.Replace("\r", string.Empty, StringComparison.Ordinal);
        cleaned = SessionControlLinePattern.Replace(cleaned, string.Empty);
        cleaned = PromptStatisticsPattern.Replace(cleaned, string.Empty).Trim();
        if (cleaned.Length >= 2
            && ((cleaned[0] == '“' && cleaned[^1] == '”') || (cleaned[0] == '"' && cleaned[^1] == '"')))
        {
            cleaned = cleaned[1..^1].Trim();
        }
        return cleaned;
    }
}

public static class LocalLmtArtifactVerifier
{
    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool IsVerified(LocalLmtOptions options) =>
        File.Exists(options.ModelPath) &&
        File.Exists(options.LlamaCliPath) &&
        !string.IsNullOrWhiteSpace(options.ExpectedModelSha256) &&
        string.Equals(Sha256File(options.ModelPath), options.ExpectedModelSha256, StringComparison.OrdinalIgnoreCase);

    public static Task<bool> IsVerifiedAsync(LocalLmtOptions options, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(options.ModelPath) || !File.Exists(options.LlamaCliPath)
                || string.IsNullOrWhiteSpace(options.ExpectedModelSha256)) return false;
            await using var stream = new FileStream(options.ModelPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return string.Equals(Convert.ToHexString(hash), options.ExpectedModelSha256, StringComparison.OrdinalIgnoreCase);
        }, cancellationToken);
}

public sealed class LocalTranslationProvider : IDisposable
{
    private const int BodyRetryChunkTargetLength = 72;
    private const string SourceUnchangedStatus = "Local LMT returned the source text unchanged.";
    private const string SourceLanguageOnlyStatus = "Local LMT returned a source-language-only body.";
    private const string MissingTargetLanguageStatus = "Local LMT returned text without target-language content.";

    public bool HasActiveSession => _session is { IsAlive: true };
    private static readonly Regex ModelTokenPattern = new(
        @"\[\[[KPQZ]\d+\]\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SessionControlLinePattern = new(
        @"(?im)^\s*Chat history cleared\.\s*\n?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static TimeSpan DefaultIdleShutdown { get; } = TimeSpan.FromMinutes(15);

    private readonly LocalLmtOptions _options;
    private readonly ILocalLmtSessionFactory _sessionFactory;
    private readonly ILocalLmtOneShotRunner? _oneShotRunner;
    private readonly ILocalLmtBatchRunner? _batchRunner;
    private readonly bool _collectLocalDiagnosticContext;
    private readonly TimeSpan _idleShutdown;
    private readonly SemaphoreSlim _translationGate = new(1, 1);
    private readonly System.Threading.Timer _idleTimer;
    private ILocalLmtSession? _session;
    private string _backend = string.Empty;
    private string? _localLmtInputMode;
    private int _currentProtectedTokenCount;
    private int _restoredProtectedTokenCount;
    private DateTimeOffset _lastUse = DateTimeOffset.MinValue;
    private bool? _artifactsVerified;
    private ArtifactSnapshot? _verifiedArtifactSnapshot;
    private bool _disposed;

    public LocalTranslationProvider(
        LocalLmtOptions options,
        ILocalLmtSessionFactory? sessionFactory = null,
        TimeSpan? idleShutdown = null,
        ILocalLmtOneShotRunner? oneShotRunner = null,
        ILocalLmtBatchRunner? batchRunner = null,
        bool? collectLocalDiagnosticContext = null)
    {
        _options = options;
        _sessionFactory = sessionFactory ?? new LocalLmtProcessSessionFactory();
        _oneShotRunner = oneShotRunner ?? (sessionFactory is null ? new LocalLmtOneShotRunner() : null);
        _batchRunner = batchRunner ?? (sessionFactory is null ? new LocalLmtBatchRunner() : null);
        _collectLocalDiagnosticContext = collectLocalDiagnosticContext ?? sessionFactory is null;
        _idleShutdown = idleShutdown ?? DefaultIdleShutdown;
        _idleTimer = new System.Threading.Timer(StopIdleSession, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var results = await TranslateManyAsync([request], cancellationToken).ConfigureAwait(false);
        return results[0];
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return [];
        foreach (var request in requests) ArgumentNullException.ThrowIfNull(request);

        var results = new TranslationResult[requests.Count];
        var work = BuildWork(requests, results);
        if (work.Count == 0) return results;
        await _translationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = CaptureArtifactSnapshot();
            if (_artifactsVerified == true && snapshot != _verifiedArtifactSnapshot)
            {
                _artifactsVerified = false;
                _verifiedArtifactSnapshot = null;
                StopSession();
            }
            // Missing or invalid artifacts can be repaired while this provider lives.
            // Serialize verification with inference, and never cache a failed check.
            if (_artifactsVerified != true)
            {
                _artifactsVerified = await LocalLmtArtifactVerifier.IsVerifiedAsync(_options, cancellationToken).ConfigureAwait(false);
                // Do not cache a digest if ordinary file metadata changed during hashing.
                _artifactsVerified = _artifactsVerified == true && snapshot == CaptureArtifactSnapshot();
                _verifiedArtifactSnapshot = _artifactsVerified == true ? snapshot : null;
            }
            if (_artifactsVerified != true)
            {
                foreach (var item in work)
                    results[item.Index] = Failure(
                        "Local LMT model is unavailable or failed verification.",
                        TranslationDiagnosticKind.ModelUnavailable,
                        TranslationDiagnosticStage.Initial);
                return results;
            }
            var batchResults = await TryTranslateBatchAsync(work, cancellationToken).ConfigureAwait(false);
            foreach (var item in work)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (batchResults.TryGetValue(item.Index, out var batchResult))
                {
                    _lastUse = DateTimeOffset.UtcNow;
                    results[item.Index] = batchResult;
                    continue;
                }
                try
                {
                    var result = await TranslateAndValidateWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
                    _lastUse = DateTimeOffset.UtcNow;
                    results[item.Index] = result;
                }
                catch (OperationCanceledException)
                {
                    StopSession();
                    throw;
                }
                catch
                {
                    StopSession();
                    results[item.Index] = Failure(
                        "Local LMT failed without exposing private text.",
                        TranslationDiagnosticKind.ProcessFailure,
                        TranslationDiagnosticStage.Initial);
                }
            }
            ArmIdleTimer();
            return results;
        }
        finally
        {
            _translationGate.Release();
        }
    }

    public bool IsIdleShutdownDue(DateTimeOffset now) =>
        _session is not null && _lastUse != DateTimeOffset.MinValue && now - _lastUse >= _idleShutdown;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _idleTimer.Dispose();
        StopSession();
    }

    private List<LocalTranslationWorkItem> BuildWork(
        IReadOnlyList<TranslationRequest> requests,
        TranslationResult[] results)
    {
        var work = new List<LocalTranslationWorkItem>();
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            var sourceLanguage = LocalLmtLanguage.NormalizeSource(request.SourceLanguage, request.OcrText);
            var targetLanguage = LocalLmtLanguage.Normalize(request.TargetLanguage);
            if (LocalLmtLanguage.AreEquivalent(sourceLanguage, targetLanguage))
            {
                results[index] = new TranslationResult(true, request.OcrText, "Local LMT bypassed equivalent languages.");
                continue;
            }
            if (LocalDeterministicTranslation.TryTranslate(request, sourceLanguage, targetLanguage, out var deterministic))
            {
                results[index] = new TranslationResult(true, deterministic, "Local deterministic translation completed.");
                continue;
            }

            var localProtected = LocalProtectedTranslationText.Create(request.OcrText, targetLanguage);
            if (!localProtected.HasTranslatableText)
            {
                results[index] = new TranslationResult(true, localProtected.ReconstructedText, "Local protected text preserved without model inference.");
                continue;
            }
            work.Add(new LocalTranslationWorkItem(
                index,
                request,
                sourceLanguage,
                targetLanguage,
                localProtected));
        }
        return work;
    }

    private async Task<ILocalLmtSession> GetOrStartSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is { IsAlive: true }) return _session;
        StopSession();
        var preferVulkan = _options.PreferredBackend == LocalLmtBackend.Vulkan;
        try
        {
            _session = await StartSessionAsync(preferVulkan, cancellationToken).ConfigureAwait(false);
            _backend = preferVulkan ? "Vulkan" : "CPU";
        }
        catch when (preferVulkan && !cancellationToken.IsCancellationRequested)
        {
            StopSession();
            _session = await StartSessionAsync(false, cancellationToken).ConfigureAwait(false);
            _backend = "CPU fallback after Vulkan startup failure";
        }
        return _session;
    }

    private Task<ILocalLmtSession> StartSessionAsync(bool vulkan, CancellationToken cancellationToken)
    {
        // LMT is based on Qwen3.  Translation needs a direct answer, not a reasoning trace
        // that can consume the small model's entire output budget before the translation begins.
        return _sessionFactory.StartAsync(
            new LocalLmtSessionStart(_options.LlamaCliPath, BuildSessionArguments(vulkan)),
            cancellationToken);
    }

    private string BuildSessionArguments(bool vulkan = true)
    {
        var arguments = $"--model \"{_options.ModelPath}\" --conversation --escape --no-display-prompt --no-warmup --simple-io --log-disable --temp 0 --ctx-size 4096 --predict 256 --reasoning-budget 0";
        if (vulkan) arguments += " --gpu-layers 99";
        return arguments;
    }

    private string BuildFileOneShotArguments(bool vulkan = true, int predict = 256)
    {
        var arguments = $"--model \"{_options.ModelPath}\" --no-conversation --single-turn --escape --no-display-prompt --no-warmup --simple-io --log-disable --temp 0 --ctx-size 4096 --predict {predict} --reasoning-budget 0";
        if (vulkan) arguments += " --gpu-layers 99";
        return arguments;
    }

    private string BuildBatchSessionArguments(bool vulkan = true)
    {
        var arguments = $"--model \"{_options.ModelPath}\" --conversation --escape --no-display-prompt --no-warmup --simple-io --log-disable --temp 0 --ctx-size 4096 --predict 256 --reasoning-budget 0";
        if (vulkan) arguments += " --gpu-layers 99";
        return arguments;
    }

    private async Task<IReadOnlyDictionary<int, TranslationResult>> TryTranslateBatchAsync(
        IReadOnlyList<LocalTranslationWorkItem> work,
        CancellationToken cancellationToken)
    {
        if (work.Count < 2 || !_collectLocalDiagnosticContext || _batchRunner is null)
            return new Dictionary<int, TranslationResult>();

        _localLmtInputMode = "interactive-batch";
        _backend = "Vulkan short-lived batch session";
        IReadOnlyList<string> responses;
        try
        {
            responses = await _batchRunner.TranslateAsync(
                new LocalLmtSessionStart(
                    _options.LlamaCliPath,
                    BuildBatchSessionArguments(),
                    ClearAfterEachResponse: true),
                work.Select(static item => BuildDirectPrompt(
                    item.SourceLanguage,
                    item.TargetLanguage,
                    item.LocalProtectedText.ModelText,
                    strictTokenCopy: false)).ToArray(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (LocalLmtProcessException)
        {
            return new Dictionary<int, TranslationResult>();
        }

        var results = new Dictionary<int, TranslationResult>();
        for (var batchIndex = 0; batchIndex < work.Count && batchIndex < responses.Count; batchIndex++)
        {
            var item = work[batchIndex];
            var normalized = NormalizeModelResponse(
                responses[batchIndex],
                item.LocalProtectedText.ModelText,
                item.TargetLanguage);
            if (normalized.Length == 0 || LocalLmtPromptEchoGuard.IsLikelyEcho(normalized, item.LocalProtectedText.ModelText)) continue;
            normalized = item.LocalProtectedText.RemoveInstructionEcho(normalized);
            if (!item.LocalProtectedText.HasModelTokenIntegrity(item.LocalProtectedText.ModelText, normalized)) continue;
            if (!item.LocalProtectedText.TryRestore(normalized, out var restored)) continue;
            if (item.LocalProtectedText.HasModelTokens
                && item.LocalProtectedText.FindProtectedValueMismatch(restored) is not null) continue;

            _currentProtectedTokenCount = item.LocalProtectedText.ModelTokenCount;
            _restoredProtectedTokenCount = item.LocalProtectedText.ModelTokenCount;
            var result = WithDiagnostic(RestoreAndValidate(item, restored), TranslationDiagnosticStage.Initial);
            if (result.IsSuccess) results[item.Index] = result;
        }
        return results;
    }

    private async Task<string> InvokeModelAsync(string prompt, CancellationToken cancellationToken)
    {
        if (_collectLocalDiagnosticContext && _oneShotRunner is not null)
        {
            _localLmtInputMode = "single-turn-file";
            _backend = "Vulkan file-backed single turn";
            return await _oneShotRunner.TranslateAsync(
                new LocalLmtSessionStart(_options.LlamaCliPath, BuildFileOneShotArguments()),
                prompt,
                cancellationToken).ConfigureAwait(false);
        }

        _localLmtInputMode = "interactive-stdin";
        var session = await GetOrStartSessionAsync(cancellationToken).ConfigureAwait(false);
        return await session.TranslateAsync(prompt, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> TranslateWorkItemAsync(
        LocalTranslationWorkItem item,
        CancellationToken cancellationToken)
    {
        var source = item.LocalProtectedText.ModelText;
        var translated = await TranslateModelTextWithTokenRepairAsync(item, source, cancellationToken).ConfigureAwait(false);
        if (item.LocalProtectedText.TryRestore(translated, out var restored)
            && (!item.LocalProtectedText.HasModelTokens
                || item.LocalProtectedText.FindProtectedValueMismatch(restored) is null))
        {
            _restoredProtectedTokenCount = item.LocalProtectedText.ModelTokenCount;
            return restored;
        }

        translated = await TranslateAfterSplittingProtectedTokensAsync(item, source, cancellationToken).ConfigureAwait(false);
        if (!item.LocalProtectedText.TryRestore(translated, out restored)
            || (item.LocalProtectedText.HasModelTokens
                && item.LocalProtectedText.FindProtectedValueMismatch(restored) is not null))
        {
            var mismatch = item.LocalProtectedText.TryRestore(translated, out var candidate)
                ? item.LocalProtectedText.FindProtectedValueMismatch(candidate) ?? LocalProtectedValueMismatch.Other
                : item.LocalProtectedText.FindMissingTokenMismatch(translated);
            throw new LocalProtectedTokenMismatchException(mismatch);
        }
        _restoredProtectedTokenCount = item.LocalProtectedText.ModelTokenCount;
        return restored;
    }

    private async Task<string> TranslateModelTextAsync(
        LocalTranslationWorkItem item,
        string source,
        CancellationToken cancellationToken,
        bool strictTokenCopy = false,
        List<TranslationChunkDiagnostic>? diagnostics = null,
        int chunkIndex = 0,
        string stage = "initial")
    {
        var response = await InvokeModelAsync(
            BuildDirectPrompt(item.SourceLanguage, item.TargetLanguage, source, strictTokenCopy),
            cancellationToken).ConfigureAwait(false);
        var normalized = NormalizeModelResponse(response, source, item.TargetLanguage);
        if (normalized.Length == 0 && IsTargetLabelOnlyResponse(response, item.TargetLanguage))
        {
            response = await InvokeModelAsync(
                BuildDirectPrompt(item.SourceLanguage, item.TargetLanguage, source, strictTokenCopy),
                cancellationToken).ConfigureAwait(false);
            normalized = NormalizeModelResponse(response, source, item.TargetLanguage);
        }
        if (normalized.Length == 0)
            throw new LocalLmtOutputException(
                response.Length > MaximumResponseLength(source)
                    ? TranslationDiagnosticKind.OutputTooLong
                    : TranslationDiagnosticKind.EmptyTranslation,
                response.Length,
                MaximumResponseLength(source),
                response.Contains(NormalizeModelSource(source), StringComparison.Ordinal));
        if (LocalLmtPromptEchoGuard.IsLikelyEcho(normalized, source))
            throw new LocalLmtOutputException(TranslationDiagnosticKind.PromptEcho);
        normalized = item.LocalProtectedText.RemoveInstructionEcho(normalized);
        if (diagnostics is not null)
        {
            diagnostics.Add(ChunkDiagnostic(
                chunkIndex,
                source,
                normalized,
                item.LocalProtectedText.HasModelTokenIntegrity(source, normalized)
                    ? "completed"
                    : "protected-token-missing",
                stage));
        }
        return PreserveBoundaryWhitespace(source, normalized);
    }

    private async Task<string> TranslateModelTextWithTokenRepairAsync(
        LocalTranslationWorkItem item,
        string source,
        CancellationToken cancellationToken,
        List<TranslationChunkDiagnostic>? diagnostics = null,
        int chunkIndex = 0,
        string stage = "initial")
    {
        var translated = await TranslateModelTextAsync(
            item, source, cancellationToken, diagnostics: diagnostics, chunkIndex: chunkIndex, stage: stage).ConfigureAwait(false);
        if (!item.LocalProtectedText.HasModelTokenIntegrity(source, translated))
            return await TranslateAfterSplittingProtectedTokensAsync(item, source, cancellationToken, diagnostics, chunkIndex).ConfigureAwait(false);
        if (!item.LocalProtectedText.HasModelTokens
            || item.LocalProtectedText.TryRestoreModelText(source, translated, out _))
        {
            return translated;
        }
        return await TranslateAfterSplittingProtectedTokensAsync(item, source, cancellationToken, diagnostics, chunkIndex).ConfigureAwait(false);
    }

    private async Task<string> TranslateAfterSplittingProtectedTokensAsync(
        LocalTranslationWorkItem item,
        string source,
        CancellationToken cancellationToken,
        List<TranslationChunkDiagnostic>? diagnostics = null,
        int chunkIndex = 0)
    {
        var rebuilt = new StringBuilder();
        var cursor = 0;
        foreach (Match token in ModelTokenPattern.Matches(source))
        {
            var part = source[cursor..token.Index];
            if (part.Length > 0)
                rebuilt.Append(item.LocalProtectedText.HasTranslatableModelText(part)
                    ? await TranslateModelTextAsync(item, part, cancellationToken,
                        diagnostics: diagnostics, chunkIndex: chunkIndex, stage: "strict-retry").ConfigureAwait(false)
                    : part);
            rebuilt.Append(token.Value);
            cursor = token.Index + token.Length;
        }
        var tail = source[cursor..];
        if (tail.Length > 0)
            rebuilt.Append(item.LocalProtectedText.HasTranslatableModelText(tail)
                ? await TranslateModelTextAsync(item, tail, cancellationToken,
                    diagnostics: diagnostics, chunkIndex: chunkIndex, stage: "strict-retry").ConfigureAwait(false)
                : tail);
        return rebuilt.ToString();
    }

    private async Task<TranslationResult> TranslateAndValidateWorkItemAsync(
        LocalTranslationWorkItem item,
        CancellationToken cancellationToken)
    {
        _localLmtInputMode = null;
        _currentProtectedTokenCount = item.LocalProtectedText.ModelTokenCount;
        _restoredProtectedTokenCount = 0;
        if (ShouldTranslateBodyInChunksBeforeFullAttempt(item))
            return await RetryBodyInChunksAsync(
                item,
                Failure(
                    "Local LMT requires bounded protected-token chunks.",
                    TranslationDiagnosticKind.ProtectedContentChanged,
                    TranslationDiagnosticStage.Initial),
                cancellationToken).ConfigureAwait(false);

        TranslationResult result;
        try
        {
            var translated = await TranslateWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
            result = WithDiagnostic(RestoreAndValidate(item, translated), TranslationDiagnosticStage.Initial);
        }
        catch (LocalLmtProcessException failure)
        {
            return WithLocalLmtContext(ProcessFailure(failure.Failure, TranslationDiagnosticStage.Initial));
        }
        catch (LocalLmtOutputException failure)
        {
            result = OutputFailure(failure, TranslationDiagnosticStage.Initial);
        }
        catch (LocalProtectedTokenMismatchException failure)
        {
            result = Failure(
                "Local LMT changed a protected local value.",
                ProtectedDiagnosticKind(failure.Mismatch),
                TranslationDiagnosticStage.Initial);
        }
        if (ShouldRetryShortBodyStrictly(item, result))
        {
            StopSession();
            var retried = await RetryShortBodyStrictlyAsync(item, cancellationToken).ConfigureAwait(false);
            return WithLocalLmtContext(retried);
        }
        if (!ShouldRetryBodyInChunks(item, result)) return WithLocalLmtContext(result);
        StopSession();
        return WithLocalLmtContext(await RetryBodyInChunksAsync(item, result, cancellationToken).ConfigureAwait(false));
    }

    private async Task<TranslationResult> RetryShortBodyStrictlyAsync(
        LocalTranslationWorkItem item,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var translated = await TranslateModelTextWithTokenRepairAsync(
                    item,
                    item.LocalProtectedText.ModelText,
                    cancellationToken,
                    stage: "strict-retry").ConfigureAwait(false);
                if (!item.LocalProtectedText.TryRestore(translated, out var restored))
                    return Failure(
                        "Local LMT changed a protected local value.",
                        TranslationDiagnosticKind.ProtectedContentChanged,
                        TranslationDiagnosticStage.RetryChunk);
                var result = WithDiagnostic(
                    RestoreAndValidate(item, restored),
                    TranslationDiagnosticStage.RetryChunk);
                if (result.IsSuccess
                    || result.Status is not SourceUnchangedStatus
                        && result.Diagnostic?.Kind != TranslationDiagnosticKind.EmptyTranslation
                    || attempt == 1)
                {
                    return result;
                }
                StopSession();
            }
            catch (LocalLmtProcessException failure)
            {
                return ProcessFailure(failure.Failure, TranslationDiagnosticStage.RetryChunk);
            }
            catch (LocalLmtOutputException failure)
            {
                var result = OutputFailure(failure, TranslationDiagnosticStage.RetryChunk);
                if (failure.Kind is TranslationDiagnosticKind.EmptyTranslation or TranslationDiagnosticKind.OutputTooLong && attempt == 0)
                {
                    StopSession();
                    continue;
                }
                return result;
            }
        }
        throw new InvalidOperationException("Bounded Local LMT retry ended without a result.");
    }

    private static bool ShouldRetryShortBodyStrictly(
        LocalTranslationWorkItem item,
        TranslationResult result) =>
        !result.IsSuccess
        && item.LocalProtectedText.ModelText.Length <= BodyRetryChunkTargetLengthFor(item.SourceLanguage)
        && (result.Diagnostic?.Kind is TranslationDiagnosticKind.EmptyTranslation or TranslationDiagnosticKind.OutputTooLong
            || item.Request.TextKind == TranslationTextKind.Body
                && (result.Status is SourceUnchangedStatus or SourceLanguageOnlyStatus or MissingTargetLanguageStatus
                    || result.Diagnostic?.Kind == TranslationDiagnosticKind.PromptEcho));

    private async Task<TranslationResult> RetryBodyInChunksAsync(
        LocalTranslationWorkItem item,
        TranslationResult initialFailure,
        CancellationToken cancellationToken)
    {
        var chunks = SplitBodyModelText(
            item.LocalProtectedText.ModelText,
            BodyRetryChunkTargetLengthFor(item.SourceLanguage));
        if (chunks.Count <= 1) return initialFailure;

        var combined = new StringBuilder();
        var diagnostics = new List<TranslationChunkDiagnostic>();
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.LocalProtectedText.HasTranslatableModelText(chunk))
            {
                combined.Append(chunk);
                diagnostics.Add(ChunkDiagnostic(index + 1, chunk, chunk, "preserved"));
                continue;
            }
            string translated;
            try
            {
                translated = await TranslateRetryChunkWithSessionRecoveryAsync(
                    item, chunk, cancellationToken, diagnostics, index + 1).ConfigureAwait(false);
            }
            catch (LocalLmtProcessException failure)
            {
                diagnostics.Add(ChunkDiagnostic(index + 1, chunk, string.Empty, "model-process-output"));
                return ProcessFailure(failure.Failure, TranslationDiagnosticStage.RetryChunk, index + 1)
                    with { ChunkDiagnostics = diagnostics };
            }
            catch (LocalLmtOutputException failure)
            {
                diagnostics.Add(ChunkDiagnostic(index + 1, chunk, string.Empty, "model-output"));
                return OutputFailure(failure, TranslationDiagnosticStage.RetryChunk, index + 1)
                    with { ChunkDiagnostics = diagnostics };
            }
            catch
            {
                diagnostics.Add(ChunkDiagnostic(index + 1, chunk, string.Empty, "chunking-failed"));
                return Failure(
                    "Local LMT chunk retry failed without exposing private text.",
                    TranslationDiagnosticKind.ProcessFailure,
                    TranslationDiagnosticStage.RetryChunk,
                    index + 1) with { ChunkDiagnostics = diagnostics };
            }
            if (!item.LocalProtectedText.HasModelTokenIntegrity(chunk, translated))
            {
                AppendChunkDiagnosticIfChanged(
                    diagnostics,
                    ChunkDiagnostic(index + 1, chunk, translated, "protected-token-missing"));
                return Failure(
                    "Local LMT changed a protected local value.",
                    TranslationDiagnosticKind.ProtectedContentChanged,
                    TranslationDiagnosticStage.RetryChunk,
                    index + 1) with { ChunkDiagnostics = diagnostics };
            }
            var semanticFailure = GetSemanticFailure(item, chunk, translated);
            if (semanticFailure is not null)
            {
                var category = semanticFailure == SourceUnchangedStatus ? "source-unchanged" : "semantic-validation";
                diagnostics.Add(ChunkDiagnostic(index + 1, chunk, translated, category));
                return Failure(
                    semanticFailure,
                    DiagnosticKindForStatus(semanticFailure),
                    TranslationDiagnosticStage.RetryChunk,
                    index + 1) with { ChunkDiagnostics = diagnostics };
            }
            combined.Append(translated);
        }

        if (!item.LocalProtectedText.TryRestore(combined.ToString(), out var restored))
            return Failure(
                "Local LMT changed a protected local value.",
                TranslationDiagnosticKind.ProtectedContentChanged,
                TranslationDiagnosticStage.RetryChunk,
                chunks.Count) with { ChunkDiagnostics = diagnostics };
        return WithDiagnostic(RestoreAndValidate(item, restored), TranslationDiagnosticStage.RetryChunk, chunks.Count)
            with { ChunkDiagnostics = diagnostics };
    }

    private async Task<string> TranslateRetryChunkWithSessionRecoveryAsync(
        LocalTranslationWorkItem item,
        string chunk,
        CancellationToken cancellationToken,
        List<TranslationChunkDiagnostic> diagnostics,
        int chunkIndex)
    {
        try
        {
            return await TranslateModelTextWithTokenRepairAsync(
                item, chunk, cancellationToken, diagnostics, chunkIndex, "chunk-retry").ConfigureAwait(false);
        }
        catch (LocalLmtOutputException failure)
            when (failure.Kind is TranslationDiagnosticKind.EmptyTranslation or TranslationDiagnosticKind.OutputTooLong)
        {
            StopSession();
            return await TranslateModelTextWithTokenRepairAsync(
                item, chunk, cancellationToken, diagnostics, chunkIndex, "chunk-retry-restarted").ConfigureAwait(false);
        }
    }

    private static TranslationChunkDiagnostic ChunkDiagnostic(
        int index,
        string source,
        string returned,
        string category,
        string stage = "chunk-retry") => new(
            index,
            source.Length,
            stage,
            category,
            ModelTokenPattern.Matches(source).Count,
            ModelTokenPattern.Matches(returned).Count);

    private static void AppendChunkDiagnosticIfChanged(
        List<TranslationChunkDiagnostic> diagnostics,
        TranslationChunkDiagnostic diagnostic)
    {
        if (diagnostics.LastOrDefault() != diagnostic) diagnostics.Add(diagnostic);
    }

    private static bool ShouldRetryBodyInChunks(LocalTranslationWorkItem item, TranslationResult result) =>
        !result.IsSuccess
        && item.LocalProtectedText.ModelText.Length > BodyRetryChunkTargetLengthFor(item.SourceLanguage)
        && (result.Diagnostic?.Kind is TranslationDiagnosticKind.EmptyTranslation or TranslationDiagnosticKind.OutputTooLong
            || item.Request.TextKind == TranslationTextKind.Body
                && (result.Status is SourceUnchangedStatus or SourceLanguageOnlyStatus or MissingTargetLanguageStatus
            || result.Diagnostic?.Kind is TranslationDiagnosticKind.ProtectedContentChanged
                or TranslationDiagnosticKind.ProtectedAccountChanged
                or TranslationDiagnosticKind.ProtectedUrlChanged
                or TranslationDiagnosticKind.ProtectedNumericChanged
                or TranslationDiagnosticKind.ProtectedOtherChanged));

    private static bool ShouldTranslateBodyInChunksBeforeFullAttempt(LocalTranslationWorkItem item) =>
        item.Request.TextKind == TranslationTextKind.Body
        && item.LocalProtectedText.HasModelTokens
        && item.LocalProtectedText.ModelText.Length > BodyRetryChunkTargetLengthFor(item.SourceLanguage);

    private static int BodyRetryChunkTargetLengthFor(string sourceLanguage) =>
        string.Equals(sourceLanguage, "Korean", StringComparison.OrdinalIgnoreCase)
            ? 36
            : BodyRetryChunkTargetLength;

    private static IReadOnlyList<string> SplitBodyModelText(string source, int targetLength)
    {
        if (source.Length <= targetLength) return [source];
        var chunks = new List<string>();
        var start = 0;
        while (start < source.Length)
        {
            var remaining = source.Length - start;
            if (remaining <= targetLength) break;
            var end = FindPreferredChunkEnd(source, start, targetLength);
            if (end <= start || end >= source.Length) break;
            chunks.Add(source[start..end]);
            start = end;
        }
        chunks.Add(source[start..]);
        return chunks;
    }

    private static int FindPreferredChunkEnd(string source, int start, int targetLength)
    {
        var ideal = Math.Min(start + targetLength, source.Length);
        var lower = Math.Min(start + targetLength * 3 / 5, source.Length);
        var upper = Math.Min(ideal + targetLength / 3, source.Length);
        foreach (var boundary in new Func<char, bool>[]
                 {
                     static value => value is '\r' or '\n',
                     static value => value is '.' or '?' or '!' or '\u3002' or '\uFF01' or '\uFF1F' or '\u2026',
                     char.IsWhiteSpace,
                 })
        {
            var end = FindPreferredBoundary(source, lower, upper, ideal, boundary);
            if (end > start) return MoveOutsideModelToken(source, start, end);
        }
        return MoveOutsideModelToken(source, start, ideal);
    }

    private static int FindPreferredBoundary(
        string source,
        int lower,
        int upper,
        int ideal,
        Func<char, bool> boundary)
    {
        var before = -1;
        var after = -1;
        for (var index = lower; index < upper; index++)
        {
            if (!boundary(source[index])) continue;
            var end = index + 1;
            if (end <= ideal) before = end;
            else if (after < 0) after = end;
        }
        if (before >= 0) return before;
        return after >= 0 ? after : before;
    }

    private static int MoveOutsideModelToken(string source, int start, int end)
    {
        foreach (Match token in ModelTokenPattern.Matches(source))
        {
            if (end <= token.Index || end >= token.Index + token.Length) continue;
            return token.Index > start ? token.Index : token.Index + token.Length;
        }
        return end;
    }

    private TranslationResult RestoreAndValidate(LocalTranslationWorkItem item, string translated)
        => RestoreAndValidateContent(item, translated) with
        {
            ExecutionEvidence = new("llama.cpp", Path.GetFileName(_options.ModelPath),
                _artifactsVerified == true ? _options.ExpectedModelSha256 : null, _backend.ToString())
        };

    private TranslationResult RestoreAndValidateContent(LocalTranslationWorkItem item, string translated)
    {
        if (string.IsNullOrWhiteSpace(translated))
            return new TranslationResult(false, string.Empty, "Local LMT returned empty translated text.");
        var invariantMismatch = TranslationContentIntegrity.FindMismatch(
            NormalizeNumericWordBoundaries(item.Request.OcrText),
            NormalizeNumericWordBoundaries(translated));
        if (invariantMismatch is not null)
            return new TranslationResult(
                false,
                string.Empty,
                "Local LMT changed a numeric, model, or hotkey invariant.",
                Diagnostic: LocalIntegrityDiagnostic(
                    InvariantDiagnosticKind(invariantMismatch.Value),
                    item.Request.OcrText,
                    translated));
        var protectedMismatch = item.LocalProtectedText.FindProtectedValueMismatch(translated);
        if (protectedMismatch is not null)
            return new TranslationResult(
                false,
                string.Empty,
                "Local LMT changed a protected local value.",
                Diagnostic: LocalIntegrityDiagnostic(
                    ProtectedDiagnosticKind(protectedMismatch.Value),
                    item.Request.OcrText,
                    translated));

        var restored = LocalDeterministicTranslation.LocalizeRelativeTime(item.Request, translated);
        var semanticFailure = GetSemanticFailure(item, item.Request.OcrText, restored);
        if (semanticFailure is not null)
            return new TranslationResult(false, string.Empty, semanticFailure);
        var residency = _session is { IsAlive: true } ? "; model kept resident locally" : string.Empty;
        return new TranslationResult(true, restored, $"Local LMT completed ({_backend}{residency})." );
    }

    private TranslationDiagnostic LocalIntegrityDiagnostic(
        TranslationDiagnosticKind kind,
        string source,
        string translated) => new(
        kind,
        TranslationDiagnosticStage.Initial,
        LocalLmtInputMode: _localLmtInputMode,
        IntegrityBoundary: "after-token-restore",
        ExpectedProtectedTokenCount: _currentProtectedTokenCount,
        RestoredProtectedTokenCount: _restoredProtectedTokenCount,
        SourceInvariantNumberCount: TranslationContentIntegrity.CountNumbers(NormalizeNumericWordBoundaries(source)),
        TranslatedInvariantNumberCount: TranslationContentIntegrity.CountNumbers(NormalizeNumericWordBoundaries(translated)));

    private TranslationResult WithLocalLmtContext(TranslationResult result) =>
        !_collectLocalDiagnosticContext || result.Diagnostic is null
            ? result
            : result with
            {
                Diagnostic = result.Diagnostic with
                {
                    LocalLmtInputMode = _localLmtInputMode,
                    ExpectedProtectedTokenCount = _currentProtectedTokenCount,
                    RestoredProtectedTokenCount = _restoredProtectedTokenCount,
                },
            };

    private static TranslationResult WithDiagnostic(
        TranslationResult result,
        TranslationDiagnosticStage stage,
        int? retryChunkIndex = null) =>
        result.IsSuccess
            ? result
            : result with
            {
                Diagnostic = result.Diagnostic is null
                    ? new TranslationDiagnostic(DiagnosticKindForStatus(result.Status), stage, retryChunkIndex)
                    : result.Diagnostic with { Stage = stage, RetryChunkIndex = retryChunkIndex },
            };

    private static TranslationResult Failure(
        string status,
        TranslationDiagnosticKind kind,
        TranslationDiagnosticStage stage,
        int? retryChunkIndex = null) =>
        new(false, string.Empty, status, Diagnostic: new TranslationDiagnostic(kind, stage, retryChunkIndex));

    private static TranslationResult OutputFailure(
        LocalLmtOutputException failure,
        TranslationDiagnosticStage stage,
        int? retryChunkIndex = null) =>
        Failure(
            "Local LMT model output failed validation.",
            failure.Kind,
            stage,
            retryChunkIndex) with
        {
            Diagnostic = new TranslationDiagnostic(
                failure.Kind,
                stage,
                retryChunkIndex,
                ModelResponseCharacterCount: failure.ResponseCharacterCount,
                MaximumAllowedResponseCharacterCount: failure.MaximumAllowedResponseCharacterCount,
                ModelResponseContainsSourceText: failure.ResponseContainsSourceText),
        };

    private static TranslationResult ProcessFailure(
        LocalLmtProcessFailureKind failure,
        TranslationDiagnosticStage stage,
        int? retryChunkIndex = null) =>
        Failure(
            "Local LMT failed without exposing private text.",
            LocalLmtProcessDiagnostics.ToDiagnosticKind(failure),
            stage,
            retryChunkIndex);

    private static TranslationDiagnosticKind DiagnosticKindForStatus(string status) => status switch
    {
        "Local LMT model is unavailable or failed verification." => TranslationDiagnosticKind.ModelUnavailable,
        "Local LMT returned empty translated text." => TranslationDiagnosticKind.EmptyTranslation,
        SourceUnchangedStatus => TranslationDiagnosticKind.SourceUnchanged,
        SourceLanguageOnlyStatus => TranslationDiagnosticKind.SourceLanguageOnly,
        MissingTargetLanguageStatus => TranslationDiagnosticKind.MissingTargetLanguage,
        "Local LMT changed a protected local value." => TranslationDiagnosticKind.ProtectedContentChanged,
        _ => TranslationDiagnosticKind.ProcessFailure,
    };

    private static TranslationDiagnosticKind InvariantDiagnosticKind(TranslationInvariantMismatch mismatch) => mismatch switch
    {
        TranslationInvariantMismatch.Number => TranslationDiagnosticKind.NumericInvariantChanged,
        TranslationInvariantMismatch.ModelToken => TranslationDiagnosticKind.ModelTokenInvariantChanged,
        TranslationInvariantMismatch.Hotkey => TranslationDiagnosticKind.HotkeyInvariantChanged,
        _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null),
    };

    private static TranslationDiagnosticKind ProtectedDiagnosticKind(LocalProtectedValueMismatch mismatch) => mismatch switch
    {
        LocalProtectedValueMismatch.Account => TranslationDiagnosticKind.ProtectedAccountChanged,
        LocalProtectedValueMismatch.Url => TranslationDiagnosticKind.ProtectedUrlChanged,
        LocalProtectedValueMismatch.Numeric => TranslationDiagnosticKind.ProtectedNumericChanged,
        LocalProtectedValueMismatch.Other => TranslationDiagnosticKind.ProtectedOtherChanged,
        _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null),
    };

    private static string? GetSemanticFailure(
        LocalTranslationWorkItem item,
        string source,
        string translated)
    {
        // A startup banner can reach any drawn region, including headings and
        // controls. Explicitly preservable names and identifiers remain exempt.
        if (!TranslationBlockContract.RequiresTargetScriptTranslation(item.Request))
        {
            return null;
        }

        if (string.Equals(source.Trim(), translated.Trim(), StringComparison.Ordinal)
            || HasSameMeaningfulCharacters(source, translated))
        {
            return SourceUnchangedStatus;
        }

        if (string.Equals(item.TargetLanguage, "Chinese", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.SourceLanguage, "Japanese", StringComparison.OrdinalIgnoreCase)
            && translated.Any(static value => value is >= '\u3040' and <= '\u30FF'))
        {
            return SourceLanguageOnlyStatus;
        }

        return ContainsTargetScript(translated, item.TargetLanguage)
            ? null
            : MissingTargetLanguageStatus;
    }

    private static bool HasSameMeaningfulCharacters(string source, string translated) =>
        string.Concat(source.Where(char.IsLetterOrDigit))
            .Equals(string.Concat(translated.Where(char.IsLetterOrDigit)), StringComparison.Ordinal);

    private static bool ContainsTargetScript(string text, string targetLanguage)
    {
        var target = targetLanguage.Trim().ToLowerInvariant();
        if (target.Contains("chinese", StringComparison.Ordinal) || target.Contains("zh", StringComparison.Ordinal))
            return text.Any(static value => value is >= '\u3400' and <= '\u9FFF');
        if (target is "english" or "en" || target.StartsWith("en-", StringComparison.Ordinal))
            return text.Any(static value => value is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
        if (target is "japanese" or "ja" || target.StartsWith("ja-", StringComparison.Ordinal))
            return text.Any(static value => value is >= '\u3040' and <= '\u30FF' or >= '\u3400' and <= '\u9FFF');
        if (target is "korean" or "ko" || target.StartsWith("ko-", StringComparison.Ordinal))
            return text.Any(static value => value is >= '\uAC00' and <= '\uD7AF');
        if (target is "russian" or "ru" || target.StartsWith("ru-", StringComparison.Ordinal))
            return text.Any(static value => value is >= '\u0400' and <= '\u052F');
        return true;
    }

    private static string BuildDirectPrompt(
        string sourceLanguage,
        string targetLanguage,
        string text,
        bool strictTokenCopy)
    {
        var source = NormalizeModelSource(text);
        var tokenInstruction = ModelTokenPattern.IsMatch(source)
            ? strictTokenCopy
                ? " Copy every protected token such as [[K0]] exactly once. Do not translate, spell out, omit, or duplicate it."
                : " Keep every token like [[K0]] unchanged."
            : string.Empty;
        return $"Translate the following text from {sourceLanguage} into {targetLanguage}:{tokenInstruction}\\n{sourceLanguage}: {source}\\n{targetLanguage}:";
    }

    private static string NormalizeModelSource(string value)
    {
        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CollapseWhitespace)
            .Where(static line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0) return string.Empty;

        var joined = new StringBuilder(lines[0]);
        foreach (var line in lines.Skip(1))
        {
            if (!IsUnspacedCjkTextCharacter(joined[^1]) || !IsUnspacedCjkTextCharacter(line[0])) joined.Append(' ');
            joined.Append(line);
        }
        return joined.ToString();
    }

    private static bool IsUnspacedCjkTextCharacter(char value) =>
        value is >= '\u3040' and <= '\u30FF'
        or >= '\u3400' and <= '\u9FFF'
        or >= '\uFF66' and <= '\uFF9D';

    private static string CollapseWhitespace(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    private static string NormalizeModelResponse(string response, string source, string targetLanguage)
    {
        var target = Regex.Escape(targetLanguage);
        var normalized = SessionControlLinePattern.Replace(response.Replace("\r", string.Empty, StringComparison.Ordinal), string.Empty).Trim();
        normalized = RemoveOutputOnlyPromptEcho(normalized, source);
        var labels = Regex.Matches(
            normalized,
            $@"(?:^|\s)(?:{target}|中文|简体中文)\s*[:：]\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (labels.Count > 0)
        {
            var answer = normalized[(labels[^1].Index + labels[^1].Length)..].Trim();
            if (answer.Length > 0) normalized = answer;
        }
        normalized = Regex.Replace(normalized, $@"^(?:{target}|中文|简体中文)\s*[:：]\s*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, $@"\s*(?:{target}|中文|简体中文)\s*[:：]\s*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        var maximumLength = MaximumResponseLength(source);
        if (normalized.Length <= maximumLength) return normalized;
        return FirstBoundedResponseLine(normalized, maximumLength);
    }

    private static string RemoveOutputOnlyPromptEcho(string response, string source)
    {
        var sourceMarker = $"Text: {NormalizeModelSource(source)}";
        var markerIndex = response.LastIndexOf(sourceMarker, StringComparison.Ordinal);
        if (markerIndex < 0) return response;
        var prefix = response[..markerIndex];
        if (!prefix.Contains("Translate from ", StringComparison.Ordinal)
            || !prefix.Contains("Output only the translated text without any language label.", StringComparison.Ordinal))
        {
            return response;
        }
        return response[(markerIndex + sourceMarker.Length)..].Trim();
    }

    private static string FirstBoundedResponseLine(string response, int maximumLength)
    {
        var first = response
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return first is { Length: > 0 } && first.Length <= maximumLength ? first : string.Empty;
    }

    private static int MaximumResponseLength(string source) => Math.Max(128, CollapseWhitespace(source).Length * 6);

    private static bool IsTargetLabelOnlyResponse(string response, string targetLanguage)
    {
        var target = Regex.Escape(targetLanguage);
        return Regex.IsMatch(
            response.Trim(),
            $@"^(?:{target}|中文|简体中文)\s*[:：]\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string PreserveBoundaryWhitespace(string source, string translated)
    {
        var leading = source.TakeWhile(char.IsWhiteSpace).ToArray();
        var trailing = source.Reverse().TakeWhile(char.IsWhiteSpace).Reverse().ToArray();
        return new string(leading) + translated.Trim() + new string(trailing);
    }

    private static string NormalizeNumericWordBoundaries(string value) =>
        Regex.Replace(value, @"(?<=[\p{L}])(?=\d)|(?<=\d)(?=[\p{L}])", " ");

    private void ArmIdleTimer()
    {
        if (!_disposed && _session is not null)
            _idleTimer.Change(_idleShutdown, Timeout.InfiniteTimeSpan);
    }

    private void StopIdleSession(object? state)
    {
        if (_disposed || !_translationGate.Wait(0))
        {
            if (!_disposed) _idleTimer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
            return;
        }
        try
        {
            if (IsIdleShutdownDue(DateTimeOffset.UtcNow)) StopSession();
            else ArmIdleTimer();
        }
        finally
        {
            _translationGate.Release();
        }
    }

    private ArtifactSnapshot CaptureArtifactSnapshot() => new(
        FileSnapshot.Capture(_options.ModelPath), FileSnapshot.Capture(_options.LlamaCliPath));

    private sealed record ArtifactSnapshot(FileSnapshot? Model, FileSnapshot? Runtime);
    private sealed record FileSnapshot(long Length, DateTime Written, DateTime Created)
    {
        public static FileSnapshot? Capture(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var file = new FileInfo(path);
            return file.Exists ? new(file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc) : null;
        }
    }

    private void StopSession()
    {
        var session = Interlocked.Exchange(ref _session, null);
        session?.Dispose();
        _backend = string.Empty;
    }

    private sealed record LocalTranslationWorkItem(
        int Index,
        TranslationRequest Request,
        string SourceLanguage,
        string TargetLanguage,
        LocalProtectedTranslationText LocalProtectedText);
}

internal static class LocalDeterministicTranslation
{
    private static readonly Dictionary<string, string> ChineseUiTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Settings"] = "设置",
        ["Search"] = "搜索",
        ["Show translation"] = "显示翻译",
        ["Home"] = "主页",
        ["Explore"] = "探索",
        ["Notifications"] = "通知",
        ["Follow"] = "关注",
        ["Following"] = "正在关注",
        ["Followers"] = "关注者",
        ["Bookmarks"] = "书签",
        ["Chat"] = "聊天",
        ["Post"] = "发布",
        ["Premium"] = "高级版",
        ["Creator Studio"] = "创作者工作室",
        ["Profile"] = "个人资料",
        ["More"] = "更多",
        ["Show more"] = "显示更多",
        ["History"] = "历史记录",
        ["Who to follow"] = "推荐关注",
        ["What's happening"] = "正在发生什么",
        ["Posts"] = "帖子",
        ["Replies"] = "回复",
        ["Highlights"] = "精选",
        ["Media"] = "媒体",
        ["Pinned"] = "置顶",
        ["Memes"] = "表情包",
    };
    private static readonly Regex RelativeTimeOnlyPattern = new(
        @"^(\d+)(mo|[smhdwy])$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WordedRelativeTimePattern = new(
        @"^(\d+)\s+(seconds?|minutes?|hours?|days?|weeks?|months?|years?)\s+ago$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryTranslate(
        TranslationRequest request,
        string sourceLanguage,
        string targetLanguage,
        out string translated)
    {
        translated = string.Empty;
        if (!string.Equals(targetLanguage, "Chinese", StringComparison.OrdinalIgnoreCase)) return false;
        if (request.TextKind is TranslationTextKind.BrandOrIdentifier or TranslationTextKind.Hashtag)
        {
            translated = request.OcrText;
            return true;
        }

        var compact = request.OcrText.Trim();
        if (request.TextKind == TranslationTextKind.UiControl
            && TryTranslateKnownUiTerm(compact, out translated)) return true;
        if (request.TextKind == TranslationTextKind.Heading
            && ChineseUiTerms.TryGetValue(compact, out var uiTranslation))
        {
            translated = uiTranslation;
            return true;
        }
        if (request.TextKind == TranslationTextKind.UiControl
            && compact.Contains('_')
            && !compact.Any(char.IsWhiteSpace))
        {
            translated = request.OcrText;
            return true;
        }
        return TryLocalizeRelativeExpression(compact, targetLanguage, out translated);
    }

    public static bool TryTranslateKnownUiTerm(string text, out string translated)
    {
        var compact = text.Trim().Replace('\u2019', '\'');
        if (IsShowTranslationControl(compact))
        {
            translated = "显示翻译";
            return true;
        }
        return ChineseUiTerms.TryGetValue(compact, out translated!);
    }

    private static bool IsShowTranslationControl(string value)
    {
        const string label = "Show translation";
        if (!value.StartsWith(label, StringComparison.OrdinalIgnoreCase)) return false;
        return value[label.Length..].All(static character => !char.IsLetterOrDigit(character));
    }

    public static bool IsRelativeTimeExpression(string value) =>
        RelativeTimeOnlyPattern.IsMatch(value) || WordedRelativeTimePattern.IsMatch(value);

    public static bool TryLocalizeRelativeExpression(string value, string targetLanguage, out string translated)
    {
        translated = string.Empty;
        if (!string.Equals(targetLanguage, "Chinese", StringComparison.OrdinalIgnoreCase)) return false;
        var compact = RelativeTimeOnlyPattern.Match(value);
        if (compact.Success)
        {
            translated = FormatRelativeTime(compact.Groups[1].Value, compact.Groups[2].Value);
            return translated.Length > 0;
        }
        var worded = WordedRelativeTimePattern.Match(value);
        if (!worded.Success) return false;
        var unit = worded.Groups[2].Value.ToLowerInvariant() switch
        {
            "second" or "seconds" => "s",
            "minute" or "minutes" => "m",
            "hour" or "hours" => "h",
            "day" or "days" => "d",
            "week" or "weeks" => "w",
            "month" or "months" => "mo",
            "year" or "years" => "y",
            _ => string.Empty,
        };
        translated = FormatRelativeTime(worded.Groups[1].Value, unit);
        return translated.Length > 0;
    }

    public static string LocalizeRelativeTime(TranslationRequest request, string translated)
    {
        if (request.TextKind != TranslationTextKind.IdentityMetadata) return translated;
        var sourceAge = Regex.Match(
            request.OcrText,
            @"(?<![\p{L}\p{N}_])(\d+)(mo|[smhdwy])\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!sourceAge.Success) return translated;
        var localized = FormatRelativeTime(sourceAge.Groups[1].Value, sourceAge.Groups[2].Value);
        if (localized.Length == 0) return translated;
        var amount = sourceAge.Groups[1].Value;
        var targetAgePattern = $@"(?<!\d){Regex.Escape(amount)}\s*(?:秒|分钟|分鐘|分|小时|小時|天|周|週|星期|个月|個月|月|年|mo|[smhdwy])(?:前)?";
        var matches = Regex.Matches(translated, targetAgePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (matches.Count == 0) return translated;
        var match = matches[^1];
        return translated[..match.Index] + localized + translated[(match.Index + match.Length)..];
    }

    private static string FormatRelativeTime(string amount, string unit) => unit.ToLowerInvariant() switch
    {
        "s" => amount + "秒前",
        "m" => amount + "分钟前",
        "h" => amount + "小时前",
        "d" => amount + "天前",
        "w" => amount + "周前",
        "mo" => amount + "个月前",
        "y" => amount + "年前",
        _ => string.Empty,
    };
}

internal sealed class LocalProtectedTranslationText
{
    private static readonly Regex ProtectedPattern = new(
        @"[\(（][^()（）\r\n]{1,32}[\)）][^\s]{0,8}|(?<![\p{L}\p{N}_])\d+\s+(?i:seconds?|minutes?|hours?|days?|weeks?|months?|years?)\s+ago\b|\b\d+(?:[.,]\d+)*(?:[KMBT])\b|(?<![\p{L}\p{N}_])\d+(?:mo|[smhdwy])\b|\{\{[^{}]+\}\}|\{[^{}]+\}|</?[^>]+>|(?<![\p{L}\p{N}_])@[A-Za-z0-9_][A-Za-z0-9_.-]*|%(?:\d+\$)?[a-zA-Z]|https?://[^\s]+|www\.[^\s]+|#[\p{L}\p{N}_]+|\b(?i:OpenAI|DeepSeek|ClipRay|Codex|GitHub|Windows|X\.com)\b|(?<![A-Za-z0-9_])\d+(?:[.,×x]\d+)*(?![A-Za-z0-9_])|\b[A-Za-z][A-Za-z0-9]*(?:[._-]\d+)+\b|\b(?i:Ctrl|Alt|Shift|Win)(?:\+[A-Za-z0-9]+)+\b|\b(?i:F)(?:[1-9]|1\d|2[0-4])\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<string> _tokens;
    private readonly IReadOnlyList<string> _values;
    private readonly IReadOnlyList<string> _unmaskedProtectedValues;

    private LocalProtectedTranslationText(
        IReadOnlyList<LocalTranslationSegment> segments,
        string modelText,
        IReadOnlyList<string> tokens,
        IReadOnlyList<string> values,
        IReadOnlyList<string> unmaskedProtectedValues)
    {
        Segments = segments;
        ModelText = modelText;
        _tokens = tokens;
        _values = values;
        _unmaskedProtectedValues = unmaskedProtectedValues;
    }

    public IReadOnlyList<LocalTranslationSegment> Segments { get; }
    public string ModelText { get; }
    public bool HasModelTokens => _tokens.Count > 0;
    public int ModelTokenCount => _tokens.Count;
    public string ReconstructedText => string.Concat(Segments.Select(static segment => segment.Text));
    public bool HasTranslatableText => Segments.Any(static segment =>
        !segment.IsProtected && segment.Text.Any(char.IsLetter));

    public bool HasProtectedValueIntegrity(string translated) => Segments
        .Where(static segment => segment.IsProtected && segment.Text.Length > 0)
        .All(segment => CountOccurrences(translated, segment.Text) == CountOccurrences(ReconstructedText, segment.Text));

    public LocalProtectedValueMismatch? FindProtectedValueMismatch(string translated)
    {
        foreach (var segment in Segments.Where(static segment => segment.IsProtected && segment.Text.Length > 0))
        {
            if (CountOccurrences(translated, segment.Text) == CountOccurrences(ReconstructedText, segment.Text)) continue;
            return ClassifyProtectedValue(segment.Text);
        }
        return null;
    }

    public LocalProtectedValueMismatch FindMissingTokenMismatch(string translated)
    {
        for (var index = 0; index < _tokens.Count; index++)
        {
            if (CountOccurrences(translated, _tokens[index]) != 1)
                return ClassifyProtectedValue(_values[index]);
        }
        return LocalProtectedValueMismatch.Other;
    }

    public bool HasModelTokenIntegrity(string source, string translated) => _tokens.All(token =>
        CountOccurrences(source, token) == CountOccurrences(translated, token));

    public bool TryRestoreModelText(string source, string translated, out string restored)
    {
        if (!HasModelTokenIntegrity(source, translated))
        {
            restored = string.Empty;
            return false;
        }

        restored = translated;
        var expected = source;
        for (var index = 0; index < _tokens.Count; index++)
        {
            if (CountOccurrences(source, _tokens[index]) == 0) continue;
            restored = restored.Replace(_tokens[index], _values[index], StringComparison.Ordinal);
            expected = expected.Replace(_tokens[index], _values[index], StringComparison.Ordinal);
        }

        var candidate = restored;
        return Segments
            .Where(static segment => segment.IsProtected && segment.Text.Length > 0)
            .All(segment => CountOccurrences(expected, segment.Text) == CountOccurrences(candidate, segment.Text));
    }

    public bool HasTranslatableModelText(string source)
    {
        foreach (var token in _tokens)
            source = source.Replace(token, string.Empty, StringComparison.Ordinal);
        foreach (var value in _unmaskedProtectedValues)
            source = source.Replace(value, string.Empty, StringComparison.Ordinal);
        return source.Any(char.IsLetter);
    }

    public string RemoveInstructionEcho(string translated)
    {
        var cleaned = Regex.Replace(
            translated,
            @"^\s*(?:The previous answer was not translated\.\s*Return only a complete translation\.|之前的答案没有翻译[。.]\s*只(?:需|要)?返回完整(?:的)?翻译[。.]?)\s*",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(
            cleaned,
            @"^\s*(?:(?:Keep|Preserve|Copy)\s+every\s+(?:protected\s+)?token\s+(?:like|such as)\s+\[\[[KPQZ]\d+\]\][^。.!！]*|(?:请)?(?:保持每个(?:标记|令牌)不变|将每个(?:标记|令牌)保持不变|保留每个(?:标记|令牌)(?:不变)?|复制每个受保护的(?:标记|令牌))\s*[，,]?\s*(?:例如|如|像)\s*\[\[[KPQZ]\d+\]\][^。.!！]*)[。.!！]\s*",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static LocalProtectedTranslationText Create(string source, string targetLanguage)
    {
        var segments = new List<LocalTranslationSegment>();
        var modelText = new StringBuilder();
        var tokens = new List<string>();
        var values = new List<string>();
        var unmaskedProtectedValues = new List<string>();
        var cursor = 0;
        foreach (Match match in ProtectedPattern.Matches(source))
        {
            if (match.Index > cursor)
            {
                var natural = source[cursor..match.Index];
                segments.Add(new LocalTranslationSegment(natural, false));
                modelText.Append(natural);
            }
            var isRelativeTime = LocalDeterministicTranslation.IsRelativeTimeExpression(match.Value);
            var protectedText = LocalDeterministicTranslation.TryLocalizeRelativeExpression(
                match.Value,
                targetLanguage,
                out var localized)
                ? localized
                : match.Value;
            segments.Add(new LocalTranslationSegment(protectedText, !isRelativeTime || localized.Length > 0));
            if (ShouldUseModelToken(match.Value, isRelativeTime))
            {
                var token = CreateModelToken(source, tokens.Count);
                tokens.Add(token);
                values.Add(protectedText);
                modelText.Append(token);
            }
            else
            {
                modelText.Append(protectedText);
                unmaskedProtectedValues.Add(protectedText);
            }
            cursor = match.Index + match.Length;
        }
        if (cursor < source.Length)
        {
            var natural = source[cursor..];
            segments.Add(new LocalTranslationSegment(natural, false));
            modelText.Append(natural);
        }
        return new LocalProtectedTranslationText(
            segments,
            modelText.ToString(),
            tokens,
            values,
            unmaskedProtectedValues);
    }

    public bool TryRestore(string translated, out string restored)
    {
        for (var index = 0; index < _tokens.Count; index++)
        {
            if (CountOccurrences(translated, _tokens[index]) != 1)
            {
                restored = string.Empty;
                return false;
            }
            translated = translated.Replace(_tokens[index], _values[index], StringComparison.Ordinal);
        }
        restored = translated;
        return true;
    }

    private static LocalProtectedValueMismatch ClassifyProtectedValue(string value)
    {
        if (value.StartsWith('@')) return LocalProtectedValueMismatch.Account;
        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return LocalProtectedValueMismatch.Url;
        return value.Any(char.IsDigit)
            ? LocalProtectedValueMismatch.Numeric
            : LocalProtectedValueMismatch.Other;
    }

    private static bool ShouldUseModelToken(string value, bool isRelativeTime) =>
        value is not "·" and not "•" and not "｜" and not "|";

    private static string CreateModelToken(string source, int index)
    {
        foreach (var prefix in new[] { "K", "P", "Q", "Z" })
        {
            var token = $"[[{prefix}{index}]]";
            if (!source.Contains(token, StringComparison.Ordinal)) return token;
        }
        throw new InvalidOperationException("Local LMT source contains every supported protected token form.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }
}

internal enum LocalProtectedValueMismatch
{
    Account,
    Url,
    Numeric,
    Other,
}

internal sealed class LocalProtectedTokenMismatchException(LocalProtectedValueMismatch mismatch) : Exception
{
    public LocalProtectedValueMismatch Mismatch { get; } = mismatch;
}

internal sealed record LocalTranslationSegment(string Text, bool IsProtected);

internal static class LocalLmtPromptEchoGuard
{
    private static readonly Regex StandaloneTokenDirective = new(
        @"^\s*(?:(?:Keep|Preserve|Copy)\s+every\s+(?:protected\s+)?token\b.{0,60}(?:unchanged|exactly once)|(?:请)?(?:保持|保留|复制).{0,12}(?:令牌|标记).{0,16}(?:不变|原样|一次))\s*[。.!！]?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TranslatedTokenExampleDirective = new(
        @"^\s*(?:请)?(?:保持|保留|复制)每个(?:令牌|标记)的?值?\s*(?:保持)?不变\s*[，,。.]?\s*(?:例如|比如|如|像)\s*[A-Za-z0-9_@.· :/\-]{1,80}$|^\s*保持每个元素的\s+\S+(?:\s+保持不变)[。.]?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SourceIsTokenDirective = new(
        @"(?:Keep|Preserve|Copy).{0,30}(?:token|element).{0,30}(?:unchanged|exactly once)|(?:保持|保留|复制).{0,20}(?:令牌|标记|元素).{0,20}(?:不变|原样|一次)|(?:mant[eé]n|conserva).{0,25}token.{0,25}(?:sin cambios|intact)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex[] PromptHeaderPatterns =
    [
        new(@"^\s*Translate the following (?:complete )?(?:social-media )?(?:passage|text) from \p{L}+ into (?:natural )?\p{L}+", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"^\s*(?:请)?(?:将|把)(?:以下|下列).{0,40}(?:从.{1,20})?(?:翻译|译成|译为).{0,20}(?:中文|汉语|英文|英语|日文|日语)", RegexOptions.Compiled | RegexOptions.CultureInvariant),
    ];

    private static readonly Regex[] InstructionFragmentPatterns =
    [
        new(@"(?:Treat\s+)?(?:wrapped|line-wrapped|换行|折行).{0,18}(?:one paragraph|as a paragraph|一个段落|一段)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(?:Translate|翻译).{0,24}(?:human-readable|human language|可读|人类语言).{0,16}(?:phrase|短语)?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(?:including|include|包括).{0,10}(?:slang|俚语)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(?:Preserve|保留).{0,24}(?:Latin|拉丁).{0,20}(?:account|name|账户|账号|名称)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(?:Keep|Preserve|保持|保留).{0,40}(?:token|令牌|标记).{0,18}(?:unchanged|不变)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(?:Output only|仅输出|只输出).{0,20}(?:translation|translated text|译文|翻译)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    ];

    public static bool IsLikelyEcho(string translated, string source) =>
        ((StandaloneTokenDirective.IsMatch(translated) || TranslatedTokenExampleDirective.IsMatch(translated))
            && !SourceIsTokenDirective.IsMatch(source))
        || PromptHeaderPatterns.Any(pattern => pattern.IsMatch(translated))
        || InstructionFragmentPatterns.Count(pattern => pattern.IsMatch(translated)) >= 2;
}
