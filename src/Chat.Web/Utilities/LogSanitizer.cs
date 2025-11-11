using System;
using System.Text.RegularExpressions;

namespace Chat.Web.Utilities
{
    /// <summary>
    /// Utility class to sanitize user input before logging to prevent log forgery attacks (CWE-117).
    /// Removes or replaces control characters (newlines, carriage returns, tabs) that could be used
    /// to inject fake log entries or break log parsing.
    /// </summary>
    public static class LogSanitizer
    {
        // Regex to match control characters that could be used for log forgery
        // Matches: \r, \n, \t, and other control characters (0x00-0x1F, 0x7F)
        // Timeout prevents ReDoS attacks
        private static readonly Regex ControlCharsRegex = new Regex(@"[\r\n\t\x00-\x1F\x7F]", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

        /// <summary>
        /// Sanitizes input by removing control characters that could forge log entries.
        /// If input is null, returns string "<null>"; if input is empty, returns "<empty>".
        /// This ensures logs never contain ambiguous/empty user values.
        /// <param name="input">The input string to sanitize</param>
        /// <returns>Sanitized string with control characters removed, or a marker if null/empty</returns>
        public static string Sanitize(string input)
        {
            if (input == null)
            {
                return "<null>";
            }
            if (input.Length == 0)
            {
                return "<empty>";
            }
            // Replace control characters with empty string
            return ControlCharsRegex.Replace(input, "");
        }

        /// <summary>
        /// Sanitizes input by replacing control characters with a visible placeholder.
        /// Useful when you want to preserve information about the attempt.
        /// </summary>
        /// <param name="input">The input string to sanitize</param>
        /// <param name="replacement">The replacement string (default: "�")</param>
        /// <returns>Sanitized string with control characters replaced</returns>
        public static string SanitizeWithReplacement(string input, string replacement = "�")
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            return ControlCharsRegex.Replace(input, replacement);
        }
    }
}
