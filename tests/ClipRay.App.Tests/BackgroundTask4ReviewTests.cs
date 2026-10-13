using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using ClipRay.App.Infrastructure;
using ClipRay.App.Settings;
using ClipRay.App.Translation;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using PasswordBox = System.Windows.Controls.PasswordBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using static BackgroundTask3Tests;
using static BackgroundTask3LifetimeTests;

internal static partial class BackgroundTask4Tests
{
    private static void LateConnectionExit()
    {
        List<string> failures = [];
        foreach (var scenario in new[] { "accepted-success", "accepted-failure", "accepted-exception", "accepted-provider-exception",
            "cancel-continues", "ordinary-close", "platform-committed" })
        {
            try { LateConnectionScenario(scenario); }
            catch (Exception error) { failures.Add(scenario + ": " + error.Message); }
        }
        Check(failures.Count == 0, string.Join(" | ", failures));
    }

    private static void LateConnectionScenario(string scenario)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var pair = Pair(); var provider = new HeldSettingsCloud(); var s = Settings(TempRoot());
        var window = Window(s, new MemoryStore(), pair.Vault, provider); var app = FakeApp(); Set(app, "_settings", s);
        app.HotkeyHostFactory = () => new Hotkeys(); app.AttachSettingsWindow(window);
        IDisposable? lease = Field<AsyncResourceLifetime>(app, "_translationRequests").Enter();
        var finals = 0; var ordinary = scenario == "ordinary-close"; var platform = scenario == "platform-committed";
        var cancelled = scenario == "cancel-continues"; var accepted = !ordinary && !platform && !cancelled;
        Task? connectionTask = null;
        try
        {
            Control<TextBox>(window, "ScreenshotDirectoryBox").Text += "-draft";
            Control<PasswordBox>(window, "CredentialValueBox").Password = C;
            Click(window, scenario.Contains("provider") ? "TestProviderButton" : "SaveCredentialButton");
            Check(provider.Entered, "Actual connection handler did not dispatch the fake provider.");
            // Before the fix the actual async-void handler has no task seam. The
            // old counterexample uses the same bounded dispatcher pump as review.
            connectionTask = (Task?)window.GetType().GetProperty("ConnectionOperationTask", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
            window.DraftDecision = () => cancelled ? SettingsDraftCloseChoice.Cancel : SettingsDraftCloseChoice.Discard;
            if (ordinary) window.Close();
            else if (platform)
            {
                var ending = (SessionEndingCancelEventArgs)Activator.CreateInstance(typeof(SessionEndingCancelEventArgs),
                    BindingFlags.Instance | BindingFlags.NonPublic, null, [ReasonSessionEnding.Logoff], null)!;
                Invoke(app, "OnSessionEnding", ending); Check(!ending.Cancel, "Platform default was cancelled.");
            }
            else
            {
                var exit = app.RequestManagedShutdownAsync(finalShutdown: () => finals++, drainBudget: TimeSpan.FromMilliseconds(10));
                PumpUntil(() => exit.IsCompleted, 1500); Check(exit.IsCompleted && !exit.Result && finals == 0, "Held exit did not stay pending or cancelled.");
            }
            var beforeStatus = Control<TextBlock>(window, "StatusText").Text;
            var warning = app.LastExitWarning;
            var tokenCancelled = provider.Token.IsCancellationRequested;
            if (scenario.Contains("exception")) provider.Response.TrySetException(new IOException("synthetic late connection failure"));
            else provider.Response.TrySetResult(new(!scenario.Contains("failure"), "translated", "Connection test succeeded."));
            if (connectionTask is not null)
            {
                PumpUntil(() => connectionTask.IsCompleted, 1500);
                Check(connectionTask.IsCompleted, "Actual handler did not finish after releasing the fake response.");
                connectionTask.GetAwaiter().GetResult();
            }
            else PumpUntil(() => false, 200);
            var changed = pair.Vault.GetForUse("selected-B") == C;
            var candidateCleared = Control<PasswordBox>(window, "CredentialValueBox").Password.Length == 0;
            var statusKept = Control<TextBlock>(window, "StatusText").Text == beforeStatus;
            Console.WriteLine($"CONNECTION {scenario} tokenCancelled={tokenCancelled} vaultChanged={changed} candidateCleared={candidateCleared} statusKept={statusKept} handlerCompleted={connectionTask?.IsCompleted} finals={finals}");
            Check(pair.Vault.GetForUse(ClipRay.App.Security.CredentialVault.FixedDeepSeekSlot) == A, "Connection changed unrelated A.");
            if (cancelled)
            {
                Check(!tokenCancelled && changed && candidateCleared && window.IsEnabled && !Field<bool>(app, "_translationClosing"), "Cancel exit cancelled current verification or lost editing/admission.");
                using (Field<AsyncResourceLifetime>(app, "_translationRequests").Enter()) { }
            }
            else
            {
                Check(tokenCancelled && !changed && !candidateCleared && statusKept, "Retired Settings operation still delivered a late result.");
                Check(!Control<Button>(window, scenario.Contains("provider") ? "TestProviderButton" : "SaveCredentialButton").IsEnabled,
                    "Late finally re-enabled a retired connection action.");
                if (accepted) Check(!window.IsEnabled && warning == app.LastExitWarning && beforeStatus == warning && Field<bool>(app, "_translationClosing"),
                    "Late callback hid exit warning or reopened an accepted retirement.");
            }
        }
        finally
        {
            provider.Response.TrySetResult(new(true, "translated", "Connection test succeeded."));
            if (connectionTask is not null) PumpUntil(() => connectionTask.IsCompleted, 1500);
            lease.Dispose(); lease = null;
            var finish = app.RequestManagedShutdownAsync(() => Task.FromResult(true), () => { }, TimeSpan.FromSeconds(1));
            PumpUntil(() => finish.IsCompleted, 1500); Check(finish.IsCompleted && finish.Result, "Fixture physical owner failed to drain.");
            CloseFixture(window); Invoke(app, "ReleaseHotkeys");
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void HotkeyStatusLifecycle()
    {
        var s = Settings(TempRoot()); var app = FakeApp(); Set(app, "_settings", s);
        var host = new Hotkeys(); app.HotkeyHostFactory = () => host; Invoke(app, "RegisterHotkeys");
        var requested = GlobalHotkey.Parse("Ctrl+Alt+Y"); host.Reject.Add(requested);
        var store = new MemoryStore(); var window = Window(s, store, Pair().Vault); app.AttachSettingsWindow(window);
        try
        {
            Invoke(window, "SetHotkeyValue", Control<TextBox>(window, "StopLiveTranslationHotkeyBox"), requested);
            Click(window, "SaveSettingsButton"); CloseFixture(window);
            window = Window(store.Saved!, store, Pair().Vault); app.AttachSettingsWindow(window);
            AssertHotkeyWarning(window, requested, s.StopLiveTranslationHotkey);
            Check(Control<TextBlock>(window, "CredentialDescriptionText").Text.Contains("legacy credential names"), "English credential description still claimed fixed-only support.");
            var registrations = host.Registrations; Click(window, "SaveSettingsButton");
            Check(host.Registrations == registrations, "Refresh pretended to retry hotkey registration.");
            AssertHotkeyWarning(window, requested, s.StopLiveTranslationHotkey);
            Click(window, "TestProviderButton"); AssertHotkeyWarning(window, requested, s.StopLiveTranslationHotkey);
            Control<ComboBox>(window, "InterfaceLanguageBox").SelectedIndex = 1; Click(window, "SaveSettingsButton");
            AssertHotkeyWarning(window, requested, s.StopLiveTranslationHotkey);
            Check(Control<TextBlock>(window, "HotkeyRegistrationWarningText").Text.Contains("原绑定仍可用")
                && Control<TextBlock>(window, "CredentialDescriptionText").Text.Contains("旧凭据名称"), "Language-only save lost current hotkey/legacy-credential explanation.");
            using (var menu = app.CreateTrayMenu())
            {
                var stop = menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Single(i => i.Text == "停止实时翻译");
                stop.Enabled = true; stop.PerformClick(); Check(!stop.Enabled, "Tray Stop lost reachability after reopen/refresh.");
            }
            host.Reject.Clear(); Control<TextBox>(window, "RecordingDirectoryBox").Text += "-recovered"; Click(window, "SaveSettingsButton");
            Check(Control<TextBlock>(window, "HotkeyRegistrationWarningText").Visibility == Visibility.Collapsed
                && host.Active.ContainsKey(requested) && !host.Active.ContainsKey(s.StopLiveTranslationHotkey), "Actual registration recovery did not clear persistent conflict.");
        }
        finally { CloseFixture(window); Invoke(app, "ReleaseHotkeys"); }
    }
    private static void AssertHotkeyWarning(SettingsWindow window, GlobalHotkey failed, GlobalHotkey retained)
    {
        var warning = window.FindName("HotkeyRegistrationWarningText") as TextBlock;
        Check(warning is { Visibility: Visibility.Visible } && warning.Text.Contains(failed.ToString()) && warning.Text.Contains(retained.ToString()),
            "Attach/reopen/ordinary feedback hid requested failure or actual retained key.");
    }
    private static void PumpUntil(Func<bool> done, int milliseconds)
    {
        if (done()) return;
        var frame = new DispatcherFrame(); var expires = DateTime.UtcNow.AddMilliseconds(milliseconds);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow >= expires) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
    private sealed class HeldSettingsCloud : IDeepSeekTranslationProvider
    {
        internal readonly TaskCompletionSource<TranslationResult> Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token; internal bool Entered;
        private Task<TranslationResult> Hold(CancellationToken token) { Entered = true; Token = token; return Response.Task; }
        public Task<TranslationResult> TestCandidateConnectionAsync(string key, string model, CancellationToken token) => Hold(token);
        public Task<TranslationResult> TestConnectionAsync(string name, string model, CancellationToken token) => Hold(token);
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, string name, CancellationToken token) => throw new Exception("Unexpected cloud translation");
        public Task<IReadOnlyList<TranslationResult>> TranslateBlocksAsync(IReadOnlyList<TranslationRequest> requests, string name, CancellationToken token) => throw new Exception("Unexpected cloud batch");
    }
}
