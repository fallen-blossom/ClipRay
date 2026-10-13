namespace ClipRay.App.Infrastructure;

internal sealed record HotkeyRegistrationRequest(string Id, string Label, GlobalHotkey Key, Action Action);
internal sealed record HotkeyRegistrationResult(IReadOnlyList<string> Failures, IReadOnlyList<string> Retained)
{
    internal static HotkeyRegistrationResult Empty { get; } = new([], []);
}

// Owns one host. Unchanged bindings survive an unrelated settings save, and a
// rejected replacement keeps its usable old binding when that key is still free.
internal sealed class GlobalHotkeyRegistration(IGlobalHotkeyHost host) : IDisposable
{
    private readonly Dictionary<GlobalHotkey, HotkeyRegistrationRequest> _active = [];
    internal HotkeyRegistrationResult Apply(IReadOnlyList<HotkeyRegistrationRequest> desired)
    {
        var failures = new List<string>();
        foreach (var old in _active.Values.ToArray())
        {
            if (!desired.Any(d => d.Id == old.Id) || desired.Any(d => d.Key == old.Key && d.Id != old.Id))
                Remove(old, failures);
        }
        foreach (var request in desired)
        {
            if (_active.TryGetValue(request.Key, out var active))
            {
                if (active.Id != request.Id) failures.Add(request.Key.ToString());
                continue;
            }
            if (!host.TryRegister(request.Key, request.Action))
            { failures.Add(request.Key.ToString()); continue; }
            _active.Add(request.Key, request);
            foreach (var old in _active.Values.Where(a => a.Id == request.Id && a.Key != request.Key).ToArray())
                Remove(old, failures);
        }
        var retained = _active.Values.Where(a => desired.Any(d => d.Id == a.Id && d.Key != a.Key))
            .Select(a => a.Key.ToString()).ToArray();
        return new(failures, retained);
    }
    private void Remove(HotkeyRegistrationRequest old, List<string> failures)
    {
        if (host.TryUnregister(old.Key)) _active.Remove(old.Key);
        else failures.Add(old.Key.ToString());
    }
    public void Dispose() { host.Dispose(); _active.Clear(); }
}
