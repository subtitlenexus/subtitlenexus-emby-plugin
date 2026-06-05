using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SubtitleNexus.Services
{
    
    
    
    
    
    
    
    
    internal static class JsonWriter
    {
        public static string Object(IDictionary<string, object> values)
        {
            var sb = new StringBuilder();
            WriteValue(sb, values);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    return;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    return;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case IDictionary<string, object> dict:
                    WriteDict(sb, dict);
                    return;
                case System.Collections.IEnumerable enumerable:
                    WriteList(sb, enumerable);
                    return;
                default:
                    WriteString(sb, v.ToString());
                    return;
            }
        }

        private static void WriteDict(StringBuilder sb, IDictionary<string, object> dict)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, kv.Key);
                sb.Append(':');
                WriteValue(sb, kv.Value);
            }
            sb.Append('}');
        }

        private static void WriteList(StringBuilder sb, System.Collections.IEnumerable seq)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in seq)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    
    
    
    
    
    internal static class JsonReader
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWs();
            var v = p.ParseValue();
            p.SkipWs();
            if (!p.AtEnd)
                throw new FormatException($"Trailing data at position {p.Pos}");
            return v;
        }

        private class Parser
        {
            private readonly string _s;
            public int Pos;
            public Parser(string s) { _s = s; Pos = 0; }

            public bool AtEnd => Pos >= _s.Length;

            public void SkipWs()
            {
                while (Pos < _s.Length && char.IsWhiteSpace(_s[Pos])) Pos++;
            }

            public object ParseValue()
            {
                SkipWs();
                if (AtEnd) throw new FormatException("Unexpected end of input");
                char c = _s[Pos];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't' || c == 'f') return ParseBool();
                if (c == 'n') { ParseLiteral("null"); return null; }
                return ParseNumber();
            }

            private void ParseLiteral(string lit)
            {
                if (Pos + lit.Length > _s.Length || _s.Substring(Pos, lit.Length) != lit)
                    throw new FormatException($"Expected {lit} at {Pos}");
                Pos += lit.Length;
            }

            private object ParseBool()
            {
                if (_s[Pos] == 't') { ParseLiteral("true"); return true; }
                ParseLiteral("false");
                return false;
            }

            private IDictionary<string, object> ParseObject()
            {
                var d = new Dictionary<string, object>();
                Pos++; 
                SkipWs();
                if (!AtEnd && _s[Pos] == '}') { Pos++; return d; }
                while (true)
                {
                    SkipWs();
                    var key = ParseString();
                    SkipWs();
                    if (AtEnd || _s[Pos] != ':') throw new FormatException($"Expected ':' at {Pos}");
                    Pos++;
                    var val = ParseValue();
                    d[key] = val;
                    SkipWs();
                    if (AtEnd) throw new FormatException("Unterminated object");
                    if (_s[Pos] == ',') { Pos++; continue; }
                    if (_s[Pos] == '}') { Pos++; return d; }
                    throw new FormatException($"Expected ',' or '}}' at {Pos}");
                }
            }

            private IList<object> ParseArray()
            {
                var list = new List<object>();
                Pos++; 
                SkipWs();
                if (!AtEnd && _s[Pos] == ']') { Pos++; return list; }
                while (true)
                {
                    var val = ParseValue();
                    list.Add(val);
                    SkipWs();
                    if (AtEnd) throw new FormatException("Unterminated array");
                    if (_s[Pos] == ',') { Pos++; continue; }
                    if (_s[Pos] == ']') { Pos++; return list; }
                    throw new FormatException($"Expected ',' or ']' at {Pos}");
                }
            }

            private string ParseString()
            {
                if (_s[Pos] != '"') throw new FormatException($"Expected '\"' at {Pos}");
                Pos++;
                var sb = new StringBuilder();
                while (Pos < _s.Length)
                {
                    char c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (Pos >= _s.Length) break;
                        char esc = _s[Pos++];
                        switch (esc)
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
                                if (Pos + 4 > _s.Length) throw new FormatException("Truncated \\u");
                                var hex = _s.Substring(Pos, 4);
                                Pos += 4;
                                sb.Append((char)int.Parse(hex, NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture));
                                break;
                            default:
                                throw new FormatException($"Bad escape \\{esc}");
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                throw new FormatException("Unterminated string");
            }

            private object ParseNumber()
            {
                int start = Pos;
                if (_s[Pos] == '-') Pos++;
                while (Pos < _s.Length && (char.IsDigit(_s[Pos]) || _s[Pos] == '.'
                       || _s[Pos] == 'e' || _s[Pos] == 'E' || _s[Pos] == '+' || _s[Pos] == '-'))
                {
                    Pos++;
                }
                var tok = _s.Substring(start, Pos - start);
                if (tok.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0)
                {
                    return double.Parse(tok, CultureInfo.InvariantCulture);
                }
                return long.Parse(tok, CultureInfo.InvariantCulture);
            }
        }
    }
}
