// ===========================================================================
// YxMod5 C# 模组层 —— 宿主 MonoBehaviour
// ---------------------------------------------------------------------------
// 生命周期驱动骨架：以 Awake/Start/Update/FixedUpdate/LateUpdate/OnGUI 为执行
// 骨架，后续功能模块（roadmap 阶段 1 及以后）以组件形式注册到本宿主。
// 本阶段（阶段 0）仅实现最小闭环：挂载成功、保持存活、输出日志。
// ===========================================================================
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
            Logger.Info("YxMod 宿主 Awake，版本 " + Version);
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
    }
}
