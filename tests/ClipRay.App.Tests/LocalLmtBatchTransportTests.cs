using ClipRay.App.Translation;

internal static class LocalLmtBatchTransportTests
{
    public static int ProtocolChild()
    {
        Console.Write("Loading model...\n> ");
        Console.Out.Flush();
        var hasHistory = false;
        while (Console.ReadLine() is { } line)
        {
            if (line == "/clear")
            {
                hasHistory = false;
                Console.Write("Chat history cleared.\n> ");
            }
            else
            {
                Console.Write(hasHistory ? "stale prior answer\n> "
                    : line == "first" ? "answer one\n> " : line == "second" ? "answer two\n> " : "unexpected prompt\n> ");
                hasHistory = true;
            }
            Console.Out.Flush();
        }
        return 0;
    }

    public static async Task StartupBannerAndPriorTurnAreNotReturnedAsTranslations()
    {
        var executable = Environment.ProcessPath ?? throw new Exception("Test process path unavailable.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var output = await new LocalLmtBatchRunner().TranslateAsync(
            new LocalLmtSessionStart(executable, "--local-lmt-batch-protocol-child",
                ClearAfterEachResponse: true),
            ["first", "second"], timeout.Token);
        if (!output.SequenceEqual(["answer one", "answer two"]))
            throw new Exception("Local LMT batch mixed startup or prior-turn output into a translation: "
                + string.Join(" | ", output));
    }
}
