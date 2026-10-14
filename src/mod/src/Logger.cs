// ===========================================================================
// YxMod5 C# 模组层 —— 日志系统
// ---------------------------------------------------------------------------
// 统一日志接口，双通道输出：
//   * Unity Debug.Log / Debug.LogWarning / Debug.LogError（编辑器与 Player.log）
//   * Console.WriteLine（原生启动层 AllocConsole 分配的调试控制台）
// 所有功能模块均应通过本类输出日志，便于定位问题。
// ===========================================================================
using System;
using UnityEngine;

namespace YxMod5
{
    /// <summary>统一日志接口（控制台 + Unity 日志）。</summary>
    public static class Logger
    {
        private const string Tag = "[YxMod5]";

        public static void Info(string message)
        {
            string line = Tag + " " + message;
            WriteConsole(line);
            Debug.Log(line);
        }

        public static void Warn(string message)
        {
            string line = Tag + " [WARN] " + message;
            WriteConsole(line);
            Debug.LogWarning(line);
        }

        public static void Error(string message)
        {
            string line = Tag + " [ERROR] " + message;
            WriteConsole(line);
            Debug.LogError(line);
        }

        public static void Exception(string context, Exception exception)
        {
            string line = Tag + " [ERROR] " + context + " -> " + exception;
            WriteConsole(line);
            Debug.LogError(line);
        }

        private static void WriteConsole(string line)
        {
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // 无控制台时忽略，仅保留 Unity 日志通道。
            }
        }
    }
}
