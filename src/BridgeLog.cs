// N.E.K.O-SP2-Bridge — 日志
//
// 写插件目录下的 sp2-bridge.log，超过 2 MB 自动截断。
// 同时把同一条消息交给 BepInEx 的控制台（Debug.Log），方便排错时对照。
//
// 为什么自己写而不直接用 BepInEx 的 Logger：
//   本项目的模组要能被 BepInEx 之外的宿主带走（HTTP 服务跑在独立线程上），
//   一份落盘日志比只打控制台更容易让用户把问题贴过来。

using System;
using System.IO;
using System.Text;

namespace NekoSp2Bridge
{
    public static class BridgeLog
    {
        private const long MaxBytes = 2L * 1024L * 1024L;

        private static readonly object Gate = new object();
        private static string path;
        private static bool fileEnabled = true;
        private static bool headerWritten;
        private static bool consoleEnabled = true;

        public static void Init(string directory, string header)
        {
            try
            {
                if (string.IsNullOrEmpty(directory))
                {
                    fileEnabled = false;
                    return;
                }
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, "sp2-bridge.log");
                TruncateIfNeeded();
                Write(header);
                headerWritten = true;
            }
            catch
            {
                fileEnabled = false;
            }
        }

        public static void SetFileEnabled(bool enabled)
        {
            fileEnabled = enabled;
        }

        public static void SetConsoleEnabled(bool enabled)
        {
            consoleEnabled = enabled;
        }

        public static string LogPath
        {
            get { return path; }
        }

        public static void Write(string message)
        {
            Emit("INFO", message);
        }

        public static void Warn(string message)
        {
            Emit("WARN", message);
        }

        public static void Error(string message)
        {
            Emit("ERROR", message);
        }

        private static void Emit(string level, string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [" + level + "] " + (message ?? "");
            if (consoleEnabled)
            {
                try
                {
                    UnityEngine.Debug.Log("[SP2-Bridge] " + level + " " + (message ?? ""));
                }
                catch
                {
                }
            }
            if (!fileEnabled || string.IsNullOrEmpty(path))
            {
                return;
            }
            lock (Gate)
            {
                try
                {
                    using (StreamWriter writer = new StreamWriter(path, true, new UTF8Encoding(false)))
                    {
                        writer.WriteLine(line);
                    }
                }
                catch
                {
                }
            }
        }

        private static void TruncateIfNeeded()
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length < MaxBytes)
                {
                    return;
                }
                // 只保留尾部 256 KB，避免日志把磁盘慢慢吃掉。
                byte[] buffer = new byte[256 * 1024];
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Seek(Math.Max(0, stream.Length - buffer.Length), SeekOrigin.Begin);
                    int read = stream.Read(buffer, 0, buffer.Length);
                    using (FileStream writer = new FileStream(path, FileMode.Create, FileAccess.Write))
                    {
                        writer.Write(Encoding.UTF8.GetBytes("... (截断) ...\n"), 0, 16);
                        writer.Write(buffer, 0, read);
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>把异常压成一行文本；绝不因为记日志再抛一次。</summary>
        public static string Safe(Exception ex)
        {
            if (ex == null)
            {
                return "(null)";
            }
            try
            {
                string text = ex.GetType().Name + ": " + ex.Message;
                if (ex.StackTrace != null)
                {
                    text += " @ " + ex.StackTrace.Replace("\n", " ").Replace("\r", "");
                }
                return text;
            }
            catch
            {
                return "(异常无法格式化)";
            }
        }
    }
}
