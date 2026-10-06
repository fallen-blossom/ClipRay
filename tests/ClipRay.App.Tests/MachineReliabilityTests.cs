using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ClipRay.App.Infrastructure;
using ClipRay.App.Translation;
using ClipRay.App.Acceptance;

internal static class MachineReliabilityTests
{
    public static void WorkerChunksAtSentenceBoundaries()
    {
        var root=WorkspaceLocator.Find(AppContext.BaseDirectory);
        var start=new ProcessStartInfo(Path.Combine(root,"artifacts","machine-translation","runtime","python.exe"))
            { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true };
        start.ArgumentList.Add(Path.Combine(root,"tests","ClipRay.App.Tests","machine_sentence_chunks_test.py"));
        start.ArgumentList.Add(Path.Combine(root,"src","ClipRay.App","Translation","machine_translation_worker.py"));
        using var process=Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(10000)) { process.Kill(entireProcessTree:true); throw new Exception("Sentence chunk probe timed out."); }
        Task.WhenAll(output,error).GetAwaiter().GetResult();
        if(process.ExitCode!=0) throw new Exception("Production sentence chunk probe failed.");
    }
    public static void WorkerDecodesTargetWordBoundaries()
    {
        var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
        using var worker = new BundledMachineTranslationProcess(
            Path.Combine(root, "artifacts", "machine-translation", "runtime", "python.exe"),
            Path.Combine(root, "src", "ClipRay.App", "Translation", "machine_translation_worker.py"),
            Path.Combine(root, "artifacts", "machine-translation", "models", "translate-en_zh-1_9"), keepAlive: true);
        var request = new MachineTranslationProcessRequest("en", "zh",
            ["If the GPT Work tab was instead called Codex it would be more approachable for non technical people."]);
        var first = worker.TranslateAsync(request, default).GetAwaiter().GetResult()[0];
        var repeated = worker.TranslateAsync(request, default).GetAwaiter().GetResult()[0];
        if (first.Contains('\u2581') || !first.Any(c => c is >= '\u4e00' and <= '\u9fff')
            || !first.Contains("Codex") || first != repeated || worker.ProcessStartCount != 1)
            throw new Exception("Target word-boundary tokens leaked into Chinese text or cached decoding changed.");
    }
    public static void MachineKeepsProtectedTokenSeparators()
    {
        using var provider = new LocalMachineTranslationProvider(new SeparatorProcess());
        var result = provider.TranslateManyAsync([new TranslationRequest("en", "zh", "GPT-6 Pro costs 5.6 dollars")], default)
            .GetAwaiter().GetResult()[0];
        if (!result.IsSuccess || !result.Text.Contains("GPT-6 Pro") || !result.Text.Contains("5.6 dollars"))
            throw new Exception("Trimmed model output joined protected identifiers with adjacent ASCII text.");
    }
    private sealed class SeparatorProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<string>>(request.Texts.Select(t => t.Contains("Pro") ? "Pro价格" : "dollars").ToArray());
    }
    public static void IntegrityAcceptsIdentifiersAdjacentToChinese()
    {
        foreach (var pair in new[] {
            ("Rethinking GPT-6 Astra", "重新思考GPT-6阿斯特拉"),
            ("50 messages", "50条消息"),
            ("Press Ctrl+C or F2", "按Ctrl+C或F2") })
            if (TranslationContentIntegrity.FindMismatch(pair.Item1, pair.Item2) is not null)
                throw new Exception("An unchanged invariant next to Chinese was rejected.");
        foreach (var pair in new[] {
            ("GPT-6", "GPT-7"), ("50 messages", "51条消息"),
            ("Press Ctrl+C", "按Ctrl+V"), ("Press F2", "按F3") })
            if (TranslationContentIntegrity.FindMismatch(pair.Item1, pair.Item2) is null)
                throw new Exception("An actually changed invariant was accepted.");
    }
    public static void FlashDisplayNameUsesApiIdentifier()
    {
        var combo = new System.Windows.Controls.ComboBox();
        var flash = new System.Windows.Controls.ComboBoxItem { Content = "DeepSeek V4.1 Flash", Tag = DeepSeekModels.Flash };
        combo.Items.Add(flash);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var type = typeof(ClipRay.App.Settings.SettingsWindow);
        type.GetMethod("SelectComboValue", flags)!.Invoke(null, [combo, DeepSeekModels.Flash]);
        var selected = type.GetMethod("SelectedValue", flags)!.Invoke(null, [combo]);
        if (!ReferenceEquals(combo.SelectedItem, flash) || !Equals(selected, DeepSeekModels.Flash))
            throw new Exception("Flash display name did not restore or save its API identifier.");
        DeepSeekModels.Validate((string)selected!);
    }
    public static void SocialCountersDoNotStartMachineTranslation()
    {
        using var provider = new LocalMachineTranslationProvider(new RejectProcess());
        foreach (var text in new[] { "O 215", "22 1K", "Ilil 1.8M", "8.9K", "215" })
        {
            var outcome = provider.TranslateManyAsync([new TranslationRequest("en", "zh", text,
                textKind: TranslationTextKind.UiControl)], default).GetAwaiter().GetResult()[0];
            if (!outcome.IsSuccess || outcome.Text != text || outcome.Status != "machine-preserved")
                throw new Exception("Social counter was translated or reported as failed: " + text);
        }
        using var echo = new LocalMachineTranslationProvider(new EchoProcess());
        foreach (var request in new[] {
            new TranslationRequest("en", "zh", "O 215 people", textKind: TranslationTextKind.UiControl),
            new TranslationRequest("en", "zh", "22 1K", textKind: TranslationTextKind.Body) })
        {
            var result = echo.TranslateManyAsync([request], default).GetAwaiter().GetResult()[0];
            if (result.Status == "machine-preserved")
                throw new Exception("Counter rule incorrectly preserved natural language or body text.");
        }
    }

    private sealed class RejectProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            throw new Exception("A numeric social counter must not start the model.");
    }
    public static void GuiDecodesUtf8() => Probe("encoding", "显示更多", null);
    public static void StderrCannotBlock() => Probe("stderr", "显示更多", null);
    public static void CancellationStopsWorker() => Probe("cancel", null, nameof(OperationCanceledException));
    public static void TimeoutStopsWorker() => Probe("timeout", null, nameof(TimeoutException));
    public static void InvalidResponseDoesNotPoisonNextRun()
    {
        Probe("invalid", null, nameof(JsonException));
        GuiDecodesUtf8();
    }

    private static void Probe(string mode, string? expectedText, string? expectedError)
    {
        var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var executable = Path.Combine(root, "tests", "ClipRay.MachineProcessProbe", "bin", configuration,
            "net10.0-windows10.0.26100.0", "ClipRay.MachineProcessProbe.exe");
        var report = Path.Combine(Path.GetTempPath(), "clipray-probe-" + Guid.NewGuid().ToString("N") + ".json");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(root, "artifacts", "machine-translation", "runtime", "python.exe"));
        start.ArgumentList.Add(Path.Combine(root, "tests", "ClipRay.MachineProcessProbe", "worker_fixture.py"));
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(report);
        using var process = Process.Start(start)!;
        try
        {
            if (!process.WaitForExit(10000))
            {
                process.Kill(entireProcessTree: true);
                throw new Exception("Probe did not finish within its bounded budget.");
            }
            if (process.ExitCode != 0) throw new Exception("Probe exited unsuccessfully.");
            using var result = JsonDocument.Parse(File.ReadAllText(report));
            if (result.RootElement.GetProperty("Text").GetString() != expectedText
                || result.RootElement.GetProperty("Error").GetString() != expectedError)
                throw new Exception("Wrong GUI worker output: " + result.RootElement.GetRawText());
            var pid = int.Parse(File.ReadAllText(report + ".pid"));
            try
            {
                using var worker = Process.GetProcessById(pid);
                if (!worker.WaitForExit(2000)) throw new Exception("Owned Python worker survived the completed probe.");
            }
            catch (ArgumentException) { /* An exited PID is the expected outcome. */ }
        }
        finally
        {
            if (File.Exists(report)) File.Delete(report);
            if (File.Exists(report + ".pid")) File.Delete(report + ".pid");
        }
    }

    public static void PrimaryExceptionFallsBack() => CheckFallback((_, _, _) => throw new IOException("private-message"));
    public static void PrimaryCountMismatchFallsBack() => CheckFallback((_, _, _) =>
        Task.FromResult<IReadOnlyList<TranslationResult>>([]));

    private static void CheckFallback(Func<TranslationRouteKind, IReadOnlyList<TranslationRequest>, CancellationToken,
        Task<IReadOnlyList<TranslationResult>>> primary)
    {
        var machine = new Machine();
        var coordinator = new TranslationFallbackCoordinator(primary, machine);
        var request = new TranslationRequest("en", "zh", "Hello");
        var outcome = coordinator.TranslateAsync(TranslationRouteKind.LocalLmt, [request], CancellationToken.None)
            .GetAwaiter().GetResult();
        if (machine.Calls != 1 || outcome.RecoveredCount != 1 || outcome.Results[0].Text != "你好")
            throw new Exception("Primary failure did not recover through machine fallback.");
    }

    public static void PrimaryCancellationDoesNotFallBack()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var machine = new Machine();
        var coordinator = new TranslationFallbackCoordinator((_, _, _) =>
            Task.FromCanceled<IReadOnlyList<TranslationResult>>(stop.Token), machine);
        try
        {
            coordinator.TranslateAsync(TranslationRouteKind.LocalLmt,
                [new TranslationRequest("en", "zh", "Hello")], stop.Token).GetAwaiter().GetResult();
            throw new Exception("Cancellation was swallowed.");
        }
        catch (OperationCanceledException) { }
        if (machine.Calls != 0) throw new Exception("Cancellation started an unwanted fallback.");
    }

    public static void UnchangedEnglishIsNotSuccessfulMachineTranslation()
    {
        var provider = new LocalMachineTranslationProvider(new EchoProcess());
        var outcome = provider.TranslateManyAsync([new TranslationRequest("en", "zh", "The weather is pleasant.")],
            CancellationToken.None).GetAwaiter().GetResult()[0];
        if (outcome.IsSuccess || outcome.Status != "machine-output-unchanged")
            throw new Exception("Unchanged English was reported as a completed Chinese translation.");
    }

    private sealed class EchoProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            Task.FromResult(request.Texts);
    }

    public static void PartialAcceptanceIsFailure()
    {
        var outcome = new ApplicationTranslationOutcome(false, TranslationPipelineStage.Translation,
            "translation-failed", [], [], [new TranslationResult(false, "", "private-provider-message")], []);
        var category = TranslationAcceptancePolicy.ErrorCategory(outcome);
        if (category != "Translation:partial-or-failed") throw new Exception("Failure was classified as successful or leaked raw status.");
    }

    public static void SuccessfulAcceptanceIsSuccess()
    {
        var outcome = new ApplicationTranslationOutcome(true, TranslationPipelineStage.None,
            "completed", [], [], [], []);
        if (TranslationAcceptancePolicy.ErrorCategory(outcome) != "none")
            throw new Exception("Successful outcome was rejected.");
    }

    private sealed class Machine : IMachineTranslationProvider
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(IReadOnlyList<TranslationRequest> requests,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<TranslationResult>>(requests.Select(_ =>
                new TranslationResult(true, "你好", "machine-completed")).ToArray());
        }
    }
}
