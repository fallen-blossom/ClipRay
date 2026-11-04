using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace ClipRay.App.Infrastructure;

public static class NativeRuntimePrivacy
{
    private static readonly Lazy<bool> Initialization = new(static () =>
    {
        // This calls the real native ORT API, not a logging/session-option flag.
        // Keep the process-wide singleton alive. Failure prevents model startup.
        OrtEnv.Instance().DisableTelemetryEvents();
        return true;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Call before any ONNX/RapidOCR session in every entry point.</summary>
    public static void Initialize() => _ = Initialization.Value;

    /// <summary>Defence in depth for child runtimes; this is not a network sandbox.</summary>
    public static void ConfigureLocalChild(ProcessStartInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        // Only the child environment changes. Inherited remote llama options
        // must not turn a local --model invocation into download/RPC activity.
        foreach (var key in info.Environment.Keys.Where(static key =>
            key.StartsWith("LLAMA_ARG_", StringComparison.OrdinalIgnoreCase)).ToArray())
            info.Environment.Remove(key);
        foreach (var key in new[] { "HF_TOKEN", "HUGGING_FACE_HUB_TOKEN", "HF_TOKEN_PATH" })
            info.Environment.Remove(key);
        info.Environment["HF_HUB_OFFLINE"] = "1";
        info.Environment["HF_DATASETS_OFFLINE"] = "1";
        info.Environment["TRANSFORMERS_OFFLINE"] = "1";
        info.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        info.Environment["HF_HUB_DISABLE_IMPLICIT_TOKEN"] = "1";
        info.Environment["DO_NOT_TRACK"] = "1";
        // PaddleX checks model-source connectivity at import time, before the
        // sidecar can supply its local model_dir. Set before starting Python.
        info.Environment["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True";
        // Avoid user sitecustomize/.pth/PYTHONPATH injecting a remote importer.
        info.Environment["PYTHONNOUSERSITE"] = "1";
        info.Environment.Remove("PYTHONPATH");
        info.Environment.Remove("PYTHONSTARTUP");
    }
}
