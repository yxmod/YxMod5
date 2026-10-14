// ===========================================================================
// YxMod5 C# 模组层 —— 日志系统
// ---------------------------------------------------------------------------
// 三通道输出：
//   * 文件：<游戏目录>\YxMod5.log（追加、UTF-8、带时间戳）
//   * Unity：Debug.Log / Debug.LogWarning / Debug.LogError
//   * 控制台：Console.WriteLine（原生层 AllocConsole 分配的调试控制台）
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
    /// <summary>统一日志接口（文件 + Unity 日志 + 控制台）。</summary>
    public static class Logger
    {
        private const string Tag = "[YxMod5]";

        private static readonly object Sync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static string _logPath;
        private static bool _logPathResolved;

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

        // ---- 内部实现 ------------------------------------------------------

        private static void Write(string level, string message, LogType type, bool useUnityLog)
        {
            string line = Tag + "[" + level + "] " + message;
            AppendToFile(line);

            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // 无控制台时忽略，仅保留文件与 Unity 日志通道。
            }

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
                string stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + line + Environment.NewLine;
                lock (Sync)
                {
                    File.AppendAllText(_logPath, stamped, Utf8NoBom);
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
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string gameDir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrEmpty(gameDir))
                {
                    _logPath = Path.Combine(gameDir, "YxMod5.log");
                    return;
                }
            }
            catch
            {
            }

            try
            {
                _logPath = Path.Combine(Directory.GetCurrentDirectory(), "YxMod5.log");
            }
            catch
            {
                _logPath = null;
            }
        }
    }
}
