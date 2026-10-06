using System.Text.RegularExpressions;

namespace ClipRay.App.Security;

public static partial class CredentialSecretPolicy
{
    private const int VisiblePrefixLength = 8;
    private const int VisibleSuffixLength = 4;

    public static void ValidateCandidate(string provider, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (provider == "DeepSeek" && !DeepSeekKeyPattern().IsMatch(value))
        {
            throw new ArgumentException("DeepSeek API Key 格式无效；应以 sk- 开头且不能包含空白字符。", nameof(value));
        }
    }

    public static string Mask(string provider, string value)
    {
        try
        {
            ValidateCandidate(provider, value);
        }
        catch (ArgumentException)
        {
            return "无效凭据";
        }

        if (value.Length <= VisiblePrefixLength + VisibleSuffixLength)
        {
            return new string('*', value.Length);
        }

        var hiddenLength = value.Length - VisiblePrefixLength - VisibleSuffixLength;
        return string.Concat(
            value.AsSpan(0, VisiblePrefixLength),
            new string('*', hiddenLength),
            value.AsSpan(value.Length - VisibleSuffixLength, VisibleSuffixLength));
    }

    [GeneratedRegex(@"^sk-[A-Za-z0-9_-]{20,}$", RegexOptions.CultureInvariant)]
    private static partial Regex DeepSeekKeyPattern();
}
