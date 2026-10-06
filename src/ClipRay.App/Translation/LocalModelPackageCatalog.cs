using System.IO;
using System.Security.Cryptography;

namespace ClipRay.App.Translation;

// Describes the model payload of the Full installer. This is a supply-chain
// check, not evidence that a language pair or translation quality is supported.
public sealed record LocalModelPackageDescriptor(
    string Id,
    string RelativeModelPath,
    string ExpectedSha256,
    string SourceUrl,
    string LicenseId,
    string RelativeLicensePath);

public enum LocalModelPackageState
{
    ModelMissing,
    LicenseMissing,
    HashMismatch,
    VerifiedFiles,
}

public sealed record LocalModelPackageCheck(
    LocalModelPackageDescriptor Package,
    LocalModelPackageState State,
    long? ModelSizeBytes = null);

public static class LocalModelPackageCatalog
{
    public static LocalModelPackageDescriptor FullInstallerModel { get; } = new(
        "niutrans-lmt-60-0.6b-q4-k-m",
        Path.Combine("models", "local-lmt", "LMT-60-0.6B.Q4_K_M.gguf"),
        LocalLmtOptions.DefaultModelSha256,
        "https://huggingface.co/NiuTrans/LMT-60-0.6B",
        "Apache-2.0",
        Path.Combine("licenses", "Apache-2.0.txt"));

    public static async Task<LocalModelPackageCheck> InspectAsync(
        string appPayloadRoot, string licenseRoot, LocalModelPackageDescriptor package,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appPayloadRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseRoot);
        ArgumentNullException.ThrowIfNull(package);
        // The installer stores licenses beside its app directory. A standalone
        // publish instead stores both under one root; callers supply each root.
        var model = ResolveWithin(Path.GetFullPath(appPayloadRoot), package.RelativeModelPath);
        var license = ResolveWithin(Path.GetFullPath(licenseRoot), package.RelativeLicensePath);
        if (!File.Exists(model)) return new(package, LocalModelPackageState.ModelMissing);
        var size = new FileInfo(model).Length;
        if (!File.Exists(license)) return new(package, LocalModelPackageState.LicenseMissing, size);
        await using var stream = new FileStream(model, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, options: FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return new(package,
            Convert.ToHexString(hash).Equals(package.ExpectedSha256, StringComparison.OrdinalIgnoreCase)
                ? LocalModelPackageState.VerifiedFiles
                : LocalModelPackageState.HashMismatch,
            size);
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("Package path must be relative.", nameof(relativePath));
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Package path escapes install root.", nameof(relativePath));
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Package path contains a reparse point.", nameof(relativePath));
        }
        return path;
    }
}
