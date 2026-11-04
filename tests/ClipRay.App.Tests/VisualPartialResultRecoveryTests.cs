using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ClipRay.App.Security;
using ClipRay.App.Translation;

internal static class VisualPartialResultRecoveryTests
{
    public static void FocusedRetryDeadlinePreservesCompletedBlocks() =>
        DeadlinePreservesCompletedBlocks(split: false);

    public static void RecursiveSplitDeadlinePreservesCompletedBlocks() =>
        DeadlinePreservesCompletedBlocks(split: true);

    private static void DeadlinePreservesCompletedBlocks(bool split)
    {
        using var handler = new ScriptedHandler(async (call, _, token) =>
        {
            if (split && call == 1) return Response("broken-json");
            if (call == (split ? 2 : 1))
                return Response(split ? "{\"b0\":\"主页\"}" : "{\"b0\":\"主页\",\"b1\":\"Explore\"}");
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new Exception("The held transport must be cancelled by the deadline.");
        });
        var vault = new Vault();
        var provider = new DeepSeekTranslationProvider(vault, handler);
        var recorded = new List<DeepSeekCostEstimate?>();
        var results = VisualTranslationExecutionPolicy.ExecuteCloudAsync(Requests(), vault, provider,
            recorded.Add, CancellationToken.None, TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult();
        Check(results.Count == 2 && results[0].IsSuccess && results[0].Text == "主页",
            "A later deadline discarded a validated completed sibling.");
        Check(!results[1].IsSuccess && results[1].Text.Length == 0
            && results[1].Status.Contains("timed out", StringComparison.Ordinal),
            "The unfinished sibling must remain a timeout failure.");
        Check(handler.Calls == (split ? 3 : 2), "The recovery path dispatched unexpected paid retries.");
        Check(recorded.Count == 1 && recorded[0]?.RequestCount == (split ? 2 : 1),
            "Completed response usage was lost or recorded more than once after the deadline.");
    }

    public static void ExplicitCallerCancellationDiscardsCompletedBlocks()
    {
        foreach (var split in new[] { false, true })
        {
            using var caller = new CancellationTokenSource();
            using var handler = new ScriptedHandler(async (call, _, token) =>
            {
                if (split && call == 1) return Response("broken-json");
                if (call == (split ? 2 : 1))
                    return Response(split ? "{\"b0\":\"主页\"}" : "{\"b0\":\"主页\",\"b1\":\"Explore\"}");
                caller.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new Exception("Caller cancellation must interrupt the held transport.");
            });
            var vault = new Vault();
            var recorded = 0;
            try
            {
                VisualTranslationExecutionPolicy.ExecuteCloudAsync(Requests(), vault,
                    new DeepSeekTranslationProvider(vault, handler), _ => recorded++, caller.Token,
                    TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (caller.IsCancellationRequested)
            {
                Check(recorded == 0, "An explicitly cancelled operation committed a partial snapshot.");
                continue;
            }
            throw new Exception("Caller cancellation returned a stale partial result instead of cancelling.");
        }
    }

    public static void MissingIdRetriesOnlyMissingBlock()
    {
        using var handler = new ScriptedHandler((call, payload, _) =>
        {
            var ids = PromptIds(payload);
            Check(ids.SequenceEqual(call == 1 ? new[] { "b0", "b1" } : new[] { "b1" }),
                "A missing ID caused a completed sibling to be sent and charged again.");
            return Task.FromResult(Response(call == 1 ? "{\"b0\":\"主页\"}" : "{\"b1\":\"探索\"}"));
        });
        var results = new DeepSeekTranslationProvider(new Vault(), handler)
            .TranslateBlocksAsync(Requests(), "fake", CancellationToken.None).GetAwaiter().GetResult();
        Check(results.All(result => result.IsSuccess) && handler.Calls == 2,
            "Focused missing-ID recovery failed or exceeded its single retry.");
    }

    public static void InvalidStructuralResponseCannotBecomeSuccess()
    {
        foreach (var content in new[] { "{\"b0\":\"主页\",\"unknown\":\"探索\"}",
            "{\"b0\":\"主页\",\"b0\":\"探索\"}", "broken-json", "{}" })
        {
            using var handler = new ScriptedHandler((_, _, _) => Task.FromResult(Response(content)));
            var results = new DeepSeekTranslationProvider(new Vault(), handler)
                .TranslateBlocksAsync(Requests(), "fake", CancellationToken.None).GetAwaiter().GetResult();
            Check(results.All(result => !result.IsSuccess), "Invalid or missing IDs fabricated a success.");
            Check(handler.Calls <= TranslationBatchCoordinator.MaximumAttempts,
                "Invalid structure escaped the bounded recovery budget.");
        }
    }

    public static void LateCallerCancellationStillCancelsOperation()
    {
        using var caller = new CancellationTokenSource();
        using var handler = new ScriptedHandler((_, _, _) =>
        {
            caller.Cancel();
            return Task.FromResult(Response("{\"b0\":\"主页\",\"b1\":\"探索\"}"));
        });
        try
        {
            new DeepSeekTranslationProvider(new Vault(), handler)
                .TranslateBlocksAsync(Requests(), "fake", caller.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested) { return; }
        throw new Exception("A transport completing after caller cancellation published its result.");
    }

    private static TranslationRequest[] Requests() =>
    [
        new("English", "Chinese", "Home", DeepSeekModels.Flash, TranslationTextKind.UiControl, "screen_capture"),
        new("English", "Chinese", "Explore", DeepSeekModels.Flash, TranslationTextKind.UiControl, "screen_capture"),
    ];

    private static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 10, completion_tokens = 5 },
        }), Encoding.UTF8, "application/json"),
    };

    private static string[] PromptIds(string payload)
    {
        using var envelope = JsonDocument.Parse(payload);
        using var prompt = JsonDocument.Parse(envelope.RootElement.GetProperty("messages")[1]
            .GetProperty("content").GetString()!);
        return prompt.RootElement.EnumerateObject().Select(item => item.Name).ToArray();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class ScriptedHandler(Func<int, string, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var payload = await request.Content!.ReadAsStringAsync(token);
            return await send(++Calls, payload, token);
        }
    }

    private sealed class Vault : ICredentialVault
    {
        public void Save(string name, string provider, string value) => throw new NotSupportedException();
        public IReadOnlyList<CredentialMetadata> List() => [];
        public void Delete(string name) => throw new NotSupportedException();
        public string? GetForUse(string name) => "local-fake-key";
    }
}
