using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Arp
{
    /// <summary>
    /// Human-readable duration and percentage fields, ported value-for-value
    /// from the Python build's TimeFormatSpinBox and PercentageSpinBox. Spelling
    /// the units out is deliberate: a screen reader announces "1 hour, 30
    /// minutes" rather than "5400", and the parser accepts whatever spelling the
    /// user types back.
    /// </summary>
    internal static class TimeText
    {
        public static string Format(int totalSeconds)
        {
            if (totalSeconds == 0) return "0 seconds (off)";

            int hours = totalSeconds / 3600;
            int remainder = totalSeconds % 3600;
            int minutes = remainder / 60;
            int seconds = remainder % 60;

            var parts = new List<string>(3);
            if (hours > 0) parts.Add(hours + (hours == 1 ? " hour" : " hours"));
            if (minutes > 0) parts.Add(minutes + (minutes == 1 ? " minute" : " minutes"));
            if (seconds > 0) parts.Add(seconds + (seconds == 1 ? " second" : " seconds"));

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Accepts "90", "90s", "1h30m", "1 hour, 30 minutes". A bare number
        /// after a unit inherits the next unit down, so "1h 30" reads as one
        /// hour thirty minutes.
        /// </summary>
        public static int Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.ToLowerInvariant();

            int total = 0;
            char prevUnit = '\0';

            int i = 0;
            while (i < text.Length)
            {
                if (!char.IsAsciiDigit(text[i])) { i++; continue; }

                int start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                if (!long.TryParse(text.AsSpan(start, i - start), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out long parsed))
                    continue;

                // Mirrors the Python regex's "\s*([a-z]*)": optional spaces then
                // the run of letters immediately following the digits.
                while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
                int uStart = i;
                while (i < text.Length && char.IsAsciiLetterLower(text[i])) i++;
                string unit = text.Substring(uStart, i - uStart);

                int num = parsed > int.MaxValue ? int.MaxValue : (int)parsed;

                if (unit.StartsWith("h", StringComparison.Ordinal))
                {
                    total += num * 3600;
                    prevUnit = 'h';
                }
                else if (unit.StartsWith("m", StringComparison.Ordinal))
                {
                    total += num * 60;
                    prevUnit = 'm';
                }
                else if (unit.StartsWith("s", StringComparison.Ordinal))
                {
                    total += num;
                    prevUnit = 's';
                }
                else if (prevUnit == 'h')
                {
                    total += num * 60;
                    prevUnit = 'm';
                }
                else if (prevUnit == 'm')
                {
                    total += num;
                    prevUnit = 's';
                }
                else
                {
                    total += num;
                    prevUnit = 's';
                }

                if (total < 0) return int.MaxValue;
            }

            return total;
        }

        /// <summary>Normalises free text to the canonical spoken form.</summary>
        public static string Normalize(string text, int min, int max) =>
            Format(Math.Clamp(Parse(text), min, max));
    }

    internal static class PercentText
    {
        public static string Format(int value) => value + " percent";

        public static int Parse(string text, int fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            int i = 0;
            while (i < text.Length && !char.IsAsciiDigit(text[i])) i++;
            if (i >= text.Length) return fallback;
            int start = i;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            return int.TryParse(text.AsSpan(start, i - start), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int v) ? v : fallback;
        }
    }

    internal static class Naming
    {
        private static readonly char[] Illegal = { '\\', '/', '*', '?', ':', '"', '<', '>', '|' };

        /// <summary>Equivalent of the Python build's re.sub on the file prefix.</summary>
        public static string SanitizePrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return string.Empty;
            var sb = new StringBuilder(prefix.Length);
            foreach (char c in prefix.Trim())
                if (Array.IndexOf(Illegal, c) < 0) sb.Append(c);
            return sb.ToString();
        }

        public static string Timestamp(DateTime when) =>
            when.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    }
}
