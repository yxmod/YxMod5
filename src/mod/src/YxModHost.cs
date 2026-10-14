// ===========================================================================
// YxMod5 C# 模组层 —— 宿主 GameObject 管理
// ---------------------------------------------------------------------------
// 负责创建并持有 "YxMod5Host" GameObject，保证：
//   * 场景切换后依然存在（DontDestroyOnLoad）；
//   * 重复调用 Ensure() 不会产生重复实例。
// ===========================================================================
using UnityEngine;

namespace YxMod5
{
    internal static class YxModHost
    {
        public const string GameObjectName = "YxMod5Host";

        private static YxMod _component;

        /// <summary>确保宿主存在并返回其组件（幂等）。</summary>
        public static YxMod Ensure()
        {
            if (_component != null)
            {
                return _component;
            }

            // 复用场景中可能已存在的宿主。
            GameObject existing = GameObject.Find(GameObjectName);
            if (existing != null)
            {
                _component = existing.GetComponent<YxMod>();
                if (_component == null)
                {
                    _component = existing.AddComponent<YxMod>();
                }
                return _component;
            }

            GameObject host = new GameObject(GameObjectName);
            host.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(host);
            _component = host.AddComponent<YxMod>();

            Logger.Info("已创建宿主 GameObject：" + GameObjectName);
            return _component;
        }
    }
}
