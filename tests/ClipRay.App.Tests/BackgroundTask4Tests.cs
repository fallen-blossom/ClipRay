using System.IO;
using System.Text.Json;
using ClipRay.App.Security;
using ClipRay.App.Settings;
using static BackgroundTask3Tests;

internal static partial class BackgroundTask4Tests
{
    internal static (string Name, Action Test)[] CreateCases() => [
            ("natural-custom", NaturalCustom),
            ("credential-conflict", CredentialConflict),
            ("null-hotkey", NullHotkey),
            ("io-config", IoConfig),
            ("single-exit", SingleExit),
            ("confirmed-default", ConfirmedDefault),
            ("recovery-startup", RecoveryStartup),
            ("credential-failures", CredentialFailures),
            ("settings-credentials", SettingsCredentials),
            ("settings-credential-failures", SettingsCredentialFailures),
            ("output-maintenance", OutputMaintenance),
            ("hotkey-apply", HotkeyApply),
            ("draft-close", DraftClose),
            ("draft-exit", DraftExit),
            ("exit-pending", () => ExitPending().GetAwaiter().GetResult()),
            ("exit-failure", () => ExitFailure().GetAwaiter().GetResult()),
            ("platform-exit", PlatformExit),
            ("uninstall-exit", UninstallExit),
            ("interface-save", InterfaceSave),
            ("legacy-settings", LegacySettings),
            ("late-connection-exit", LateConnectionExit),
            ("hotkey-status-lifecycle", HotkeyStatusLifecycle),
        ];

    internal static int Run(string[] args)
    {
        var cases = CreateCases();
        var names = args.Where(a => !a.StartsWith("--")).ToHashSet();
        var selected = cases.Where(c => names.Count == 0 || names.Contains(c.Name)).ToArray();
        if (selected.Length == 0 || names.Except(cases.Select(c => c.Name)).Any()) return 2;
        var failures = 0;
        foreach (var test in selected)
        {
            var previousContext = SynchronizationContext.Current;
            try { SynchronizationContext.SetSynchronizationContext(null); test.Test(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception error) { failures++; Console.WriteLine($"FAIL {test.Name}: {error}"); }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        }
        Console.WriteLine($"TASK4 {selected.Length - failures}/{selected.Length}");
        return failures == 0 ? 0 : 1;
    }
    internal static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipray-task4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return root;
    }
    private static void NaturalCustom()
    {
        var root = TempRoot(); var custom = Path.Combine(root, "chosen");
        var s = AppSettings.CreateDefault(root) with {
            ScreenshotDirectory = Path.Combine(custom, "Screenshots"),
            LongCaptureDirectory = Path.Combine(custom, "Long Captures"),
            RecordingDirectory = Path.Combine(custom, "Recordings") };
        var path = Path.Combine(root, "settings.json"); File.WriteAllText(path, JsonSerializer.Serialize(s));
        var store = new SettingsStore(path, root); var first = store.LoadRecoverable();
        Check(first.Settings.ScreenshotDirectory == s.ScreenshotDirectory
            && first.Settings.LongCaptureDirectory == s.LongCaptureDirectory
            && first.Settings.RecordingDirectory == s.RecordingDirectory, "Natural custom folders were rebased.");
        store.Save(first.Settings); Check(store.Load() == first.Settings, "Save/reopen changed custom folders.");
    }
    private static void CredentialConflict()
    {
        var backend = new Backend(); var vault = new CredentialVault(backend, () => DateTimeOffset.UnixEpoch);
        vault.Save(CredentialVault.FixedDeepSeekSlot, "DeepSeek", "sk-aaaaaaaaaaaaaaaaaaaaaaaA");
        vault.Save("selected-B", "DeepSeek", "sk-bbbbbbbbbbbbbbbbbbbbbbbB");
        var changed = vault.MigrateSelectedDeepSeekCredential("selected-B");
        Check(!changed && vault.GetForUse("selected-B") == "sk-bbbbbbbbbbbbbbbbbbbbbbbB"
            && vault.GetForUse(CredentialVault.FixedDeepSeekSlot) == "sk-aaaaaaaaaaaaaaaaaaaaaaaA", "Migration replaced selected B or lost a credential.");
        Check(!vault.MigrateSelectedDeepSeekCredential("selected-B") && vault.List().Count == 2, "Conflict migration was not idempotent.");
    }
    private static void NullHotkey()
    {
        var root = TempRoot(); var path = Path.Combine(root, "settings.json");
        var json = JsonSerializer.Serialize(AppSettings.CreateDefault(root) with { RegionCaptureHotkey = null! });
        File.WriteAllText(path, json); var result = new SettingsStore(path, root).LoadRecoverable();
        Check(result.Warning is not null && result.Settings.RegionCaptureHotkey is not null && File.ReadAllText(path) == json,
            "Null hotkey did not recover while preserving original bytes.");
    }
    private static void IoConfig()
    {
        var root = TempRoot(); var result = new SettingsStore(root, root).LoadRecoverable();
        Check(result.Warning is not null && !result.Warning.Contains("无效", StringComparison.Ordinal), "I/O failure was treated as missing/corrupt content.");
    }
    private static void SingleExit() => SingleExitAsync().GetAwaiter().GetResult();
    private static async Task SingleExitAsync()
    {
        var app = BackgroundTask3LifetimeTests.FakeApp(); var release = NewSignal(); var decisions = 0;
        async Task<bool> MayExit() { decisions++; await release.Task; return false; }
        var first = app.RequestManagedShutdownAsync(MayExit, () => throw new Exception("Cancelled exit reached shutdown"));
        var second = app.RequestManagedShutdownAsync(MayExit, () => throw new Exception("Cancelled exit reached shutdown"));
        release.TrySetResult(); await Task.WhenAll(first, second);
        Check(decisions == 1 && !first.Result && !second.Result, "Concurrent exit requests duplicated the draft decision.");
    }
    internal sealed class Backend : ICredentialBackend
    {
        internal readonly Dictionary<string, string> Values = [];
        internal Func<string, bool>? FailWrite;
        internal Func<string, bool>? FailDelete;
        public void Write(string target, string value) { if (FailWrite?.Invoke(target) == true) throw new IOException("synthetic write"); Values[target] = value; }
        public string? Read(string target) => Values.GetValueOrDefault(target);
        public void Delete(string target) { if (FailDelete?.Invoke(target) == true) throw new IOException("synthetic delete"); Values.Remove(target); }
        public IEnumerable<string> ListTargets(string prefix) => Values.Keys.Where(k => k.StartsWith(prefix)).ToArray();
    }
}
