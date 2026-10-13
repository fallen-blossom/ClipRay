using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ClipRay.App.Security;

public sealed record CredentialMetadata(string Name, string Provider, DateTimeOffset UpdatedAt, string MaskedSuffix);

public interface ICredentialVault
{
    void Save(string name, string provider, string value);

    IReadOnlyList<CredentialMetadata> List();

    void Delete(string name);

    string? GetForUse(string name);
}

public interface ICredentialBackend
{
    void Write(string target, string value);

    string? Read(string target);

    void Delete(string target);

    IEnumerable<string> ListTargets(string prefix) => [];
}

public sealed class CredentialVault : ICredentialVault
{
    public const string FixedDeepSeekSlot = "ClipRay.DeepSeek";
    private const string SecretPrefix = "ClipRay.Credential.";
    private const string MetadataPrefix = "ClipRay.Metadata.";
    private readonly ICredentialBackend _backend;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly ConcurrentDictionary<string, byte> _knownMetadataTargets = new(StringComparer.Ordinal);

    public CredentialVault()
        : this(new WindowsCredentialBackend(), () => DateTimeOffset.UtcNow)
    {
    }

    public CredentialVault(ICredentialBackend backend, Func<DateTimeOffset> utcNow)
    {
        _backend = backend;
        _utcNow = utcNow;
    }

    public void Save(string name, string provider, string value)
    {
        ValidateName(name);
        ValidateProvider(provider);
        CredentialSecretPolicy.ValidateCandidate(provider, value);
        var id = EncodeName(name);
        var secretTarget = SecretPrefix + id;
        var metadataTarget = MetadataPrefix + id;
        var oldSecret = _backend.Read(secretTarget);
        var oldMetadata = _backend.Read(metadataTarget);
        var metadata = new StoredMetadata(name, provider, _utcNow().ToUniversalTime(), CredentialSecretPolicy.Mask(provider, value));
        _backend.Write(secretTarget, value);
        try
        {
            _backend.Write(metadataTarget, JsonSerializer.Serialize(metadata));
            _knownMetadataTargets[metadataTarget] = 0;
        }
        catch
        {
            Restore(secretTarget, oldSecret);
            Restore(metadataTarget, oldMetadata);
            throw;
        }
    }

    public IReadOnlyList<CredentialMetadata> List()
    {
        var targets = _backend.ListTargets(MetadataPrefix)
            .Concat(_knownMetadataTargets.Keys)
            .Distinct(StringComparer.Ordinal);
        var result = new List<CredentialMetadata>();
        foreach (var target in targets)
        {
            var json = _backend.Read(target);
            if (json is null) continue;
            var metadata = JsonSerializer.Deserialize<StoredMetadata>(json);
            if (metadata is not null)
            {
                var id = target[MetadataPrefix.Length..];
                var secret = _backend.Read(SecretPrefix + id);
                var mask = secret is null ? "凭据缺失" : CredentialSecretPolicy.Mask(metadata.Provider, secret);
                result.Add(new CredentialMetadata(metadata.Name, metadata.Provider, metadata.UpdatedAt, mask));
            }
        }

        return result.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void Delete(string name)
    {
        ValidateName(name);
        var id = EncodeName(name);
        var secretTarget = SecretPrefix + id;
        var metadataTarget = MetadataPrefix + id;
        var oldSecret = _backend.Read(secretTarget);
        var oldMetadata = _backend.Read(metadataTarget);
        _backend.Delete(secretTarget);
        try
        {
            _backend.Delete(metadataTarget);
        }
        catch
        {
            Restore(secretTarget, oldSecret);
            Restore(metadataTarget, oldMetadata);
            throw;
        }
        _knownMetadataTargets.TryRemove(metadataTarget, out _);
    }

    public string? GetForUse(string name)
    {
        ValidateName(name);
        return _backend.Read(SecretPrefix + EncodeName(name));
    }

    public bool MigrateSelectedDeepSeekCredential(string? selectedName)
    {
        if (string.IsNullOrWhiteSpace(selectedName)
            || string.Equals(selectedName, FixedDeepSeekSlot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var existingFixed = GetForUse(FixedDeepSeekSlot);
        if (!string.IsNullOrWhiteSpace(existingFixed))
        {
            // A different (or equal) existing slot is not proof that this selection
            // was migrated. Keep both identities and the caller's current selection.
            return false;
        }
        var selected = GetForUse(selectedName);
        if (string.IsNullOrWhiteSpace(selected)) return false;

        Save(FixedDeepSeekSlot, "DeepSeek", selected);
        // Keep the old slot until selection persistence is known to have succeeded.
        // A failed settings write can safely reopen the original selected identity.
        return true;
    }

    public IReadOnlyList<string> DeleteAllClipRayCredentials()
    {
        var metadataTargets = _backend.ListTargets(MetadataPrefix).ToArray();
        var names = metadataTargets.Select(TryReadStoredName)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var targets = _backend.ListTargets(SecretPrefix)
            .Concat(metadataTargets)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var failures = new List<Exception>();
        foreach (var target in targets)
        {
            try { _backend.Delete(target); }
            catch (Exception exception) { failures.Add(exception); }
        }
        _knownMetadataTargets.Clear();
        if (failures.Count > 0) throw new AggregateException("One or more ClipRay credentials could not be deleted.", failures);
        return names;
    }

    private string? TryReadStoredName(string metadataTarget)
    {
        try
        {
            var json = _backend.Read(metadataTarget);
            return json is null ? null : JsonSerializer.Deserialize<StoredMetadata>(json)?.Name;
        }
        catch
        {
            return null;
        }
    }

    private static string EncodeName(string name) => Convert.ToHexString(Encoding.UTF8.GetBytes(name));

    private void Restore(string target, string? value)
    {
        if (value is null) _backend.Delete(target);
        else _backend.Write(target, value);
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 128) throw new ArgumentOutOfRangeException(nameof(name));
    }

    private static void ValidateProvider(string provider)
    {
        if (provider is not ("DeepSeek" or "Local LMT"))
        {
            throw new ArgumentException("Provider 必须是 DeepSeek 或 Local LMT。", nameof(provider));
        }
    }

    private sealed record StoredMetadata(string Name, string Provider, DateTimeOffset UpdatedAt, string MaskedSuffix);
}

internal sealed class WindowsCredentialBackend : ICredentialBackend
{
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;

    public void Write(string target, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = checked((uint)bytes.Length),
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException("保存 Windows 凭据失败。", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public string? Read(string target)
    {
        if (!CredRead(target, GenericCredential, 0, out var pointer))
        {
            const int NotFound = 1168;
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new InvalidOperationException("读取 Windows 凭据失败。", new System.ComponentModel.Win32Exception(error));
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            return credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Delete(string target)
    {
        if (!CredDelete(target, GenericCredential, 0))
        {
            const int NotFound = 1168;
            var error = Marshal.GetLastWin32Error();
            if (error != NotFound)
            {
                throw new InvalidOperationException("删除 Windows 凭据失败。", new System.ComponentModel.Win32Exception(error));
            }
        }
    }

    public IEnumerable<string> ListTargets(string prefix)
    {
        if (!CredEnumerate(prefix + "*", 0, out var count, out var pointer))
        {
            const int NotFound = 1168;
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return [];
            throw new InvalidOperationException("枚举 Windows 凭据失败。", new System.ComponentModel.Win32Exception(error));
        }

        try
        {
            var targets = new List<string>(checked((int)count));
            for (var index = 0; index < count; index++)
            {
                var credentialPointer = Marshal.ReadIntPtr(pointer, checked((int)index * IntPtr.Size));
                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
                if (credential.TargetName is not null) targets.Add(credential.TargetName);
            }

            return targets;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredEnumerate(string filter, uint flags, out uint count, out IntPtr credentials);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
