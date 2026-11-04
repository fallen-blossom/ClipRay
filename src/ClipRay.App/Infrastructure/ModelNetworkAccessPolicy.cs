using System.Net.Http;

namespace ClipRay.App.Infrastructure;

public sealed class ModelNetworkAccessDeniedException() : InvalidOperationException("model-network-access-denied");

/// <summary>Process-local model permission. Saved provider preferences are not permission.</summary>
public sealed class ModelNetworkAccessPolicy : IDisposable
{
    public static ModelNetworkAccessPolicy Shared { get; } = new();
    public static Uri DeepSeekEndpoint { get; } = new("https://api.deepseek.com/chat/completions");

    private readonly object _gate = new();
    private CancellationTokenSource? _cloud;
    private long _epoch;
    private bool _disposed;

    public bool IsCloudEnabled { get { lock (_gate) return !_disposed && _cloud is not null; } }

    // Call only for a current, explicit cloud selection. Reapplying that selection
    // is harmless; entering local mode always revokes the old epoch permanently.
    public void EnableDeepSeek()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cloud is not null) return;
            _cloud = new CancellationTokenSource();
            _epoch++;
        }
    }

    public void UseLocal()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _cloud;
            _cloud = null;
            _epoch++;
        }
        CancelAndDispose(previous);
    }

    // Acquire before accessing the credential vault. Keep this lease alive until
    // response-body parsing completes, using its token for both send and reads.
    public ModelNetworkRequestLease AcquireRequestLease(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cloud is null || !IsAllowedEndpoint(endpoint)) throw new ModelNetworkAccessDeniedException();
            cancellationToken.ThrowIfCancellationRequested();
            return new ModelNetworkRequestLease(this, _epoch,
                CancellationTokenSource.CreateLinkedTokenSource(_cloud.Token, cancellationToken));
        }
    }

    public static bool IsAllowedEndpoint(Uri endpoint) => endpoint.IsAbsoluteUri
        && endpoint.Scheme == Uri.UriSchemeHttps
        // Uri canonicalizes escaped unreserved characters even in UriEscaped
        // form. The one fixed ASCII endpoint requires no percent encoding.
        && !endpoint.OriginalString.Contains('%')
        && string.Equals(endpoint.Host, "api.deepseek.com", StringComparison.OrdinalIgnoreCase)
        && endpoint.Port == 443 && endpoint.UserInfo.Length == 0
        && endpoint.AbsolutePath == "/chat/completions"
        && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0;

    public static HttpClientHandler CreateProductionTransport() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        UseDefaultCredentials = false,
        // Revocation-list fetching is another outbound surface. TLS chain and
        // hostname validation remain enabled; no custom validation callback.
        CheckCertificateRevocationList = false,
    };

    internal void ThrowIfRevoked(ModelNetworkRequestLease lease)
    {
        lock (_gate) ValidateLease(lease);
    }

    internal T RunAuthorized<T>(ModelNetworkRequestLease lease, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            ValidateLease(lease);
            return action();
        }
    }

    private void ValidateLease(ModelNetworkRequestLease lease)
    {
        if (_disposed || _cloud is null || lease.Epoch != _epoch || lease.IsDisposed)
            throw new OperationCanceledException("model-network-access-revoked", lease.CancellationToken);
        lease.CancellationToken.ThrowIfCancellationRequested();
    }

    internal async Task<HttpResponseMessage> SendAsync(ModelNetworkRequestLease lease,
        HttpMessageInvoker transport, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(lease.CancellationToken, cancellationToken);
        Task<HttpResponseMessage> pending;
        lock (_gate)
        {
            ValidateLease(lease);
            if (request.RequestUri is not { } endpoint || !IsAllowedEndpoint(endpoint)
                || request.Method != HttpMethod.Post || request.Headers.Host is not null)
                throw new ModelNetworkAccessDeniedException();
            stopped.Token.ThrowIfCancellationRequested();
            // Validation and dispatch initiation share the revocation lock. A
            // queued old request cannot start after UseLocal changes the epoch.
            pending = transport.SendAsync(request, stopped.Token);
        }
        var response = await pending.ConfigureAwait(false);
        try
        {
            ThrowIfRevoked(lease);
            stopped.Token.ThrowIfCancellationRequested();
            if ((int)response.StatusCode is >= 300 and < 400)
                throw new HttpRequestException("model-network-redirect-denied");
            return response;
        }
        catch { response.Dispose(); throw; }
    }

    internal void Release(ModelNetworkRequestLease lease, CancellationTokenSource lifetime)
    {
        lock (_gate)
        {
            if (lease.IsDisposed) return;
            lease.IsDisposed = true;
        }
        CancelAndDispose(lifetime);
    }

    private static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null) return;
        // Callback failures stay visible, but permission was already revoked.
        try { source.Cancel(); }
        finally { source.Dispose(); }
    }

    public void Dispose()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            previous = _cloud;
            _cloud = null;
            _epoch++;
        }
        CancelAndDispose(previous);
    }
}

public sealed class ModelNetworkRequestLease : IDisposable
{
    private readonly ModelNetworkAccessPolicy _owner;
    private readonly CancellationTokenSource _lifetime;
    internal long Epoch { get; }
    internal bool IsDisposed { get; set; }
    public CancellationToken CancellationToken { get; }

    internal ModelNetworkRequestLease(ModelNetworkAccessPolicy owner, long epoch, CancellationTokenSource lifetime)
    {
        _owner = owner;
        Epoch = epoch;
        _lifetime = lifetime;
        CancellationToken = lifetime.Token;
    }

    public void ThrowIfRevoked() => _owner.ThrowIfRevoked(this);

    // Synchronous local work only, e.g. vault read. Permission cannot change
    // between validation and that read. Never use this to start async I/O.
    public T RunAuthorized<T>(Func<T> action) => _owner.RunAuthorized(this, action);

    // Supplied transports are trusted dependency-injection seams, never plugins.
    // The caller owns supplied transport lifetime; default production transport
    // is owned here. Known HTTP transports must disable internal redirects.
    public HttpMessageHandler CreateGuardedHandler(HttpMessageHandler? transport = null)
    {
        ThrowIfRevoked();
        if (transport is HttpClientHandler { AllowAutoRedirect: true }
            || transport is SocketsHttpHandler { AllowAutoRedirect: true })
            throw new ArgumentException("model-network-transport-must-disable-redirects", nameof(transport));
        return new GuardedHandler(this, _owner,
            new HttpMessageInvoker(transport ?? ModelNetworkAccessPolicy.CreateProductionTransport(), disposeHandler: transport is null));
    }

    public void Dispose() => _owner.Release(this, _lifetime);

    private sealed class GuardedHandler(ModelNetworkRequestLease lease, ModelNetworkAccessPolicy policy,
        HttpMessageInvoker transport) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            policy.SendAsync(lease, transport, request, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing) transport.Dispose();
            base.Dispose(disposing);
        }
    }
}
