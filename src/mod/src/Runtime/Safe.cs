// ===========================================================================
// YxMod5 C# 模组层 —— 空引用 / 异常安全防护层（roadmap 1.4）
// ---------------------------------------------------------------------------
// 统一异常防护：所有反射访问 / 运行时操作都走 Safe，保证：
//   * 目标为 null、成员缺失、类型不匹配、调用异常时安全降级；
//   * 单功能故障不拖垮整个模组；
//   * 异常可观测（写日志，且同一上下文限流，避免刷屏）。
// ===========================================================================
using System;
using System.Collections.Generic;

namespace YxMod5.Runtime
{
    /// <summary>空引用 / 异常安全防护层。</summary>
    public static class Safe
    {
        // 同一上下文的异常日志限流：最多每 60 秒记录一次，避免刷屏。
        private static readonly Dictionary<string, DateTime> LastLog = new Dictionary<string, DateTime>();
        private static readonly object LogSync = new object();

        /// <summary>执行函数并返回结果；异常 / 失败时返回 fallback。</summary>
        public static T Run<T>(Func<T> action, T fallback = default(T), string context = null)
        {
            try
            {
                return action();
            }
            catch (Exception exception)
            {
                LogException(context, exception);
                return fallback;
            }
        }

        /// <summary>执行动作；异常时安全降级（不抛出）。</summary>
        public static void Run(Action action, string context = null)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                LogException(context, exception);
            }
        }

        /// <summary>执行函数，返回是否成功；异常时不抛出。</summary>
        public static bool Try(Action action, string context = null)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception exception)
            {
                LogException(context, exception);
                return false;
            }
        }

        private static void LogException(string context, Exception exception)
        {
            string key = string.IsNullOrEmpty(context) ? exception.GetType().Name : context;

            bool shouldLog;
            lock (LogSync)
            {
                DateTime now = DateTime.UtcNow;
                if (LastLog.TryGetValue(key, out DateTime last) && (now - last).TotalSeconds < 60)
                {
                    shouldLog = false;
                }
                else
                {
                    LastLog[key] = now;
                    shouldLog = true;
                }
            }

            if (!shouldLog)
            {
                return;
            }

            string message = string.IsNullOrEmpty(context)
                ? "安全降级：" + exception
                : "[" + context + "] 安全降级：" + exception.Message + "（" + exception.GetType().Name + "）";
            Logger.Warn(message);
        }
    }
}
