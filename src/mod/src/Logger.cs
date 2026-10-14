// ===========================================================================
// YxMod5 C# 模组层 —— 日志系统
// ---------------------------------------------------------------------------
// 三通道输出：
//   * 文件：<游戏目录>\YxMod5\YxMod5.log（追加、UTF-8、带时间戳、常驻流自动刷新）
//   * 控制台：YXMOD5 Debug Console（NativeConsole 直写控制器句柄）
//   * Unity：Debug.Log / Debug.LogWarning / Debug.LogError
//
// 线程约定：
//   * 由原生注入线程调用的入口请使用 …File 系列（不触碰 Unity API）；
//   * 主线程（Unity 回调）可使用普通 Info/Warn/Error。
// ===========================================================================
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace YxMod5
{
    /// <summary>统一日志接口（文件 + 控制台 + Unity 日志）。</summary>
    public static class Logger
    {
        private const string Tag = "[YxMod5]";

        private static readonly object Sync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static string _logPath;
        private static bool _logPathResolved;
        private static StreamWriter _writer;

        /// <summary>日志文件完整路径（解析失败时为 null）。</summary>
        public static string LogPath
        {
            get
            {
                ResolveLogPath();
                return _logPath;
            }
        }

        // ---- 主线程可用（带 Unity 日志） -----------------------------------

        public static void Info(string message)
        {
            Write("INFO", message, LogType.Log, useUnityLog: true);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, LogType.Warning, useUnityLog: true);
        }

        public static void Error(string message)
        {
            Write("ERROR", message, LogType.Error, useUnityLog: true);
        }

        public static void Exception(string context, Exception exception)
        {
            Write("ERROR", context + " -> " + exception, LogType.Error, useUnityLog: true);
        }

        // ---- 任意线程可用（不触碰 Unity API） ------------------------------

        public static void InfoFile(string message)
        {
            Write("INFO", message, LogType.Log, useUnityLog: false);
        }

        public static void ErrorFile(string message)
        {
            Write("ERROR", message, LogType.Error, useUnityLog: false);
        }

        /// <summary>原样写入一行到日志文件（不添加 [YxMod5] 前缀）。</summary>
        public static void WriteRaw(string line)
        {
            AppendToFile(line);
        }

        /// <summary>刷新并关闭日志文件（退出时调用）。</summary>
        public static void Shutdown()
        {
            lock (Sync)
            {
                if (_writer != null)
                {
                    try
                    {
                        _writer.Flush();
                        _writer.Dispose();
                    }
                    catch
                    {
                    }
                    _writer = null;
                }
            }
        }

        // ---- 内部实现 ------------------------------------------------------

        private static void Write(string level, string message, LogType type, bool useUnityLog)
        {
            string line = Tag + "[" + level + "] " + message;
            AppendToFile(line);

            // 直接写原生调试控制台（Console.Out 可能持有失效句柄）。
            NativeConsole.WriteLine(line);

            if (!useUnityLog)
            {
                return;
            }

            try
            {
                switch (type)
                {
                    case LogType.Warning:
                        Debug.LogWarning(line);
                        break;
                    case LogType.Error:
                    case LogType.Exception:
                    case LogType.Assert:
                        Debug.LogError(line);
                        break;
                    default:
                        Debug.Log(line);
                        break;
                }
            }
            catch
            {
                // Unity 尚未就绪时忽略。
            }
        }

        private static void AppendToFile(string line)
        {
            ResolveLogPath();
            if (string.IsNullOrEmpty(_logPath))
            {
                return;
            }

            try
            {
                string stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + line;
                lock (Sync)
                {
                    if (_writer == null)
                    {
                        FileStream stream = new FileStream(
                            _logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                        _writer = new StreamWriter(stream, Utf8NoBom);
                        _writer.AutoFlush = true;
                    }
                    _writer.WriteLine(stamped);
                }
            }
            catch
            {
                // 写文件失败时不影响模组运行。
            }
        }

        private static void ResolveLogPath()
        {
            if (_logPathResolved)
            {
                return;
            }
            _logPathResolved = true;

            // 只使用 BCL/系统 API，保证可从原生注入线程安全调用
            // （Application.dataPath 等 Unity API 不在非主线程触碰）。
            // 日志迁入 <游戏根目录>\YxMod5\（阶段 1 约定）。
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string gameDir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrEmpty(gameDir))
                {
                    string root = Path.Combine(gameDir, "YxMod5");
                    Directory.CreateDirectory(root);
                    _logPath = Path.Combine(root, "YxMod5.log");
                    return;
                }
            }
            catch
            {
            }

            try
            {
                string root = Path.Combine(Directory.GetCurrentDirectory(), "YxMod5");
                Directory.CreateDirectory(root);
                _logPath = Path.Combine(root, "YxMod5.log");
            }
            catch
            {
                _logPath = null;
            }
        }
    }
}
