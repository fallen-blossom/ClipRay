using ClipRay.App.Capture;
using ClipRay.App.Infrastructure;
using ClipRay.App.Recording;
using ClipRay.App.Settings;

internal static class LocalPrivacyIntegrationTests
{
    public static void LocalStartupDoesNotReadLegacyCloudCredential()
    {
        foreach (var provider in new[] { "Machine", "Local LMT" })
        {
            var settings = AppSettings.CreateDefault(System.IO.Path.GetTempPath()) with
                { Provider = provider, HasExplicitProviderChoice = true, CredentialName = "synthetic-legacy-slot" };
            var reads = 0;
            var result = StartupSettingsReconciliation.Apply(new(settings, null, false), settings.StartWithWindows,
                _ => { reads++; return true; }, _ => { });
            if (reads != 0 || result.Settings.CredentialName != settings.CredentialName)
                throw new InvalidOperationException("Local startup accessed or replaced a saved cloud credential.");
        }
    }

    public static void IndependentOutputEntriesRejectRemotePaths()
    {
        var rejected = 0;
        try { _ = new ImageOutputService(@"\\fixture.invalid\share", copyToClipboard: false); }
        catch (LocalPathAccessDeniedException) { rejected++; }
        try { _ = RecordingOutputPaths.Create(@"\\fixture.invalid\share", new DateTime(2026, 10, 25)); }
        catch (LocalPathAccessDeniedException) { rejected++; }
        if (rejected != 2) throw new InvalidOperationException("An independent output entry accepted a remote sink.");
    }
}
