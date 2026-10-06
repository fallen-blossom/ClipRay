using System.Security.Cryptography;
using System.IO;
using System.Text;
using ClipRay.App.Translation;

internal static class LocalModelPackageCatalogTests
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipray-package-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var content = Encoding.UTF8.GetBytes("package fixture");
            var package = new LocalModelPackageDescriptor("fixture", "models/model.gguf",
                Convert.ToHexString(SHA256.HashData(content)), "https://example.invalid/model", "MIT", "licenses/MIT.txt");
            var model = Path.Combine(root, "models", "model.gguf");
            var license = Path.Combine(root, "licenses", "MIT.txt");
            Assert(LocalModelPackageState.ModelMissing, (await LocalModelPackageCatalog.InspectAsync(root, root, package)).State);
            Directory.CreateDirectory(Path.GetDirectoryName(model)!);
            await File.WriteAllBytesAsync(model, content);
            Assert(LocalModelPackageState.LicenseMissing, (await LocalModelPackageCatalog.InspectAsync(root, root, package)).State);
            Directory.CreateDirectory(Path.GetDirectoryName(license)!);
            await File.WriteAllTextAsync(license, "fixture license");
            var verified = await LocalModelPackageCatalog.InspectAsync(root, root, package);
            Assert(LocalModelPackageState.VerifiedFiles, verified.State);
            Assert((long)content.Length, verified.ModelSizeBytes);
            var installedAppRoot = Path.Combine(root, "installed", "app");
            var installedLicenseRoot = Path.Combine(root, "installed");
            var installedModel = Path.Combine(installedAppRoot, "models", "model.gguf");
            var installedLicense = Path.Combine(installedLicenseRoot, "licenses", "MIT.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(installedModel)!);
            Directory.CreateDirectory(Path.GetDirectoryName(installedLicense)!);
            await File.WriteAllBytesAsync(installedModel, content);
            await File.WriteAllTextAsync(installedLicense, "fixture license");
            Assert(LocalModelPackageState.VerifiedFiles,
                (await LocalModelPackageCatalog.InspectAsync(installedAppRoot, installedLicenseRoot, package)).State);
            await File.WriteAllTextAsync(model, "changed model");
            Assert(LocalModelPackageState.HashMismatch, (await LocalModelPackageCatalog.InspectAsync(root, root, package)).State);
            var escaped = package with { RelativeModelPath = "../outside.gguf" };
            try { await LocalModelPackageCatalog.InspectAsync(root, root, escaped); throw new Exception("Traversal accepted"); }
            catch (ArgumentException) { }
            Assert(LocalLmtOptions.DefaultModelSha256, LocalModelPackageCatalog.FullInstallerModel.ExpectedSha256);
            Console.WriteLine("Local model package catalog checks passed.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Assert<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}");
    }
}
