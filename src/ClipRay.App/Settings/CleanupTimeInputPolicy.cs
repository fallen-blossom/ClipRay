using System.Globalization;

namespace ClipRay.App.Settings;

public static class CleanupTimeInputPolicy
{
    public static string Normalize(string text)
    {
        var normalized = (text ?? string.Empty).Trim().Replace('：', ':');
        var parts = normalized.Split(':');
        if (parts.Length is < 1 or > 2
            || !TryParseComponent(parts[0], out var hour)
            || hour is < 0 or > 23)
        {
            throw InvalidTime();
        }

        var minute = 0;
        if (parts.Length == 2
            && (!TryParseOptionalMinute(parts[1], out minute) || minute is < 0 or > 59))
        {
            throw InvalidTime();
        }

        return string.Create(CultureInfo.InvariantCulture, $"{hour:00}:{minute:00}");
    }

    public static bool IsCandidateText(string text)
    {
        if (text is null) return false;
        var colonCount = 0;
        foreach (var character in text)
        {
            if (character is ':' or '：')
            {
                colonCount++;
                if (colonCount > 1) return false;
                continue;
            }

            if (!char.IsAsciiDigit(character)) return false;
        }

        var separator = text.IndexOfAny([':', '：']);
        var hourLength = separator < 0 ? text.Length : separator;
        var minuteLength = separator < 0 ? 0 : text.Length - separator - 1;
        return hourLength <= 2 && minuteLength <= 2;
    }

    private static bool TryParseComponent(string text, out int value)
    {
        value = 0;
        return text.Length is 1 or 2
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseOptionalMinute(string text, out int value)
    {
        if (text.Length == 0)
        {
            value = 0;
            return true;
        }

        return TryParseComponent(text, out value);
    }

    private static InvalidOperationException InvalidTime() =>
        new("执行时间必须是有效的 24 小时时间，例如 3、3:00 或 03:00。");
}
