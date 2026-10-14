// ===========================================================================
// YxMod5 C# 模组层 —— Doorstop 入口
// ---------------------------------------------------------------------------
// 由原生启动层通过 mono_runtime_invoke 调用：
//     Doorstop.Entrypoint.Start()
//
// 重要：本方法运行在原生注入线程（非 Unity 主线程），因此不能直接创建
// GameObject / 调用 Unity API。这里只注册场景切换事件，待 Unity 主线程
// 触发 activeSceneChanged 时再挂载宿主，确保线程安全。
// ===========================================================================
using System;
using UnityEngine.SceneManagement;

namespace Doorstop
{
    /// <summary>注入入口点（与 UnityDoorstop 约定一致）。</summary>
    public static class Entrypoint
    {
        private static bool _subscribed;
        private static bool _hostCreated;

        /// <summary>由原生启动层调用的静态无参入口。</summary>
        public static void Start()
        {
            // 幂等：重复调用不再重复注册 / 挂载。
            if (_subscribed || _hostCreated)
            {
                return;
            }

            try
            {
                SceneManager.activeSceneChanged += OnActiveSceneChanged;
                _subscribed = true;
                // 本方法运行在原生注入线程，禁止触碰 Unity API（Debug.Log 等），
                // 因此仅写文件/控制台。
                YxMod5.Logger.InfoFile("Entrypoint.Start 已调用，等待场景切换后挂载宿主");
            }
            catch (Exception exception)
            {
                YxMod5.Logger.ErrorFile("Entrypoint.Start 注册场景事件失败 -> " + exception);
            }
        }

        private static void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene oldScene,
                                                 UnityEngine.SceneManagement.Scene newScene)
        {
            try
            {
                // 每次场景切换都确保宿主存在（Ensure 幂等）：
                // 若宿主已被销毁则自动重建，保证跨场景稳定存活。
                YxMod5.YxModHost.Ensure();

                if (!_hostCreated)
                {
                    _hostCreated = true;
                    YxMod5.Logger.Info("模组注入成功，宿主已挂载（场景：" + newScene.name + "）");
                }
            }
            catch (Exception exception)
            {
                YxMod5.Logger.Exception("挂载模组宿主失败", exception);
            }
        }
    }
}
