#nullable enable

namespace Chat.Web.Utilities;

public static class PreferredLanguageMerger
{
    public static string? Merge(string? incomingPreferredLanguage, string? existingPreferredLanguage)
    {
        return !string.IsNullOrWhiteSpace(incomingPreferredLanguage)
            ? incomingPreferredLanguage
            : existingPreferredLanguage;
    }
}
