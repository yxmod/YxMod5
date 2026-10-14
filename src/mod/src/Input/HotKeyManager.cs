// ===========================================================================
// YxMod5 C# 模组层 —— 热键系统（roadmap 1.6）
// ---------------------------------------------------------------------------
// 自研注册 / 触发框架：单键 / 组合键 / 数字编号。
// 由宿主 YxMod.Update 每帧调用 Poll() 轮询触发。
// 具体功能键位由后续功能阶段各自注册（本阶段仅框架 + 少量示例）。
// ===========================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using YxMod5.Runtime;

namespace YxMod5.Input
{
    /// <summary>热键注册项。</summary>
    internal sealed class HotKeyBinding
    {
        public string Id;
        public HotKey HotKey;
        public Action Callback;
        public bool WasDown;
    }

    /// <summary>热键管理器（单例，由宿主驱动轮询）。</summary>
    public sealed class HotKeyManager
    {
        public static HotKeyManager Instance { get; } = new HotKeyManager();

        private readonly object _sync = new object();
        private readonly Dictionary<string, HotKeyBinding> _bindings =
            new Dictionary<string, HotKeyBinding>(StringComparer.Ordinal);

        private HotKeyManager()
        {
        }

        /// <summary>注册热键（同 id 覆盖）。</summary>
        public bool Register(string id, HotKey hotKey, Action callback)
        {
            if (string.IsNullOrEmpty(id) || callback == null || !hotKey.IsValid)
            {
                Logger.Warn("HotKeyManager.Register 参数无效（id/key/callback）");
                return false;
            }

            lock (_sync)
            {
                _bindings[id] = new HotKeyBinding
                {
                    Id = id,
                    HotKey = hotKey,
                    Callback = callback,
                    WasDown = false
                };
            }
            Logger.Info("已注册热键 [" + id + "] = " + hotKey);
            return true;
        }

        /// <summary>注销热键。</summary>
        public bool Unregister(string id)
        {
            lock (_sync)
            {
                return _bindings.Remove(id);
            }
        }

        /// <summary>轮询触发（每帧调用一次）。</summary>
        public void Poll()
        {
            List<HotKeyBinding> snapshot;
            lock (_sync)
            {
                if (_bindings.Count == 0)
                {
                    return;
                }
                snapshot = new List<HotKeyBinding>(_bindings.Values);
            }

            foreach (HotKeyBinding binding in snapshot)
            {
                bool down = IsDown(binding.HotKey);
                if (down && !binding.WasDown)
                {
                    Safe.Run(() => binding.Callback(),
                        "HotKeyManager.Poll(" + binding.Id + ")");
                }
                binding.WasDown = down;
            }
        }

        /// <summary>判定组合键是否按下。</summary>
        private static bool IsDown(HotKey hotKey)
        {
            if (hotKey.Key == KeyCode.None)
            {
                return false;
            }

            if (hotKey.Ctrl && !(UnityEngine.Input.GetKey(KeyCode.LeftControl) ||
                                 UnityEngine.Input.GetKey(KeyCode.RightControl)))
            {
                return false;
            }
            if (hotKey.Alt && !(UnityEngine.Input.GetKey(KeyCode.LeftAlt) ||
                                UnityEngine.Input.GetKey(KeyCode.RightAlt)))
            {
                return false;
            }
            if (hotKey.Shift && !(UnityEngine.Input.GetKey(KeyCode.LeftShift) ||
                                  UnityEngine.Input.GetKey(KeyCode.RightShift)))
            {
                return false;
            }

            return UnityEngine.Input.GetKey(hotKey.Key);
        }
    }
}
