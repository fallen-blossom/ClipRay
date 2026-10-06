using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using ClipRay.App.Translation;

// Test-only, isolated loopback server for pinned Qwen / Hy-MT2 candidates. No production route or cloud fallback.
internal sealed class QwenHttpWorkbenchProbe : IDisposable
{
    private const string ModelHash = "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597";
    private const string HyMt2Hash = "5c3fe0b1408a5ceb0143184ef247b11b579c525f4b02b060e6c851bb76fef1a4";
    private readonly bool _hyMt2;
    private readonly bool _sendScene;
    private readonly string _expectedHash;
    private readonly string _output, _model;
    private readonly Process _process;
    private readonly HttpClient _client;
    private readonly Task<string> _stdout, _stderr;
    private bool _ready, _disposed;
    private int _next;
    private readonly bool _protectSource;
    public QwenHttpWorkbenchProbe(string model, string server, string output, bool protectSource = false, bool hyMt2 = false, bool sendScene = false)
    {
        _hyMt2 = hyMt2;
        _sendScene = sendScene;
        _expectedHash = hyMt2 ? HyMt2Hash : ModelHash;
        using var stream = File.OpenRead(model);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(_expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate model hash mismatch");
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory");
        Directory.CreateDirectory(output);
        _output = output; _model = model; _protectSource = protectSource;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(60) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var start = new ProcessStartInfo(Path.GetFullPath(server)) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["LLAMA_API_KEY"] = key;
        foreach (var arg in new[] { "--model", Path.GetFullPath(model), "--host", "127.0.0.1", "--port", port.ToString(),
            "--no-webui", "--parallel", "1", "--ctx-size", "2048", "--gpu-layers", "20", "--cache-ram", "0" })
            start.ArgumentList.Add(arg);
        try { _process = Process.Start(start) ?? throw new IOException("Server start failed"); }
        catch { _client.Dispose(); throw; }
        _process.StandardInput.Close();
        _stdout = _process.StandardOutput.ReadToEndAsync(); _stderr = _process.StandardError.ReadToEndAsync();
    }
    private async Task ReadyAsync(CancellationToken token)
    {
        if (_ready) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var timer = Stopwatch.StartNew();
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (_process.HasExited) throw new IOException("Server exited before ready");
            try
            {
                using var health = await _client.GetAsync("health", timeout.Token).ConfigureAwait(false);
                if (health.IsSuccessStatusCode) break;
            }
            catch (HttpRequestException) { }
            await Task.Delay(200, timeout.Token).ConfigureAwait(false);
        }
        using var anonymous = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        using var denied = await anonymous.PostAsJsonAsync(new Uri(_client.BaseAddress!, "v1/chat/completions"), new { }, timeout.Token).ConfigureAwait(false);
        if (denied.StatusCode != HttpStatusCode.Unauthorized) throw new InvalidDataException("Server did not reject anonymous request");
        await File.WriteAllTextAsync(Path.Combine(_output, "server-start.json"), JsonSerializer.Serialize(new
            { ReadinessWaitMs = timer.ElapsedMilliseconds, AnonymousRejected = true, ProcessId = _process.Id }), timeout.Token).ConfigureAwait(false);
        _ready = true;
    }
    public async Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(IReadOnlyList<TranslationRequest> requests, CancellationToken token)
    {
        try
        {
            await ReadyAsync(token).ConfigureAwait(false);
            var results = new List<TranslationResult>();
            foreach (var request in requests)
            {
                var index = ++_next;
                var prompt = _hyMt2 ? BuildHyMt2Prompt(request, _protectSource, _sendScene) : _protectSource ? QwenWorkbenchProbe.BuildProtectedPrompt(request) : QwenWorkbenchProbe.BuildPrompt(request, true);
                await File.WriteAllTextAsync(Path.Combine(_output, $"{index}.prompt.txt"), prompt, token).ConfigureAwait(false);
                var timer = Stopwatch.StartNew();
                using var response = await _client.PostAsJsonAsync("v1/chat/completions", new
                { messages = new[] { new { role = "user", content = prompt } }, temperature = _hyMt2 ? 0.7 : 0, seed = 0,
                    top_p = _hyMt2 ? 0.6 : 1.0, top_k = _hyMt2 ? 20 : 40, repeat_penalty = _hyMt2 ? 1.05 : 1.0,
                    max_tokens = 512, stream = false, cache_prompt = false }, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var raw = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(_output, $"{index}.response.json"), raw, token).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(_output, $"{index}.timing.json"), JsonSerializer.Serialize(new { RequestMilliseconds = timer.ElapsedMilliseconds, Candidate = _hyMt2 ? "Hy-MT2-1.8B-Q8_0" : "Qwen3-4B-Q4_K_M", Sampling = _hyMt2 ? "temperature=0.7,top_p=0.6,top_k=20,repeat_penalty=1.05,seed=0,max_tokens=512" : "greedy", ProtectSource = _protectSource }), token).ConfigureAwait(false);
                using var json = JsonDocument.Parse(raw);
                var choice = json.RootElement.GetProperty("choices")[0];
                var text = choice.GetProperty("message").GetProperty("content").GetString() ?? "";
                var complete = choice.GetProperty("finish_reason").GetString() == "stop" && !string.IsNullOrWhiteSpace(text);
                results.Add(new(complete, complete ? text : "", complete ? "candidate-http-unreviewed" : "candidate-http-incomplete",
                    ExecutionEvidence: new("llama.cpp-http-test", Path.GetFileName(_model), _expectedHash, "20 GPU layers requested")));
            }
            return results;
        }
        catch (Exception error)
        {
            try { File.WriteAllText(Path.Combine(_output, "probe-error.txt"), error.ToString()); }
            finally { Dispose(); }
            throw;
        }
    }
    private static string BuildHyMt2Prompt(TranslationRequest request, bool protectSource, bool sendScene)
    {
        // Official default template; no oracle criteria, reference answers or private context.
        var language = request.TargetLanguage switch
        {
            "zh" or "zh-CN" or "zh-Hans" or "zh-Hans-CN" or "简体中文" => "简体中文", "en" => "英语", "ja" => "日语",
            "ko" => "韩语", "es" => "西班牙语", _ => throw new ArgumentException("Unreviewed candidate target language")
        };
        var literals = protectSource ? LocalProtectedTranslationText.Create(request.OcrText, request.TargetLanguage).Segments
            .Where(segment => segment.IsProtected && segment.Text.Any(char.IsLetter)
                && request.OcrText.Contains(segment.Text, StringComparison.Ordinal))
            .Select(segment => segment.Text).Distinct(StringComparer.Ordinal).ToArray() : [];
        var terms = literals.Length == 0 ? "" : "参考下面的翻译：\n" + string.Join("\n", literals.Select(value => $"{value} 翻译成 {value}")) + "\n";
        return sendScene
            ? terms + $"【背景信息】\n{request.Scene}\n\n请结合背景信息将以下文本翻译为{language}。只需要输出翻译后的结果，不要额外解释。\n\n【待翻译文本】\n{request.OcrText}"
            : terms + $"将以下文本翻译为{language}，注意只需要输出翻译后的结果，不要额外解释：\n{request.OcrText}";
    }
    internal static async Task<int> RunSyntheticAsync(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("model server fixture fresh-output required");
        using var fixture = JsonDocument.Parse(File.ReadAllText(args[3]));
        using var probe = new QwenHttpWorkbenchProbe(args[1], args[2], args[4],
            protectSource: args[0] != "--candidate-hymt2-synthetic", hyMt2: args[0].StartsWith("--candidate-hymt2-", StringComparison.Ordinal),
            sendScene: args[0] == "--candidate-hymt2-context-synthetic");
        static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
        File.WriteAllText(Path.Combine(args[4], "input-manifest.json"), JsonSerializer.Serialize(new
        {
            FixtureSha256 = HashFile(args[3]), ServerSha256 = HashFile(args[2]), ModelSha256 = probe._expectedHash,
            TestAssemblySha256 = HashFile(typeof(QwenHttpWorkbenchProbe).Assembly.Location),
            AppAssemblySha256 = HashFile(typeof(TranslationRequest).Assembly.Location),
            SceneSent = !probe._hyMt2 || probe._sendScene, SourceLiterals = probe._protectSource,
            Scope = "Input identity and execution only; semantic review is separate"
        }));
        var records = new List<object>();
        foreach (var item in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            // Read source and declared scene only. Never read semanticCriteria or supplied term answers.
            var request = new TranslationRequest(item.GetProperty("sourceLanguage").GetString()!,
                item.GetProperty("targetLanguage").GetString()!, item.GetProperty("sourceText").GetString()!,
                textKind: TranslationTextKind.Body, scene: item.GetProperty("sourceMetadata").GetProperty("scene").GetString()!);
            var result = (await probe.TranslateManyAsync([request], CancellationToken.None).ConfigureAwait(false))[0];
            records.Add(new { Id = item.GetProperty("id").GetString(), result.IsSuccess, result.Text, result.Status });
        }
        File.WriteAllText(Path.Combine(args[4], "synthetic-results.json"), JsonSerializer.Serialize(records));
        return 0; // Execution only. No semantic pass claim.
    }

    internal static async Task<int> VerifyCancellationAsync(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("model server fresh-output required");
        using var probe = new QwenHttpWorkbenchProbe(args[1], args[2], args[3], hyMt2: args[0] == "--candidate-hymt2-cancel-probe");
        await probe.ReadyAsync(CancellationToken.None).ConfigureAwait(false);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var timer = Stopwatch.StartNew();
        try
        {
            await probe.TranslateManyAsync([new TranslationRequest("en", "zh",
                string.Join(" ", Enumerable.Repeat("The user must keep the original file until all requests finish.", 90)))], cancel.Token).ConfigureAwait(false);
            throw new InvalidDataException("Cancellation probe unexpectedly completed");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (!probe._disposed || !File.Exists(Path.Combine(args[3], "server-stop.json")))
                throw new InvalidDataException("Cancelled server was not disposed");
            File.WriteAllText(Path.Combine(args[3], "cancellation.json"), JsonSerializer.Serialize(new
                { Cancelled = true, ElapsedMs = timer.ElapsedMilliseconds, Scope = "request cancellation and owned process exit; not UI acceptance" }));
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            if (!_process.WaitForExit(10000)) throw new TimeoutException("Server did not exit");
            File.WriteAllText(Path.Combine(_output, "server.stdout.log"), _stdout.GetAwaiter().GetResult());
            File.WriteAllText(Path.Combine(_output, "server.stderr.log"), _stderr.GetAwaiter().GetResult());
            File.WriteAllText(Path.Combine(_output, "server-stop.json"), JsonSerializer.Serialize(new { ProcessId = _process.Id, Exited = _process.HasExited }));
        }
        finally { _client.Dispose(); _process.Dispose(); }
    }
}
