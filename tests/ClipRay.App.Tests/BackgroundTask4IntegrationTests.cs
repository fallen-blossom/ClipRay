using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ClipRay.App.Cleanup;
using ClipRay.App.Infrastructure;
using ClipRay.App.Security;
using ClipRay.App.Settings;
using ClipRay.App.Translation;
using static BackgroundTask3Tests;
using static BackgroundTask3LifetimeTests;

internal static partial class BackgroundTask4Tests
{
    private const string A = "sk-aaaaaaaaaaaaaaaaaaaaaaaA", B = "sk-bbbbbbbbbbbbbbbbbbbbbbbB", C = "sk-cccccccccccccccccccccccC";
    private static void ConfirmedDefault()
    {
        var root = TempRoot(); var oldRoot = Path.Combine(root, "old"); var newRoot = Path.Combine(root, "new");
        var store = new SettingsStore(Path.Combine(root, "settings.json"), newRoot);
        store.Save(AppSettings.CreateDefault(oldRoot));
        var loaded = store.LoadRecoverable();
        Check(loaded.RequiresSave && loaded.Settings.ScreenshotDirectory == Path.Combine(newRoot, "Outputs", "Screenshots")
            && loaded.Settings.DefaultOutputLayoutRoot == newRoot, "A confirmed default was not rebased.");
        store.Save(loaded.Settings); Check(!store.LoadRecoverable().RequiresSave, "Confirmed migration repeated.");
        var broken = store.Load() with { ScreenshotDirectory = Path.Combine(root, "explicit", "Screenshots") };
        store.Save(broken); Check(store.Load().ScreenshotDirectory == broken.ScreenshotDirectory && store.Load().DefaultOutputLayoutRoot is null,
            "An invalidated origin marker authorized a custom-path migration.");
    }
    private static void RecoveryStartup()
    {
        var root = TempRoot(); var path = Path.Combine(root, "bad.json");
        foreach (var json in new[] { "{broken", JsonSerializer.Serialize(AppSettings.CreateDefault(root) with { RegionCaptureHotkey = null! }),
            JsonSerializer.Serialize(AppSettings.CreateDefault(root) with { StartRecordingHotkey = new GlobalHotkey { Key = "not-a-key" } }) })
        {
            File.WriteAllText(path, json); var store = new SettingsStore(path, root); var loaded = store.LoadRecoverable();
            var writes = 0; var migrations = 0;
            var final = StartupSettingsReconciliation.Apply(loaded, true, _ => { migrations++; return true; }, s => { writes++; store.Save(s); });
            Check(final.PreserveOriginalFile && !final.RequiresSave && final.Settings.StartWithWindows && writes == 0 && migrations == 0
                && File.ReadAllText(path) == json, "Startup rewrote the recovered original or touched credentials.");
            Check(SettingsLoadWarningText.ForInterface(final.Warning!, "en").Contains("original", StringComparison.OrdinalIgnoreCase), "Recovery lost English guidance.");
        }
        var valid = AppSettings.CreateDefault(root) with { CredentialName = "selected-B" };
        File.WriteAllText(path, JsonSerializer.Serialize(valid)); var original = File.ReadAllText(path);
        var failed = StartupSettingsReconciliation.Apply(new(valid, null, true), false, _ => throw new IOException("synthetic migration"), _ => throw new IOException("synthetic save"));
        Check(failed.Settings.CredentialName == "selected-B" && failed.Warning!.Contains("原选择保留") && File.ReadAllText(path) == original,
            "Migration/save failure changed the original choice or file.");
    }
    private static void CredentialFailures()
    {
        var backend = new Backend(); var vault = new CredentialVault(backend, () => DateTimeOffset.UnixEpoch);
        vault.Save("selected-B", "DeepSeek", B); var before = backend.Values.ToDictionary();
        backend.FailWrite = target => target.StartsWith("ClipRay.Metadata.");
        try { vault.MigrateSelectedDeepSeekCredential("selected-B"); throw new Exception("Expected fake failure"); } catch (IOException) { }
        Check(backend.Values.OrderBy(p => p.Key).SequenceEqual(before.OrderBy(p => p.Key)), "Failed migration changed backend state.");
        backend.FailWrite = null;
        Check(vault.MigrateSelectedDeepSeekCredential("selected-B") && vault.GetForUse("selected-B") == B
            && vault.GetForUse(CredentialVault.FixedDeepSeekSlot) == B && !vault.MigrateSelectedDeepSeekCredential("selected-B"),
            "Safe copy migration was destructive or non-idempotent.");
    }
    internal static SettingsWindowEnvironment SafeEnvironment(string root) => new()
    {
        LoadCleanupRun = () => null,
        ResolveUninstall = () => new(false, null),
        ResolveLocalModelPath = path => path ?? Path.Combine(root, "never-opened.gguf"),
        ResolveLocalModels = () => [],
        ModelExists = _ => false,
    };
    private static SettingsWindow Window(AppSettings settings, MemoryStore store, CredentialVault? vault = null, IDeepSeekTranslationProvider? cloud = null)
    {
        typeof(Program).GetMethod("EnsureWpfResources", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new SettingsWindow(settings, null, new SettingsApplyCoordinator(store, new NoStartup(), "synthetic.exe"),
            vault ?? new CredentialVault(new Backend(), () => DateTimeOffset.UnixEpoch), () => cloud ?? new Cloud(),
            (_, _) => throw new Exception("Unexpected local provider"), DeepSeekCostSummary.Empty(DateTimeOffset.UnixEpoch),
            () => DeepSeekCostSummary.Empty(DateTimeOffset.UnixEpoch), () => throw new Exception("Unexpected cost reset"),
            () => new(CleanupScheduleRegistrationState.Missing), () => false, SafeEnvironment(TempRoot()));
        window.DraftDecision = () => SettingsDraftCloseChoice.Cancel;
        window.CredentialDeletionConfirmation = (_, _) => false;
        window.UninstallConfirmation = () => false;
        window.LaunchUninstaller = () => throw new Exception("Unexpected uninstall launch");
        return window;
    }
    private static AppSettings Settings(string root) => AppSettings.CreateDefault(root, "en-US") with {
        Provider = "DeepSeek", LiveTranslationProvider = "DeepSeek", HasExplicitProviderChoice = true,
        MachineTranslationFallbackEnabled = false, CredentialName = "selected-B", InterfaceLanguageFollowsWindows = false };
    private static T Control<T>(SettingsWindow window, string name) => (T)window.FindName(name);
    private static void Click(SettingsWindow w, string name) => Control<Button>(w, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void CloseFixture(SettingsWindow w) { w.DiscardDraftOnClose = true; w.Close(); }
    private static (Backend Backend, CredentialVault Vault) Pair()
    { var backend = new Backend(); var vault = new CredentialVault(backend, () => DateTimeOffset.UnixEpoch); vault.Save(CredentialVault.FixedDeepSeekSlot, "DeepSeek", A); vault.Save("selected-B", "DeepSeek", B); return (backend, vault); }
    internal static void SettingsCredentials()
    {
        var root = TempRoot(); var pair = Pair(); var cloud = new Cloud(); var store = new MemoryStore();
        var initial = Settings(root); var window = Window(initial, store, pair.Vault, cloud);
        try
        {
            Check(Control<TextBlock>(window, "CredentialSlotStatusText").Text.Contains("selected-B")
                && Control<Button>(window, "SaveCredentialButton").ToolTip!.ToString()!.Contains("selected-B"), "Settings did not display selected B.");
            Control<TextBox>(window, "ScreenshotDirectoryBox").Text = Path.Combine(root, "Screenshots"); Click(window, "SaveSettingsButton");
            Check(store.Saved!.CredentialName == "selected-B" && store.Saved.DefaultOutputLayoutRoot is null, "Settings save redirected B or retained an edited default marker.");
            Click(window, "TestProviderButton"); Check(cloud.Name == "selected-B", "Actual verification used the fixed A slot.");
            Control<PasswordBox>(window, "CredentialValueBox").Password = C; Click(window, "SaveCredentialButton");
            Check(pair.Vault.GetForUse("selected-B") == C && pair.Vault.GetForUse(CredentialVault.FixedDeepSeekSlot) == A, "Import/replace changed unrelated A.");
            var confirmedNames = new List<string>(); window.CredentialDeletionConfirmation = (m, _) => { confirmedNames.Add(m); return true; };
            Click(window, "DeleteCredentialButton");
            Check(pair.Vault.GetForUse("selected-B") is null && pair.Vault.GetForUse(CredentialVault.FixedDeepSeekSlot) == A
                && Control<TextBlock>(window, "CredentialSlotStatusText").Text.Contains("selected-B") && confirmedNames.Any(m => m.Contains("selected-B")),
                "Clear changed unrelated A or silently redirected the selection.");
            Control<PasswordBox>(window, "CredentialValueBox").Password = B; Click(window, "SaveCredentialButton");
            Check(pair.Vault.GetForUse("selected-B") == B && pair.Vault.GetForUse(CredentialVault.FixedDeepSeekSlot) == A, "Import after clear was redirected.");
            Control<TextBox>(window, "ScreenshotDirectoryBox").Text += "-after-clear"; Click(window, "SaveSettingsButton");
            Click(window, "TestProviderButton");
            Check(store.Saved!.CredentialName == "selected-B" && cloud.Name == "selected-B"
                && pair.Vault.GetForUse(CredentialVault.FixedDeepSeekSlot) == A, "Save/verify after clear/import was redirected to A.");
        }
        finally { CloseFixture(window); }
        var reopened = Window(store.Saved!, new MemoryStore(), pair.Vault, cloud);
        try { Check(Control<TextBlock>(reopened, "CredentialSlotStatusText").Text.Contains("selected-B"), "Reopened settings lost B."); }
        finally { CloseFixture(reopened); }
        var defaultStore = new MemoryStore(); var defaultWindow = Window(initial with { CredentialName = null }, defaultStore, pair.Vault, cloud);
        try
        {
            Control<TextBox>(defaultWindow, "ScreenshotDirectoryBox").Text += "-default"; Click(defaultWindow, "SaveSettingsButton");
            Click(defaultWindow, "TestProviderButton");
            Check(defaultStore.Saved!.CredentialName == CredentialVault.FixedDeepSeekSlot && cloud.Name == CredentialVault.FixedDeepSeekSlot,
                "A configuration without an explicit selection lost the default fixed slot.");
        }
        finally { CloseFixture(defaultWindow); }
    }
    private static void SettingsCredentialFailures()
    {
        var pair = Pair(); var before = pair.Backend.Values.ToDictionary(); var cloud = new Cloud { Success = false };
        var window = Window(Settings(TempRoot()), new MemoryStore(), pair.Vault, cloud);
        try
        {
            Control<PasswordBox>(window, "CredentialValueBox").Password = C; Click(window, "SaveCredentialButton");
            Check(pair.Backend.Values.OrderBy(p => p.Key).SequenceEqual(before.OrderBy(p => p.Key)), "Failed verification changed A/B.");
            cloud.Success = true; pair.Backend.FailWrite = t => t.StartsWith("ClipRay.Metadata."); Click(window, "SaveCredentialButton");
            Check(pair.Backend.Values.OrderBy(p => p.Key).SequenceEqual(before.OrderBy(p => p.Key)), "Failed replace changed A/B.");
            pair.Backend.FailWrite = null; pair.Backend.FailDelete = t => t.StartsWith("ClipRay.Metadata."); window.CredentialDeletionConfirmation = (_, _) => true;
            Click(window, "DeleteCredentialButton");
            Check(pair.Backend.Values.OrderBy(p => p.Key).SequenceEqual(before.OrderBy(p => p.Key))
                && Control<TextBlock>(window, "CredentialSlotStatusText").Text.Contains("selected-B"), "Failed clear changed A/B/selection.");
        }
        finally { CloseFixture(window); }
    }
    private static void OutputMaintenance()
    {
        var root = TempRoot(); var app = FakeApp(); var s = Settings(root); var blocked = Path.Combine(root, "occupied-file"); File.WriteAllText(blocked, "own fixture");
        s = s with { ScreenshotDirectory = blocked }; Set(app, "_settings", s);
        var failures = app.PrepareOutputDirectories(s);
        Check(failures.Count == 1 && failures[0].StartsWith("Screenshot") && Field<AppSettings>(app, "_settings").ScreenshotDirectory == blocked, "Output failure changed paths or escaped maintenance.");
        var window = Window(s, new MemoryStore()); app.AttachSettingsWindow(window); app.HotkeyHostFactory = () => new Hotkeys();
        using var menu = app.CreateTrayMenu();
        try
        {
            Check(menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Any(i => i.Text == "Settings")
                && menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Any(i => i.Text == "Stop live translation"), "Maintenance tray/settings/stop entry was lost.");
            Invoke(app, "ApplySettings", s with { CopyToClipboard = false }, false);
            var output = Field<ClipRay.App.Capture.ImageOutputService>(app, "_outputService");
            using var pixel = new System.Drawing.Bitmap(2, 2);
            try { output.SavePng(pixel, DateTime.UnixEpoch); throw new Exception("Unavailable output task should fail"); } catch (IOException) { }
            var healthy = new ClipRay.App.Capture.ImageOutputService(s.LongCaptureDirectory, false, ClipRay.App.Capture.OutputArtifactKind.LongCapture);
            pixel.SetPixel(0, 0, System.Drawing.Color.Red); var path = healthy.SavePng(pixel, DateTime.UnixEpoch);
            using var reloaded = new System.Drawing.Bitmap(path); Check(reloaded.GetPixel(0, 0).ToArgb() == System.Drawing.Color.Red.ToArgb(), "Independent output path was blocked or lost pixels.");
        }
        finally { CloseFixture(window); Invoke(app, "ReleaseHotkeys"); }
    }
    private static void HotkeyApply()
    {
        var root = TempRoot(); var initial = Settings(root); var app = FakeApp(); Set(app, "_settings", initial);
        var host = new Hotkeys(); app.HotkeyHostFactory = () => host; Invoke(app, "RegisterHotkeys"); var registrations = host.Registrations;
        var store = new MemoryStore(); var window = Window(initial, store); app.AttachSettingsWindow(window);
        try
        {
            Control<TextBox>(window, "ScreenshotDirectoryBox").Text = Path.Combine(root, "custom"); Click(window, "SaveSettingsButton");
            Check(host.Registrations == registrations && !host.Disposed, "Unrelated save tore down usable hotkeys.");
            var requested = GlobalHotkey.Parse("Ctrl+Alt+Y"); host.Reject.Add(requested);
            Invoke(window, "SetHotkeyValue", Control<TextBox>(window, "StopLiveTranslationHotkeyBox"), requested);
            Click(window, "SaveSettingsButton");
            Check(store.Saved!.StopLiveTranslationHotkey == requested && host.Active.ContainsKey(initial.StopLiveTranslationHotkey)
                && !host.Active.ContainsKey(requested) && Control<TextBlock>(window, "StatusText").Text.Contains("could not be applied")
                && Control<TextBlock>(window, "StatusText").Text.Contains("tray"), "Actual registration conflict was reported as fully applied or lost its usable stop binding.");
            using var menu = app.CreateTrayMenu(); var stop = menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Single(i => i.Text == "Stop live translation");
            stop.Enabled = true; stop.PerformClick(); Check(!stop.Enabled, "Actual tray Stop handler was not reachable after conflict.");
            host.Reject.Clear(); Click(window, "SaveSettingsButton");
            // Refresh is intentionally read-only; changing a draft and saving retries registration.
            Control<TextBox>(window, "RecordingDirectoryBox").Text = Path.Combine(root, "recordings2"); Click(window, "SaveSettingsButton");
            Check(host.Active.ContainsKey(requested) && !host.Active.ContainsKey(initial.StopLiveTranslationHotkey), "Retry did not apply the recovered binding.");
        }
        finally { CloseFixture(window); Invoke(app, "ReleaseHotkeys"); }
    }
    private static void DraftClose()
    {
        foreach (var choice in new[] { SettingsDraftCloseChoice.Cancel, SettingsDraftCloseChoice.Discard, SettingsDraftCloseChoice.Save })
        {
            var store = new MemoryStore(); var window = Window(Settings(TempRoot()), store); var prompts = 0;
            try
            {
                Control<TextBox>(window, "ScreenshotDirectoryBox").Text += "-edited";
                window.DraftDecision = () => { prompts++; return choice; }; var args = new CancelEventArgs(); window.SettingsClosing(window, args);
                Check(args.Cancel == (choice == SettingsDraftCloseChoice.Cancel) && prompts == 1 && (store.Saved is not null) == (choice == SettingsDraftCloseChoice.Save), "Ordinary closing ignored its draft decision.");
                if (choice == SettingsDraftCloseChoice.Save) { window.SettingsClosing(window, new()); Check(prompts == 1, "Saved draft prompted twice."); }
            }
            finally { CloseFixture(window); }
        }
        var failing = new MemoryStore { Fail = true }; var failed = Window(Settings(TempRoot()), failing);
        try { Control<TextBox>(failed, "ScreenshotDirectoryBox").Text += "-edited"; failed.DraftDecision = () => SettingsDraftCloseChoice.Save;
            var args = new CancelEventArgs(); failed.SettingsClosing(failed, args); Check(args.Cancel && Control<Button>(failed, "SaveSettingsButton").Content!.ToString() == "Save settings", "Failed save discarded the draft."); }
        finally { CloseFixture(failed); }
    }
    private static void DraftExit()
    {
        var app = FakeApp(); var s = Settings(TempRoot()); Set(app, "_settings", s); var window = Window(s, new MemoryStore()); app.AttachSettingsWindow(window);
        var finals = 0; var prompts = 0; Task<bool>? nested = null;
        try
        {
            Control<TextBox>(window, "ScreenshotDirectoryBox").Text += "-draft";
            window.DraftDecision = () => { prompts++; nested = app.RequestManagedShutdownAsync(finalShutdown: () => finals++); return SettingsDraftCloseChoice.Cancel; };
            Check(!app.RequestManagedShutdownAsync(finalShutdown: () => finals++).GetAwaiter().GetResult()
                && !nested!.GetAwaiter().GetResult() && prompts == 1 && finals == 0 && !Field<bool>(app, "_translationClosing") && window.IsEnabled,
                "Cancelled/reentrant tray exit retired resources or duplicated its prompt.");
            using (Field<AsyncResourceLifetime>(app, "_translationRequests").Enter()) { }
            window.DraftDecision = () => SettingsDraftCloseChoice.Discard;
            Check(app.RequestManagedShutdownAsync(finalShutdown: () => finals++).GetAwaiter().GetResult() && finals == 1, "Confirmed exit did not reach final shutdown.");
        }
        finally { CloseFixture(window); }
    }
    private static async Task ExitPending()
    {
        var app = FakeApp(); var lease = Field<AsyncResourceLifetime>(app, "_translationRequests").Enter(); var finals = 0;
        Check(!await app.RequestManagedShutdownAsync(finalShutdown: () => finals++, drainBudget: TimeSpan.FromMilliseconds(20))
            && app.LastExitWarning!.Contains("仍有") && finals == 0, "Pending owner was released or reported as a failure.");
        lease.Dispose(); Check(await app.RequestManagedShutdownAsync(finalShutdown: () => finals++) && finals == 1, "Physically drained retry could not finish.");
        Check(await app.RequestManagedShutdownAsync(finalShutdown: () => finals++) && finals == 1, "Completed exit called final shutdown twice.");
    }
    private static async Task ExitFailure()
    {
        var app = FakeApp(); var registry = Field<ResourceRetirementRegistry>(app, "_translationRetirements"); registry.Track("failed-owner", Task.FromException(new IOException("synthetic retirement")));
        var finals = 0;
        for (var i = 0; i < 2; i++) Check(!await app.RequestManagedShutdownAsync(finalShutdown: () => finals++)
            && app.LastExitWarning!.Contains("释放失败") && !app.LastExitWarning.Contains("稍后重试") && finals == 0,
            "Completed release failure was disguised as pending/retry or forced through shutdown.");
        Check((await registry.DrainAsync(TimeSpan.FromSeconds(1))).Failures.Any(f => f.StartsWith("failed-owner")), "Retry hid the failed owner.");
    }
    private static void PlatformExit()
    {
        var app = FakeApp(); var store = new MemoryStore(); var window = Window(Settings(TempRoot()), store); app.AttachSettingsWindow(window);
        try
        {
            Control<TextBox>(window, "ScreenshotDirectoryBox").Text += "-draft"; window.DraftDecision = () => throw new Exception("Platform shutdown prompted interactively");
            var ending = (SessionEndingCancelEventArgs)Activator.CreateInstance(typeof(SessionEndingCancelEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null, [ReasonSessionEnding.Logoff], null)!;
            Invoke(app, "OnSessionEnding", ending);
            var closing = new CancelEventArgs(); window.SettingsClosing(window, closing);
            Check(!ending.Cancel && !closing.Cancel && window.DiscardDraftOnClose && store.Saved is null, "Platform shutdown was cancelled or silently saved a draft.");
        }
        finally { CloseFixture(window); }
    }
    private static void UninstallExit()
    {
        var app = FakeApp(); var s = Settings(TempRoot()); Set(app, "_settings", s); var window = Window(s, new MemoryStore()); app.AttachSettingsWindow(window);
        var launched = 0; var finals = 0; window.UninstallConfirmation = () => true; window.LaunchUninstaller = () => launched++;
        window.RequestApplicationExit = launch => app.RequestManagedShutdownAsync(finalShutdown: () => { launch(); finals++; });
        try
        {
            Control<TextBox>(window, "ScreenshotDirectoryBox").Text += "-draft"; Click(window, "UninstallButton");
            Check(launched == 0 && finals == 0 && !Field<bool>(app, "_translationClosing"), "Uninstall launched before draft cancellation.");
            window.DraftDecision = () => SettingsDraftCloseChoice.Discard; Click(window, "UninstallButton");
            SpinWait.SpinUntil(() => finals == 1, 2000); Check(launched == 1 && finals == 1, "Confirmed uninstall did not compose with managed exit.");
        }
        finally { CloseFixture(window); }
    }
    private static void InterfaceSave()
    {
        var s = Settings(TempRoot()); var store = new MemoryStore(); var window = Window(s, store, Pair().Vault); var applied = 0;
        window.SettingsApplied += (_, updated) => { Check(AppInterfaceLanguage.IsOnlyInterfaceChange(s, updated), "Language-only save normalized other settings."); applied++; };
        try
        {
            Control<ComboBox>(window, "InterfaceLanguageBox").SelectedIndex = 1; Click(window, "SaveSettingsButton");
            Check(store.Saved!.CredentialName == s.CredentialName && store.Saved.DefaultOutputLayoutRoot == s.DefaultOutputLayoutRoot
                && store.Saved.ScreenshotDirectory == s.ScreenshotDirectory && applied == 1
                && Control<TextBlock>(window, "CredentialSlotStatusText").Text.Contains("selected-B")
                && Control<Button>(window, "SaveCredentialButton").Content!.ToString()!.Contains("当前凭据"), "Language-only save changed selected identity/default folders or mislabeled B as fixed A.");
        }
        finally { CloseFixture(window); }
    }
    private static void LegacySettings()
    {
        // Audited existing cases use only their own TemporaryWorkspace/fake vault;
        // no default Program suite (clipboard/native/provider probes) is dispatched.
        string[] names = ["PreservesUnmarkedCompleteLegacyOutputLayout", "PreservesUnmarkedSpacedLongCaptureLayout",
            "MarksConfirmedDefaultLayoutForStartupPersistence", "PreservesExplicitlyCustomOutputDirectories",
            "MigratesSelectedLegacyCredentialIntoFixedSlot", "MigratesCredentialBeforeApplicationStartupRouting",
            "RecoversInvalidSettingsJsonWithWarning", "RecoversPersistedHotkeyConflictsWithWarning"];
        foreach (var name in names) typeof(Program).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        Console.WriteLine($"LEGACY_SETTINGS {names.Length}/{names.Length}");
    }
    private sealed class NoStartup : IStartupRegistration { public void SetEnabled(bool value, string path) => throw new Exception("Unexpected startup write"); }
    private sealed class MemoryStore : ISettingsStore
    {
        internal AppSettings? Saved; internal bool Fail;
        public AppSettings Load() => Saved!; public SettingsLoadResult LoadRecoverable() => new(Saved!, null);
        public void Save(AppSettings s) { if (Fail) throw new IOException("synthetic save"); Saved = s; }
    }
    private sealed class Cloud : IDeepSeekTranslationProvider
    {
        internal string? Name; internal bool Success = true;
        public Task<TranslationResult> TranslateAsync(TranslationRequest r, string name, CancellationToken t) { Name = name; return Task.FromResult(new TranslationResult(Success, Success ? "translated" : "", Success ? "Connection test succeeded." : "synthetic provider failure")); }
        public Task<IReadOnlyList<TranslationResult>> TranslateBlocksAsync(IReadOnlyList<TranslationRequest> r, string name, CancellationToken t) => throw new Exception("Unexpected cloud translation");
        public Task<TranslationResult> TestConnectionAsync(string name, string model, CancellationToken t) => TranslateAsync(null!, name, t);
        public Task<TranslationResult> TestCandidateConnectionAsync(string key, string model, CancellationToken t) => TranslateAsync(null!, "candidate", t);
    }
    private sealed class Hotkeys : IGlobalHotkeyHost
    {
        internal readonly Dictionary<GlobalHotkey, Action> Active = []; internal readonly HashSet<GlobalHotkey> Reject = [];
        internal int Registrations; internal bool Disposed;
        public bool TryRegister(GlobalHotkey key, Action action) { Registrations++; if (Reject.Contains(key)) return false; Active.Add(key, action); return true; }
        public bool TryUnregister(GlobalHotkey key) => Active.Remove(key);
        public void Dispose() { Disposed = true; Active.Clear(); }
    }
}
