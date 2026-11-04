using System.Diagnostics;
using System.Net;
using System.Net.Http;
using ClipRay.App.Infrastructure;

internal static class ModelNetworkAccessPolicyTests
{
    public static void DefaultDenyPreventsCredentialAndTransportUse()
    {
        using var policy = new ModelNetworkAccessPolicy();
        var credentialReads = 0;
        Expect<ModelNetworkAccessDeniedException>(() =>
        {
            using var lease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
            credentialReads++;
        });
        Require(!policy.IsCloudEnabled && credentialReads == 0, "Default local mode reached a credential read.");
    }

    public static void AllowsOnlyExactHttpsModelEndpoint()
    {
        using var policy = new ModelNetworkAccessPolicy();
        policy.EnableDeepSeek();
        using var allowed = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        foreach (var address in new[]
        {
            "http://api.deepseek.com/chat/completions", "https://api.deepseek.com:444/chat/completions",
            "https://api.deepseek.com.evil.invalid/chat/completions", "https://api.deepseek.com./chat/completions",
            "https://other.invalid/chat/completions", "https://api.deepseek.com/models",
            "https://api.deepseek.com/chat/completions/", "https://api.deepseek.com/chat/%63ompletions",
            "https://api.deepseek.com/chat/completions?report=1", "https://api.deepseek.com/chat/completions#other",
            "https://name@api.deepseek.com/chat/completions", "chat/completions",
        })
            Expect<ModelNetworkAccessDeniedException>(() =>
                policy.AcquireRequestLease(new Uri(address, UriKind.RelativeOrAbsolute)).Dispose());
    }

    public static async Task GuardRechecksDestinationAndMethodAtDispatch()
    {
        using var policy = new ModelNetworkAccessPolicy();
        policy.EnableDeepSeek();
        using var lease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        using var transport = new CountingTransport();
        using var client = new HttpClient(lease.CreateGuardedHandler(transport));
        using (var wrongHost = new HttpRequestMessage(HttpMethod.Post, "https://report.invalid/events"))
            await ExpectAsync<ModelNetworkAccessDeniedException>(() => client.SendAsync(wrongHost));
        using (var wrongMethod = new HttpRequestMessage(HttpMethod.Get, ModelNetworkAccessPolicy.DeepSeekEndpoint))
            await ExpectAsync<ModelNetworkAccessDeniedException>(() => client.SendAsync(wrongMethod));
        using (var wrongHeader = new HttpRequestMessage(HttpMethod.Post, ModelNetworkAccessPolicy.DeepSeekEndpoint))
        {
            wrongHeader.Headers.Host = "report.invalid";
            await ExpectAsync<ModelNetworkAccessDeniedException>(() => client.SendAsync(wrongHeader));
        }
        Require(transport.Calls == 0, "A disallowed request reached transport.");
        using var valid = new HttpRequestMessage(HttpMethod.Post, ModelNetworkAccessPolicy.DeepSeekEndpoint);
        using var response = await client.SendAsync(valid);
        Require(response.StatusCode == HttpStatusCode.OK && transport.Calls == 1, "The permitted model request did not dispatch once.");
    }

    public static async Task SwitchingToLocalCancelsActiveRequest()
    {
        using var policy = new ModelNetworkAccessPolicy();
        policy.EnableDeepSeek();
        using var lease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        using var transport = new BlockingTransport();
        using var client = new HttpClient(lease.CreateGuardedHandler(transport));
        using var request = new HttpRequestMessage(HttpMethod.Post, ModelNetworkAccessPolicy.DeepSeekEndpoint);
        var pending = client.SendAsync(request);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        policy.UseLocal();
        await ExpectAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Require(lease.CancellationToken.IsCancellationRequested && !policy.IsCloudEnabled,
            "Switching to local did not revoke and cancel cloud permission.");
        Expect<ModelNetworkAccessDeniedException>(() => policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint));
    }

    public static async Task RevokedLeaseCannotDispatchAfterCloudReturns()
    {
        using var policy = new ModelNetworkAccessPolicy();
        policy.EnableDeepSeek();
        using var old = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        using var transport = new CountingTransport();
        using var client = new HttpClient(old.CreateGuardedHandler(transport));
        policy.UseLocal();
        policy.EnableDeepSeek();
        using var request = new HttpRequestMessage(HttpMethod.Post, ModelNetworkAccessPolicy.DeepSeekEndpoint);
        await ExpectAsync<OperationCanceledException>(() => client.SendAsync(request));
        Require(transport.Calls == 0, "A stale cloud epoch regained permission.");
        using var current = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        current.ThrowIfRevoked();
    }

    public static async Task RejectsRedirectWithoutFollowingDestination()
    {
        foreach (var destination in new[] { "https://report.invalid/events", "https://api.deepseek.com/other" })
        {
            using var policy = new ModelNetworkAccessPolicy();
            policy.EnableDeepSeek();
            using var lease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
            using var transport = new CountingTransport(HttpStatusCode.TemporaryRedirect, destination);
            using var client = new HttpClient(lease.CreateGuardedHandler(transport));
            using var request = new HttpRequestMessage(HttpMethod.Post, ModelNetworkAccessPolicy.DeepSeekEndpoint);
            await ExpectAsync<HttpRequestException>(() => client.SendAsync(request));
            Require(transport.Calls == 1, "A redirect dispatched a second request.");
        }
    }

    public static async Task LateResponseFromRevokedEpochIsRejected()
    {
        using var policy = new ModelNetworkAccessPolicy();
        policy.EnableDeepSeek();
        using var lease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        using var transport = new BlockingTransport(ignoreCancellation: true);
        using var client = new HttpClient(lease.CreateGuardedHandler(transport));
        using var request = new HttpRequestMessage(HttpMethod.Post, ModelNetworkAccessPolicy.DeepSeekEndpoint);
        var pending = client.SendAsync(request);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        policy.UseLocal();
        transport.Complete();
        await ExpectAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    public static void CancellationAndDisposalCannotLeaveUsableLeases()
    {
        using var policy = new ModelNetworkAccessPolicy();
        policy.EnableDeepSeek();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Expect<OperationCanceledException>(() => policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint, cancelled.Token));
        using var lifetime = new CancellationTokenSource();
        using var lease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint, lifetime.Token);
        lifetime.Cancel();
        Expect<OperationCanceledException>(lease.ThrowIfRevoked);
        var disposedLease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        disposedLease.Dispose();
        Require(disposedLease.CancellationToken.IsCancellationRequested, "Disposing a lease left its token live.");
        Expect<OperationCanceledException>(disposedLease.ThrowIfRevoked);
        using var finalLease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        policy.Dispose();
        Require(finalLease.CancellationToken.IsCancellationRequested, "Disposing policy left active requests alive.");
        Expect<ObjectDisposedException>(policy.EnableDeepSeek);
    }

    public static void CallbackFailureDoesNotRestoreRevokedPermission()
    {
        using var policy = new ModelNetworkAccessPolicy();
        policy.EnableDeepSeek();
        using var lease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        using var registration = lease.CancellationToken.Register(static () => throw new InvalidOperationException("fixture-callback"));
        Expect<AggregateException>(policy.UseLocal);
        Require(!policy.IsCloudEnabled && lease.CancellationToken.IsCancellationRequested,
            "A failing cancellation callback restored cloud permission.");
        Expect<OperationCanceledException>(lease.ThrowIfRevoked);
    }

    public static async Task AuthorizedVaultReadCannotRaceLocalSwitch()
    {
        using var policy = new ModelNetworkAccessPolicy();
        policy.EnableDeepSeek();
        using var lease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var switchAttempted = new ManualResetEventSlim();
        var read = Task.Run(() => lease.RunAuthorized(() =>
        {
            entered.TrySetResult();
            if (!switchAttempted.Wait(TimeSpan.FromSeconds(2))) throw new Exception("Local switch was not scheduled.");
            Require(policy.IsCloudEnabled, "Local mode entered in the middle of an authorized vault read.");
            return "fixture";
        }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var switching = Task.Run(() => { switchAttempted.Set(); policy.UseLocal(); });
        await Task.WhenAll(read, switching).WaitAsync(TimeSpan.FromSeconds(2));
        var extraReads = 0;
        Expect<OperationCanceledException>(() => lease.RunAuthorized(() => ++extraReads));
        Require(extraReads == 0 && !policy.IsCloudEnabled, "A revoked epoch reached the credential reader.");
    }

    public static void ProductionTransportDisablesIncidentalRequests()
    {
        using var transport = ModelNetworkAccessPolicy.CreateProductionTransport();
        Require(!transport.AllowAutoRedirect && !transport.UseProxy && !transport.UseCookies
            && !transport.UseDefaultCredentials && !transport.CheckCertificateRevocationList,
            "Production transport permits an incidental redirect/proxy/credential/certificate request.");
        using var policy = new ModelNetworkAccessPolicy();
        policy.EnableDeepSeek();
        using var lease = policy.AcquireRequestLease(ModelNetworkAccessPolicy.DeepSeekEndpoint);
        using var unsafeTransport = new HttpClientHandler { AllowAutoRedirect = true };
        Expect<ArgumentException>(() => lease.CreateGuardedHandler(unsafeTransport));
    }

    public static void ChildPrivacySanitizesOnlyChildEnvironment()
    {
        var before = Environment.GetEnvironmentVariable("HF_HUB_OFFLINE");
        var info = new ProcessStartInfo("fixture-local-model.exe") { UseShellExecute = false };
        info.Environment["LLAMA_ARG_HF_REPO"] = "fixture/remote-model";
        info.Environment["LLAMA_ARG_RPC"] = "fixture.invalid:1234";
        info.Environment["HF_TOKEN"] = "fixture-not-a-secret";
        info.Environment["PYTHONPATH"] = "fixture-untrusted-import";
        info.Environment["CLIPRAY_CUDA_BIN"] = "fixture-local-cuda";
        NativeRuntimePrivacy.ConfigureLocalChild(info);
        Require(!info.Environment.Keys.Any(static key => key.StartsWith("LLAMA_ARG_", StringComparison.OrdinalIgnoreCase))
            && !info.Environment.ContainsKey("HF_TOKEN") && !info.Environment.ContainsKey("PYTHONPATH"),
            "Inherited remote/import options survived child privacy configuration.");
        Require(info.Environment["HF_HUB_OFFLINE"] == "1" && info.Environment["HF_HUB_DISABLE_TELEMETRY"] == "1"
            && info.Environment["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] == "True"
            && info.Environment["CLIPRAY_CUDA_BIN"] == "fixture-local-cuda", "Child privacy configuration lost local settings.");
        Require(Environment.GetEnvironmentVariable("HF_HUB_OFFLINE") == before, "Child privacy changed the parent environment.");
    }

    private sealed class CountingTransport(HttpStatusCode status = HttpStatusCode.OK, string? redirect = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var response = new HttpResponseMessage(status) { Content = new StringContent("fixture") };
            if (redirect is not null) response.Headers.Location = new Uri(redirect);
            return Task.FromResult(response);
        }
    }

    private sealed class BlockingTransport(bool ignoreCancellation = false) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<HttpResponseMessage> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => _completion.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var registration = ignoreCancellation ? default : cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
            Started.TrySetResult();
            return await _completion.Task;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
