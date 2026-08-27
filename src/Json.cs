using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Arp
{
    // A deliberately tiny JSON reader/writer for flat configuration objects.
    // System.Text.Json would work under NativeAOT via source generation, but it
    // costs roughly a megabyte of binary and cannot round-trip keys it does not
    // know about. The recorder config is a flat map of string/number/bool, and
    // unknown keys must survive so this build and the Python build can share one
    // file, so a purpose-built reader is both smaller and more correct here.
    internal sealed class JsonObject
    {
        private readonly List<string> _order = new();
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Keys => _order;

        public bool Has(string key) => _values.ContainsKey(key);

        public object GetRaw(string key) => _values.TryGetValue(key, out var v) ? v : null;

        public void Set(string key, object value)
        {
            if (!_values.ContainsKey(key)) _order.Add(key);
            _values[key] = value;
        }

        public void Remove(string key)
        {
            if (_values.Remove(key)) _order.Remove(key);
        }

        public string GetString(string key, string fallback)
        {
            var v = GetRaw(key);
            if (v is string s) return s;
            if (v is double d) return d.ToString(CultureInfo.InvariantCulture);
            if (v is bool b) return b ? "true" : "false";
            return fallback;
        }

        public int GetInt(string key, int fallback)
        {
            var v = GetRaw(key);
            if (v is double d) return (int)Math.Round(d);
            // The Python app stores sample_rate, bit_depth and channels as
            // strings, so an int read has to tolerate a quoted number.
            if (v is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
            if (v is bool b) return b ? 1 : 0;
            return fallback;
        }

        public long GetLong(string key, long fallback)
        {
            var v = GetRaw(key);
            if (v is double d) return (long)Math.Round(d);
            if (v is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i)) return i;
            return fallback;
        }

        public bool GetBool(string key, bool fallback)
        {
            var v = GetRaw(key);
            if (v is bool b) return b;
            if (v is double d) return d != 0;
            if (v is string s)
            {
                if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return fallback;
        }

        public string ToJson(int indent = 4)
        {
            var sb = new StringBuilder();
            string pad = new string(' ', indent);
            sb.Append("{\n");
            for (int i = 0; i < _order.Count; i++)
            {
                string key = _order[i];
                sb.Append(pad);
                WriteString(sb, key);
                sb.Append(": ");
                WriteValue(sb, _values[key]);
                if (i < _order.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteString(sb, s); break;
                case double d:
                    if (d == Math.Floor(d) && Math.Abs(d) < 9.007199254740992E15)
                        sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                    else
                        sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                default: WriteString(sb, v.ToString()); break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static JsonObject Parse(string text)
        {
            int pos = 0;
            var result = ParseObject(text, ref pos);
            return result;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static JsonObject ParseObject(string s, ref int i)
        {
            var obj = new JsonObject();
            SkipWhitespace(s, ref i);
            if (i >= s.Length || s[i] != '{') throw new FormatException("Expected '{' at " + i);
            i++;
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':' at " + i);
                i++;
                object value = ParseValue(s, ref i);
                obj.Set(key, value);
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                throw new FormatException("Expected ',' or '}' at " + i);
            }
            return obj;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '"') return ParseString(s, ref i);
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == 't' && s.AsSpan(i).StartsWith("true")) { i += 4; return true; }
            if (c == 'f' && s.AsSpan(i).StartsWith("false")) { i += 5; return false; }
            if (c == 'n' && s.AsSpan(i).StartsWith("null")) { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // consume '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                throw new FormatException("Expected ',' or ']' at " + i);
            }
            return list;
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+')) i++;
            if (double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            throw new FormatException("Bad number at " + start);
        }

        private static string ParseString(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length || s[i] != '"') throw new FormatException("Expected string at " + i);
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= s.Length &&
                            int.TryParse(s.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp))
                        {
                            sb.Append((char)cp);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("Unterminated string");
        }
    }
}
