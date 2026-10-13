using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Globalization;
using ClipRay.Core.Geometry;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using ClipRay.App.Infrastructure;

namespace ClipRay.App.Ocr;

public sealed record OcrWordEvidence(string EngineId, string ModelId, string? Script, double? RawScore,
    string? ModelSha256 = null, string? AlternativeText = null)
{
    public static OcrWordEvidence FromScriptCandidate(ScriptOcrCandidate candidate, string modelId)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        return new("rapidocr-common-script", modelId, candidate.Script,
            candidate.HasRawConfidence && double.IsFinite(candidate.Confidence) ? candidate.Confidence : null);
    }
}

public sealed record OcrWord(string Text, PixelRect Bounds, OcrWordEvidence? Evidence = null)
{
    public bool IsDetectedLine { get; init; }
}

public sealed record OcrTextBlock(string Text, PixelRect Bounds, IReadOnlyList<OcrWord> Words);

public sealed record OcrRecognitionResult(bool IsSuccess, IReadOnlyList<OcrTextBlock> Blocks, string Status);

public interface IOcrRuntime
{
    Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken);
}

public sealed class OcrService : IDisposable, IAsyncDisposable
{
    private readonly Func<IOcrRuntime> _runtimeFactory;
    private readonly string _successStatus;
    private IOcrRuntime? _runtime;
    private readonly object _runtimeGate = new();
    private readonly AsyncResourceLifetime _lifetime = new();
    private readonly SemaphoreSlim _runtimeUse = new(1,1);
    private readonly TimeProvider _clock;
    private readonly TimeSpan _idleTimeout;
    private readonly ITimer _idleTimer;
    private DateTimeOffset _lastUse;
    private Exception? _idleFailure;
    private IOcrRuntime Runtime { get { lock (_runtimeGate) {
        if(_idleFailure is not null)throw new InvalidOperationException("The OCR idle owner failed retirement.",_idleFailure);
        return _runtime ??= _runtimeFactory(); } } }

    public OcrService() : this(static () => new FastFirstOcrRuntime(
        new ScriptSupplementalOcrRuntime(
            new RapidOcrRuntime(),
            new RapidOcrRuntime(useKoreanSupplement: true),
            ScriptSupplementalOcr.ContainsHangul),
        new WindowsOcrRuntime())) { }

    public OcrService(Func<IOcrRuntime> runtimeFactory, string? successStatus = null, TimeProvider? clock=null, TimeSpan? idleTimeout=null)
    {
        _runtimeFactory = runtimeFactory;
        _successStatus = successStatus ?? "Local OCR completed (multilingual PP-OCRv6 with Korean PP-OCRv5 supplement; Windows OCR fallback).";
        _clock = clock ?? TimeProvider.System;
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(15);
        if (_idleTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        _lastUse = _clock.GetUtcNow();
        _idleTimer = _clock.CreateTimer(_ => _ = ReleaseIdleAsync(), null, _idleTimeout, _idleTimeout);
    }

    public static OcrService CreateForEnglishMachine(IOcrRuntime? english = null, IOcrRuntime? fallback = null) =>
        new(() => {
            var primary = new FastFirstOcrRuntime(
                english ?? new WindowsOcrRuntime("en-US", englishOnly: true),
                fallback ?? new RapidOcrRuntime(useLatinModel: true));
            return english is null && fallback is null
                ? new CommonScriptOcrRuntime()
                : primary;
        }, "Local common-script OCR completed (shared line detection and script recognition).");

    public static OcrService CreateForCommonMachine() => CreateForEnglishMachine();

    public static IReadOnlyList<string> InstalledWindowsLanguages() => OcrEngine.AvailableRecognizerLanguages
        .Select(language => language.LanguageTag).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray();

    public static OcrService CreateForWindowsLanguage(string languageTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        return new(() => new WindowsOcrRuntime(languageTag, requireExactLanguage: true),
            $"Local Windows OCR completed ({languageTag}).");
    }

    public Task<OcrRecognitionResult> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken) => RecognizeAsync(bitmap,cancellationToken,null);
    public async Task<OcrRecognitionResult> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken, Action<OcrRecognitionTrace>? observe)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using var admission = _lifetime.Enter();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = linked.Token;
        using var owned = new Bitmap(bitmap);
        await _runtimeUse.WaitAsync(cancellationToken).ConfigureAwait(false);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var plan = OcrPreprocessingPlan.Create(owned.Width, owned.Height);
            using var prepared = plan.CreatePreparedBitmap(owned);
            var words = await Runtime.RecognizeAsync(prepared, cancellationToken).ConfigureAwait(false);
            var restored = words.Select(word => word with { Bounds = plan.Restore(word.Bounds) }).ToArray();
            var blocks = OcrBlockGrouping.GroupWords(restored);
            try { observe?.Invoke(new(owned.Width,owned.Height,plan.Scale,prepared.Width,prepared.Height,
                words.Select(w => new OcrLineTrace(w.Bounds,w.IsDetectedLine,w.Evidence?.EngineId,w.Evidence?.ModelId,w.Evidence?.RawScore,w.Text.Length)).ToArray(),blocks.Count,clock.ElapsedMilliseconds)); } catch { }
            return new OcrRecognitionResult(true, blocks, _successStatus);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OcrUnavailableException exception)
        {
            return new OcrRecognitionResult(false, [], exception.Message);
        }
        catch
        {
            return new OcrRecognitionResult(false, [], "Local OCR failed; the image stayed on this device.");
        }
        finally { _lastUse = _clock.GetUtcNow(); _runtimeUse.Release(); }
    }

    public void Dispose() => _ = DisposeAsync();
    public ValueTask DisposeAsync()
    {
        _idleTimer.Dispose();
        return new(_lifetime.RetireAsync(async () =>
    {
        if (_runtime is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (_runtime is IDisposable disposable) await Task.Run(disposable.Dispose).ConfigureAwait(false);
        if(_idleFailure is not null) throw new InvalidOperationException("OCR idle retirement failed.",_idleFailure);
        }));
    }

    internal async Task ReleaseIdleAsync()
    {
        IDisposable admission;
        try { admission = _lifetime.Enter(); } catch (ObjectDisposedException) { return; }
        using (admission)
        {
            // Never wait for an active native call just to unload an idle resource.
            if (!await _runtimeUse.WaitAsync(0).ConfigureAwait(false)) return;
            try
            {
                if (_clock.GetUtcNow()-_lastUse < _idleTimeout || _runtime is null) return;
                await OcrRuntimeRelease.ReleaseAsync(_runtime).ConfigureAwait(false);
                _runtime = null;
            }
            catch(Exception error) { _idleFailure=error; }
            finally { _runtimeUse.Release(); }
        }
    }

    public async Task WarmupAsync(CancellationToken cancellationToken)
    {
        using var admission = _lifetime.Enter();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _runtimeUse.WaitAsync(linked.Token).ConfigureAwait(false);
        try { if (Runtime is IOcrWarmup warmup) await warmup.WarmupAsync(linked.Token).ConfigureAwait(false); }
        finally { _lastUse = _clock.GetUtcNow(); _runtimeUse.Release(); }
    }
}

public static class OcrBlockGrouping
{
    public static IReadOnlyList<OcrTextBlock> GroupWords(IEnumerable<OcrWord> words)
    {
        var ordered = words.Where(static word => !string.IsNullOrWhiteSpace(word.Text))
            .OrderBy(static word => word.Bounds.Top)
            .ThenBy(static word => word.Bounds.Left)
            .ThenBy(static word => word.Text, StringComparer.Ordinal)
            .ToArray();
        // Complete detector lines already have a spatial boundary. Only word-level
        // outputs need horizontal joining; paragraph joining happens downstream.
        var detectedLines = ordered.Where(word => word.IsDetectedLine).Select(word => new List<OcrWord> { word }).ToArray();
        ordered = ordered.Where(word => !word.IsDetectedLine).ToArray();
        var lines = new List<List<OcrWord>>();
        var typicalHeight = ordered.Length == 0 ? 0 : ordered.Select(static word => word.Bounds.Height).Order().ElementAt(ordered.Length / 2);
        // Attach punctuation after substantial glyphs, so a comma or apostrophe
        // cannot become the reference row for the next sentence.
        foreach (var word in ordered.OrderBy(word => word.Bounds.Height < typicalHeight * 0.55))
        {
            var center = word.Bounds.Top + word.Bounds.Height / 2;
            var line = lines.FirstOrDefault(candidate =>
            {
                var reference = candidate[0].Bounds;
                var referenceCenter = reference.Top + reference.Height / 2;
                var smallGlyph = word.Bounds.Height < typicalHeight * 0.55;
                return Math.Abs(center - referenceCenter) <= (smallGlyph
                    ? reference.Height * 0.6
                    : Math.Min(reference.Height, word.Bounds.Height) * 0.45);
            });
            (line ?? AddLine(lines)).Add(word);
        }

        return lines.SelectMany(SplitLine).Concat(detectedLines).Select(line =>
        {
            var sorted = line.OrderBy(static word => word.Bounds.Left).ThenBy(static word => word.Text, StringComparer.Ordinal).ToArray();
            var left = sorted.Min(static word => word.Bounds.Left);
            var top = sorted.Min(static word => word.Bounds.Top);
            var right = sorted.Max(static word => word.Bounds.Right);
            var bottom = sorted.Max(static word => word.Bounds.Bottom);
            return new OcrTextBlock(JoinLine(sorted), new PixelRect(left, top, right - left, bottom - top), sorted);
        }).OrderBy(static block => block.Bounds.Top).ThenBy(static block => block.Bounds.Left).ToArray();
    }

    private static IEnumerable<IReadOnlyList<OcrWord>> SplitLine(IReadOnlyList<OcrWord> line)
    {
        var sorted = line.OrderBy(static word => word.Bounds.Left).ThenBy(static word => word.Text, StringComparer.Ordinal).ToArray();
        if (sorted.Length == 0) yield break;
        var segment = new List<OcrWord> { sorted[0] };
        for (var index = 1; index < sorted.Length; index++)
        {
            var previous = sorted[index - 1];
            var current = sorted[index];
            var gap = current.Bounds.Left - previous.Bounds.Right;
            var splitThreshold = Math.Max(32, Math.Max(previous.Bounds.Height, current.Bounds.Height) * 2.5);
            // An isolated icon-shaped OCR glyph can sit in another control on the
            // same baseline. Its box height must not bridge that control gap.
            var isolatedGlyphBoundary = previous.Text.Trim().Length == 1
                && previous.Bounds.Width >= previous.Bounds.Height * 0.75
                && current.Text.Trim().Length >= 3
                && gap > Math.Max(32, Math.Max(previous.Bounds.Height, current.Bounds.Height) * 1.5);
            if (gap > splitThreshold || isolatedGlyphBoundary)
            {
                yield return segment;
                segment = [];
            }
            segment.Add(current);
        }
        yield return segment;
    }

    private static string JoinLine(IReadOnlyList<OcrWord> words)
    {
        if (words.Count == 0) return string.Empty;
        var text = new System.Text.StringBuilder(words[0].Text);
        for (var index = 1; index < words.Count; index++)
        {
            var previous = words[index - 1].Text;
            var current = words[index].Text;
            if (NeedsSeparator(previous, current)) text.Append(' ');
            text.Append(current);
        }
        return text.ToString();
    }

    private static bool NeedsSeparator(string previous, string current)
    {
        if (string.IsNullOrEmpty(previous) || string.IsNullOrEmpty(current)) return false;
        if (IsCjk(previous) && IsCjk(current)) return false;
        if (IsCjkPunctuation(current) || IsCjkPunctuation(previous)) return false;
        return true;
    }

    private static bool IsCjk(string text) => text.Any(static character =>
        character is >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF');

    private static bool IsCjkPunctuation(string text) => text.All(static character =>
        "，。！？；：、（）【】《》“”‘’「」『』…·".Contains(character));

    private static List<OcrWord> AddLine(List<List<OcrWord>> lines)
    {
        var line = new List<OcrWord>();
        lines.Add(line);
        return line;
    }
}

internal sealed class WindowsOcrRuntime : IOcrRuntime
{
    private readonly string? _preferredLanguage;
    private readonly bool _requireExactLanguage;
    private readonly bool _englishOnly;
    private readonly bool _recognizeChinese;

    public WindowsOcrRuntime(string? preferredLanguage = null, bool englishOnly = false, bool recognizeChinese = false, bool requireExactLanguage = false)
    {
        _preferredLanguage = preferredLanguage;
        _requireExactLanguage = requireExactLanguage;
        _englishOnly = englishOnly;
        _recognizeChinese = recognizeChinese;
    }

    public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var available = OcrEngine.AvailableRecognizerLanguages.ToArray();
        if (available.Length == 0)
        {
            throw new OcrUnavailableException("No local Windows OCR language is installed; add one in Windows Settings and retry.");
        }

        var availableTags = available.Select(static language => language.LanguageTag).ToArray();
        var selectedTag = _requireExactLanguage
            ? OcrLanguageSelector.SelectExact(availableTags, _preferredLanguage!)
            : _recognizeChinese
            ? OcrLanguageSelector.SelectEnglishAndChinese(availableTags)
            : _englishOnly
            ? OcrLanguageSelector.SelectEnglish(availableTags)
            : OcrLanguageSelector.Select(availableTags, _preferredLanguage, CultureInfo.CurrentUICulture.Name);
        var png = await OcrBitmapEncoder.EncodePngAsync(bitmap, null, cancellationToken).ConfigureAwait(false);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken);
        var language = available.Single(item => string.Equals(item.LanguageTag, selectedTag, StringComparison.OrdinalIgnoreCase));
        var engine = OcrEngine.TryCreateFromLanguage(language);
        if (engine is null)
        {
            throw new OcrUnavailableException("The selected local Windows OCR language is unavailable.");
        }
        var result = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken);
        var words = new List<OcrWord>();
        foreach (var line in result.Lines)
        foreach (var word in line.Words)
        {
            var rect = word.BoundingRect;
            words.Add(new OcrWord(word.Text, new PixelRect(rect.X, rect.Y, rect.Width, rect.Height),
                new OcrWordEvidence("windows-ocr", selectedTag, null, null)));
        }
        return words;
    }
}

public static class OcrLanguageSelector
{
    public static string SelectExact(IReadOnlyList<string> available, string preferred)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentException.ThrowIfNullOrWhiteSpace(preferred);
        return available.FirstOrDefault(tag => string.Equals(tag, preferred, StringComparison.OrdinalIgnoreCase))
            ?? throw new OcrUnavailableException("The selected Windows OCR language is not installed; install it in Windows Settings or choose another language.");
    }
    public static string SelectEnglishAndChinese(IReadOnlyList<string> available) =>
        available.FirstOrDefault(static tag => tag.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
        ?? available.FirstOrDefault(static tag => tag.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
        ?? SelectEnglish(available);

    public static string SelectEnglish(IReadOnlyList<string> available) =>
        available.FirstOrDefault(static tag => tag.Equals("en-US", StringComparison.OrdinalIgnoreCase))
        ?? available.FirstOrDefault(static tag => tag.Equals("en", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
        ?? throw new OcrUnavailableException("A local English Windows OCR recognizer is unavailable.");

    public static string Select(IReadOnlyList<string> available, string? preferred, string? uiLanguage)
    {
        if (available.Count == 0) throw new OcrUnavailableException("No local Windows OCR language is installed.");
        return OrderCandidates(available, preferred, uiLanguage)[0];
    }

    public static IReadOnlyList<string> OrderCandidates(IReadOnlyList<string> available, string? preferred, string? uiLanguage)
    {
        if (available.Count == 0) throw new OcrUnavailableException("No local Windows OCR language is installed.");
        var ordered = new List<string>(available.Count);
        AddMatch(ordered, available, preferred);
        AddMatch(ordered, available, uiLanguage);
        foreach (var priority in new[] { "en", "zh", "ja", "ko" }) AddMatch(ordered, available, priority);
        foreach (var tag in available.OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)) AddUnique(ordered, tag);
        return ordered;
    }

    private static string? Match(IReadOnlyList<string> available, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        return available.FirstOrDefault(tag => string.Equals(tag, candidate, StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault(tag => tag.StartsWith(candidate + "-", StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault(tag => candidate.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddMatch(ICollection<string> ordered, IReadOnlyList<string> available, string? candidate)
    {
        if (Match(available, candidate) is { } match) AddUnique(ordered, match);
    }

    private static void AddUnique(ICollection<string> ordered, string tag)
    {
        if (!ordered.Contains(tag, StringComparer.OrdinalIgnoreCase)) ordered.Add(tag);
    }
}

public static class OcrBitmapEncoder
{
    public static Task<byte[]> EncodePngAsync(
        Bitmap bitmap,
        Action<int>? observeThread,
        CancellationToken cancellationToken) => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            observeThread?.Invoke(Environment.CurrentManagedThreadId);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }, cancellationToken);
}

public readonly record struct OcrPreprocessingPlan(double Scale)
{
    private const double TargetSmallestDimension = 160;
    private const double MaximumScale = 4;

    public static OcrPreprocessingPlan Create(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        return new OcrPreprocessingPlan(Math.Clamp(TargetSmallestDimension / Math.Min(width, height), 1, MaximumScale));
    }

    public PixelRect Restore(PixelRect bounds) => new(bounds.X / Scale, bounds.Y / Scale, bounds.Width / Scale, bounds.Height / Scale);

    public Bitmap CreatePreparedBitmap(Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Scale == 1) return new Bitmap(source);
        var width = (int)Math.Ceiling(source.Width * Scale);
        var height = (int)Math.Ceiling(source.Height * Scale);
        // OCR 只需要不透明的 RGB；避免高质量插值在截图边缘采样到透明黑，导致首像素变暗。
        var prepared = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(prepared);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, width, height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);
        return prepared;
    }
}

internal sealed class OcrUnavailableException(string message) : Exception(message);

internal static class OcrRuntimeRelease
{
    internal static async Task ReleaseAsync(object runtime)
    {
        if (runtime is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (runtime is IDisposable disposable) await Task.Run(disposable.Dispose).ConfigureAwait(false);
    }
}
