using System.IO;

namespace ClipRay.App.Translation;

// A file inventory, deliberately not a runtime/quality readiness claim.
public sealed record TranslationFileCheck(string ModelName, IReadOnlyList<string> MissingFiles)
{
    public string Describe(string language)
    {
        var english = language == "en";
        return MissingFiles.Count > 0
            ? (english ? "Local translation files missing: " : "本地翻译缺少文件：")
                + string.Join(" · ", MissingFiles)
                + (english ? ". Restore these files, then reopen the workbench to check again. The original image is retained."
                    : "。补齐文件后重新打开工作台可再次检查；原图仍保留。")
            : ModelName + (english
                ? " · Files found; model integrity, runtime dependencies and translation quality are not verified by this check."
                : " · 已找到文件；本次检查未验证模型完整性、运行依赖或翻译质量。");
    }

    public static TranslationFileCheck? Inspect(TranslationRouteKind route, string root, string? configuredModel = null)
    {
        if (route == TranslationRouteKind.LocalLmt)
        {
            var options = LocalLmtOptions.Discover(configuredModel, root);
            var missing = new List<string>();
            if (!File.Exists(options.ModelPath)) missing.Add("GGUF");
            if (!File.Exists(options.LlamaCliPath)) missing.Add("llama-cli.exe");
            return new(Path.GetFileName(options.ModelPath), missing);
        }
        if (route != TranslationRouteKind.Machine) return null;
        var model = MachineTranslationModelPaths.Resolve(root);
        var common = MachineTranslationModelPaths.IsCommonLanguageModel(model);
        var weights = common ? model : Path.Combine(model, "model");
        var paths = new[]
        {
            Path.Combine(root, "machine-translation", "runtime", "python.exe"),
            Path.Combine(root, "machine-translation", "machine_translation_worker.py"),
            Path.Combine(weights, "model.bin"),
            Path.Combine(weights, "config.json"),
            Path.Combine(weights, "shared_vocabulary.json"),
            Path.Combine(model, common
                ? "sentencepiece.bpe.model" : "sentencepiece.model")
        };
        return new(Path.GetFileName(model), paths.Where(path => !File.Exists(path))
            .Select(path => Path.GetRelativePath(root, path)).ToArray());
    }
}
