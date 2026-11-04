using System.IO;

namespace ClipRay.App.Translation;

public static class MachineTranslationModelPaths
{
    public static string Resolve(string installRoot)
    {
        installRoot = ClipRay.App.Infrastructure.LocalPathPolicy.RequireLocal(installRoot);
        var shared = Path.Combine(installRoot, "machine-translation", "models", "small100");
        if (IsCommonLanguageModel(shared)) return shared;
        return ClipRay.App.Infrastructure.LocalPathPolicy.RequireLocal(
            Path.Combine(installRoot, "machine-translation", "models", "translate-en_zh-1_9"));
    }

    public static bool IsCommonLanguageModel(string path) =>
        ClipRay.App.Infrastructure.LocalPathPolicy.FileExists(Path.Combine(ClipRay.App.Infrastructure.LocalPathPolicy.RequireLocal(path), "model.bin"))
        && ClipRay.App.Infrastructure.LocalPathPolicy.FileExists(Path.Combine(path, "config.json"))
        && ClipRay.App.Infrastructure.LocalPathPolicy.FileExists(Path.Combine(path, "shared_vocabulary.json"))
        && ClipRay.App.Infrastructure.LocalPathPolicy.FileExists(Path.Combine(path, "sentencepiece.bpe.model"));

    internal static void ValidateLocalFiles(string modelRoot)
    {
        // Both supported CT2 layouts have local tokenizer files at the root;
        // the legacy EN→ZH weights/vocabularies live in its model/ subfolder.
        foreach (var directory in new[] { modelRoot, Path.Combine(modelRoot, "model") })
        {
            var local = ClipRay.App.Infrastructure.LocalPathPolicy.RequireLocal(directory);
            if (!Directory.Exists(local)) continue;
            foreach (var path in Directory.EnumerateFiles(local))
                ClipRay.App.Infrastructure.LocalPathPolicy.RequireLocal(path);
        }
    }
}
