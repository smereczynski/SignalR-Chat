#nullable enable
using System;
using System.Collections.Generic;

namespace Chat.Web.Utilities;

public static class LanguageCode
{
    public static string? NormalizeToLanguageCode(string? value, bool allowAuto = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        if (allowAuto && trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return "auto";
        }

        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Accept either ISO language codes ("pl") or cultures ("pl-PL", "pl_PL").
        trimmed = trimmed.Replace('_', '-');

        var dashIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
        var lang = dashIndex > 0 ? trimmed[..dashIndex] : trimmed;

        lang = lang.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(lang) ? null : lang;
    }

    public static List<string> BuildTargetLanguages(IEnumerable<string>? roomLanguages, string? sourceLanguage)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "en"
        };

        if (roomLanguages != null)
        {
            foreach (var lang in roomLanguages)
            {
                var normalized = NormalizeToLanguageCode(lang);
                if (normalized != null)
                {
                    targets.Add(normalized);
                }
            }
        }

        // Keep EN always present, but skip "auto" and remove duplicates.
        targets.Remove("auto");

        // Avoid targeting source language to reduce no-op translations.
        var normalizedSource = NormalizeToLanguageCode(sourceLanguage, allowAuto: true);
        if (!string.IsNullOrWhiteSpace(normalizedSource) && !normalizedSource.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            targets.Remove(normalizedSource);
            targets.Add("en");
        }

        return new List<string>(targets);
    }
}
