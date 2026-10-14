// ===========================================================================
// YxMod5 C# 模组层 —— 宿主 MonoBehaviour
// ---------------------------------------------------------------------------
// 生命周期驱动骨架：以 Awake/Start/Update/FixedUpdate/LateUpdate/OnGUI 为执行
// 骨架，后续功能模块（roadmap 阶段 1 及以后）以组件形式注册到本宿主。
// 本阶段（阶段 0）仅实现最小闭环：挂载成功、保持存活、输出日志，并注册
// 异常/日志捕获，便于将崩溃信息写入 <游戏目录>\YxMod5.log。
// ===========================================================================
using System;
using UnityEngine;

namespace YxMod5
{
    /// <summary>YxMod5 模组宿主组件。</summary>
    public sealed class YxMod : MonoBehaviour
    {
        /// <summary>当前版本号（阶段 0 初始版本）。</summary>
        public const string Version = "0.1.0";

        /// <summary>当前唯一实例。</summary>
        public static YxMod Instance { get; private set; }

        private static bool _handlersRegistered;

        private void Awake()
        {
            // 单例保护：避免重复挂载导致多实例。
            if (Instance != null && Instance != this)
            {
                Logger.Warn("检测到重复宿主，销毁多余实例");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            RegisterHandlers();

            Logger.Info("YxMod 宿主 Awake，版本 " + Version);
            Logger.Info("日志文件：" + Logger.LogPath);
            Logger.Info("游戏目录：" + Application.dataPath);
        }

        private void Start()
        {
            Logger.Info("YxMod 宿主 Start（最小闭环就绪）");
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            Logger.Info("YxMod 宿主 OnDestroy");
        }

        private void OnApplicationQuit()
        {
            Logger.Info("游戏退出（OnApplicationQuit）");
            Logger.Shutdown();
        }

        // ---- 异常 / Unity 日志捕获 -----------------------------------------

        private static void RegisterHandlers()
        {
            if (_handlersRegistered)
            {
                return;
            }
            _handlersRegistered = true;

            try
            {
                Application.logMessageReceived += OnUnityLogMessage;
            }
            catch (Exception exception)
            {
                Logger.ErrorFile("注册 Unity 日志回调失败 -> " + exception);
            }

            try
            {
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            }
            catch (Exception exception)
            {
                Logger.ErrorFile("注册 AppDomain 异常回调失败 -> " + exception);
            }
        }

        // 是否把游戏的全部 Debug.Log 输出接入 YXMOD5 Debug Console。
        // true  = 转发全部日志；false = 仅转发 Warning/Error/Exception/Assert。
        private const bool ForwardAllUnityLogs = true;

        private static void OnUnityLogMessage(string message, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            // 跳过本模组自身的日志，避免重复刷屏。
            if (message.Contains("[YxMod5]"))
            {
                return;
            }

            bool isImportant = type == LogType.Warning || type == LogType.Error ||
                               type == LogType.Exception || type == LogType.Assert;

            if (!ForwardAllUnityLogs && !isImportant)
            {
                return;
            }

            // 转发到 YXMOD5 Debug Console（游戏自带 debug 信息由此进入控制台）。
            string line = "[Game][" + type + "] " + message;
            NativeConsole.WriteLine(line);

            // 写入 <游戏目录>\YxMod5.log，保留完整记录；重要日志附堆栈。
            if (isImportant && !string.IsNullOrEmpty(stackTrace))
            {
                line += "\n" + stackTrace;
            }
            Logger.WriteRaw(line);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            // 可能发生在任意线程，禁止调用 Unity API。
            Logger.ErrorFile("AppDomain 未处理异常：" + args.ExceptionObject);
        }
    }
}
