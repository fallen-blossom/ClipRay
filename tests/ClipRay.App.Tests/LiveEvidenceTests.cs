using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Translation;

internal static class LiveEvidenceTests
{
    public static void BundleHasLinkedContentAndChecksums()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClipRay-live-evidence-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var priorRoot = Environment.GetEnvironmentVariable("CLIPRAY_LIVE_ACCEPTANCE_ROOT");
            try
            {
                Environment.SetEnvironmentVariable("CLIPRAY_LIVE_ACCEPTANCE_ROOT", root);
                using (LiveTranslationWorkContext.Push(new LiveTranslationWorkIdentity(7, 8, 9)))
                {
                    LiveTranslationAcceptanceTrace.RecordProtectedFailure(
                        "nine.Keepticket XR-17", "九点出发，请保留 XR-17 车票");
                    LiveTranslationAcceptanceTrace.RecordIntegrityFailure(
                        "ticketXR-17until", "车票 XR-17",
                        TranslationInvariantMismatch.Number);
                }
                var failures = File.ReadAllLines(Path.Combine(root, "integrity-failures.jsonl"));
                if (failures.Length != 2) throw new Exception("Machine failure evidence lost an event.");
                using var protectedFailure = JsonDocument.Parse(failures[0]);
                using var integrityFailure = JsonDocument.Parse(failures[1]);
                if (protectedFailure.RootElement.GetProperty("source").GetString() != "nine.Keepticket XR-17"
                    || protectedFailure.RootElement.GetProperty("restored").GetString() != "九点出发，请保留 XR-17 车票"
                    || protectedFailure.RootElement.GetProperty("reason").GetString() != "machine-protected-content-failed"
                    || protectedFailure.RootElement.GetProperty("capture_id").GetInt64() != 7
                    || protectedFailure.RootElement.GetProperty("pipeline_id").GetInt64() != 8
                    || integrityFailure.RootElement.GetProperty("source").GetString() != "ticketXR-17until"
                    || integrityFailure.RootElement.GetProperty("restored").GetString() != "车票 XR-17"
                    || integrityFailure.RootElement.GetProperty("mismatch").GetString() != "Number")
                    throw new Exception("Machine failure evidence lost source, rejected output, or frame identity.");
            }
            finally { Environment.SetEnvironmentVariable("CLIPRAY_LIVE_ACCEPTANCE_ROOT", priorRoot); }
            using (var rejectedNetworkTrace = new LiveTranslationAcceptanceTrace(@"\\server\share"))
                if (rejectedNetworkTrace.Enabled)
                    throw new Exception("Private live evidence accepted a network root.");
            var identity = new LiveTranslationWorkIdentity(1, 2, 3);
            string sessionDirectory;
            using (var trace = new LiveTranslationAcceptanceTrace(root))
            {
                if (!trace.Enabled || trace.SessionDirectory is null)
                    throw new Exception("Local evidence trace did not start.");
                sessionDirectory = trace.SessionDirectory;
                trace.Record("session_context", data: new {
                    AppVersion = "1.0.0",
                    ExecutableSha256 = new string('a', 64),
                    Region = new Rectangle(0, 0, 2, 2),
                    Monitor = new Rectangle(0, 0, 2, 2),
                    Route = "Machine",
                    TargetLanguage = "简体中文",
                    ShowInRecordings = false
                });
                using var source = new Bitmap(2, 2);
                trace.SaveSource(source, identity);
                trace.Record("translation_requests", identity: identity, data: new { text = "hello" });
                trace.Record("translation_results", identity: identity, data: new { text = "你好" });
                trace.Record("region_decisions", identity: identity, data: new { layout = "aligned" });
                trace.Record("applied", 1, identity, new { State = "Ready", Layouts = 1 });
                var pixels = new byte[16];
                var rendered = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32,
                    null, pixels, 8);
                trace.SaveRendered(rendered, 1, identity);
                trace.Record("render_cycle_observed", 1, identity, new { Visible = true, Layouts = 1 });
                trace.Record("ended");
                if (trace.WriteFailures != 0)
                    throw new Exception("Evidence image or event write failed.");
            }

            var eventsPath = Path.Combine(sessionDirectory, "events.jsonl");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "manifest.json")));
            var metadata = manifest.RootElement;
            var lines = File.ReadAllLines(eventsPath);
            if (!metadata.GetProperty("complete").GetBoolean()
                || !metadata.GetProperty("first_source_rendered").GetBoolean()
                || !metadata.GetProperty("first_source_was_first_render").GetBoolean()
                || metadata.GetProperty("event_count").GetInt32() != lines.Length
                || metadata.GetProperty("write_failures").GetInt32() != 0
                || metadata.GetProperty("events_sha256").GetString()
                    != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(eventsPath))).ToLowerInvariant())
                throw new Exception("Evidence manifest does not prove a complete event file.");
            var kinds = new List<string>();
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                var item = document.RootElement;
                kinds.Add(item.GetProperty("kind").GetString()!);
                if (item.GetProperty("sequence").GetInt32() != kinds.Count)
                    throw new Exception("Evidence sequence is not contiguous.");
                if (kinds[^1] is not ("source_image_saved" or "rendered_image_saved")) continue;
                var data = item.GetProperty("data");
                var imagePath = Path.Combine(sessionDirectory, data.GetProperty("name").GetString()!);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath))).ToLowerInvariant();
                if (data.GetProperty("sha256").GetString() != hash)
                    throw new Exception("Evidence image checksum does not match its artifact.");
                if (item.GetProperty("capture_id").GetInt64() != identity.CaptureId
                    || item.GetProperty("pipeline_id").GetInt64() != identity.PipelineId)
                    throw new Exception("Evidence image lost its pipeline identity.");
            }
            if (kinds[0] != "started" || kinds[^1] != "trace_closed"
                || !kinds.Contains("translation_requests") || !kinds.Contains("translation_results")
                || !kinds.Contains("source_image_saved") || !kinds.Contains("rendered_image_saved")
                || !kinds.Contains("render_cycle_observed"))
                throw new Exception("Evidence bundle lost a required stage.");

            string invalidContextDirectory;
            using (var trace = new LiveTranslationAcceptanceTrace(root))
            {
                invalidContextDirectory = trace.SessionDirectory!;
                trace.Record("session_context", data: new { Route = "Machine" });
                using var source = new Bitmap(2, 2);
                trace.SaveSource(source, identity);
                trace.Record("applied", 1, identity, new { State = "Ready", Layouts = 1 });
                var rendered = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32,
                    null, new byte[16], 8);
                trace.SaveRendered(rendered, 1, identity);
                trace.Record("render_cycle_observed", 1, identity,
                    new { Visible = true, Layouts = 1 });
                trace.Record("ended");
            }
            using var invalidContext = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(invalidContextDirectory, "manifest.json")));
            if (invalidContext.RootElement.GetProperty("complete").GetBoolean()
                || invalidContext.RootElement.GetProperty("has_context").GetBoolean())
                throw new Exception("A missing session version or viewport context was claimed complete.");

            string incompleteDirectory;
            using (var trace = new LiveTranslationAcceptanceTrace(root))
            {
                incompleteDirectory = trace.SessionDirectory!;
                trace.Record("session_context");
                trace.Record("applied", 2, identity, new { State = "Ready", Layouts = 1 });
                trace.Record("render_cycle_not_observed", 2, identity,
                    new { Reason = "render-or-visibility-check-failed" });
                trace.Record("ended");
            }
            using var incomplete = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(incompleteDirectory, "manifest.json")));
            if (incomplete.RootElement.GetProperty("complete").GetBoolean()
                || incomplete.RootElement.GetProperty("missing_rendered_frame_count").GetInt32() != 1
                || incomplete.RootElement.GetProperty("missing_render_cycle_count").GetInt32() != 1
                || incomplete.RootElement.GetProperty("failed_render_cycle_count").GetInt32() != 1)
                throw new Exception("Missing rendered or acknowledged live frame was claimed complete.");

            string skippedFirstDirectory;
            using (var trace = new LiveTranslationAcceptanceTrace(root))
            {
                skippedFirstDirectory = trace.SessionDirectory!;
                trace.Record("session_context", data: new { route = "Machine" });
                using var source = new Bitmap(2, 2);
                var first = new LiveTranslationWorkIdentity(11, 11, 11);
                var second = new LiveTranslationWorkIdentity(12, 12, 12);
                trace.SaveSource(source, first);
                trace.Record("region_decisions", identity: first);
                trace.SaveSource(source, second);
                trace.Record("region_decisions", identity: second);
                trace.Record("applied", 2, second, new { State = "Ready", Layouts = 1 });
                var pixels = new byte[16];
                var rendered = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32,
                    null, pixels, 8);
                trace.SaveRendered(rendered, 2, second);
                trace.Record("render_cycle_observed", 2, second,
                    new { Visible = true, Layouts = 1 });
                trace.Record("ended");
                if (trace.WriteFailures != 0)
                    throw new Exception("First-frame counterexample failed to write.");
            }
            using var skippedFirst = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(skippedFirstDirectory, "manifest.json")));
            if (skippedFirst.RootElement.GetProperty("complete").GetBoolean()
                || skippedFirst.RootElement.GetProperty("source_frame_count").GetInt32() != 2
                || skippedFirst.RootElement.GetProperty("first_source_rendered").GetBoolean()
                || skippedFirst.RootElement.GetProperty("first_source_was_first_render").GetBoolean())
                throw new Exception("Second live frame made an unrendered first source look complete.");

            string lateFirstDirectory;
            using (var trace = new LiveTranslationAcceptanceTrace(root))
            {
                lateFirstDirectory = trace.SessionDirectory!;
                trace.Record("session_context");
                using var source = new Bitmap(2, 2);
                var first = new LiveTranslationWorkIdentity(21, 21, 21);
                var second = new LiveTranslationWorkIdentity(22, 22, 22);
                trace.SaveSource(source, first);
                trace.SaveSource(source, second);
                var rendered = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32,
                    null, new byte[16], 8);
                trace.Record("applied", 2, second, new { State = "Ready", Layouts = 1 });
                trace.SaveRendered(rendered, 2, second);
                trace.Record("render_cycle_observed", 2, second,
                    new { Visible = true, Layouts = 1 });
                trace.Record("applied", 1, first, new { State = "Ready", Layouts = 1 });
                trace.SaveRendered(rendered, 1, first);
                trace.Record("render_cycle_observed", 1, first,
                    new { Visible = true, Layouts = 1 });
                trace.Record("ended");
            }
            using var lateFirst = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(lateFirstDirectory, "manifest.json")));
            if (lateFirst.RootElement.GetProperty("complete").GetBoolean()
                || !lateFirst.RootElement.GetProperty("first_source_rendered").GetBoolean()
                || lateFirst.RootElement.GetProperty("first_source_was_first_render").GetBoolean())
                throw new Exception("A late first-source render was mistaken for the first frame.");

            string invisibleFirstDirectory;
            using (var trace = new LiveTranslationAcceptanceTrace(root))
            {
                invisibleFirstDirectory = trace.SessionDirectory!;
                trace.Record("session_context");
                using var source = new Bitmap(2, 2);
                var first = new LiveTranslationWorkIdentity(31, 31, 31);
                trace.SaveSource(source, first);
                trace.Record("applied", 1, first, new { State = "Ready", Layouts = 1 });
                var rendered = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32,
                    null, new byte[16], 8);
                trace.SaveRendered(rendered, 1, first);
                trace.Record("render_cycle_observed", 1, first,
                    new { Visible = false, Layouts = 1 });
                trace.Record("ended");
            }
            using var invisibleFirst = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(invisibleFirstDirectory, "manifest.json")));
            if (invisibleFirst.RootElement.GetProperty("complete").GetBoolean()
                || invisibleFirst.RootElement.GetProperty("first_source_rendered").GetBoolean()
                || invisibleFirst.RootElement.GetProperty("failed_render_cycle_count").GetInt32() != 1)
                throw new Exception("An invisible first frame was claimed rendered.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    public static void CacheAndWireDiagnosticsAreScoped()
    {
        var diagnostics = new List<LiveTranslationCacheDiagnostic>();
        var providerCalls = 0;
        var cache = new LiveTranslationRequestCache((route, requests, _) =>
        {
            providerCalls++;
            return Task.FromResult(new TranslationBatchOutcome(
                requests.Select(_ => new TranslationResult(true, "译文", "completed")).ToArray(),
                new TranslationExecutionTrace(route, 0, 0, 0)));
        }, diagnostics.Add);
        var request = new TranslationRequest("en", "zh", "A sentence.");
        cache.TranslateAsync(TranslationRouteKind.Machine, [request], default).GetAwaiter().GetResult();
        cache.TranslateAsync(TranslationRouteKind.Machine, [request], default).GetAwaiter().GetResult();
        if (providerCalls != 1 || diagnostics.Count != 4
            || diagnostics[0].Kind != "cache_plan" || diagnostics[0].DistinctMissCount != 1
            || diagnostics[2].Kind != "cache_plan" || !diagnostics[2].HitIndices.SequenceEqual([0]))
            throw new Exception("Cache evidence does not identify reuse.");

        var recorded = new List<string>();
        TranslationWireTrace.Record("outside", null);
        using (TranslationWireTrace.Push((kind, _) => recorded.Add(kind)))
            TranslationWireTrace.Record("inside", new { text = "A sentence." });
        TranslationWireTrace.Record("outside", null);
        if (!recorded.SequenceEqual(["inside"]))
            throw new Exception("Provider wire evidence escaped the opt-in scope.");
    }
}
