// N.E.K.O-SP2-Bridge — 本地 HTTP 服务
//
// 只监听 127.0.0.1，只接受 GET，只读。
//
// 两条硬约束
//   1. **只读**：这里不存在任何会改变游戏状态的路由。将来要加控制接口，
//      必须改成「HTTP 线程入队 → 主线程执行」。
//   2. **不碰 Unity**：HTTP 线程只读取 Telemetry 已经渲染好的 JSON 字符串。
//      在后台线程调 Unity API 会直接把游戏搞崩（姊妹项目踩过）。
//
// 为什么不用 Unity 的协程 / 主线程轮询：
//   游戏一切换场景，挂在 BepInEx manager 上的组件就会被销毁，
//   接口会跟着一起没。独立线程不受影响。

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace NekoSp2Bridge
{
    public static class BridgeServer
    {
        private static HttpListener listener;
        private static Thread worker;
        private static volatile bool running;
        private static int port;

        public static bool IsRunning
        {
            get { return running; }
        }

        public static int Port
        {
            get { return port; }
        }

        public static bool Start(int listenPort)
        {
            if (running)
            {
                return true;
            }
            try
            {
                port = listenPort;
                listener = new HttpListener();
                // 只绑回环，不对局域网开放 —— 这是本机调试接口。
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                listener.IgnoreWriteExceptions = true;
                listener.Start();
                running = true;

                worker = new Thread(Loop);
                worker.IsBackground = true;
                worker.Name = "NekoSp2Bridge";
                worker.Start();
                return true;
            }
            catch (Exception ex)
            {
                BridgeLog.Error("HTTP 服务启动失败: " + BridgeLog.Safe(ex));
                running = false;
                return false;
            }
        }

        public static void Stop()
        {
            running = false;
            try
            {
                if (listener != null)
                {
                    listener.Stop();
                    listener.Close();
                }
            }
            catch
            {
            }
            listener = null;
        }

        private static void Loop()
        {
            while (running)
            {
                HttpListenerContext context = null;
                try
                {
                    context = listener.GetContext();
                }
                catch
                {
                    if (!running)
                    {
                        return;
                    }
                    continue;
                }
                try
                {
                    Handle(context);
                }
                catch (Exception ex)
                {
                    BridgeLog.Error("处理请求失败: " + BridgeLog.Safe(ex));
                    try
                    {
                        context.Response.StatusCode = 500;
                        context.Response.Close();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void Handle(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            string path = request.Url.AbsolutePath;
            if (path == null)
            {
                path = "/";
            }
            path = path.TrimEnd('/');
            if (path.Length == 0)
            {
                path = "/";
            }

            if (!string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                Respond(context, 405, "{\"ok\":false,\"error\":\"只支持 GET（本服务只读）\"}");
                return;
            }

            switch (path.ToLowerInvariant())
            {
                case "/":
                case "/ping":
                    Respond(context, 200, Telemetry.ProtocolVersionJson);
                    return;
                case "/state":
                    Respond(context, 200, Telemetry.StateJson);
                    return;
                case "/aircraft":
                    Respond(context, 200, Telemetry.AircraftJson);
                    return;
                case "/navigation":
                    Respond(context, 200, Telemetry.NavigationJson);
                    return;
                case "/environment":
                    Respond(context, 200, Telemetry.EnvironmentJson);
                    return;
                case "/all":
                    Respond(context, 200, BuildAll());
                    return;
#if NEKO_DEBUG
                case "/probe":
                    Respond(context, 200, DebugProbe.Render());
                    return;
#endif
                default:
                    Respond(context, 404, "{\"ok\":false,\"error\":\"未知路径 " + path + "\"}");
                    return;
            }
        }

        /// <summary>一次性拿全，省得消费方连着打四个请求。</summary>
        private static string BuildAll()
        {
            // 各段已经是现成的 JSON 字符串，直接拼接，不再走一遍序列化
            // （否则会变成被转义的字符串，消费方还得解两次）。
            StringBuilder sb = new StringBuilder(4096);
            sb.Append("{\"ok\":true,\"protocol\":1,\"service\":\"SP2-Bridge\",");
            sb.Append("\"game\":\"SimplePlanes 2\",\"captures\":").Append(Telemetry.CaptureCount);
            sb.Append(",\"state\":").Append(Telemetry.StateJson);
            sb.Append(",\"aircraft\":").Append(Telemetry.AircraftJson);
            sb.Append(",\"navigation\":").Append(Telemetry.NavigationJson);
            sb.Append(",\"environment\":").Append(Telemetry.EnvironmentJson);
            sb.Append('}');
            return sb.ToString();
        }

        private static void Respond(HttpListenerContext context, int status, string body)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body ?? "");
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                context.Response.AddHeader("Cache-Control", "no-store");
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch
            {
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch
                {
                }
            }
        }
    }
}
