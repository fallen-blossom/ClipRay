using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using ClipRay.App.Translation;

// Experimental adapter only. No production routing, model substitution or cloud fallback.
internal sealed class QwenWorkbenchProbe
{
    private const string ExpectedHash = "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597";
    private readonly string _model, _cli, _output;
    private int _next;
    private readonly bool _includeContext;
    public QwenWorkbenchProbe(string model, string cli, string output, bool includeContext = false)
    {
        using var stream = File.OpenRead(model);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(ExpectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate hash mismatch");
        _model = Path.GetFullPath(model); _cli = Path.GetFullPath(cli); _output = output; _includeContext = includeContext;
        if (Directory.Exists(output)) throw new IOException("Use a fresh candidate output directory");
        Directory.CreateDirectory(output);
    }
    internal static string BuildPrompt(TranslationRequest request, bool includeContext)
    {
        return $"Translate the following text from {LocalLmtLanguage.Normalize(request.SourceLanguage)} into {request.TargetLanguage}. "
                + "Return only the translation. Preserve product names and usernames.\n"
                + (includeContext ? $"Source text type: {request.TextKind}. Scene: {request.Scene}.\n{request.TranslationHint}\n" : "")
                + "\n" + request.OcrText;
    }

    internal static string BuildProtectedPrompt(TranslationRequest request)
    {
        var values = LocalProtectedTranslationText.Create(request.OcrText, request.TargetLanguage).Segments
            .Where(segment => segment.IsProtected && segment.Text.Any(char.IsLetter)
                && request.OcrText.Contains(segment.Text, StringComparison.Ordinal))
            .Select(segment => segment.Text).Distinct(StringComparer.Ordinal).ToArray();
        return $"Translate the following text from {LocalLmtLanguage.Normalize(request.SourceLanguage)} into {request.TargetLanguage}. "
            + "Return only the translation, without explanation. Preserve who performs each action, who receives it, "
            + "negation, conditions, comparisons, quantities and uncertainty. Do not add, omit or summarize claims.\n"
            + $"Source text type: {request.TextKind}. Scene: {request.Scene}.\n"
            + (values.Length == 0 ? "" : "Keep these source literals exactly: " + System.Text.Json.JsonSerializer.Serialize(values) + ".\n")
            + "\n" + request.OcrText;
    }

    internal static bool HasTruncatedEcho(string raw, string prompt)
    {
        var normalized = raw.Replace("\r", "", StringComparison.Ordinal);
        return !normalized.Contains(prompt.Replace("\r", "", StringComparison.Ordinal), StringComparison.Ordinal)
            && normalized.Contains(" ... (truncated)", StringComparison.Ordinal);
    }

    internal static void VerifyTransportGuard()
    {
        const string prompt = "Translate this complete input.";
        if (!HasTruncatedEcho("Loading model...\r\n> Translate this\r\n ... (truncated)\r\n译文", prompt)
            || HasTruncatedEcho("> " + prompt + "\n译文 ... (truncated)", prompt)
            || HasTruncatedEcho("译文", prompt))
            throw new InvalidDataException("Candidate truncated echo guard regression");
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(IReadOnlyList<TranslationRequest> requests, CancellationToken token)
    {
        var results = new List<TranslationResult>();
        foreach (var request in requests)
        {
            var index = ++_next;
            var prompt = BuildPrompt(request, _includeContext);
            var path = Path.Combine(_output, $"{index}.prompt.txt");
            await File.WriteAllTextAsync(path, prompt, token);
            var start = new ProcessStartInfo(_cli) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
            foreach (var arg in new[] { "--model", _model, "--conversation", "--single-turn", "--no-escape",
                "--no-display-prompt", "--no-warmup", "--simple-io", "--log-disable", "--temp", "0",
                "--ctx-size", "2048", "--predict", "512", "--reasoning-budget", "0", "--gpu-layers", "20", "--file", path })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Candidate process failed to start");
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(180));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            var raw = await stdout;
            await File.WriteAllTextAsync(Path.Combine(_output, $"{index}.response.txt"), raw, token);
            await File.WriteAllTextAsync(Path.Combine(_output, $"{index}.runtime.log"), await stderr, token);
            // A truncated CLI echo cannot be separated by the full-prompt parser.
            // Reject this transport result instead of treating the banner as translation.
            if (HasTruncatedEcho(raw, prompt))
            {
                results.Add(new(false, "", "candidate-transport-truncated-echo"));
                continue;
            }
            var text = LocalLmtOneShotRunner.RemoveCurrentPromptEcho(raw, prompt);
            results.Add(new(process.ExitCode == 0 && !string.IsNullOrWhiteSpace(text), text,
                process.ExitCode == 0 ? "candidate-response-unreviewed" : "candidate-process-failed",
                ExecutionEvidence: new("llama.cpp-test-probe", Path.GetFileName(_model), ExpectedHash, "20 GPU layers requested")));
        }
        return results;
    }
}
