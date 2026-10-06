using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ClipRay.App.Translation;

internal static class ResidentModelProbe
{
    public static async Task<int> Run(string[] args)
    {
        if (args.Length is not (4 or 5)) throw new ArgumentException("model cli fresh-output-directory [prior-evidence-directory]");
        using var model = File.OpenRead(args[1]);
        var hash = Convert.ToHexString(SHA256.HashData(model)).ToLowerInvariant();
        if (hash != "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597")
            throw new InvalidDataException("Candidate hash mismatch");
        if (args[1].Contains('"')) throw new ArgumentException("Invalid model path");
        if (Directory.Exists(args[3])) throw new IOException("Use fresh output directory");
        Directory.CreateDirectory(args[3]);
        var steps = new List<object>();
        ILocalLmtSession? session = null;
        var phase = "startup";
        var watch = Stopwatch.StartNew();
        var success = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            session = await LocalLmtProcessSession.StartAsync(new(args[2],
                $"--model \"{args[1]}\" --conversation --escape --no-display-prompt --no-warmup --simple-io --log-disable --temp 0 --ctx-size 2048 --predict 512 --reasoning-budget 0 --gpu-layers 20"), timeout.Token);
            steps.Add(new { phase, milliseconds = watch.ElapsedMilliseconds });
            var prompts = args.Length == 5
                ? Directory.GetFiles(args[4], "*.prompt.txt").OrderBy(path => int.Parse(Path.GetFileName(path).Split('.')[0]))
                    .Select(path => (Path: path, Text: File.ReadAllText(path))).ToArray()
                : new[] { "The document is saved locally.", "The document is saved locally.", "The network is unavailable. Please try again." }
                    .Select(text => (Path: "", Text: "Translate from English to Simplified Chinese. Return only translation. Text: " + text)).ToArray();
            if (prompts.Length == 0) throw new InvalidDataException("No prompts to replay");
            foreach (var prompt in prompts)
            {
                phase = "translate-and-clear"; watch.Restart();
                using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var escaped = prompt.Text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
                var response = await session.TranslateAsync(escaped, requestTimeout.Token);
                var elapsed = watch.ElapsedMilliseconds;
                var baselinePath = prompt.Path.Replace(".prompt.txt", ".response.txt", StringComparison.Ordinal);
                var baseline = args.Length == 5 ? LocalLmtOneShotRunner.RemoveCurrentPromptEcho(File.ReadAllText(baselinePath), prompt.Text) : null;
                steps.Add(new { phase, text = prompt.Text, response, milliseconds = elapsed,
                    baseline, identicalToBaseline = baseline is null ? (bool?)null : response == baseline });
            }
            phase = "cancel"; watch.Restart();
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try { await session.TranslateAsync("Write the numbers from 1 to 10000 in Chinese.", cancel.Token); }
            catch (OperationCanceledException) { steps.Add(new { phase, cancelled = true, milliseconds = watch.ElapsedMilliseconds, sessionAlive = session.IsAlive }); }
            success = !session.IsAlive;
        }
        catch (Exception error) { steps.Add(new { phase, error = error.GetType().Name, milliseconds = watch.ElapsedMilliseconds }); }
        finally
        {
            session?.Dispose();
            await File.WriteAllTextAsync(Path.Combine(args[3], "report.json"), JsonSerializer.Serialize(new
            {
                modelSha256 = hash, scope = args.Length == 5 ? "same-prompt resident replay; no foreground or quality acceptance" : "synthetic resident session probe; no foreground or quality acceptance",
                success, steps, sessionAliveAfterDispose = session?.IsAlive
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"Resident probe success={success}; report={args[3]}");
        return success ? 0 : 1;
    }
}
