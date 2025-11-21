using System;
using System.Linq;
using System.Text;
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
        /// </summary>
        /// <param name="input">The input string to sanitize</param>
        /// <param name="max">Maximum length to preserve (default: 200)</param>
        /// <returns>Sanitized string with control characters removed, or a marker if null/empty</returns>
        public static string Sanitize(string input, int max = 200)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if (ch == '\r' || ch == '\n') continue; // drop new lines entirely
                if (char.IsControl(ch)) continue; // remove other control chars (tabs, etc.)
                sb.Append(ch);
                if (sb.Length >= max)
                {
                    sb.Append("…");
                    break;
                }
            }
            return sb.ToString();
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
            if (input == null)
            {
                return "<null>";
            }
            if (input.Length == 0)
            {
                return "<empty>";
            }
            if (string.IsNullOrWhiteSpace(input))
            {
                return "<whitespace>";
            }
            // Replace control characters, then wrap user input in visible delimiters to prevent confusion
            var sanitized = ControlCharsRegex.Replace(input, replacement);
            return $"<<<{sanitized}>>>";
        }

        /// <summary>
        /// Mask an email address, preserving only the domain suffix for minimal utility.
        /// Example: john.doe@example.com -> ***@***.com
        /// </summary>
        public static string MaskEmail(string email)
        {
            var s = Sanitize(email, max: 256);
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var at = s.IndexOf('@');
            if (at < 0) return MaskGeneric(s);
            var lastDot = s.LastIndexOf('.');
            var suffix = lastDot > at && lastDot < s.Length - 1 ? s.Substring(lastDot) : string.Empty;
            return $"***@***{suffix}";
        }

        /// <summary>
        /// Mask a phone number keeping leading '+' (if present) and last 2 digits.
        /// Non-digit characters (spaces/dashes) are removed in the mask.
        /// Example: +48604970937 -> +*********37
        /// </summary>
        public static string MaskPhone(string phone)
        {
            var s = Sanitize(phone, max: 64);
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var hasPlus = s.StartsWith('+');
            var digits = new string(s.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return hasPlus ? "+**" : "**";
            var keep = Math.Min(2, digits.Length);
            var stars = new string('*', Math.Max(0, digits.Length - keep));
            var tail = digits.Substring(digits.Length - keep, keep);
            return (hasPlus ? "+" : string.Empty) + stars + tail;
        }

        /// <summary>
        /// Heuristic mask for destinations (email or phone or other handle)
        /// </summary>
        public static string MaskDestination(string dest)
        {
            if (string.IsNullOrWhiteSpace(dest)) return string.Empty;
            var s = Sanitize(dest, max: 256);
            if (s.Contains('@')) return MaskEmail(s);
            // Assume phone-like if it contains 5+ digits
            var digitCount = s.Count(char.IsDigit);
            if (digitCount >= 5) return MaskPhone(s);
            return MaskGeneric(s);
        }

        private static string MaskGeneric(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var keep = Math.Min(2, s.Length);
            var tail = s.Substring(s.Length - keep, keep);
            return new string('*', Math.Max(0, s.Length - keep)) + tail;
        }
    }
}
