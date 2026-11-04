using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using ClipRay.App.Infrastructure;

public static class AppCliProtocolTests
{
    private static readonly string Exe = Path.Combine(Path.GetTempPath(), "clipray-cli-fixture", "ClipRay.App.exe");
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "clipray-cli-fixture-output");

    public static void Run()
    {
        ParseAccepted();
        ParseRejected();
        ValidatePeerRequests();
        PipeIdentity();
        PipeRoundTripAndDeduplication().GetAwaiter().GetResult();
        PipeRejectsMalformedPeers().GetAwaiter().GetResult();
        PipeCacheIsBoundedWithoutForgetting().GetAwaiter().GetResult();
        PipePreservesUnknownOutcomes().GetAwaiter().GetResult();
        PipeSanitizesHandlerErrors().GetAwaiter().GetResult();
        HelpAndMissingInstance().GetAwaiter().GetResult();
    }

    private static void ParseAccepted()
    {
        foreach (var command in new[] { "help", "status", "settings-open", "settings-close", "translation-open", "recording-settings" })
        {
            var request = Parse("--cli", command);
            Check(request.Command == command && request.RequestId != Guid.Empty && request.Protocol == 1, "Simple command was not parsed.");
            Check(request.FramesPerSecond is null && request.IncludeCursor is null && request.IncludeSystemAudio is null,
                "A read command acquired an implicit settings patch.");
        }
        var stableId = Guid.NewGuid();
        var patch = Parse("--cli", "recording-settings", "--fps", "60", "--quality", "balanced", "--cursor", "false",
            "--audio", "true", "--request-id", stableId.ToString());
        Check(patch.RequestId == stableId && patch.FramesPerSecond == 60 && patch.Quality == "Balanced"
            && patch.IncludeCursor == false && patch.IncludeSystemAudio == true, "Patch values/default distinctions were lost.");
        foreach (var fps in new[] { 15, 24, 30, 60 })
            Check(Parse("--cli", "recording-settings", "--fps", fps.ToString()).FramesPerSecond == fps, "Supported fps rejected.");
        foreach (var quality in new[] { "Compact", "Balanced", "High" })
            Check(Parse("--cli", "recording-settings", "--quality", quality).Quality == quality, "Supported quality rejected.");
        var start = Parse("--cli", "recording-start", "--region", "-1920", "-30", "1920", "1080", "--output", Output,
            "--no-copy", "--show-ui");
        Check(start.Region!.SequenceEqual(new[] { -1920, -30, 1920, 1080 }) && start.OutputDirectory == Output
            && start.NoCopy && start.ShowUi, "Start options or negative monitor coordinates were lost.");
        var run = Guid.NewGuid();
        Check(Parse("--cli", "recording-stop", "--run-id", run.ToString()).RunId == run, "Stop lost its recording identity.");
        Check(AppCliProtocol.IsRequested(["--cli", "status"]) && AppCliProtocol.IsRequested(["--interactive", "--cli", "status"])
            && !AppCliProtocol.IsRequested(["--interactive"]), "CLI activation detection changed.");
    }

    private static void ParseRejected()
    {
        string[][] cases =
        [
            [], ["--cli"], ["--interactive", "--cli", "status"], ["--cli", "unknown"],
            ["--cli", "help", "--unknown"], ["--cli", "status", "--fps", "60"],
            ["--cli", "settings-close", "--show-ui"], ["--cli", "recording-settings", "--fps", "60", "--FPS", "30"],
            ["--cli", "translation-open", "--fps", "30"], ["--cli", "translation-open", "--show-ui"],
            ["--cli", "recording-settings", "--fps"], ["--cli", "recording-settings", "--fps", "59"],
            ["--cli", "recording-settings", "--fps", "NaN"], ["--cli", "recording-settings", "--quality", "1"],
            ["--cli", "recording-settings", "--quality", ""], ["--cli", "recording-settings", "--cursor", "1"],
            ["--cli", "recording-settings", "--audio", "yes"], ["--cli", "recording-settings", "--run-id", Guid.NewGuid().ToString()],
            ["--cli", "recording-start"], ["--cli", "recording-start", "--region", "0", "0", "10"],
            ["--cli", "recording-start", "--region", "0", "0", "0", "100"],
            ["--cli", "recording-start", "--region", "2147483647", "0", "10", "100"],
            ["--cli", "recording-start", "--region", "0", "0", "100", "100", "--output", "relative-output"],
            ["--cli", "recording-start", "--region", "0", "0", "100", "100", "--fps", "60"],
            ["--cli", "recording-start", "--region", "0", "0", "100", "100", "--no-copy", "--no-copy"],
            ["--cli", "recording-stop"], ["--cli", "recording-stop", "--run-id", Guid.Empty.ToString()],
            ["--cli", "recording-stop", "--run-id", Guid.NewGuid().ToString(), "--cursor", "true"],
            ["--cli", "status", "--request-id", "bad-id"], ["--cli", "status", "--request-id", Guid.Empty.ToString()],
            ["--cli", "status", "--request-id", Guid.NewGuid().ToString(), "--request-id", Guid.NewGuid().ToString()],
            ["--cli", "status", "unexpected-positional"], ["--cli", "status", "--request-install-exit"]
        ];
        foreach (var args in cases)
            Check(!AppCliProtocol.TryParse(args, Exe, out var request, out var error) && request is null && !string.IsNullOrEmpty(error),
                "Invalid CLI arguments were accepted: " + string.Join(' ', args.Take(3)));
        Check(!AppCliProtocol.TryParse(["--cli", "status"], "relative.exe", out _, out _), "Relative executable identity was accepted.");
    }

    private static void ValidatePeerRequests()
    {
        var original = new AppCliRequest(1, Guid.NewGuid(), Exe, "status");
        AppCliRequest[] invalid =
        [
            original with { Protocol = 2 }, original with { RequestId = Guid.Empty },
            original with { ExecutablePath = Path.Combine(Path.GetTempPath(), "sibling", "ClipRay.App.exe") },
            original with { Command = "unknown" }, original with { Command = null! }, original with { FramesPerSecond = 60 },
            original with { Command = "recording-settings", FramesPerSecond = 1000 },
            original with { Command = "recording-settings", Quality = "1" }, original with { Region = [0, 0, 100, 100] },
            original with { Command = "recording-start", Region = [0, 0, 100] },
            original with { Command = "recording-start", Region = [0, 0, -1, 100] },
            original with { Command = "recording-start", Region = [0, int.MaxValue, 100, 1] },
            original with { Command = "recording-stop", RunId = Guid.Empty }, original with { Command = "recording-stop" },
            original with { ShowUi = true }, original with { NoCopy = true }, original with { IncludeSystemAudio = false }
        ];
        foreach (var request in invalid) Check(!AppCliProtocol.Validate(request, Exe, out _), "Direct peer bypassed request validation.");
        Check(AppCliProtocol.Validate(original with { Command = "recording-settings", IncludeSystemAudio = false }, Exe, out _),
            "A false-valued settings patch was treated as absent.");
    }

    private static void PipeIdentity()
    {
        var first = AppCliProtocol.PipeName("scope-a", Exe);
        Check(first == AppCliProtocol.PipeName("scope-a", Exe.ToUpperInvariant()), "Windows path case changed the endpoint.");
        Check(first != AppCliProtocol.PipeName("scope-b", Exe)
            && first != AppCliProtocol.PipeName("scope-a", Path.Combine(Path.GetTempPath(), "sibling", "ClipRay.App.exe")),
            "Endpoint did not isolate scope and exact installation path.");
        Check(first != InstallExitProtocol.PipeName("scope-a") && first.StartsWith("ClipRay.Cli.v1.", StringComparison.Ordinal),
            "CLI reused the installer/activation endpoint.");
    }

    private static async Task PipeRoundTripAndDeduplication()
    {
        var scope = NewScope();
        var writes = 0;
        var reads = 0;
        var entered = Signal();
        var release = Signal();
        using var server = new AppCliServer(scope, Exe, async request =>
        {
            if (request.FramesPerSecond is not null)
            {
                Interlocked.Increment(ref writes);
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            }
            else Interlocked.Increment(ref reads);
            return new(1, request.RequestId, true, "fixture-ok", 0, new { fps = request.FramesPerSecond });
        });
        server.Start();
        var patch = new AppCliRequest(1, Guid.NewGuid(), Exe, "recording-settings", FramesPerSecond: 60);
        var first = AppCliProtocol.SendAsync(scope, patch);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var duplicate = AppCliProtocol.SendAsync(scope, patch);
        try
        {
            var conflict = await AppCliProtocol.SendAsync(scope, patch with { FramesPerSecond = 30 }).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Check(!conflict.Success && conflict.Status == "request-id-conflict" && writes == 1, "Conflicting mutation reused an accepted ID.");
        }
        finally { release.TrySetResult(true); }
        var results = await Task.WhenAll(first, duplicate).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Check(results.All(result => result.Success) && writes == 1
            && JsonSerializer.Serialize(results[0].Data) == JsonSerializer.Serialize(results[1].Data), "Concurrent duplicate mutations ran twice or lost the result.");
        var cached = await AppCliProtocol.SendAsync(scope, patch).ConfigureAwait(false);
        Check(cached.Success && writes == 1, "Completed mutation was forgotten.");
        var read = new AppCliRequest(1, Guid.NewGuid(), Exe, "recording-settings");
        Check((await AppCliProtocol.SendAsync(scope, read).ConfigureAwait(false)).Success
            && (await AppCliProtocol.SendAsync(scope, read).ConfigureAwait(false)).Success && reads == 2, "Read requests were incorrectly cached or patched.");
    }

    private static async Task PipeRejectsMalformedPeers()
    {
        var scope = NewScope();
        var calls = 0;
        using var server = new AppCliServer(scope, Exe, request =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new AppCliResponse(1, request.RequestId, true, "fixture-ok", 0));
        });
        server.Start();
        var read = new AppCliRequest(1, Guid.NewGuid(), Exe, "status");
        var json = JsonSerializer.Serialize(read);
        foreach (var line in new[]
        {
            json[..^1] + ",\"Unexpected\":true}", json[..^1] + ",\"Command\":\"recording-start\"}",
            JsonSerializer.Serialize(read with { Protocol = 2 }), JsonSerializer.Serialize(read with { RequestId = Guid.Empty }),
            JsonSerializer.Serialize(read with { ExecutablePath = Path.Combine(Path.GetTempPath(), "sibling", "ClipRay.App.exe") }),
            JsonSerializer.Serialize(read with { Command = "recording-start", Region = [0, 0, -1, 100] }),
            new string(' ', AppCliProtocol.MaximumRequestCharacters + 1)
        })
        {
            var response = await RawRequest(scope, line).ConfigureAwait(false);
            Check(!response.Success && response.Status == "invalid-request" && response.ExitCode == 2, "Malformed peer request reached handler.");
        }
        Check(calls == 0, "Rejected wire requests invoked the app handler.");
    }

    private static async Task PipeCacheIsBoundedWithoutForgetting()
    {
        var scope = NewScope();
        var calls = 0;
        using var server = new AppCliServer(scope, Exe, request =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new AppCliResponse(1, request.RequestId, true, "fixture-saved", 0));
        });
        server.Start();
        var first = new AppCliRequest(1, Guid.NewGuid(), Exe, "settings-close");
        for (var index = 0; index < 127; index++)
        {
            var request = index == 0 ? first : first with { RequestId = Guid.NewGuid() };
            Check((await AppCliProtocol.SendAsync(scope, request).ConfigureAwait(false)).Success, "Cache rejected a request before its bound.");
        }
        var start = new AppCliRequest(1, Guid.NewGuid(), Exe, "recording-start", Region:[0,0,100,100]);
        Check((await AppCliProtocol.SendAsync(scope, start).ConfigureAwait(false)).Success && calls==128,
            "The last replay-cache slot did not accept a recording start.");
        var full = await AppCliProtocol.SendAsync(scope, first with { RequestId = Guid.NewGuid() }).ConfigureAwait(false);
        Check(!full.Success && full.Status == "mutation-cache-full" && calls == 128, "Full cache forgot an old write or executed an extra one.");
        Check((await AppCliProtocol.SendAsync(scope, first).ConfigureAwait(false)).Success && calls == 128,
            "Full cache could not return a previously accepted result.");
        Check((await AppCliProtocol.SendAsync(scope, new(1, Guid.NewGuid(), Exe, "status")).ConfigureAwait(false)).Success && calls == 129,
            "Full mutation cache blocked a harmless read.");
        var stop = new AppCliRequest(1, Guid.NewGuid(), Exe, "recording-stop", RunId:Guid.NewGuid());
        Check((await AppCliProtocol.SendAsync(scope, stop).ConfigureAwait(false)).Success && calls==130,
            "A full replay cache blocked stopping the recording it accepted.");
        Check((await AppCliProtocol.SendAsync(scope, stop).ConfigureAwait(false)).Success && calls==131,
            "RunId-idempotent stop could not query the controller again.");
    }

    private static async Task PipePreservesUnknownOutcomes()
    {
        var scope = NewScope();
        var entered = Signal();
        var release = Signal();
        var writes = 0;
        using var server = new AppCliServer(scope, Exe, async request =>
        {
            Interlocked.Increment(ref writes);
            entered.TrySetResult(true);
            await release.Task.ConfigureAwait(false);
            return new(1, request.RequestId, true, "fixture-saved", 0);
        });
        server.Start();
        var request = new AppCliRequest(1, Guid.NewGuid(), Exe, "recording-start", Region: [0, 0, 100, 100]);
        using var cancellation = new CancellationTokenSource();
        var pending = AppCliProtocol.SendAsync(scope, request, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        cancellation.Cancel();
        var unknown = await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Check(unknown.Status == "request-outcome-unknown" && unknown.ExitCode == 5 && unknown.RequestId == request.RequestId && writes == 1,
            "An accepted-but-interrupted write was reported as not executed or retried.");
        release.TrySetResult(true);
        var recovered = await AppCliProtocol.SendAsync(scope, request).ConfigureAwait(false);
        Check(recovered.Success && writes == 1 && recovered.RequestId == request.RequestId, "Unknown outcome could not recover the original saved result.");
    }

    private static async Task PipeSanitizesHandlerErrors()
    {
        var scope = NewScope();
        const string privateMarker = "fixture-secret-must-not-escape";
        var calls = 0;
        using var server = new AppCliServer(scope, Exe, request =>
        {
            Interlocked.Increment(ref calls);
            throw new IOException(privateMarker);
        });
        server.Start();
        var request = new AppCliRequest(1, Guid.NewGuid(), Exe, "settings-close");
        var response = await AppCliProtocol.SendAsync(scope, request).ConfigureAwait(false);
        Check(!response.Success && response.Status == "handler-error" && !JsonSerializer.Serialize(response).Contains(privateMarker, StringComparison.Ordinal)
            && JsonSerializer.Serialize(response).Contains("IOException", StringComparison.Ordinal), "Handler error leaked its message or lost error type.");
        Check(!(await AppCliProtocol.SendAsync(scope, request).ConfigureAwait(false)).Success && calls == 1, "Failed accepted mutation was automatically executed again.");
    }

    private static async Task HelpAndMissingInstance()
    {
        var scope = NewScope();
        var help = await AppCliProtocol.SendAsync(scope, new(1, Guid.NewGuid(), Exe, "help")).ConfigureAwait(false);
        Check(help.Success && help.Status == "help", "Local help attempted an app connection.");
        var request = new AppCliRequest(1, Guid.NewGuid(), Exe, "status");
        var missing = await AppCliProtocol.SendAsync(scope, request).ConfigureAwait(false);
        Check(!missing.Success && missing.ExitCode == 3 && missing.Status == "not-running" && missing.RequestId == request.RequestId,
            "Missing endpoint did not return not-running with the original request identity.");
    }

    private static async Task<AppCliResponse> RawRequest(string scope, string line)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new NamedPipeClientStream(".", AppCliProtocol.PipeName(scope, Exe), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
        using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, leaveOpen: true);
        await writer.WriteLineAsync(line.AsMemory(), timeout.Token).ConfigureAwait(false);
        var response = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AppCliResponse>(response ?? throw new InvalidOperationException("Pipe returned no response."))!;
    }

    private static AppCliRequest Parse(params string[] args)
    {
        Check(AppCliProtocol.TryParse(args, Exe, out var request, out var error), error ?? "Expected a valid CLI request.");
        return request!;
    }
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string NewScope() => "ClipRay.CliFixture." + Guid.NewGuid().ToString("N");
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
