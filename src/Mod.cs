// N.E.K.O-SP2-Bridge — BepInEx 插件入口
//
// 把《简单飞机2 SimplePlanes 2》的飞行遥测通过本地 HTTP 暴露出去，
// 供外部程序（例如 N.E.K.O. 的「和猫娘一起开飞机」插件）读取。
//
// 为什么是 BepInEx
//   SP2 目前处于抢先体验，**官方 modding 支持还没上线**，没有任何官方
//   扩展点可用。社区（SP2FreeCamera、SP2 小地图等）已验证 BepInEx 5 Mono
//   在这款游戏上可用：它不修改任何游戏原文件，只是在目录里多放几个文件，
//   卸载就是删掉它们。
//
// 设计原则（与姊妹项目 N.E.K.O-Juno-bridge / N.E.K.O-KSP-bridge 一致）
//   1. **只读** —— 只报数据，不碰操纵。玩家永远握着操纵杆。
//   2. **公用** —— 不依赖 N.E.K.O.，任何程序都能读这个 HTTP 接口。
//   3. **不打扰** —— 不弹窗、不改 UI、不接管输入。
//
// 生命周期（Juno 项目实测踩过的坑，这里同样适用）
//   BepInEx 挂在它 manager 对象上的插件组件，可能在场景切换时被销毁 ——
//   所以 Start()/Update() 不一定会被派发。因此：
//     · HTTP 服务由**独立后台线程**持有，不随任何 Unity 对象消亡；
//     · 遥测采集挂在一个自己创建、显式 DontDestroyOnLoad 的运行器上。
//
// 线程模型
//   主线程    BridgeRunner.Update() -> GameAccess.Tick() + Telemetry.Capture()
//   后台线程  HTTP 服务只读那份已渲染好的 JSON 字符串，完全不碰 Unity 对象
//
//   ⚠️ 将来若要加控制接口，必须改成「HTTP 线程入队 -> 主线程 Update 执行」。
//      在后台线程调 Unity API 会直接把游戏搞崩。

using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

namespace NekoSp2Bridge
{
    [BepInPlugin(Sp2BridgePlugin.PluginGuid, Sp2BridgePlugin.ModName, Sp2BridgePlugin.PluginVersion)]
    public class Sp2BridgePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "top.xhth.neko.sp2-bridge";
        public const string ModName = "N.E.K.O-SP2-Bridge";
        public const string PluginVersion = "0.1.0";

        private static GameObject runnerObject;

        private void Awake()
        {
            try
            {
                string dir = PluginDirectory();
                BridgeLog.Init(dir, "=== " + ModName + " v" + PluginVersion + " ===");
                BridgeConfig.Load(dir);
                BridgeLog.SetFileEnabled(BridgeConfig.LogToFile);

                BridgeLog.Write("插件目录: " + (dir ?? "(未知)"));
                BridgeLog.Write("配置: " + BridgeConfig.Describe());
                BridgeLog.Write("协议: v1（见 docs/protocol.md）");
            }
            catch (Exception ex)
            {
                BridgeLog.Error("初始化失败: " + BridgeLog.Safe(ex));
            }

            StartServer();
            CreateRunner();
        }

        private void StartServer()
        {
            if (BridgeServer.IsRunning)
            {
                return;
            }
            try
            {
                if (BridgeServer.Start(BridgeConfig.Port))
                {
                    BridgeLog.Write("HTTP 服务已启动: http://127.0.0.1:" + BridgeConfig.Port + "/");
                }
                else
                {
                    BridgeLog.Error("HTTP 服务未能启动，端口 " + BridgeConfig.Port
                        + " 可能被占用。改 neko-sp2-bridge.ini 里的 port 再重启游戏；"
                        + "改完记得同步改 N.E.K.O. 插件里的 bridge_url。");
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Error("启动 HTTP 服务失败: " + BridgeLog.Safe(ex));
            }
        }

        private static void CreateRunner()
        {
            if (runnerObject != null)
            {
                return;
            }
            try
            {
                runnerObject = new GameObject("NekoSp2Bridge_Runner");
                runnerObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(runnerObject);
                runnerObject.AddComponent<BridgeRunner>();
                BridgeLog.Write("采集运行器已创建");
            }
            catch (Exception ex)
            {
                BridgeLog.Error("创建采集运行器失败: " + BridgeLog.Safe(ex));
            }
        }

        private void Update()
        {
            BridgeRunner.Pump();
        }

        private void OnDestroy()
        {
            // 故意不停 HTTP 服务：这个对象被销毁不代表游戏退出。
            BridgeLog.Write("插件宿主对象被销毁（HTTP 服务保持运行）");
        }

        private static string PluginDirectory()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(location))
                {
                    return null;
                }
                return Path.GetDirectoryName(location);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 遥测采集运行器。挂在自建的常驻对象上，按间隔把游戏状态渲染成 JSON 快照。
    /// </summary>
    public class BridgeRunner : MonoBehaviour
    {
        private static BridgeRunner active;
        private static float nextCapture;
        private static bool loggedStart;

        private void Awake()
        {
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            if (active != null && active != this)
            {
                BridgeLog.Warn("检测到重复的采集运行器，销毁多余的那个");
                UnityEngine.Object.Destroy(this);
                return;
            }
            active = this;
        }

        private void Update()
        {
            if (!loggedStart)
            {
                loggedStart = true;
                BridgeLog.Write("Update 循环已启动");
            }
            Pump();
        }

        /// <summary>可由任意常驻组件驱动；自带节流，重复调用无害。</summary>
        public static void Pump()
        {
            try
            {
#if NEKO_DEBUG
                DebugProbe.Pump();
#endif
                if (!BridgeServer.IsRunning)
                {
                    return;
                }

                GameAccess.Tick();

                float now = Time.realtimeSinceStartup;
                if (now < nextCapture)
                {
                    return;
                }
                double interval = BridgeConfig.PollIntervalMs / 1000.0;
                if (interval < 0.05)
                {
                    interval = 0.05;
                }
                nextCapture = now + (float)interval;
                Telemetry.Capture();
            }
            catch (Exception ex)
            {
                BridgeLog.Error("采集异常: " + BridgeLog.Safe(ex));
            }
        }
    }

    /// <summary>
    /// 配置。放在插件目录下的 neko-sp2-bridge.ini，不存在时自动生成一份带注释的。
    /// 改完需要重启游戏。
    /// </summary>
    public static class BridgeConfig
    {
        public static int Port = 21580;
        public static double PollIntervalMs = 200;
        public static bool LogToFile = true;

        public static string ConfigPath { get; private set; }

        public static void Load(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }
                ConfigPath = Path.Combine(dir, "neko-sp2-bridge.ini");
                if (!File.Exists(ConfigPath))
                {
                    WriteDefault(ConfigPath);
                    return;
                }

                string[] lines = File.ReadAllLines(ConfigPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();

                    if (key == "port")
                    {
                        int parsed;
                        if (int.TryParse(value, out parsed) && parsed > 1024 && parsed < 65536)
                        {
                            Port = parsed;
                        }
                    }
                    else if (key == "poll_interval_ms")
                    {
                        double parsed;
                        if (double.TryParse(value, out parsed) && parsed >= 50 && parsed <= 60000)
                        {
                            PollIntervalMs = parsed;
                        }
                    }
                    else if (key == "log_to_file")
                    {
                        LogToFile = value == "1" || value.ToLowerInvariant() == "true";
                    }
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Error("读配置失败: " + BridgeLog.Safe(ex));
            }
        }

        private static void WriteDefault(string path)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# N.E.K.O-SP2-Bridge 配置");
                sb.AppendLine("# 改完要重启《简单飞机2》才生效");
                sb.AppendLine();
                sb.AppendLine("# HTTP 端口，只监听 127.0.0.1（不对局域网开放），范围 1025-65535");
                sb.AppendLine("# 注意：改这里要同时改 N.E.K.O. 插件配置里的 bridge_url，");
                sb.AppendLine("#       只改一处两边就连不上了。");
                sb.AppendLine("port=21580");
                sb.AppendLine();
                sb.AppendLine("# 遥测采集间隔（毫秒），范围 50-60000");
                sb.AppendLine("# 调小更实时但更耗 CPU；调大更省。默认 200（每秒 5 次）");
                sb.AppendLine("poll_interval_ms=200");
                sb.AppendLine();
                sb.AppendLine("# 是否写日志文件（sp2-bridge.log，超过 2 MB 自动截断）");
                sb.AppendLine("log_to_file=true");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public static string Describe()
        {
            return "port=" + Port + " poll=" + PollIntervalMs + "ms log=" + LogToFile;
        }
    }
}
