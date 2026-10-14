// ===========================================================================
// YxMod5 C# 模组层 —— NetGame 访问器（roadmap 1.3 / 1.8 发送）
// ---------------------------------------------------------------------------
// 封装对 Multiplayer.NetGame 的访问：
//   * 房主 / 客户端 / 本地玩家状态；
//   * 玩家 / 客户端列表遍历；
//   * 消息收发入口（BeginMessage / SendReliable / SendReliableToServer 等）。
//
// 发送统一走反射（Delegate.CreateDelegate 委托化），符合 roadmap 1.8：
//   "发送走反射调用 NetGame.BeginMessage + SendReliable/SendReliableToServer"。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using Multiplayer;

namespace YxMod5.Runtime.Accessors
{
    /// <summary>NetGame 访问器。</summary>
    public static class NetGameAccessor
    {
        // 发送方法委托（委托化，避免每次发送 Invoke）。
        private static Func<NetMsgId, NetStream> _beginMessage;
        private static Action<NetGame, NetHost, NetStream> _sendReliable;
        private static Action<NetGame, NetStream> _sendReliableToServer;
        private static Action<NetGame, NetHost, NetStream, int> _sendUnreliable;

        private static bool _sendDelegatesReady;
        private static bool _sendDelegatesFailed;

        // ---- 身份状态 -----------------------------------------------------

        public static bool IsServer => Safe.Run(() => NetGame.isServer, false, "NetGameAccessor.IsServer");
        public static bool IsClient => Safe.Run(() => NetGame.isClient, false, "NetGameAccessor.IsClient");
        public static bool IsLocal => Safe.Run(() => NetGame.isLocal, false, "NetGameAccessor.IsLocal");

        public static NetGame Instance => Safe.Run(() => NetGame.instance, null, "NetGameAccessor.Instance");

        public static NetHost Server => Safe.Run(() => NetGame.instance != null ? NetGame.instance.server : null,
            null, "NetGameAccessor.Server");

        public static NetHost Local => Safe.Run(() => NetGame.instance != null ? NetGame.instance.local : null,
            null, "NetGameAccessor.Local");

        public static List<NetHost> AllClients => Safe.Run(() =>
            NetGame.instance != null ? NetGame.instance.allclients : null, null, "NetGameAccessor.AllClients");

        public static List<NetHost> ReadyClients => Safe.Run(() =>
            NetGame.instance != null ? NetGame.instance.readyclients : null, null, "NetGameAccessor.ReadyClients");

        public static List<NetPlayer> Players => Safe.Run(() =>
            NetGame.instance != null ? NetGame.instance.players : null, null, "NetGameAccessor.Players");

        /// <summary>当前本地玩家（遍历玩家列表找 isLocalPlayer）。</summary>
        public static NetPlayer LocalPlayer => Safe.Run(() =>
        {
            List<NetPlayer> players = Players;
            if (players == null)
            {
                return null;
            }
            for (int i = 0; i < players.Count; i++)
            {
                NetPlayer player = players[i];
                if (player != null && player.isLocalPlayer)
                {
                    return player;
                }
            }
            return null;
        }, null, "NetGameAccessor.LocalPlayer");

        /// <summary>当前本地玩家主机（本地玩家所在 host）。</summary>
        public static NetHost LocalHost => Safe.Run(() =>
        {
            NetPlayer local = LocalPlayer;
            return local != null ? local.host : null;
        }, null, "NetGameAccessor.LocalHost");

        // ---- 消息发送 -----------------------------------------------------

        /// <summary>初始化发送委托（委托化反射调用）。</summary>
        private static void EnsureSendDelegates()
        {
            if (_sendDelegatesReady || _sendDelegatesFailed)
            {
                return;
            }

            _sendDelegatesReady = true;

            try
            {
                MethodInfo begin = Reflector.CacheMethod(typeof(NetGame), "BeginMessage", 1,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (begin != null)
                {
                    _beginMessage = (Func<NetMsgId, NetStream>)
                        Delegate.CreateDelegate(typeof(Func<NetMsgId, NetStream>), begin);
                }

                MethodInfo reliable = Reflector.CacheMethod(typeof(NetGame), "SendReliable", 2,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (reliable != null)
                {
                    _sendReliable = (Action<NetGame, NetHost, NetStream>)
                        Delegate.CreateDelegate(typeof(Action<NetGame, NetHost, NetStream>), reliable);
                }

                MethodInfo reliableToServer = Reflector.CacheMethod(typeof(NetGame), "SendReliableToServer", 1,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (reliableToServer != null)
                {
                    _sendReliableToServer = (Action<NetGame, NetStream>)
                        Delegate.CreateDelegate(typeof(Action<NetGame, NetStream>), reliableToServer);
                }

                MethodInfo unreliable = Reflector.CacheMethod(typeof(NetGame), "SendUnreliable", 3,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (unreliable != null)
                {
                    _sendUnreliable = (Action<NetGame, NetHost, NetStream, int>)
                        Delegate.CreateDelegate(typeof(Action<NetGame, NetHost, NetStream, int>), unreliable);
                }
            }
            catch (Exception exception)
            {
                _sendDelegatesFailed = true;
                Logger.Warn("NetGameAccessor 初始化发送委托失败 -> " + exception.Message);
            }
        }

        /// <summary>创建消息流（MsgId 已写入流头部）。支持自定义 id（roadmap 1.7）。</summary>
        public static NetStream BeginMessage(NetMsgId id)
        {
            EnsureSendDelegates();

            return Safe.Run(() =>
            {
                if (_beginMessage != null)
                {
                    return _beginMessage(id);
                }
                // 委托化失败时回退到直接静态调用。
                return NetGame.BeginMessage(id);
            }, null, "NetGameAccessor.BeginMessage");
        }

        /// <summary>可靠发送到指定主机。</summary>
        public static void SendReliable(NetHost host, NetStream stream)
        {
            if (stream == null)
            {
                return;
            }

            EnsureSendDelegates();

            Safe.Run(() =>
            {
                NetGame instance = NetGame.instance;
                if (instance == null || host == null)
                {
                    return;
                }
                if (_sendReliable != null)
                {
                    _sendReliable(instance, host, stream);
                }
                else
                {
                    instance.SendReliable(host, stream);
                }
            }, "NetGameAccessor.SendReliable");
        }

        /// <summary>可靠发送到房主（客户端使用）。</summary>
        public static void SendReliableToServer(NetStream stream)
        {
            if (stream == null)
            {
                return;
            }

            EnsureSendDelegates();

            Safe.Run(() =>
            {
                NetGame instance = NetGame.instance;
                if (instance == null)
                {
                    return;
                }
                if (_sendReliableToServer != null)
                {
                    _sendReliableToServer(instance, stream);
                }
                else
                {
                    instance.SendReliableToServer(stream);
                }
            }, "NetGameAccessor.SendReliableToServer");
        }

        /// <summary>不可靠发送到指定主机。</summary>
        public static void SendUnreliable(NetHost host, NetStream stream, int expectedContainerId = -1)
        {
            if (stream == null)
            {
                return;
            }

            EnsureSendDelegates();

            Safe.Run(() =>
            {
                NetGame instance = NetGame.instance;
                if (instance == null || host == null)
                {
                    return;
                }
                if (_sendUnreliable != null)
                {
                    _sendUnreliable(instance, host, stream, expectedContainerId);
                }
                else
                {
                    instance.SendUnreliable(host, stream, expectedContainerId);
                }
            }, "NetGameAccessor.SendUnreliable");
        }
    }
}
