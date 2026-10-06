using System.IO;
using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClipRay.Core.Geometry;
using ClipRay.App.Translation;

namespace ClipRay.TestLab;

// The oracle lives beside a private TestLab manifest. It is written from the source image,
// never generated from OCR or translated output.
public sealed record OracleBounds(double X, double Y, double Width, double Height)
{
    public double OverlapFraction(VisualTextRegion region)
    {
        var left = Math.Max(X, region.Bounds.Left);
        var top = Math.Max(Y, region.Bounds.Top);
        var right = Math.Min(X + Width, region.Bounds.Right);
        var bottom = Math.Min(Y + Height, region.Bounds.Bottom);
        return Math.Max(0, right - left) * Math.Max(0, bottom - top) / (Width * Height);
    }

    public double IntersectionArea(OracleBounds other) =>
        Math.Max(0, Math.Min(X + Width, other.X + other.Width) - Math.Max(X, other.X))
        * Math.Max(0, Math.Min(Y + Height, other.Y + other.Height) - Math.Max(Y, other.Y));

    public bool Contains(PixelRect bounds, double tolerance = 0) =>
        bounds.Left >= X - tolerance && bounds.Top >= Y - tolerance
        && bounds.Right <= X + Width + tolerance && bounds.Bottom <= Y + Height + tolerance;
}

public sealed record OracleMeaningSignal(string Id, IReadOnlyList<string> AnyOf);

public sealed record OracleRegion(
    string Id,
    OracleBounds Bounds,
    string SourceText,
    IReadOnlyList<OracleMeaningSignal> MeaningSignals,
    IReadOnlyList<string> ProtectedTokens,
    bool MustRender,
    double MinimumFontPixels,
    string? FontTier = null,
    string? ForegroundRgb = null,
    double? MaximumColorDistance = null,
    OracleBounds? AllowedLayoutBounds = null,
    string? ReadingGroup = null,
    string? ExpectedAlignment = null,
    bool MustPreserveSource = false,
    bool MustPreservePixels = false);

public sealed record OracleCase(
    string Id,
    string ImageSha256,
    string TargetLanguage,
    IReadOnlyList<OracleRegion> Regions);

public sealed record OracleFile(int SchemaVersion, IReadOnlyList<OracleCase> Cases);

public sealed record TestLabOracleFinding(
    string Region,
    string Stage,
    string Code,
    string Expected,
    string Actual);

public sealed record TestLabOracleAssessment(
    string OracleSha256,
    string ContentQuality,
    string LayoutQuality,
    IReadOnlyList<TestLabOracleFinding> Findings);

public sealed class TestLabQualityOracle
{
    // The source image explicitly assigns these regions to the same visual
    // tier. A 50% difference concealed visibly shrunken body translations.
    private const double MaximumSameTierFontRatio = 1.1;
    private const double MaximumUnexpectedOverlapFraction = 0.1;
    private const double AnnotatedBoundsTolerancePixels = 5;
    private readonly IReadOnlyDictionary<string, OracleCase> _cases;

    private TestLabQualityOracle(string sha256, IReadOnlyDictionary<string, OracleCase> cases)
    {
        Sha256 = sha256;
        _cases = cases;
    }

    public string Sha256 { get; }

    public bool Covers(IReadOnlyList<TestLabCase> cases) =>
        cases.Count == _cases.Count
        && cases.All(testCase => _cases.TryGetValue(testCase.Id, out var expected)
            && string.Equals(expected.TargetLanguage, testCase.TargetLanguage, StringComparison.Ordinal));

    public bool AllowsIntentionalPreservation(string caseId, string imageSha256,
        string targetLanguage, ApplicationTranslationOutcome outcome)
    {
        if (!_cases.TryGetValue(caseId, out var expected)
            || !string.Equals(expected.ImageSha256, imageSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(expected.TargetLanguage, targetLanguage, StringComparison.Ordinal))
            return false;
        foreach (var rejection in outcome.LayoutRejections ?? [])
        {
            if (rejection.Reason != "identity-pixels-preserved"
                || rejection.Region < 1 || rejection.Region > outcome.Regions.Count)
                return false;
            var region = outcome.Regions[rejection.Region - 1];
            if (!expected.Regions.Any(truth => (truth.MustPreservePixels || truth.MustPreserveSource)
                && truth.Bounds.OverlapFraction(region) >= 0.2
                && Normalize(truth.SourceText) == Normalize(region.SourceText)))
                return false;
        }
        return true;
    }

    public static TestLabQualityOracle? Load(string root)
    {
        var path = Path.Combine(root, "quality-oracle.json");
        if (!File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        var file = JsonSerializer.Deserialize<OracleFile>(bytes, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        if (file is null || file.SchemaVersion != 1 || file.Cases is null || file.Cases.Count == 0)
            throw new InvalidDataException("quality-oracle-invalid");
        var cases = new Dictionary<string, OracleCase>(StringComparer.Ordinal);
        foreach (var item in file.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !IsSha(item.ImageSha256)
                || string.IsNullOrWhiteSpace(item.TargetLanguage) || item.Regions is null
                || item.Regions.Count == 0 || !cases.TryAdd(item.Id, item))
                throw new InvalidDataException("quality-oracle-invalid");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var region in item.Regions)
            {
                if (string.IsNullOrWhiteSpace(region.Id) || !ids.Add(region.Id)
                    || !ValidBounds(region.Bounds) || string.IsNullOrWhiteSpace(region.SourceText)
                    || region.MeaningSignals is null || region.ProtectedTokens is null
                    || !double.IsFinite(region.MinimumFontPixels) || region.MinimumFontPixels < 0
                    || region.FontTier is not null && string.IsNullOrWhiteSpace(region.FontTier)
                    || region.ReadingGroup is not null && string.IsNullOrWhiteSpace(region.ReadingGroup)
                    || region.ExpectedAlignment is not null
                        && region.ExpectedAlignment is not ("left" or "center" or "right")
                    || (region.MustPreserveSource || region.MustPreservePixels) && region.MustRender
                    || (region.ForegroundRgb is null) != (region.MaximumColorDistance is null)
                    || region.ForegroundRgb is not null && !Regex.IsMatch(region.ForegroundRgb, @"^#[0-9a-fA-F]{6}$")
                    || region.MaximumColorDistance is { } maximumColorDistance
                        && (!double.IsFinite(maximumColorDistance) || maximumColorDistance < 0
                            || maximumColorDistance > Math.Sqrt(3 * 255 * 255))
                    || region.AllowedLayoutBounds is not null && !ValidBounds(region.AllowedLayoutBounds)
                    || region.MeaningSignals.Any(signal => string.IsNullOrWhiteSpace(signal.Id)
                        || signal.AnyOf is null || signal.AnyOf.Count == 0
                        || signal.AnyOf.Any(string.IsNullOrWhiteSpace))
                    || region.ProtectedTokens.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException("quality-oracle-invalid");
            }
            if (item.Regions.Where(region => region.FontTier is not null)
                .GroupBy(region => region.FontTier, StringComparer.Ordinal)
                .Any(group => group.Count() < 2 || group.Any(region => !region.MustRender)))
                throw new InvalidDataException("quality-oracle-invalid");
        }
        return new TestLabQualityOracle(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), cases);
    }

    public TestLabOracleAssessment? Assess(
        string caseId,
        string imageSha256,
        string targetLanguage,
        ApplicationTranslationOutcome outcome,
        bool artifactWritten,
        int imageWidth = 0,
        int imageHeight = 0,
        Bitmap? sourceImage = null,
        Bitmap? renderedImage = null)
    {
        if (!_cases.TryGetValue(caseId, out var expected)) return null;
        var findings = new List<TestLabOracleFinding>();
        void Add(string region, string stage, string code, string want, string got) =>
            findings.Add(new TestLabOracleFinding(region, stage, code, want, got));

        if (!string.Equals(expected.ImageSha256, imageSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(expected.TargetLanguage, targetLanguage, StringComparison.Ordinal))
        {
            Add("case", "input", "oracle-input-mismatch",
                $"sha256={expected.ImageSha256}; target={expected.TargetLanguage}",
                $"sha256={imageSha256}; target={targetLanguage}");
            return new TestLabOracleAssessment(Sha256, "blocked", "blocked", findings);
        }

        var previousIndexByGroup = new Dictionary<string, int>(StringComparer.Ordinal);
        var claimed = new HashSet<int>();
        var rendered = new List<(OracleRegion Truth, InPlaceLayoutItem Item)>();
        foreach (var truth in expected.Regions)
        {
            var matches = outcome.Regions.Select((region, index) => (region, index))
                .Where(item => truth.Bounds.OverlapFraction(item.region) >= 0.2)
                .OrderBy(item => item.index).ToArray();
            if (matches.Length == 0)
            {
                Add(truth.Id, "ocr", "region-missing", truth.SourceText, "<missing>");
                continue;
            }
            if (matches.Length > 1)
                Add(truth.Id, "region", "region-split", "one complete region",
                    string.Join(",", matches.Select(item => item.region.Id)));
            // Older oracles have no group and retain their original global ordering rule.
            var readingGroup = truth.ReadingGroup ?? string.Empty;
            var previousIndex = previousIndexByGroup.TryGetValue(readingGroup, out var index)
                ? index : -1;
            if (matches[0].index <= previousIndex)
                Add(truth.Id, "region", "reading-order", $"after index {previousIndex}",
                    $"index {matches[0].index}");
            previousIndexByGroup[readingGroup] = matches[0].index;
            foreach (var match in matches)
            {
                if (!claimed.Add(match.index))
                    Add(truth.Id, "region", "region-shared", "distinct region", match.region.Id);
            }
            var actualSource = string.Join(" ", matches.Select(item => item.region.SourceText));
            if (Normalize(actualSource) != Normalize(truth.SourceText))
                Add(truth.Id, "ocr", "text-mismatch", truth.SourceText, actualSource);

            if (truth.MustPreservePixels || truth.MustPreserveSource)
            {
                var covering = outcome.LayoutItems.FirstOrDefault(item =>
                    new OracleBounds(item.Bounds.X, item.Bounds.Y,
                        item.Bounds.Width, item.Bounds.Height)
                    .IntersectionArea(truth.Bounds) > 0);
                var pixelsMatch = sourceImage is not null && renderedImage is not null
                    && PixelsMatch(sourceImage, renderedImage, truth.Bounds);
                if (covering is not null && !pixelsMatch
                    && sourceImage is not null && renderedImage is not null)
                    Add(truth.Id, "integrity", "protected-pixels-covered",
                        "no overlay changes protected pixels",
                        covering.SourceRegion?.Id ?? "unbound overlay");
                if (sourceImage is null || renderedImage is null)
                    Add(truth.Id, "integrity", "protected-pixels-unverified",
                        "source and rendered PNG available", "pixel comparison unavailable");
                else if (!pixelsMatch)
                    Add(truth.Id, "integrity", "protected-pixels-changed",
                        "source and rendered PNG pixels match", Format(truth.Bounds));
            }

            if (truth.MustPreserveSource)
            {
                foreach (var match in matches)
                {
                    var changedResult = match.index < outcome.Results.Count
                        && outcome.Results[match.index].IsSuccess
                        && Normalize(outcome.Results[match.index].Text)
                            != Normalize(match.region.SourceText);
                    var covered = outcome.LayoutItems.Any(item => item.SourceRegion?.Id == match.region.Id);
                    if (changedResult || covered)
                        Add(truth.Id, "integrity", "protected-source-changed",
                            match.region.SourceText, changedResult
                                ? outcome.Results[match.index].Text : "translated overlay");
                }
                continue;
            }

            if (truth.MeaningSignals.Count == 0 && truth.ProtectedTokens.Count == 0 && !truth.MustRender)
                continue;
            var translations = matches.Select(item => item.index < outcome.Results.Count
                ? outcome.Results[item.index] : null).ToArray();
            if (translations.Any(result => result is null || !result.IsSuccess
                || string.IsNullOrWhiteSpace(result.Text)))
            {
                Add(truth.Id, "translation", "translation-missing", "successful nonempty translation",
                    string.Join("; ", translations.Select(result => result?.Status ?? "<missing>")));
                continue;
            }
            var translated = string.Join(" ", translations.Select(result => result!.Text));
            foreach (var signal in truth.MeaningSignals)
            {
                if (!signal.AnyOf.Any(option => translated.Contains(option, StringComparison.OrdinalIgnoreCase)))
                    Add(truth.Id, "translation", "meaning-signal-missing",
                        signal.Id + ": " + string.Join(" | ", signal.AnyOf), translated);
            }
            foreach (var token in truth.ProtectedTokens)
            {
                var count = Regex.Matches(translated, Regex.Escape(token), RegexOptions.CultureInvariant).Count;
                if (count != 1)
                    Add(truth.Id, "integrity", "protected-token-count", token + " exactly once",
                        token + " count=" + count);
            }
            if (!truth.MustRender) continue;
            foreach (var match in matches)
            {
                var layoutMatches = outcome.LayoutItems
                    .Where(item => item.SourceRegion?.Id == match.region.Id).ToArray();
                if (layoutMatches.Length > 1)
                    Add(truth.Id, "layout", "layout-duplicated", "one layout item",
                        $"{layoutMatches.Length} layout items for {match.region.Id}");
                var layout = layoutMatches.FirstOrDefault();
                if (layout is null)
                {
                    var reason = outcome.LayoutRejections?.FirstOrDefault(item => item.Region == match.index + 1)?.Reason
                        ?? "no layout item or rejection";
                    Add(truth.Id, "layout", "region-not-rendered", "readable complete region", reason);
                    continue;
                }
                var translatedText = outcome.Results[match.index].Text;
                if (Normalize(layout.Text) != Normalize(translatedText))
                    Add(truth.Id, "layout", "layout-text-mismatch", translatedText, layout.Text);
                if (layout.TextLayout?.ShouldRender != true
                    || !double.IsFinite(layout.TextLayout.FontSizePixels)
                    || layout.TextLayout.FontSizePixels < truth.MinimumFontPixels)
                    Add(truth.Id, "layout", "unreadable-layout",
                        $"render=true; font>={truth.MinimumFontPixels}",
                        $"render={layout.TextLayout?.ShouldRender}; font={layout.TextLayout?.FontSizePixels}");
                if (!ValidBounds(layout.Bounds))
                {
                    Add(truth.Id, "layout", "invalid-layout-bounds", "finite positive rectangle",
                        Format(layout.Bounds));
                    continue;
                }
                if (imageWidth > 0 && imageHeight > 0
                    && !new OracleBounds(0, 0, imageWidth, imageHeight)
                        .Contains(layout.Bounds))
                    Add(truth.Id, "layout", "layout-outside-image",
                        $"0,0,{imageWidth},{imageHeight}", Format(layout.Bounds));
                if (truth.AllowedLayoutBounds is { } allowed
                    && !allowed.Contains(layout.Bounds, AnnotatedBoundsTolerancePixels))
                    Add(truth.Id, "layout", "layout-outside-allowed-bounds",
                        Format(allowed), Format(layout.Bounds));
                if (truth.ExpectedAlignment is { } expectedAlignment
                    && !string.Equals(layout.TextLayout?.Alignment.ToString(), expectedAlignment,
                        StringComparison.OrdinalIgnoreCase))
                    Add(truth.Id, "layout", "alignment-mismatch", expectedAlignment,
                        layout.TextLayout?.Alignment.ToString() ?? "<missing>");
                if (truth.ForegroundRgb is { } expectedColor
                    && truth.MaximumColorDistance is { } colorTolerance)
                {
                    var distance = ColorDistance(expectedColor, layout.ForegroundColor);
                    if (distance > colorTolerance)
                        Add(truth.Id, "layout", "foreground-color-mismatch",
                            $"{expectedColor}; RGB distance <= {colorTolerance:0.#}",
                            $"#{layout.ForegroundColor.R:X2}{layout.ForegroundColor.G:X2}{layout.ForegroundColor.B:X2}; RGB distance {distance:0.#}");
                }
                rendered.Add((truth, layout));
            }
        }
        foreach (var tier in rendered.Where(item => item.Truth.FontTier is not null
                && item.Item.TextLayout is { ShouldRender: true, FontSizePixels: > 0 })
            .GroupBy(item => item.Truth.FontTier!, StringComparer.Ordinal))
        {
            var sizes = tier.Select(item => (item.Truth.Id, item.Item.TextLayout!.FontSizePixels))
                .Where(item => double.IsFinite(item.FontSizePixels)).ToArray();
            if (sizes.Length > 1 && sizes.Max(item => item.FontSizePixels)
                / sizes.Min(item => item.FontSizePixels) > MaximumSameTierFontRatio)
                Add(tier.Key, "layout", "font-tier-inconsistent",
                    $"same-tier font ratio <= {MaximumSameTierFontRatio:0.##}",
                    string.Join(", ", sizes.Select(item => $"{item.Id}:{item.FontSizePixels:0.##}")));
        }
        for (var first = 0; first < rendered.Count; first++)
        for (var second = first + 1; second < rendered.Count; second++)
        {
            var a = rendered[first];
            var b = rendered[second];
            if (a.Truth.Id == b.Truth.Id || a.Truth.Bounds.IntersectionArea(b.Truth.Bounds) > 0)
                continue;
            var actualA = new OracleBounds(a.Item.Bounds.X, a.Item.Bounds.Y,
                a.Item.Bounds.Width, a.Item.Bounds.Height);
            var actualB = new OracleBounds(b.Item.Bounds.X, b.Item.Bounds.Y,
                b.Item.Bounds.Width, b.Item.Bounds.Height);
            var overlap = actualA.IntersectionArea(actualB)
                / Math.Min(actualA.Width * actualA.Height, actualB.Width * actualB.Height);
            if (overlap > MaximumUnexpectedOverlapFraction)
                Add($"{a.Truth.Id}|{b.Truth.Id}", "layout", "layout-overlap",
                    "independently annotated regions do not overlap",
                    $"overlap={overlap:P1}; {Format(actualA)}; {Format(actualB)}");
        }
        if (!artifactWritten)
            Add("case", "layout", "artifact-missing", "rendered artifact", "<missing>");
        // A lexical signal is a review aid, not proof of a semantic violation.
        var meaningReview = findings.Any(item => item.Code == "meaning-signal-missing");
        var contentFailed = findings.Any(item => item.Code != "meaning-signal-missing"
            && item.Stage is "ocr" or "region" or "translation" or "integrity");
        var layoutFailed = findings.Any(item => item.Stage == "layout");
        return new TestLabOracleAssessment(Sha256,
            contentFailed ? "failed" : meaningReview ? "meaning-needs-human-review" : "screened-needs-human-review",
            layoutFailed ? "failed" : contentFailed ? "blocked-by-content"
                : "machine-checked-needs-visual-review",
            findings);
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.Normalize().Replace('’', '\''), @"\s+", " ").Trim();
    private static bool IsSha(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool ValidBounds(OracleBounds? bounds) => bounds is not null
        && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
        && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height)
        && double.IsFinite(bounds.X + bounds.Width) && double.IsFinite(bounds.Y + bounds.Height)
        && double.IsFinite(bounds.Width * bounds.Height)
        && bounds.Width > 0 && bounds.Height > 0;
    private static bool ValidBounds(PixelRect bounds) => double.IsFinite(bounds.X)
        && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width)
        && double.IsFinite(bounds.Height) && double.IsFinite(bounds.Right)
        && double.IsFinite(bounds.Bottom) && double.IsFinite(bounds.Width * bounds.Height)
        && bounds.Width > 0 && bounds.Height > 0;
    private static string Format(OracleBounds bounds) =>
        $"{bounds.X:0.##},{bounds.Y:0.##},{bounds.Width:0.##},{bounds.Height:0.##}";
    private static string Format(PixelRect bounds) =>
        $"{bounds.X:0.##},{bounds.Y:0.##},{bounds.Width:0.##},{bounds.Height:0.##}";
    private static bool PixelsMatch(Bitmap source, Bitmap rendered, OracleBounds region)
    {
        if (source.Width != rendered.Width || source.Height != rendered.Height) return false;
        var left = (int)Math.Floor(region.X);
        var top = (int)Math.Floor(region.Y);
        var right = (int)Math.Ceiling(region.X + region.Width);
        var bottom = (int)Math.Ceiling(region.Y + region.Height);
        if (left < 0 || top < 0 || right > source.Width || bottom > source.Height)
            return false;
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
            if (source.GetPixel(x, y).ToArgb() != rendered.GetPixel(x, y).ToArgb())
                return false;
        return true;
    }
    private static double ColorDistance(string expected, System.Drawing.Color actual)
    {
        var red = Convert.ToInt32(expected.Substring(1, 2), 16) - actual.R;
        var green = Convert.ToInt32(expected.Substring(3, 2), 16) - actual.G;
        var blue = Convert.ToInt32(expected.Substring(5, 2), 16) - actual.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }
}
