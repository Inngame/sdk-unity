using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace InngameSDK
{
    /// <summary>
    /// Small, dependency-free JSON reader/writer.
    /// JsonUtility cannot read dictionaries (message <c>translations</c>, free-form <c>card</c>, <c>config.appearance</c>)
    /// nor write the free-form <c>context</c> object, and depending on Newtonsoft would force a package on every game,
    /// so the SDK parses into <c>Dictionary&lt;string, object&gt;</c> / <c>List&lt;object&gt;</c> / string / double / long / bool / null.
    /// </summary>
    public static class InngameJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var p = new Parser(json);
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw new FormatException("Trailing characters in JSON at " + p.Pos);
            return v;
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            try { return Parse(json) as Dictionary<string, object>; }
            catch (Exception) { return null; }
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder(256);
            Write(sb, value, 0);
            return sb.ToString();
        }

        // ---------- typed getters ----------

        public static string GetString(this IDictionary<string, object> d, string key, string fallback = null)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is string s) return s;
            if (v is double db) return db.ToString(CultureInfo.InvariantCulture);
            if (v is long l) return l.ToString(CultureInfo.InvariantCulture);
            if (v is bool b) return b ? "true" : "false";
            return fallback;
        }

        public static long GetLong(this IDictionary<string, object> d, string key, long fallback = 0)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            switch (v)
            {
                case long l: return l;
                case double db: return (long)db;
                case int i: return i;
                case string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r): return r;
                default: return fallback;
            }
        }

        public static long? GetNullableLong(this IDictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return null;
            return d.GetLong(key);
        }

        public static double GetDouble(this IDictionary<string, object> d, string key, double fallback = 0)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            switch (v)
            {
                case double db: return db;
                case long l: return l;
                case int i: return i;
                case float f: return f;
                default: return fallback;
            }
        }

        public static bool GetBool(this IDictionary<string, object> d, string key, bool fallback = false)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            return v is bool b ? b : fallback;
        }

        public static Dictionary<string, object> GetDict(this IDictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v)) return null;
            return v as Dictionary<string, object>;
        }

        public static List<object> GetList(this IDictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v)) return null;
            return v as List<object>;
        }

        // ---------- writer ----------

        static void Write(StringBuilder sb, object v, int depth)
        {
            if (depth > 32) { sb.Append("null"); return; }
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case char c: WriteString(sb, c.ToString()); return;
                case double d: WriteNumber(sb, d); return;
                case float f: WriteNumber(sb, f); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
                case int or long or short or byte or sbyte or uint or ulong or ushort:
                    sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return;
                case Enum e: WriteString(sb, e.ToString()); return;
                case DateTime dt: WriteString(sb, dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)); return;
                case IDictionary dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                        sb.Append(':');
                        Write(sb, kv.Value, depth + 1);
                    }
                    sb.Append('}');
                    return;
                }
                case IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, item, depth + 1);
                    }
                    sb.Append(']');
                    return;
                }
                default:
                    WriteString(sb, v.ToString()); return;
            }
        }

        static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            if (Math.Abs(d % 1) < double.Epsilon && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------- reader ----------

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string json) { s = json; }
            public bool End => i >= s.Length;
            public int Pos => i;

            public void SkipWs() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            public object ReadValue()
            {
                SkipWs();
                if (End) throw new FormatException("Unexpected end of JSON");
                char c = s[i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw new FormatException("Unexpected character '" + c + "' at " + i);
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("Expected " + word + " at " + i);
                i += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                i++; SkipWs();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (End || s[i] != '"') throw new FormatException("Expected key at " + i);
                    var key = ReadString();
                    SkipWs();
                    if (End || s[i] != ':') throw new FormatException("Expected ':' at " + i);
                    i++;
                    d[key] = ReadValue();
                    SkipWs();
                    if (End) throw new FormatException("Unterminated object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("Expected ',' or '}' at " + i);
                }
            }

            List<object> ReadArray()
            {
                var l = new List<object>();
                i++; SkipWs();
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ReadValue());
                    SkipWs();
                    if (End) throw new FormatException("Unterminated array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("Expected ',' or ']' at " + i);
                }
            }

            string ReadString()
            {
                i++; // opening quote
                StringBuilder sb = null;
                int start = i;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == '"')
                    {
                        string result = sb == null ? s.Substring(start, i - start) : sb.Append(s, start, i - start).ToString();
                        i++;
                        return result;
                    }
                    if (c == '\\')
                    {
                        sb ??= new StringBuilder();
                        sb.Append(s, start, i - start);
                        i++;
                        if (End) break;
                        char e = s[i];
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
                                if (i + 4 >= s.Length) throw new FormatException("Bad unicode escape");
                                sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                i += 4;
                                break;
                            default: throw new FormatException("Bad escape at " + i);
                        }
                        i++;
                        start = i;
                        continue;
                    }
                    i++;
                }
                throw new FormatException("Unterminated string");
            }

            object ReadNumber()
            {
                int start = i;
                bool isFloat = false;
                if (s[i] == '-') i++;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c >= '0' && c <= '9') { i++; continue; }
                    if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { isFloat = true; i++; continue; }
                    break;
                }
                var str = s.Substring(start, i - start);
                if (!isFloat && long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
                return double.Parse(str, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
