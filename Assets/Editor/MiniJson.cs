using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// ─────────────────────────────────────────────────────────────
//  MiniJson — 의존성 없는 소형 JSON 파서 (data-tools 임포터 전용, Editor)
//  우리 변환기(xlsx_to_json.py)가 만든 JSON만 파싱하면 되므로 표준 서브셋만 지원.
//  반환: object = Dictionary<string,object> | List<object> | string | double | bool | null
//  Newtonsoft 등 외부 패키지가 없어도 동작하도록 자체 구현.
// ─────────────────────────────────────────────────────────────
public static class MiniJson
{
    public static object Parse(string json)
    {
        int i = 0;
        var v = ParseValue(json, ref i);
        return v;
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
            else break;
        }
    }

    private static object ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length) return null;
        char c = s[i];
        switch (c)
        {
            case '{': return ParseObject(s, ref i);
            case '[': return ParseArray(s, ref i);
            case '"': return ParseString(s, ref i);
            case 't': i += 4; return true;        // true
            case 'f': i += 5; return false;       // false
            case 'n': i += 4; return null;        // null
            default: return ParseNumber(s, ref i);
        }
    }

    private static Dictionary<string, object> ParseObject(string s, ref int i)
    {
        var dict = new Dictionary<string, object>();
        i++; // {
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return dict; }
        while (i < s.Length)
        {
            SkipWs(s, ref i);
            string key = ParseString(s, ref i);
            SkipWs(s, ref i);
            i++; // :
            object val = ParseValue(s, ref i);
            dict[key] = val;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; break; }
            break;
        }
        return dict;
    }

    private static List<object> ParseArray(string s, ref int i)
    {
        var list = new List<object>();
        i++; // [
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return list; }
        while (i < s.Length)
        {
            object val = ParseValue(s, ref i);
            list.Add(val);
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; break; }
            break;
        }
        return list;
    }

    private static string ParseString(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++; // opening quote
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') break;
            if (c == '\\' && i < s.Length)
            {
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
                        if (i + 4 <= s.Length)
                        {
                            string hex = s.Substring(i, 4);
                            i += 4;
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                sb.Append((char)code);
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static object ParseNumber(string s, ref int i)
    {
        int start = i;
        while (i < s.Length)
        {
            char c = s[i];
            if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
            else break;
        }
        string num = s.Substring(start, i - start);
        if (double.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;
        return null;
    }

    // ── 편의 캐스팅 헬퍼 ──
    public static Dictionary<string, object> AsObj(object o) => o as Dictionary<string, object>;
    public static List<object> AsArr(object o) => o as List<object>;

    public static string AsStr(object o)
    {
        if (o == null) return null;
        if (o is string s) return s;
        if (o is double d) return d.ToString(CultureInfo.InvariantCulture);
        if (o is bool b) return b ? "true" : "false";
        return o.ToString();
    }
}
