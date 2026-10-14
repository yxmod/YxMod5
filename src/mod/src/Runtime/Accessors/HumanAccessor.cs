// ===========================================================================
// YxMod5 C# 模组层 —— Human 访问器（roadmap 1.2）
// ---------------------------------------------------------------------------
// 封装对 Human（状态 / 刚体 / 扩展属性）的访问，业务层只依赖本层接口语义，
// 不直接依赖游戏字段名。字段名随游戏版本集中在本层维护。
//
// 扩展属性：为后续能力标记（飞天 / 超跳 / 闪现 / 超人，roadmap 3.1）预留的
// 通用键值存储，挂在 Human 实例上（弱引用，不阻止 GC）。
// ===========================================================================
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace YxMod5.Runtime.Accessors
{
    /// <summary>Human 访问器。</summary>
    public static class HumanAccessor
    {
        // 扩展属性：Human -> (key -> value)。使用 ConditionalWeakTable 以便
        // Human 销毁后自动回收，不阻止 GC。
        private sealed class ExtensionBag
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        }

        private static readonly ConditionalWeakTable<Human, ExtensionBag> Extensions =
            new ConditionalWeakTable<Human, ExtensionBag>();

        // ---- 状态 ---------------------------------------------------------

        public static HumanState GetState(Human human)
        {
            return Safe.Run(() => human != null ? human.state : HumanState.Idle,
                HumanState.Idle, "HumanAccessor.GetState");
        }

        public static void SetState(Human human, HumanState state)
        {
            Safe.Run(() =>
            {
                if (human != null)
                {
                    human.state = state;
                }
            }, "HumanAccessor.SetState");
        }

        public static bool IsOnGround(Human human)
        {
            return Safe.Run(() => human != null && human.onGround, false, "HumanAccessor.IsOnGround");
        }

        public static bool IsLocalPlayer(Human human)
        {
            return Safe.Run(() => human != null && human.IsLocalPlayer, false, "HumanAccessor.IsLocalPlayer");
        }

        // ---- 刚体 ---------------------------------------------------------

        public static Rigidbody[] GetRigidbodies(Human human)
        {
            return Safe.Run(() => human != null ? human.rigidbodies : null,
                null, "HumanAccessor.GetRigidbodies");
        }

        public static Vector3 GetVelocity(Human human)
        {
            return Safe.Run(() => human != null ? human.velocity : Vector3.zero,
                Vector3.zero, "HumanAccessor.GetVelocity");
        }

        public static float GetMass(Human human)
        {
            return Safe.Run(() => human != null ? human.mass : 0f, 0f, "HumanAccessor.GetMass");
        }

        public static void SetVelocity(Human human, Vector3 velocity)
        {
            Safe.Run(() =>
            {
                Rigidbody[] bodies = human != null ? human.rigidbodies : null;
                if (bodies == null)
                {
                    return;
                }
                for (int i = 0; i < bodies.Length; i++)
                {
                    Rigidbody body = bodies[i];
                    if (body != null)
                    {
                        body.velocity = velocity;
                    }
                }
            }, "HumanAccessor.SetVelocity");
        }

        // ---- 关联对象 -----------------------------------------------------

        public static Multiplayer.NetPlayer GetNetPlayer(Human human)
        {
            return Safe.Run(() => human != null ? human.player : null,
                null, "HumanAccessor.GetNetPlayer");
        }

        /// <summary>获取本地玩家 Human（由游戏 Human.Localplayer 提供）。</summary>
        public static Human GetLocalPlayer()
        {
            return Safe.Run(() => Human.Localplayer, null, "HumanAccessor.GetLocalPlayer");
        }

        public static IEnumerable<Human> GetAll()
        {
            return Safe.Run(() => Human.all, null, "HumanAccessor.GetAll");
        }

        // ---- 扩展属性（能力标记等，roadmap 3.x 使用） --------------------

        public static void SetExtension<T>(Human human, string key, T value)
        {
            Safe.Run(() =>
            {
                if (human == null)
                {
                    return;
                }
                ExtensionBag bag = Extensions.GetOrCreateValue(human);
                lock (bag.Values)
                {
                    bag.Values[key] = value;
                }
            }, "HumanAccessor.SetExtension");
        }

        public static T GetExtension<T>(Human human, string key, T fallback = default(T))
        {
            return Safe.Run(() =>
            {
                if (human == null)
                {
                    return fallback;
                }
                if (Extensions.TryGetValue(human, out ExtensionBag bag))
                {
                    lock (bag.Values)
                    {
                        if (bag.Values.TryGetValue(key, out object value) && value is T typed)
                        {
                            return typed;
                        }
                    }
                }
                return fallback;
            }, fallback, "HumanAccessor.GetExtension");
        }

        public static bool HasExtension(Human human, string key)
        {
            return Safe.Run(() =>
            {
                if (human == null || !Extensions.TryGetValue(human, out ExtensionBag bag))
                {
                    return false;
                }
                lock (bag.Values)
                {
                    return bag.Values.ContainsKey(key);
                }
            }, false, "HumanAccessor.HasExtension");
        }
    }
}
