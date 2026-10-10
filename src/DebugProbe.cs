// N.E.K.O-SP2-Bridge — 探针（仅在 DEBUG 构建里编译）
//
// 作用：把当前这几个关键对象的**全部成员名 + 当前值**列出来，
// 交给 /probe 返回。这样在实机上跑一次，就能把「空速到底叫什么」
// 「Controls 上有哪些轴」这类问题一次问清楚，不用反复猜。
//
// 用法
//   build.ps1 -Debug 装上去 -> 浏览器打开 http://127.0.0.1:21580/probe
//   把输出贴给开发者即可（注意：里面可能有你的载具名，没有别的隐私信息）。
//
// 只读取，不写入、不调用任何方法。

#if NEKO_DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NekoSp2Bridge
{
    public static class DebugProbe
    {
        private const int MaxMembers = 400;
        private static string cached = "{\"ok\":false,\"error\":\"探针还没跑\"}";
        private static float nextProbe;

        public static string Render()
        {
            return cached;
        }

        /// <summary>主线程调用（在 BridgeRunner.Pump 里）。</summary>
        public static void Pump()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now < nextProbe)
                {
                    return;
                }
                nextProbe = now + 2.0f;

                JsonObj root = new JsonObj();
                root.Set("ok", true);
                root.Set("protocol", 1);
                root.Set("note", "DEBUG 构建专用：列出关键对象的成员名与当前值，用于补齐字段名");
                root.Set("captures", Telemetry.CaptureCount);
                root.SetIfNotNull("last_error", Telemetry.LastError);
                root.SetIfNotNull("access_error", GameAccess.LastError);

                root.Set("scene", DumpObject("FlightSceneScript", GameAccess.Scene));
                root.Set("player", DumpObject("FlightScenePlayer", GameAccess.Player));
                root.Set("aircraft", DumpObject("AircraftScript", GameAccess.Aircraft));
                root.Set("controls", DumpObject("Controls", GameAccess.Controls));
                root.Set("engines", DumpEngines());
                root.Set("terrain_count", Terrain.activeTerrains != null ? Terrain.activeTerrains.Length : 0);

                cached = BridgeJson.Serialize(root);
            }
            catch (Exception ex)
            {
                cached = "{\"ok\":false,\"error\":" + Quote(BridgeLog.Safe(ex)) + "}";
            }
        }

        private static JsonObj DumpObject(string label, object target)
        {
            JsonObj obj = new JsonObj();
            obj.Set("label", label);
            if (target == null)
            {
                obj.Set("present", false);
                return obj;
            }
            obj.Set("present", true);
            obj.Set("type", target.GetType().FullName);

            List<JsonObj> members = new List<JsonObj>();
            List<MemberInfo> infos = Reflect.ListMembers(target);
            int count = 0;
            for (int i = 0; i < infos.Count && count < MaxMembers; i++)
            {
                MemberInfo member = infos[i];
                string name = member.Name;
                // 跳过编译器生成的基础成员，噪音太大
                if (name.StartsWith("<") || name.StartsWith("k__"))
                {
                    continue;
                }
                object value = Reflect.ReadMember(target, member);
                JsonObj entry = new JsonObj();
                entry.Set("name", name);
                entry.Set("kind", member is PropertyInfo ? "prop" : "field");
                entry.Set("type", MemberTypeName(member));
                entry.Set("value", ShortValue(value));
                members.Add(entry);
                count++;
            }
            obj.Set("members", members);
            return obj;
        }

        private static List<JsonObj> DumpEngines()
        {
            List<JsonObj> list = new List<JsonObj>();
            MonoBehaviour behaviour = GameAccess.Aircraft as MonoBehaviour;
            if (behaviour == null)
            {
                return list;
            }
            MonoBehaviour[] all = behaviour.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < all.Length && list.Count < 40; i++)
            {
                MonoBehaviour component = all[i];
                if (component == null)
                {
                    continue;
                }
                string typeName = component.GetType().Name;
                if (typeName.IndexOf("Engine", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                JsonObj obj = new JsonObj();
                obj.Set("type", component.GetType().FullName);
                List<JsonObj> members = new List<JsonObj>();
                List<MemberInfo> infos = Reflect.ListMembers(component);
                for (int j = 0; j < infos.Count && members.Count < 80; j++)
                {
                    string name = infos[j].Name;
                    if (name.StartsWith("<"))
                    {
                        continue;
                    }
                    JsonObj entry = new JsonObj();
                    entry.Set("name", name);
                    entry.Set("kind", infos[j] is PropertyInfo ? "prop" : "field");
                    entry.Set("value", ShortValue(Reflect.ReadMember(component, infos[j])));
                    members.Add(entry);
                }
                obj.Set("members", members);
                list.Add(obj);
            }
            return list;
        }

        private static string MemberTypeName(MemberInfo member)
        {
            try
            {
                PropertyInfo prop = member as PropertyInfo;
                if (prop != null)
                {
                    return prop.PropertyType.Name;
                }
                FieldInfo field = member as FieldInfo;
                if (field != null)
                {
                    return field.FieldType.Name;
                }
            }
            catch
            {
            }
            return "?";
        }

        private static string ShortValue(object value)
        {
            if (value == null)
            {
                return "null";
            }
            try
            {
                if (value is Vector3)
                {
                    Vector3 v = (Vector3)value;
                    return "(" + v.x.ToString("0.##") + ", " + v.y.ToString("0.##") + ", " + v.z.ToString("0.##") + ")";
                }
                if (value is Quaternion)
                {
                    Quaternion q = (Quaternion)value;
                    return "(" + q.x.ToString("0.###") + ", " + q.y.ToString("0.###") + ", " +
                           q.z.ToString("0.###") + ", " + q.w.ToString("0.###") + ")";
                }
                if (value is float)
                {
                    return ((float)value).ToString("0.###");
                }
                if (value is double)
                {
                    return ((double)value).ToString("0.###");
                }
                if (value is bool || value is int || value is long || value is short)
                {
                    return Convert.ToString(value);
                }
                if (value is string)
                {
                    string s = (string)value;
                    return s.Length > 60 ? s.Substring(0, 60) + "..." : s;
                }
                if (value is UnityEngine.Object)
                {
                    UnityEngine.Object uo = (UnityEngine.Object)value;
                    return "<" + value.GetType().Name + ">" + (uo != null ? " " + uo.name : " (null)");
                }
                if (value is System.Collections.IEnumerable)
                {
                    int n = 0;
                    System.Collections.IEnumerator it = ((System.Collections.IEnumerable)value).GetEnumerator();
                    while (it.MoveNext() && n < 1000)
                    {
                        n++;
                    }
                    return "<" + value.GetType().Name + " count=" + n + ">";
                }
                return "<" + value.GetType().Name + ">";
            }
            catch
            {
                return "(读取失败)";
            }
        }

        private static string Quote(string text)
        {
            if (text == null)
            {
                return "null";
            }
            return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";
        }
    }
}
#endif
