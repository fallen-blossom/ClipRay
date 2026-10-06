using System.Text.RegularExpressions;
using System.Text;

namespace ClipRay.App.Translation;

public static class VisualTranslationRequestFactory
{
    private static readonly HashSet<string> PreservedBrandLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "X", "Grok", "OpenAI", "ChatGPT", "Tailscale", "Windows", "Linux", "Mac",
        "Docker", "Kubernetes", "AWS", "Microsoft Azure", "Google Cloud Platform", "React Native", "Astra",
    };
    private static readonly HashSet<string> KnownUiLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Search", "Explore", "Notifications", "Follow", "Chat", "Grok", "Bookmarks", "Profile",
        "Home", "Posts", "Replies", "Highlights", "Media", "Pinned", "More", "Post", "Premium",
        "Who to follow",
    };
    private static readonly Regex StandaloneWebAddress = new(
        @"^(?:https?://)?(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,24}(?:[/?#][^\s]*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex StandaloneCompactAge = new(
        @"^[·•]?\s*\d+[smhdw]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StandaloneAccountHandle = new(
        @"^@[\p{L}\p{N}_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, string[]> KnownOfficialLatinNames = new(StringComparer.Ordinal)
    {
        ["コミックス・ウェーブ・フィルム"] = ["CoMix Wave Films", "CoMix Wave Films Inc."],
    };

    public static TranslationRequest[] Create(
        IReadOnlyList<VisualTextRegion> regions,
        string targetLanguage,
        string model)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var scene = DetectScene(regions);
        var kinds = ClassifyRegions(regions);
        return regions.Select((region, index) => Create(region, targetLanguage, model, scene, kinds[index]))
            .ToArray();
    }

    public static TranslationTextKind[] ClassifyRegions(IReadOnlyList<VisualTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var scene = DetectScene(regions);
        return regions.Select(region => Classify(region, scene,
            IsLatinAccountDisplayName(region, regions)
                || IsSocialTrendProperName(region, regions, scene))).ToArray();
    }

    private static TranslationRequest Create(
        VisualTextRegion region,
        string targetLanguage,
        string model,
        string scene,
        TranslationTextKind kind)
    {
        return new TranslationRequest(
            "auto",
            targetLanguage,
            kind == TranslationTextKind.Body
                ? BuildSemanticBodyText(region.SourceText)
                : region.SourceText,
            model,
            kind,
            scene,
            Hint(kind, scene, region.SourceText));
    }

    private static string BuildSemanticBodyText(string source)
    {
        var lines = Regex.Split(source.Trim(), @"\r?\n");
        if (lines.Length == 1) return lines[0].Trim();
        var result = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
            {
                if (result.Length > 0 && result[^1] != '\n') result.Append('\n');
                continue;
            }
            if (result.Length > 0 && result[^1] != '\n')
            {
                var previous = lines.Take(index)
                    .LastOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.TrimEnd();
                result.Append(previous is not null && EndsSemanticUnit(previous) ? '\n' : ' ');
            }
            result.Append(line);
        }
        return result.ToString().Trim();

        static bool EndsSemanticUnit(string value) =>
            value.Length > 0 && value[^1] is '.' or '!' or '?' or '。' or '！' or '？' or '…';
    }

    private static string DetectScene(IReadOnlyList<VisualTextRegion> regions)
    {
        var hasIdentity = regions.Any(static region => region.SourceText.Contains('@'));
        var hasSocialSignal = regions.Any(static region =>
            IsHashtag(region.SourceText)
            || region.SourceText.Contains("Show translation", StringComparison.OrdinalIgnoreCase)
            || HasCompactAge(region.SourceText)
            || IsProfileControl(region.SourceText));
        return hasIdentity && hasSocialSignal ? "social_media" : "screen_capture";
    }

    private static bool IsProfileControl(string text)
    {
        var value = text.Trim();
        return string.Equals(value, "Follow", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Joined ", StringComparison.OrdinalIgnoreCase);
    }

    private static TranslationTextKind Classify(
        VisualTextRegion region, string scene, bool isLatinAccountDisplayName)
    {
        if (region.Role == VisualTextRole.IdentityMetadata) return TranslationTextKind.IdentityMetadata;
        if (isLatinAccountDisplayName) return TranslationTextKind.BrandOrIdentifier;
        if (IsOpaqueHashtag(region.SourceText)) return TranslationTextKind.BrandOrIdentifier;
        if (region.Role is VisualTextRole.Control or VisualTextRole.Metadata
            && Regex.IsMatch(region.SourceText.Trim(), @"^(?:[OQIil|uUtThHC]+\s*)?\d+(?:[.,]\d+)?[KkMmBb]?$"))
            return TranslationTextKind.BrandOrIdentifier;
        if (region.Role == VisualTextRole.Control && (
            Regex.IsMatch(region.SourceText.Trim(), @"^(?:[A-Z](?:\s+[A-Z])*|pp|[A-Z]\s*(?:\.{2,}|…))$")
            || Regex.IsMatch(region.SourceText.Trim(), @"^GPT-\d+(?:\.\d+)?\s+(?:Astra|Sol)(?:\s+Thinking)?$",RegexOptions.IgnoreCase)))
            return TranslationTextKind.BrandOrIdentifier;
        var addressLines = region.SourceText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (addressLines.Length > 0 && addressLines.All(StandaloneWebAddress.IsMatch))
            return TranslationTextKind.BrandOrIdentifier;
        if (StandaloneCompactAge.IsMatch(region.SourceText.Trim())) return TranslationTextKind.IdentityMetadata;
        if (IsStandaloneBrandOrIdentifier(region.SourceText)
            || LooksLikeOcrIconNoise(region.SourceText)
            || LooksLikeIconPrefixedBrand(region.SourceText))
        {
            return TranslationTextKind.BrandOrIdentifier;
        }
        if (IsHashtag(region.SourceText)) return TranslationTextKind.Hashtag;
        if (IsSeparatedKatakanaProperName(region.SourceText)) return TranslationTextKind.Heading;
        if (region.SourceText.Contains('@') && region.Role != VisualTextRole.Body)
            return TranslationTextKind.IdentityMetadata;
        if (LooksLikeGroupedTranslationUi(region.SourceText)) return TranslationTextKind.Metadata;
        if (LooksLikeIconPrefixedUiLabel(region.SourceText)) return TranslationTextKind.UiControl;
        if (scene == "social_media" && LooksLikeSocialIdentityWithControl(region.SourceText))
        {
            return TranslationTextKind.IdentityMetadata;
        }
        if (region.Role == VisualTextRole.Body && LooksLikeAllCapsHeading(region.SourceText))
        {
            return TranslationTextKind.Heading;
        }
        if (region.Role == VisualTextRole.Control
            && LooksLikeShortSocialBody(region.SourceText))
        {
            return TranslationTextKind.Body;
        }
        return region.Role switch
        {
            VisualTextRole.Body => TranslationTextKind.Body,
            VisualTextRole.Control => TranslationTextKind.UiControl,
            VisualTextRole.Heading => TranslationTextKind.Heading,
            VisualTextRole.Metadata => TranslationTextKind.Metadata,
            _ => TranslationTextKind.General,
        };
    }

    private static bool IsStandaloneBrandOrIdentifier(string text)
    {
        var value = text.Trim();
        if (value.IndexOfAny(['\r', '\n']) >= 0) return false;
        if (PreservedBrandLabels.Contains(value)) return true;
        if (KnownUiLabels.Contains(value)) return false;
        if (value.StartsWith('#') || value.StartsWith('＃')) value = value[1..];
        if (value.Length is < 2 or > 12 || !value.Any(char.IsLetter)) return false;
        return value.All(static character =>
            char.IsAsciiLetterUpper(character)
            || char.IsDigit(character)
            || character is '.' or '_' or '+' or '-');
    }

    private static bool IsLatinAccountDisplayName(
        VisualTextRegion region, IReadOnlyList<VisualTextRegion> regions)
    {
        if (region.Role is not (VisualTextRole.Control or VisualTextRole.Metadata)) return false;
        var name = region.SourceText.Trim();
        if (name.Length is < 2 or > 48 || !char.IsAsciiLetter(name[0])
            || name.IndexOfAny(['\r', '\n', '@', ':', '!', '?', '#']) >= 0
            || name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 3
            || KnownUiLabels.Contains(name))
            return false;

        var nameBounds = region.Bounds;
        return regions.Any(candidate =>
        {
            if (ReferenceEquals(candidate, region)
                || !StandaloneAccountHandle.IsMatch(candidate.SourceText.Trim())) return false;
            var handleBounds = candidate.Bounds;
            var maximumHeight = Math.Max(nameBounds.Height, handleBounds.Height);
            return handleBounds.Top >= nameBounds.Top + nameBounds.Height * .5
                && handleBounds.Top - nameBounds.Bottom <= maximumHeight * .75
                && Math.Abs(handleBounds.Left - nameBounds.Left) <= maximumHeight * .75;
        });
    }

    private static bool IsSocialTrendProperName(
        VisualTextRegion region, IReadOnlyList<VisualTextRegion> regions, string scene)
    {
        if (scene != "social_media" || region.Role != VisualTextRole.Control) return false;
        var value = region.SourceText.Trim();
        if (KnownUiLabels.Contains(value) || value.Length is < 2 or > 48
            || !Regex.IsMatch(value,
                @"^[\p{Lu}][\p{L}.'-]*(?:[ ,]+[\p{Lu}][\p{L}.'-]*){0,2}$",
                RegexOptions.CultureInvariant))
            return false;
        var bounds = region.Bounds;
        return regions.Any(candidate =>
        {
            if (ReferenceEquals(candidate, region) || candidate.Role != VisualTextRole.Metadata)
                return false;
            var label = candidate.SourceText.Trim();
            var labelBounds = candidate.Bounds;
            var height = Math.Max(bounds.Height, labelBounds.Height);
            if (label.Contains("Sports", StringComparison.OrdinalIgnoreCase)
                && label.Contains("Trending", StringComparison.OrdinalIgnoreCase))
                return Math.Abs(bounds.Left - labelBounds.Left) <= height * .5
                    && bounds.Top >= labelBounds.Top
                    && bounds.Top - labelBounds.Bottom <= height * .8;
            return label.Equals("Trending with", StringComparison.OrdinalIgnoreCase)
                && Math.Abs(bounds.Top - labelBounds.Top) <= height * .45
                && bounds.Left >= labelBounds.Right - height * .35
                && bounds.Left - labelBounds.Right <= height * .8;
        });
    }

    private static bool IsOpaqueHashtag(string text)
    {
        var value = text.Trim();
        if (value.StartsWith('＃')) value = "#" + value[1..];
        return value.Length is >= 4 and <= 64
            && Regex.IsMatch(value, @"^#[\p{L}\p{N}_]*\d[\p{L}\p{N}_]*$",
                RegexOptions.CultureInvariant);
    }

    private static bool LooksLikeOcrIconNoise(string text)
    {
        var value = text.Trim();
        if (value.Length is < 2 or > 4 || value.Any(char.IsWhiteSpace)) return false;
        if (value[0] is not ('Q' or 'O' or '0' or '口')) return false;
        return value[1..].All(static character => character is '.' or '…' or '·');
    }

    private static bool LooksLikeIconPrefixedUiLabel(string text)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0].Length > 2 || !KnownUiLabels.Contains(parts[1])) return false;
        return parts[0].All(static character => !char.IsLower(character));
    }

    private static bool LooksLikeIconPrefixedBrand(string text)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0].Length > 2 || !PreservedBrandLabels.Contains(parts[1])) return false;
        return parts[0].All(static character => !char.IsLower(character));
    }

    private static bool LooksLikeGroupedTranslationUi(string text) =>
        text.Contains("Translated from ", StringComparison.OrdinalIgnoreCase)
        && text.Contains("Show original", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSocialIdentityWithControl(string text)
    {
        var value = text.Trim();
        if (KnownUiLabels.Contains(value) || !value.EndsWith(" Follow", StringComparison.OrdinalIgnoreCase)) return false;
        var identity = value[..^" Follow".Length].TrimEnd('.', ' ', '·');
        return identity.Length > 0 && identity.Any(static character => char.IsLetter(character));
    }

    private static bool LooksLikeAllCapsHeading(string text)
    {
        var value = text.Trim();
        if (value.IndexOfAny(['\r', '\n']) >= 0) return false;
        var letters = value.Where(char.IsLetter).ToArray();
        return letters.Length >= 4 && letters.All(char.IsUpper);
    }

    private static bool HasCompactAge(string text) => Regex.IsMatch(
        text,
        @"(?<![\p{L}\p{N}_])\d+(?:mo|[smhdwy])\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool LooksLikeShortSocialBody(string text)
    {
        var value = text.Trim();
        if (value.Length == 0) return false;
        if (value[^1] is '.' or '?' or '!' or '。' or '？' or '！' or '…') return true;
        return value.Length >= 10 && value.Any(static character =>
            char.IsLetter(character) && character > '\u024F');
    }

    private static bool IsHashtag(string text)
    {
        var value = text.TrimStart();
        return value.StartsWith('#') || value.StartsWith('＃');
    }

    internal static bool IsSeparatedKatakanaProperName(string text)
    {
        var value = text.Trim();
        if (value.Length is < 3 or > 48 || value.IndexOfAny(['\r', '\n']) >= 0) return false;
        var segments = value.Split('・');
        if (segments.Length is < 2 or > 6 || segments.Any(static segment => segment.Length == 0)) return false;
        return segments.All(static segment => segment.All(IsKatakanaNameCharacter)
            && segment.Any(IsKatakanaLetter));
    }

    internal static bool IsKnownOfficialLatinRendering(string sourceText, string translated)
    {
        if (!KnownOfficialLatinNames.TryGetValue(sourceText.Trim(), out var names)) return false;
        var value = translated.Trim();
        return names.Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsKatakanaNameCharacter(char value) =>
        IsKatakanaLetter(value) || value is 'ー' or ' ' or '　';

    private static bool IsKatakanaLetter(char value) =>
        value is >= '\u30A1' and <= '\u30FA' or >= '\u30FD' and <= '\u30FF';

    private static string Hint(TranslationTextKind kind, string scene, string sourceText)
    {
        if (KnownOfficialLatinNames.TryGetValue(sourceText.Trim(), out var officialNames))
        {
            return $"Use the established official Latin name '{officialNames[0]}'. Do not return the source-script spelling unchanged. Return only the name.";
        }
        if (IsSeparatedKatakanaProperName(sourceText))
        {
            return "Translate or transliterate this probable proper name into the target language. Do not return a Latin-only rendering unless the input supplies an established Latin name. Do not return the source-script spelling unchanged. Return only the name.";
        }

        return kind switch
        {
        TranslationTextKind.IdentityMetadata =>
            "Preserve a Latin display name exactly. Romanize a non-Latin display name and retain its original in parentheses. Preserve the exact @handle. Localize relative time such as 6h.",
        TranslationTextKind.Hashtag =>
            "Translate the human-readable hashtag text and preserve the leading hashtag marker.",
        TranslationTextKind.BrandOrIdentifier =>
            "Preserve this standalone brand, product, acronym, or identifier exactly.",
        TranslationTextKind.UiControl =>
            "Translate as a concise UI label. Do not preserve ordinary interface words merely because they are short.",
        TranslationTextKind.Metadata =>
            "Localize human-readable metadata and relative time while preserving identifiers and numbers.",
        TranslationTextKind.Heading =>
            "Translate as a concise heading.",
        TranslationTextKind.Body when scene == "social_media" =>
            "Translate wrapped lines as one complete, natural social-media passage. Translate every human-readable phrase, including slang around handles and arrows. Preserve exact handles and Latin account names. In this scene, icon usually means profile avatar; translate a depicted person as a character unless context proves otherwise.",
        TranslationTextKind.Body =>
            "Translate the complete body text naturally and faithfully.",
        _ => "Translate every human-readable segment faithfully and concisely.",
        };
    }
}
