using System.Drawing;
using System.IO;
using ClipRay.Core.Geometry;
using RapidOcrNet;
using SkiaSharp;
using ClipRay.App.Infrastructure;

namespace ClipRay.App.Ocr;

// All recognizers see the same complete detected line. Supplemental fragments
// cannot remove a primary line merely because a tiny box overlaps it.
internal sealed class CommonScriptOcrRuntime(bool includeKorean = false, Action<PixelRect, IReadOnlyList<ScriptOcrCandidate>>? observeCandidates = null,
    Action? initializeBackend = null, Func<SKBitmap, CancellationToken, IReadOnlyList<OcrWord>>? recognizeBackend = null,
    Action? releaseBackend = null) : IOcrRuntime, IOcrWarmup, IDisposable, IAsyncDisposable
{
    internal static IReadOnlyList<string> RecognitionScripts { get; } = ["latin", "cjk"];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncResourceLifetime _lifetime = new();
    private RapidOcr? _detector;
    private readonly List<(string Script, string ModelId, TextRecognizer Recognizer)> _recognizers = [];

    private void Initialize()
    {
        if (initializeBackend is not null) { initializeBackend(); return; }
        if (_detector is not null) return;
        var v5 = Path.Combine(AppContext.BaseDirectory, "models", "v5");
        var cjk = RapidOcrModelPaths.ResolveForRuntime();
        using var options = RapidOcr.GetDefaultSessionOptions(OcrExecutionPolicy.SelectThreads(Environment.ProcessorCount));
        foreach (var entry in OcrExecutionPolicy.SessionEntries) options.AddSessionConfigEntry(entry.Key, entry.Value);
        var detector = new RapidOcr();
        try
        {
            detector.InitModels(Path.Combine(v5, "ch_PP-OCRv5_mobile_det.onnx"),
                Path.Combine(v5, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
                Path.Combine(v5, "latin_PP-OCRv5_rec_mobile_infer.onnx"),
                Path.Combine(v5, "ppocrv5_latin_dict.txt"), options);
            foreach (var script in includeKorean ? new[] { "latin", "cjk", "korean" } : RecognitionScripts)
            {
                var paths = script switch
                {
                    "latin" => (Path.Combine(v5, "latin_PP-OCRv5_rec_mobile_infer.onnx"), Path.Combine(v5, "ppocrv5_latin_dict.txt")),
                    "cjk" => (cjk.Recognizer, cjk.Dictionary),
                    "korean" => (KoreanRapidOcrModelPaths.ResolveForRuntime().Recognizer, KoreanRapidOcrModelPaths.ResolveForRuntime().Dictionary),
                    _ => throw new InvalidOperationException("Unsupported machine OCR script."),
                };
                Add(script, paths.Item1, paths.Item2);
            }
            _detector = detector;
        }
        catch
        {
            detector.Dispose();
            foreach (var entry in _recognizers) entry.Recognizer.Dispose();
            _recognizers.Clear();
            throw;
        }
        void Add(string script, string model, string dictionary)
        {
            var recognizer = new TextRecognizer();
            try { recognizer.InitModel(model, dictionary, options); _recognizers.Add((script, Path.GetFileName(model), recognizer)); }
            catch { recognizer.Dispose(); throw; }
        }
    }

    public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        using var admission = _lifetime.Enter();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = linked.Token;
        using var prepared = OcrPolarityPlan.CreatePreparedBitmap(bitmap);
        using var image = SKBitmap.Decode(await OcrBitmapEncoder.EncodePngAsync(prepared, null, cancellationToken).ConfigureAwait(false));
        if (image is null) throw new OcrUnavailableException("The local OCR image could not be decoded.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Initialize();
            return await Task.Run(() => recognizeBackend is null ? Recognize(image, cancellationToken)
                : recognizeBackend(image, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private IReadOnlyList<OcrWord> Recognize(SKBitmap image, CancellationToken token)
    {
        var boxes = _detector!.DetectBoxes(image, RapidOcrOptions.Default with { DoAngle = false, UnClipRatio = 1.0f });
        var crops = new List<SKBitmap>();
        var bounds = new List<PixelRect>();
        try
        {
            foreach (var box in boxes)
            {
                var rect = new SKRectI(Math.Max(0, box.BoxPoints.Min(p => p.X) - 2),
                    Math.Max(0, box.BoxPoints.Min(p => p.Y) - 2),
                    Math.Min(image.Width, box.BoxPoints.Max(p => p.X) + 3),
                    Math.Min(image.Height, box.BoxPoints.Max(p => p.Y) + 3));
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                var crop = new SKBitmap();
                if (!image.ExtractSubset(crop, rect)) { crop.Dispose(); continue; }
                crops.Add(crop);
                bounds.Add(new(rect.Left, rect.Top, rect.Width, rect.Height));
            }
            if (crops.Count == 0) return [];
            var candidates = Enumerable.Range(0, crops.Count).Select(_ => new List<ScriptOcrCandidate>()).ToArray();
            foreach (var entry in _recognizers)
            {
                token.ThrowIfCancellationRequested();
                var indices = Enumerable.Range(0,crops.Count).Where(i => !candidates[i].Any(candidate =>
                    candidate.Script == "latin" && candidate.Confidence >= .99
                    && candidate.Text.Count(char.IsLetter) >= 4
                    && candidate.Text.All(c => c < '\u0250'))).ToArray();
                if(indices.Length == 0) break;
                var lines = entry.Recognizer.GetTextLines(indices.Select(i=>crops[i]).ToArray());
                for (var i = 0; i < lines.Length; i++)
                    candidates[indices[i]].Add(ScriptOcrCandidate.FromRecognition(ScriptOcrSelection.RestoreReadingOrder(string.Concat(lines[i].Chars ?? []), entry.Script),
                        lines[i].CharScores, entry.Script));
            }
            if (observeCandidates is not null)
                for (var i = 0; i < candidates.Length; i++) observeCandidates(bounds[i], candidates[i].ToArray());
            return candidates.Select((choices, i) => (Selection: ScriptOcrSelection.SelectWithReview(choices), Bounds: bounds[i]))
                .Where(item => item.Selection.Candidate is not null).Select(item =>
                {
                    var choice = item.Selection.Candidate!;
                    var modelId = _recognizers.Single(entry => entry.Script == choice.Script).ModelId;
                    return new OcrWord(choice.Text, item.Bounds,
                        OcrWordEvidence.FromScriptCandidate(choice, modelId) with { AlternativeText = item.Selection.AlternativeText })
                        { IsDetectedLine = true };
                }).ToArray();
        }
        finally { foreach (var crop in crops) crop.Dispose(); }
    }

    public async Task WarmupAsync(CancellationToken token)
    {
        using var admission = _lifetime.Enter();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try { await Task.Run(Initialize, linked.Token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public void Dispose() => _ = DisposeAsync();
    public ValueTask DisposeAsync() => new(_lifetime.RetireAsync(() => Task.Run(() =>
    {
        if (releaseBackend is not null) releaseBackend();
        else
        {
            try { _detector?.Dispose(); }
            finally { foreach (var entry in _recognizers) entry.Recognizer.Dispose(); }
        }
        _detector = null; _recognizers.Clear();
    })));
}
