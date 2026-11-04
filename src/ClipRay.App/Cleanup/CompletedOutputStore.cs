using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipRay.App.Cleanup;

public sealed record CompletedOutputReceipt(int Schema, string Path, long Size, DateTime WrittenUtc,
    string Sha256, CleanupFileKind Kind, string State = "saved");

/// <summary>Actual file commit owns this proof. Unknown historical outputs are not inferred to be delivered.</summary>
public sealed class CompletedOutputStore
{
    internal Action<string>? BeforeReceiptWrite { get; init; }

    public static bool IsOrdinaryPath(string path)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            if (!System.IO.Path.IsPathFullyQualified(path) || full.StartsWith("\\\\",StringComparison.Ordinal)
                || !string.Equals(full,path.TrimEnd(System.IO.Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)) return false;
            for (string? current = full; current is not null; current = System.IO.Path.GetDirectoryName(current))
            {
                if (!File.Exists(current) && !Directory.Exists(current)) continue;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch { return false; }
    }

    public static IDisposable EnterDirectory(string directory)
    {
        var full = System.IO.Path.GetFullPath(directory).TrimEnd(System.IO.Path.DirectorySeparatorChar).ToUpperInvariant();
        var name = "Local\\ClipRay.Output." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)));
        var mutex = new Mutex(false, name);
        try
        {
            try { if (!mutex.WaitOne(TimeSpan.FromSeconds(5))) throw new TimeoutException("output-coordination-timeout"); }
            catch (AbandonedMutexException) { /* Recheck file and receipt under the recovered ownership. */ }
            return new DirectoryLease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    public bool TryRecordSaved(string path, CleanupFileKind kind)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            if (!File.Exists(full) || !IsOrdinaryPath(full)) return false;
            var info = new FileInfo(full);
            if (info.Length <= 0) return false;
            var receipt = new CompletedOutputReceipt(1, full, info.Length, info.LastWriteTimeUtc,
                HashFile(full), kind);
            var target = ReceiptPath(full);
            if (!IsOrdinaryPath(target)) return false;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            BeforeReceiptWrite?.Invoke(full);
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(receipt));
                File.Move(temporary, target, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return true;
        }
        catch { return false; } // Bookkeeping failure cannot reverse an already committed file.
    }

    public bool TryGetEligible(string path, CleanupFileKind kind, DateTime cutoff, out CompletedOutputReceipt? receipt)
        => TryGetEligible(path, kind, cutoff, out receipt, out _);

    public bool TryGetEligible(string path, CleanupFileKind kind, DateTime cutoff,
        out CompletedOutputReceipt? receipt, out string reason)
    {
        receipt = null;
        reason = "receipt-unavailable";
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var proof = ReceiptPath(full);
            if (!File.Exists(full)) { reason = "output-missing"; return false; }
            if (!File.Exists(proof)) { reason = "completion-receipt-missing"; return false; }
            if (!IsOrdinaryPath(full) || !IsOrdinaryPath(proof)) { reason = "output-or-receipt-unsafe"; return false; }
            var proofInfo = new FileInfo(proof);
            if (proofInfo.Length > 16_384) { reason = "completion-receipt-invalid"; return false; }
            var record = JsonSerializer.Deserialize<CompletedOutputReceipt>(File.ReadAllText(proof));
            var info = new FileInfo(full);
            if (record is null || record.Schema != 1 || record.State != "saved" || record.Kind != kind
                || !string.Equals(record.Path, full, StringComparison.OrdinalIgnoreCase) || !IsOrdinaryPath(record.Path)
                || record.Size <= 0) { reason = "completion-receipt-invalid"; return false; }
            if (record.Size != info.Length || record.WrittenUtc != info.LastWriteTimeUtc)
            { reason = "output-metadata-changed"; return false; }
            if (record.WrittenUtc >= cutoff) { reason = "within-retention"; return false; }
            if (record.Sha256?.Length != 64) { reason = "completion-receipt-invalid"; return false; }
            if (!string.Equals(record.Sha256, HashFile(full), StringComparison.OrdinalIgnoreCase))
            { reason = "output-content-changed"; return false; }
            receipt = record;
            reason = "completed-expired";
            return true;
        }
        catch { return false; }
    }

    public static string ReceiptPath(string file) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!, ".clipray-completed", System.IO.Path.GetFileName(file) + ".json");
    private static string HashFile(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private sealed class DirectoryLease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
