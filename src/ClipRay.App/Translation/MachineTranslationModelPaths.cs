using System.IO;

namespace ClipRay.App.Translation;

public static class MachineTranslationModelPaths
{
    public static string Resolve(string installRoot)
    {
        var shared = Path.Combine(installRoot, "machine-translation", "models", "small100");
        if (IsCommonLanguageModel(shared)) return shared;
        return Path.Combine(installRoot, "machine-translation", "models", "translate-en_zh-1_9");
    }

    public static bool IsCommonLanguageModel(string path) =>
        File.Exists(Path.Combine(path, "model.bin"))
        && File.Exists(Path.Combine(path, "config.json"))
        && File.Exists(Path.Combine(path, "shared_vocabulary.json"))
        && File.Exists(Path.Combine(path, "sentencepiece.bpe.model"));
}
