// ===========================================================================
// YxMod5 C# 模组层 —— NetHost 访问器（roadmap 1.3 辅助）
// ---------------------------------------------------------------------------
// 封装对 Multiplayer.NetHost 的访问（房主 / 客户端身份、玩家列表等）。
// ===========================================================================
using System.Collections.Generic;
using Multiplayer;

namespace YxMod5.Runtime.Accessors
{
    /// <summary>NetHost 访问器。</summary>
    public static class NetHostAccessor
    {
        public static uint GetHostId(NetHost host)
        {
            return Safe.Run(() => host != null ? host.hostId : 0u, 0u, "NetHostAccessor.GetHostId");
        }

        public static string GetName(NetHost host)
        {
            return Safe.Run(() => host != null ? host.name : null, null, "NetHostAccessor.GetName");
        }

        public static bool IsLocal(NetHost host)
        {
            return Safe.Run(() => host != null && host.isLocal, false, "NetHostAccessor.IsLocal");
        }

        public static bool IsReady(NetHost host)
        {
            return Safe.Run(() => host != null && host.isReady, false, "NetHostAccessor.IsReady");
        }

        public static List<NetPlayer> GetPlayers(NetHost host)
        {
            return Safe.Run(() => host != null ? host.players : null, null, "NetHostAccessor.GetPlayers");
        }

        public static NetPlayer FindPlayer(NetHost host, uint netId)
        {
            return Safe.Run(() => host != null ? host.FindPlayer(netId) : null,
                null, "NetHostAccessor.FindPlayer");
        }
    }
}
