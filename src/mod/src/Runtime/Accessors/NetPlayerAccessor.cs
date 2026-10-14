// ===========================================================================
// YxMod5 C# 模组层 —— NetPlayer 访问器（roadmap 1.3）
// ---------------------------------------------------------------------------
// 封装对 Multiplayer.NetPlayer 私有 / 公共成员的访问，只暴露接口语义。
// ===========================================================================
using Multiplayer;

namespace YxMod5.Runtime.Accessors
{
    /// <summary>NetPlayer 访问器。</summary>
    public static class NetPlayerAccessor
    {
        public static uint GetNetId(NetPlayer player)
        {
            return Safe.Run(() => player != null ? player.netId : 0u, 0u, "NetPlayerAccessor.GetNetId");
        }

        public static NetHost GetHost(NetPlayer player)
        {
            return Safe.Run(() => player != null ? player.host : null, null, "NetPlayerAccessor.GetHost");
        }

        public static Human GetHuman(NetPlayer player)
        {
            return Safe.Run(() => player != null ? player.human : null, null, "NetPlayerAccessor.GetHuman");
        }

        public static bool IsLocalPlayer(NetPlayer player)
        {
            return Safe.Run(() => player != null && player.isLocalPlayer, false, "NetPlayerAccessor.IsLocalPlayer");
        }

        public static uint GetLocalCoopIndex(NetPlayer player)
        {
            return Safe.Run(() => player != null ? player.localCoopIndex : 0u, 0u, "NetPlayerAccessor.GetLocalCoopIndex");
        }

        public static string GetSkinUserId(NetPlayer player)
        {
            return Safe.Run(() => player != null ? player.skinUserId : null, null, "NetPlayerAccessor.GetSkinUserId");
        }

        public static string GetName(NetPlayer player)
        {
            return Safe.Run(() =>
            {
                if (player == null)
                {
                    return null;
                }
                NetHost host = player.host;
                return host != null ? host.name : null;
            }, null, "NetPlayerAccessor.GetName");
        }

        public static uint GetHostId(NetPlayer player)
        {
            return Safe.Run(() =>
            {
                NetHost host = player != null ? player.host : null;
                return host != null ? host.hostId : 0u;
            }, 0u, "NetPlayerAccessor.GetHostId");
        }
    }
}
