using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using ClipRay.App.Infrastructure;
using ClipRay.Core.Geometry;
using RapidOcrNet;
using SkiaSharp;

namespace ClipRay.App.Ocr;

public sealed record RapidOcrModelPaths(string Detector, string Recognizer, string Dictionary)
{
    public static RapidOcrModelPaths Resolve(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var directory = Path.Combine(workspaceRoot, "artifacts", "models", "rapidocr");
        return new RapidOcrModelPaths(
            RequireFile(directory, "PP-OCRv6_det_small.onnx"),
            RequireFile(directory, "PP-OCRv6_rec_small.onnx"),
            RequireFile(directory, "ppocrv6_small_dict.txt"));
    }

    internal static RapidOcrModelPaths ResolveForRuntime()
    {
        var packagedDirectory = Path.Combine(AppContext.BaseDirectory, "models", "v6");
        if (HasModelSet(packagedDirectory))
        {
            return new RapidOcrModelPaths(
                Path.Combine(packagedDirectory, "PP-OCRv6_det_small.onnx"),
                Path.Combine(packagedDirectory, "PP-OCRv6_rec_small.onnx"),
                Path.Combine(packagedDirectory, "ppocrv6_small_dict.txt"));
        }

        return Resolve(WorkspaceLocator.Find(AppContext.BaseDirectory));
    }

    private static bool HasModelSet(string directory) =>
        File.Exists(Path.Combine(directory, "PP-OCRv6_det_small.onnx"))
        && File.Exists(Path.Combine(directory, "PP-OCRv6_rec_small.onnx"))
        && File.Exists(Path.Combine(directory, "ppocrv6_small_dict.txt"));

    private static string RequireFile(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        return File.Exists(path)
            ? path
            : throw new OcrUnavailableException("The locked local PP-OCRv6 model set is incomplete.");
    }
}

public sealed record KoreanRapidOcrModelPaths(string Recognizer, string Dictionary)
{
    public static KoreanRapidOcrModelPaths Resolve(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var directory = Path.Combine(workspaceRoot, "artifacts", "models", "rapidocr");
        return new KoreanRapidOcrModelPaths(
            RequireFile(directory, "korean_PP-OCRv5_rec_mobile.onnx"),
            RequireFile(directory, "ppocrv5_korean_dict.txt"));
    }

    internal static KoreanRapidOcrModelPaths ResolveForRuntime()
    {
        var packagedDirectory = Path.Combine(AppContext.BaseDirectory, "models", "v5");
        if (HasModelSet(packagedDirectory))
        {
            return new KoreanRapidOcrModelPaths(
                Path.Combine(packagedDirectory, "korean_PP-OCRv5_rec_mobile.onnx"),
                Path.Combine(packagedDirectory, "ppocrv5_korean_dict.txt"));
        }

        return Resolve(WorkspaceLocator.Find(AppContext.BaseDirectory));
    }

    private static bool HasModelSet(string directory) =>
        File.Exists(Path.Combine(directory, "korean_PP-OCRv5_rec_mobile.onnx"))
        && File.Exists(Path.Combine(directory, "ppocrv5_korean_dict.txt"));

    private static string RequireFile(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        return File.Exists(path)
            ? path
            : throw new OcrUnavailableException("The Korean PP-OCRv5 supplemental model set is incomplete.");
    }
}

public static class RapidOcrThreadPolicy
{
    public static int Select(int processorCount)
    {
        if (processorCount <= 0) throw new ArgumentOutOfRangeException(nameof(processorCount));
        return Math.Clamp(processorCount / 2, 1, 12);
    }
}

internal sealed class RapidOcrRuntime(bool useKoreanSupplement = false, bool useLatinModel = false, int? threadCountOverride = null, bool disableSpinning = false, Action<OcrResult>? observeResult = null) : IOcrRuntime, IOcrWarmup, IDisposable
{
    private const string ClassifierName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";
    private const string V5DetectorName = "ch_PP-OCRv5_mobile_det.onnx";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RapidOcr? _ocr;
    private string _recognizerModelId = string.Empty;
    private string? _recognizerModelSha256;
    private bool _disposed;

    public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using var prepared = OcrPolarityPlan.CreatePreparedBitmap(bitmap);
        var png = await OcrBitmapEncoder.EncodePngAsync(prepared, null, cancellationToken).ConfigureAwait(false);
        using var skBitmap = SKBitmap.Decode(png)
            ?? throw new OcrUnavailableException("The local OCR image could not be decoded.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ocr = EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Task.Run(
                () => ocr.Detect(
                    skBitmap,
                    (useKoreanSupplement || useLatinModel ? RapidOcrOptions.Default : RapidOcrOptions.PPOCRv6) with { DoAngle = false }),
                cancellationToken).ConfigureAwait(false);
            observeResult?.Invoke(result);
            return result.TextBlocks
                .Where(static block => !string.IsNullOrWhiteSpace(block.Text))
                .Select(block =>
                {
                    var left = block.BoxPoints.Min(static point => point.X);
                    var top = block.BoxPoints.Min(static point => point.Y);
                    var right = block.BoxPoints.Max(static point => point.X);
                    var bottom = block.BoxPoints.Max(static point => point.Y);
                    return new OcrWord(block.Text, new PixelRect(left, top, right - left, bottom - top),
                        new OcrWordEvidence("rapidocr", _recognizerModelId,
                            useLatinModel ? "latin" : useKoreanSupplement ? "korean" : "multilingual", null,
                            _recognizerModelSha256));
                })
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WarmupAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_disposed) await Task.Run(EnsureInitialized, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { /* foreground recognition will use the Windows OCR fallback */ }
    }

    private RapidOcr EnsureInitialized()
    {
        if (_ocr is not null) return _ocr;
        var classifier = Path.Combine(AppContext.BaseDirectory, "models", "v5", ClassifierName);
        if (!File.Exists(classifier))
            throw new OcrUnavailableException("The packaged PP-OCR orientation classifier is unavailable.");

        var modelSet = useLatinModel ? CreateLatinModelSet(classifier) : useKoreanSupplement
            ? CreateKoreanModelSet(classifier)
            : CreateMultilingualModelSet(classifier);
        var ocr = new RapidOcr();
        try
        {
            using var sessionOptions = RapidOcr.GetDefaultSessionOptions(
                threadCountOverride ?? (useLatinModel ? Math.Min(4, RapidOcrThreadPolicy.Select(Environment.ProcessorCount))
                    : RapidOcrThreadPolicy.Select(Environment.ProcessorCount)));
            if (disableSpinning)
            {
                sessionOptions.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                sessionOptions.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
            }
            ocr.InitModels(modelSet, sessionOptions);
            _recognizerModelId = Path.GetFileName(modelSet.RecModelPath);
            try
            {
                using var model = File.OpenRead(modelSet.RecModelPath);
                _recognizerModelSha256 = Convert.ToHexString(SHA256.HashData(model)).ToLowerInvariant();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _recognizerModelSha256 = null;
            }
            _ocr = ocr;
            return ocr;
        }
        catch
        {
            ocr.Dispose();
            throw;
        }
    }

    private static RapidOcrModelSet CreateMultilingualModelSet(string classifier)
    {
        var paths = RapidOcrModelPaths.ResolveForRuntime();
        return RapidOcrModelSet.PPOCRv6Small with
        {
            DetModelPath = paths.Detector,
            ClsModelPath = classifier,
            RecModelPath = paths.Recognizer,
            KeysPath = paths.Dictionary,
        };
    }

    private static RapidOcrModelSet CreateLatinModelSet(string classifier)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "models", "v5");
        var detector = Path.Combine(directory, V5DetectorName);
        var recognizer = Path.Combine(directory, "latin_PP-OCRv5_rec_mobile_infer.onnx");
        var dictionary = Path.Combine(directory, "ppocrv5_latin_dict.txt");
        if (!File.Exists(detector) || !File.Exists(recognizer) || !File.Exists(dictionary))
            throw new OcrUnavailableException("The packaged local Latin OCR model set is incomplete.");
        return RapidOcrModelSet.PPOCRv5Latin with
        {
            DetModelPath = detector, ClsModelPath = classifier,
            RecModelPath = recognizer, KeysPath = dictionary,
        };
    }

    private static RapidOcrModelSet CreateKoreanModelSet(string classifier)
    {
        var paths = KoreanRapidOcrModelPaths.ResolveForRuntime();
        var detector = Path.Combine(AppContext.BaseDirectory, "models", "v5", V5DetectorName);
        if (!File.Exists(detector))
            throw new OcrUnavailableException("The packaged PP-OCRv5 detector is unavailable.");
        return RapidOcrModelSet.PPOCRv5Latin with
        {
            DetModelPath = detector,
            ClsModelPath = classifier,
            RecModelPath = paths.Recognizer,
            KeysPath = paths.Dictionary,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Wait();
        try
        {
            _ocr?.Dispose();
            _ocr = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
