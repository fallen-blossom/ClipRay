using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ClipRay.App.Infrastructure;

public sealed record InstallProcessIdentity(int ProcessId, long StartUtcTicks, string ExecutablePath);
public sealed record InstallExitRequest(int Protocol, Guid RequestId, InstallProcessIdentity Target);
public sealed record InstallExitResponse(int Protocol, Guid RequestId, InstallProcessIdentity Target, string Status);
public interface IInstallProcessHandle : IDisposable
{
    InstallProcessIdentity Identity { get; }
    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken token);
}
public sealed record InstallProcessInventory(IReadOnlyList<IInstallProcessHandle> Targets, bool Complete, bool SiblingInstallRunning = false);
public interface IInstallExitBackend
{
    InstallProcessInventory Find(string exactExecutablePath);
    Task<InstallExitResponse?> RequestAsync(string scope, InstallExitRequest request, TimeSpan timeout, CancellationToken token);
}

public static class InstallExitProtocol
{
    public const int Version = 1;
    public static string PipeName(string scope) => string.Concat(scope.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '.')) + ".Activation";
    public static bool Same(InstallProcessIdentity first, InstallProcessIdentity second) => first.ProcessId == second.ProcessId
        && first.StartUtcTicks == second.StartUtcTicks && string.Equals(first.ExecutablePath, second.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    public static InstallProcessIdentity Current() => new(Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
        Path.GetFullPath(Environment.ProcessPath ?? throw new IOException("Executable identity unavailable.")));
    public static async Task<InstallExitResponse> HandleAsync(InstallExitRequest request, InstallProcessIdentity current, Func<Task<bool>> retire)
    {
        if (request.Protocol != Version || request.RequestId == Guid.Empty || request.Target is null || !Same(request.Target, current))
            return new(Version, request.RequestId, current, "identity-or-protocol-mismatch");
        var accepted = await retire();
        return new(Version, request.RequestId, current, accepted ? "retired" : "cancelled-or-busy");
    }
}

public sealed class InstallExitClient(IInstallExitBackend backend)
{
    public async Task<bool> ConfirmExitAsync(string executablePath, string scope, TimeSpan timeout, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(executablePath) || !string.Equals(executablePath,Path.GetFullPath(executablePath),StringComparison.OrdinalIgnoreCase)) return false;
        InstallProcessInventory? inventory = null;
        InstallProcessInventory? after = null;
        try
        {
            inventory = backend.Find(executablePath);
            if (!inventory.Complete || inventory.SiblingInstallRunning) return false;
            foreach (var process in inventory.Targets)
            {
                if (!string.Equals(process.Identity.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)) return false;
                var request = new InstallExitRequest(InstallExitProtocol.Version, Guid.NewGuid(), process.Identity);
                var response = await backend.RequestAsync(scope, request, timeout, token).WaitAsync(timeout,token);
                if (response is null || response.Protocol != request.Protocol || response.RequestId != request.RequestId
                    || !InstallExitProtocol.Same(response.Target,request.Target) || response.Status != "retired") return false;
                if (!await process.WaitForExitAsync(timeout,token).WaitAsync(timeout,token)) return false;
            }
            after = backend.Find(executablePath);
            return after.Complete && !after.SiblingInstallRunning && after.Targets.Count == 0;
        }
        catch { return false; }
        finally
        {
            if (inventory is not null) foreach (var item in inventory.Targets) item.Dispose();
            if (after is not null) foreach (var item in after.Targets) item.Dispose();
        }
    }
}

public sealed class InstallMaintenance(IInstallExitBackend processes)
{
    public Task<bool> ConfirmExitAsync(string executable,string scope,TimeSpan timeout,CancellationToken token=default)
        =>new InstallExitClient(processes).ConfirmExitAsync(executable,scope,timeout,token);
    public async Task<UninstallCleanupResult?> ExecuteUninstallAsync(string executable,string scope,ApplicationPathSet paths,
        bool recycleOutputs,TimeSpan timeout,IUninstallCleanupBackend? cleanup=null,CancellationToken token=default)
    {
        if(!await ConfirmExitAsync(executable,scope,timeout,token)) return null;
        return new UninstallCleanupService(paths,executable,cleanup).Execute(recycleOutputs);
    }
}

internal interface IInstallProcessObservation : IDisposable
{
    bool IsSelf { get; }
    bool HasExited { get; }
    string UserSid { get; }
    InstallProcessIdentity Identity { get; }
    IInstallProcessHandle RetainHandle();
}
internal interface IInstallProcessInventorySource
{
    string CurrentUserSid { get; }
    IEnumerable<IInstallProcessObservation> Enumerate(string executableName);
}

internal sealed class WindowsInstallExitBackend : IInstallExitBackend
{
    private readonly IInstallProcessInventorySource _source;
    private readonly Func<string,InstallExitRequest,TimeSpan,CancellationToken,Task<InstallExitResponse?>>? _transport;
    public WindowsInstallExitBackend() : this(new WindowsInventorySource()) { }
    internal WindowsInstallExitBackend(IInstallProcessInventorySource source,
        Func<string,InstallExitRequest,TimeSpan,CancellationToken,Task<InstallExitResponse?>>? transport=null)
        { _source=source; _transport=transport; }
    public InstallProcessInventory Find(string exactExecutablePath)
    {
        var targets = new List<IInstallProcessHandle>();
        var complete = true;
        var sibling = false;
        var user=_source.CurrentUserSid;
        if(string.IsNullOrWhiteSpace(user)) throw new IOException("User identity unavailable.");
        // The name only bounds enumeration; actions require a verified path, start time and retained handle.
        try
        {
        foreach (var process in _source.Enumerate(Path.GetFileNameWithoutExtension(exactExecutablePath)))
        {
            using var observation=process;
            try
            {
                if (process.IsSelf) continue;
                if (process.HasExited) continue;
                var processUser=process.UserSid;
                if(string.IsNullOrWhiteSpace(processUser)) throw new IOException("Process user unavailable.");
                if(!string.Equals(processUser,user,StringComparison.Ordinal)) continue;
                var identity=process.Identity;
                var path = identity.ExecutablePath;
                if(identity.ProcessId<=0 || identity.StartUtcTicks<=0 || !Path.IsPathFullyQualified(path)
                    || !string.Equals(path,Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Process identity unavailable.");
                if (!string.Equals(path, exactExecutablePath,StringComparison.OrdinalIgnoreCase))
                {
                    // Startup, cleanup task and CredentialVault are shared by the entire current user,
                    // even though release/development DataRoot differs. Never retire the sibling.
                    sibling=true;
                    continue;
                }
                targets.Add(process.RetainHandle());
            }
            catch { complete = false; }
        }
        }
        catch { complete=false; }
        return new(targets,complete,sibling);
    }
    [DllImport("advapi32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process,uint access,out SafeAccessTokenHandle token);
    public async Task<InstallExitResponse?> RequestAsync(string scope, InstallExitRequest request, TimeSpan timeout, CancellationToken token)
    {
        if(_transport is not null) return await _transport(scope,request,timeout,token);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        stop.CancelAfter(timeout);
        using var client = new NamedPipeClientStream(".",InstallExitProtocol.PipeName(scope),PipeDirection.InOut,PipeOptions.Asynchronous);
        await client.ConnectAsync(stop.Token);
        using var writer = new StreamWriter(client,leaveOpen:true) { AutoFlush = true };
        using var reader = new StreamReader(client,leaveOpen:true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(),stop.Token);
        var value = await reader.ReadLineAsync(stop.Token);
        return value is null ? null : JsonSerializer.Deserialize<InstallExitResponse>(value);
    }
    private sealed class WindowsInventorySource:IInstallProcessInventorySource
    {
        public string CurrentUserSid=>WindowsIdentity.GetCurrent().User?.Value ?? throw new IOException("User identity unavailable.");
        public IEnumerable<IInstallProcessObservation> Enumerate(string name)
            =>Process.GetProcessesByName(name).Select(process=>(IInstallProcessObservation)new WindowsObservation(process));
    }
    private sealed class WindowsObservation(Process process):IInstallProcessObservation
    {
        private bool _retained;
        public bool IsSelf=>process.Id==Environment.ProcessId;
        public bool HasExited { get { _=process.Handle; return process.HasExited; } }
        public string UserSid
        {
            get
            {
                if(!OpenProcessToken(process.Handle,0x0008,out var token)) throw new IOException("Process user unavailable.");
                using(token)
                using(var identity=new WindowsIdentity(token.DangerousGetHandle()))
                    return identity.User?.Value ?? throw new IOException("Process user unavailable.");
            }
        }
        public InstallProcessIdentity Identity=>new(process.Id,process.StartTime.ToUniversalTime().Ticks,
            Path.GetFullPath(process.MainModule?.FileName ?? throw new IOException("Executable unavailable.")));
        public IInstallProcessHandle RetainHandle()
        { var handle=new Handle(process,Identity); _retained=true; return handle; }
        public void Dispose(){if(!_retained)process.Dispose();}
    }
    private sealed class Handle(Process process, InstallProcessIdentity identity) : IInstallProcessHandle
    {
        public InstallProcessIdentity Identity => identity;
        public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken token)
        {
            try { await process.WaitForExitAsync(token).WaitAsync(timeout,token); return process.HasExited; }
            catch { return false; }
        }
        public void Dispose() => process.Dispose();
    }
}
