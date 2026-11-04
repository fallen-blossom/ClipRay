using ClipRay.App.Cleanup;

internal static class SettingsCleanupReasonTests
{
    internal static (string Name, Action Test)[] CreateCases() =>
    [
        ("cleanup-reason-categories", Categories),
        ("cleanup-legacy-reasons", Legacy),
        ("cleanup-reason-privacy", Privacy),
    ];

    private static void Categories()
    {
        var status = new CleanupRunStatus(DateTimeOffset.UtcNow, false, 9, 4, 1)
        {
            Items = [new("private-a", "skipped", "within-retention"),
                new("private-b", "skipped", "within-retention"),
                new("private-c", "skipped", "completion-receipt-missing"),
                new("private-d", "skipped", "completion-receipt-invalid"),
                new("private-e", "failed", "recycle-unavailable")]
        };
        var summaries = CleanupRunStatusFormatter.SummarizeReasons(status);
        Check(summaries.Single(x => x.Reason == "within-retention").Count == 2, "Retention aggregation lost counts.");
        Check(summaries.Where(x => x.Category == "retained").Sum(x => x.Count) == 3, "Normal retention counted as an error.");
        Check(summaries.Single(x => x.Reason == "completion-receipt-invalid").Category == "attention", "Invalid receipt hidden as normal retention.");
        Check(summaries.Single(x => x.Status == "failed").Category == "failed", "Recycle failure hidden as a skip.");
        Check(CleanupRunStatusFormatter.FormatReasons(status, "en").Contains("Within retention period: 2"), "English reasons missing.");
    }

    private static void Legacy()
    {
        var old = new CleanupRunStatus(DateTimeOffset.UtcNow, true, 9, 174, 0)
        { Items = Enumerable.Range(0, 174).Select(_ => new CleanupPathOutcome("private", "skipped", "unknown-incomplete-recent-or-changed")).ToArray() };
        var summary = CleanupRunStatusFormatter.SummarizeReasons(old).Single();
        Check(summary.Count == 174 && summary.Category == "unknown", "Legacy reasons were invented.");
        var missing = CleanupRunStatusFormatter.SummarizeReasons(old with { Items = [] }).Single();
        Check(missing.Count == 174 && missing.Reason == "details-not-recorded", "Old aggregate-only status lost counts.");
        Check(CleanupRunStatusFormatter.SummarizeReasons(null).Count == 0, "Missing status invented a run.");
    }

    private static void Privacy()
    {
        const string secret = "private-filename-do-not-display";
        var status = new CleanupRunStatus(DateTimeOffset.UtcNow, false, 0, 1, 1)
        { Items = [new(secret, "skipped", secret), new(secret, "failed", secret)] };
        foreach (var language in new[] { "zh-CN", "en" })
        {
            var text = CleanupRunStatusFormatter.FormatReasons(status, language);
            Check(!text.Contains(secret), "Unknown reason or stored path leaked to the UI.");
            Check(CleanupRunStatusFormatter.SummarizeReasons(status, language).All(x => x.Reason == "unrecognized"), "Raw unknown reason leaked through summary API.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
