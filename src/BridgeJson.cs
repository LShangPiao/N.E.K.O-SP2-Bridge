// N.E.K.O-SP2-Bridge — 极简 JSON 拼装
//
// 为什么不用 Unity 的 JsonUtility 或 Newtonsoft：
//   1) 我们要拼的是**任意结构**的字典，JsonUtility 对嵌套字典支持很差；
//   2) 游戏自带的 Newtonsoft 版本不可控，强引用容易在更新后炸掉；
//   3) 这里只需要「能序列化 + 数值格式稳定」，自己写反而最省事。
//
// 约定（与 docs/protocol.md 一致）：
//   · 取不到的一律写成 null，绝不写 0 / -1 / ""；
//   · NaN / Infinity 写成 null（JSON 里它们不是合法字面量）。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NekoSp2Bridge
{
    /// <summary>有序的 JSON 对象。键的插入顺序即输出顺序。</summary>
    public sealed class JsonObj
    {
        private readonly List<string> keys = new List<string>();
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();

        public JsonObj Set(string key, object value)
        {
            if (!values.ContainsKey(key))
            {
                keys.Add(key);
            }
            values[key] = value;
            return this;
        }

        /// <summary>只在值不是 null 时写入 —— 用来「取不到就省略」。</summary>
        public JsonObj SetIfNotNull(string key, object value)
        {
            if (value == null)
            {
                return this;
            }
            if (value is float)
            {
                float f = (float)value;
                if (float.IsNaN(f) || float.IsInfinity(f))
                {
                    return this;
                }
            }
            if (value is double)
            {
                double d = (double)value;
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    return this;
                }
            }
            return Set(key, value);
        }

        public int Count
        {
            get { return keys.Count; }
        }

        internal IEnumerable<KeyValuePair<string, object>> Entries()
        {
            foreach (string key in keys)
            {
                yield return new KeyValuePair<string, object>(key, values[key]);
            }
        }
    }

    public static class BridgeJson
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Serialize(object value)
        {
            StringBuilder sb = new StringBuilder(1024);
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int depth)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            if (value is JsonObj)
            {
                WriteObject(sb, (JsonObj)value, depth);
                return;
            }
            if (value is string)
            {
                WriteString(sb, (string)value);
                return;
            }
            if (value is bool)
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }
            if (value is float)
            {
                WriteNumber(sb, (float)value);
                return;
            }
            if (value is double)
            {
                WriteNumber(sb, (double)value);
                return;
            }
            if (value is int || value is long || value is short || value is byte)
            {
                sb.Append(Convert.ToInt64(value, Inv).ToString(Inv));
                return;
            }
            if (value is IDictionary)
            {
                WriteDictionary(sb, (IDictionary)value, depth);
                return;
            }
            if (value is IEnumerable)
            {
                WriteArray(sb, (IEnumerable)value, depth);
                return;
            }
            // 兜底：转成字符串，至少不会输出非法 JSON。
            WriteString(sb, Convert.ToString(value, Inv));
        }

        private static void WriteObject(StringBuilder sb, JsonObj obj, int depth)
        {
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> entry in obj.Entries())
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                WriteString(sb, entry.Key);
                sb.Append(':');
                WriteValue(sb, entry.Value, depth + 1);
            }
            sb.Append('}');
        }

        private static void WriteDictionary(StringBuilder sb, IDictionary map, int depth)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in map)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                WriteString(sb, Convert.ToString(entry.Key, Inv));
                sb.Append(':');
                WriteValue(sb, entry.Value, depth + 1);
            }
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable items, int depth)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in items)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                WriteValue(sb, item, depth + 1);
            }
            sb.Append(']');
        }

        private static void WriteNumber(StringBuilder sb, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                sb.Append("null");
                return;
            }
            // 保留 3 位小数：遥测不需要更高精度，位数多了反而难读。
            sb.Append(Round(value).ToString("0.###", Inv));
        }

        private static void WriteNumber(StringBuilder sb, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                sb.Append("null");
                return;
            }
            sb.Append(Round((float)value).ToString("0.###", Inv));
        }

        private static float Round(float value)
        {
            return (float)Math.Round(value, 3);
        }

        private static void WriteString(StringBuilder sb, string text)
        {
            sb.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
