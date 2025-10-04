using System;
#nullable enable

namespace Chat.Web.Configuration
{
    /// <summary>
    /// Centralized helpers for validating required configuration values.
    /// Treats common placeholder patterns as unset so the app fails fast with a clear message
    /// instead of later throwing obscure connection exceptions.
    /// </summary>
    public static class ConfigurationGuards
    {
        public static bool IsPlaceholder(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            var v = value.Trim();
            // Generic placeholder prefixes
            if (v.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) return true;
            // Common template markers
            if (v.Contains("<ACCOUNT>", StringComparison.OrdinalIgnoreCase) || v.Contains("<KEY>", StringComparison.OrdinalIgnoreCase)) return true;
            // Azure style intentionally left blank values
            if (v.Equals("CHANGEME", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string Require(string? value, string configKey)
        {
            if (IsPlaceholder(value))
                throw new InvalidOperationException($"Configuration value '{configKey}' is required and is missing or a placeholder.");
            return value!;
        }
    }
}
