namespace LidGuard.Notifications.Models;

internal static class WebhookTextPreview
{
    private const int MaximumCharacterCount = 50;
    private const string TrimmingSuffix = "...";

    public static string Create(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalizedText = NormalizeLineBreaks(text).Trim();
        if (normalizedText.Length <= MaximumCharacterCount) return normalizedText;

        var maximumPrefixCharacterCount = MaximumCharacterCount - TrimmingSuffix.Length;
        var prefix = normalizedText[..maximumPrefixCharacterCount].TrimEnd();
        if (CutsThroughWord(normalizedText, maximumPrefixCharacterCount))
        {
            var wordBoundaryIndex = FindLastWordBoundary(prefix);
            if (wordBoundaryIndex > 0) prefix = prefix[..wordBoundaryIndex].TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(prefix)) prefix = normalizedText[..maximumPrefixCharacterCount].TrimEnd();
        return $"{prefix}{TrimmingSuffix}";
    }

    private static string NormalizeLineBreaks(string text)
        => text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\n', ' ');

    private static bool CutsThroughWord(string text, int maximumPrefixCharacterCount)
        => maximumPrefixCharacterCount < text.Length
            && maximumPrefixCharacterCount > 0
            && !char.IsWhiteSpace(text[maximumPrefixCharacterCount])
            && !char.IsWhiteSpace(text[maximumPrefixCharacterCount - 1]);

    private static int FindLastWordBoundary(string text)
    {
        for (var characterIndex = text.Length - 1; characterIndex >= 0; characterIndex--) if (char.IsWhiteSpace(text[characterIndex])) return characterIndex;

        return -1;
    }
}
