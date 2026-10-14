// ===========================================================================
// YxMod5 C# 模组层 —— 自定义消息收发通道（roadmap 1.8）
// ---------------------------------------------------------------------------
// 发送：反射调用 NetGame.BeginMessage + SendReliable / SendReliableToServer。
// 接收：原生启动层对 NetGame.OnServerReceive / OnClientReceive 做地址级钩子，
//       在本方法 NativeOnServerReceive / NativeOnClientReceive 中分流自定义
//       消息 id（MsgId=0），非自定义消息返回 false 交给原方法继续处理。
//
// 线程模型：
//   * 接收可能发生在游戏网络工作线程；本层只做「位级解析 + 入队」，
//     不触碰 Unity API。主线程由 YxMod.Update 调用 Drain() 分发。
//   * 发送由主线程调用（NetStream / NetGame 发送路径线程安全）。
// ===========================================================================
using System;
using System.Collections.Concurrent;
using Multiplayer;
using YxMod5.Runtime;
using YxMod5.Runtime.Accessors;

namespace YxMod5.Net
{
    /// <summary>自定义消息收发通道。</summary>
    public static class SyncChannel
    {
        /// <summary>入站消息队列（线程安全）。</summary>
        private static readonly ConcurrentQueue<SyncProtocol.Message> Inbound =
            new ConcurrentQueue<SyncProtocol.Message>();

        /// <summary>子消息类型 -> 处理器。</summary>
        private static readonly ConcurrentDictionary<byte, Action<SyncProtocol.Message>> Handlers =
            new ConcurrentDictionary<byte, Action<SyncProtocol.Message>>();

        private static volatile bool _initialized;

        // ===================================================================
        // 注册 / 分发
        // ===================================================================

        /// <summary>初始化（幂等）。</summary>
        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;

            // 内置子消息处理（均在主线程 Drain 中执行，不触碰接收工作线程）。
            RegisterHandler(SyncProtocol.SubType.Ping, OnPing);
            RegisterHandler(SyncProtocol.SubType.Request, OnRequest);

            Logger.Info("SyncChannel 已初始化（自定义 MsgId=" + SyncProtocol.CustomMsgId + "）");
        }

        private static void OnPing(SyncProtocol.Message message)
        {
            // 房主收到 Ping 后回 Pong（发送者由 senderId 解析）。
            if (NetGameAccessor.IsServer)
            {
                NetHost sender = FindHost(message.SenderId);
                if (sender != null)
                {
                    SendToHost(sender, SyncProtocol.PermissionHost, SyncProtocol.SubType.Pong, message.Payload);
                }
            }
            Logger.Info("SyncChannel 收到 Ping（sender=" + message.SenderId + "）");
        }

        private static void OnRequest(SyncProtocol.Message message)
        {
            // 房主权威：客户端请求 → 房主校验 → 广播。
            if (NetGameAccessor.IsServer)
            {
                if (IsAuthorized(message))
                {
                    Broadcast(SyncProtocol.PermissionHost, SyncProtocol.SubType.Broadcast, message.Payload);
                }
                else
                {
                    Logger.Warn("SyncChannel 拒绝未授权请求（sender=" + message.SenderId + "）");
                }
            }
        }

        private static NetHost FindHost(uint hostId)
        {
            if (hostId == 0u)
            {
                return NetGameAccessor.Server;
            }
            var clients = NetGameAccessor.ReadyClients;
            if (clients == null)
            {
                return null;
            }
            for (int i = 0; i < clients.Count; i++)
            {
                if (clients[i] != null && clients[i].hostId == hostId)
                {
                    return clients[i];
                }
            }
            return null;
        }

        /// <summary>注册子消息处理器。</summary>
        public static bool RegisterHandler(byte subType, Action<SyncProtocol.Message> handler)
        {
            if (handler == null)
            {
                return false;
            }
            Handlers[subType] = handler;
            return true;
        }

        /// <summary>注销子消息处理器。</summary>
        public static bool UnregisterHandler(byte subType)
        {
            return Handlers.TryRemove(subType, out _);
        }

        /// <summary>主线程每帧调用：分发入站消息。</summary>
        public static void Drain()
        {
            int budget = 256;
            while (budget-- > 0 && Inbound.TryDequeue(out SyncProtocol.Message message))
            {
                if (Handlers.TryGetValue(message.SubType, out Action<SyncProtocol.Message> handler))
                {
                    Safe.Run(() => handler(message),
                        "SyncChannel.Drain(subType=" + message.SubType + ")");
                }
                else
                {
                    Logger.Warn("SyncChannel 丢弃未知子消息类型：" + message.SubType +
                                "（sender=" + message.SenderId + "）");
                }
            }
        }

        // ===================================================================
        // 发送（反射调用 NetGame 传输层）
        // ===================================================================

        /// <summary>构建自定义消息流（MsgId + 头部 + 子类型 + 子流 payload）。</summary>
        public static NetStream BuildMessage(uint senderId, byte permission, byte subType, byte[] payload)
        {
            NetStream stream = NetGameAccessor.BeginMessage(SyncProtocol.CustomNetMsgId);
            if (stream == null)
            {
                return null;
            }

            stream.WriteNetId(senderId);
            stream.Write(permission);
            stream.Write(subType);

            NetStream sub = null;
            try
            {
                if (payload != null && payload.Length > 0)
                {
                    sub = NetStream.AllocStream(payload.Length + 4);
                    sub.WriteArray(payload);
                    stream.WriteStream(sub);
                }
                else
                {
                    stream.WriteStream(null);
                }
            }
            finally
            {
                if (sub != null)
                {
                    sub = sub.Release();
                }
            }

            return stream;
        }

        /// <summary>发送到指定主机（房主 → 指定客户端）。</summary>
        public static bool SendToHost(Multiplayer.NetHost host, byte permission, byte subType, byte[] payload)
        {
            if (host == null)
            {
                return false;
            }

            NetStream stream = BuildMessage(CurrentSenderId(), permission, subType, payload);
            if (stream == null)
            {
                return false;
            }

            try
            {
                NetGameAccessor.SendReliable(host, stream);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warn("SyncChannel.SendToHost 失败 -> " + exception.Message);
                return false;
            }
            finally
            {
                stream = stream.Release();
            }
        }

        /// <summary>发送到房主（客户端 → 房主请求）。</summary>
        public static bool SendToServer(byte permission, byte subType, byte[] payload)
        {
            NetStream stream = BuildMessage(CurrentSenderId(), permission, subType, payload);
            if (stream == null)
            {
                return false;
            }

            try
            {
                NetGameAccessor.SendReliableToServer(stream);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warn("SyncChannel.SendToServer 失败 -> " + exception.Message);
                return false;
            }
            finally
            {
                stream = stream.Release();
            }
        }

        /// <summary>房主广播到所有就绪客户端（不含自己）。</summary>
        public static int Broadcast(byte permission, byte subType, byte[] payload)
        {
            if (!NetGameAccessor.IsServer)
            {
                Logger.Warn("SyncChannel.Broadcast 仅在房主侧有效");
                return 0;
            }

            var clients = NetGameAccessor.ReadyClients;
            if (clients == null)
            {
                return 0;
            }

            int sent = 0;
            for (int i = 0; i < clients.Count; i++)
            {
                if (clients[i] != null && !clients[i].isLocal)
                {
                    if (SendToHost(clients[i], permission, subType, payload))
                    {
                        sent++;
                    }
                }
            }
            return sent;
        }

        /// <summary>本地（单机）直接生效：构造消息并立即分发到处理器。</summary>
        public static void DispatchLocal(byte permission, byte subType, byte[] payload)
        {
            SyncProtocol.Message message = new SyncProtocol.Message
            {
                SenderId = CurrentSenderId(),
                Permission = permission,
                SubType = subType,
                Payload = payload
            };
            Inbound.Enqueue(message);
        }

        /// <summary>发送文本消息（真机可观测的最小 payload）。</summary>
        public static bool SendText(string text)
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
            if (NetGameAccessor.IsServer)
            {
                Broadcast(SyncProtocol.PermissionHost, SyncProtocol.SubType.Text, payload);
                return true;
            }
            return SendToServer(SyncProtocol.PermissionNormal, SyncProtocol.SubType.Text, payload);
        }

        private static uint CurrentSenderId()
        {
            NetHost local = NetGameAccessor.LocalHost;
            return local != null ? local.hostId : 0u;
        }

        // ===================================================================
        // 接收（由原生启动层钩子调用；返回 true 表示已消费自定义消息）
        // ===================================================================

        /// <summary>
        /// 房主侧接收分流（原生钩子调用）。返回 1 表示消费了自定义消息，
        /// 否则调用方应继续交给原 OnServerReceive 处理。
        /// </summary>
        public static int NativeOnServerReceive(NetHost client, NetStream stream)
        {
            return TryDispatch(stream) ? 1 : 0;
        }

        /// <summary>
        /// 客户端侧接收分流（原生钩子调用）。返回 1 表示消费了自定义消息，
        /// 否则调用方应继续交给原 OnClientReceive 处理。
        /// </summary>
        public static int NativeOnClientReceive(object connection, NetStream stream)
        {
            return TryDispatch(stream) ? 1 : 0;
        }

        private static bool TryDispatch(NetStream stream)
        {
            try
            {
                if (stream == null)
                {
                    return false;
                }

                // 读取 MsgId；非自定义消息回退位置交给原方法，
                // 自定义消息则在 MsgId 之后继续解析。
                int position = stream.position;
                NetMsgId msgId = stream.ReadMsgId();

                if ((int)msgId != SyncProtocol.CustomMsgId)
                {
                    stream.Seek(position); // 回退，交还原方法继续读取。
                    return false;
                }

                // 消费自定义消息（仅解析 + 入队；分发在 Drain 主线程进行）。
                SyncProtocol.Message message = ReadMessage(stream);
                Inbound.Enqueue(message);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warn("SyncChannel.TryDispatch 失败 -> " + exception.Message);
                return false;
            }
        }

        private static SyncProtocol.Message ReadMessage(NetStream stream)
        {
            uint senderId = stream.ReadNetId();
            byte permission = stream.ReadByte();
            byte subType = stream.ReadByte();

            byte[] payload = null;
            NetStream sub = stream.ReadStream(forceIndependent: true);
            try
            {
                if (sub != null)
                {
                    payload = sub.ReadArray();
                }
            }
            finally
            {
                if (sub != null)
                {
                    sub = sub.Release();
                }
            }

            return new SyncProtocol.Message
            {
                SenderId = senderId,
                Permission = permission,
                SubType = subType,
                Payload = payload
            };
        }

        private static bool IsAuthorized(SyncProtocol.Message message)
        {
            return message.Permission == SyncProtocol.PermissionHost ||
                   message.Permission == SyncProtocol.PermissionAuthorized;
        }

        // ===================================================================
        // 自测（编码往返，roadmap 阶段 1 验收项 3）
        // ===================================================================

        /// <summary>离线自测：编码 → 解码往返一致（可在游戏内启动时调用）。</summary>
        public static bool RunSelfTest()
        {
            try
            {
                const string text = "YxMod5 同步协议自测";
                byte[] payload = System.Text.Encoding.UTF8.GetBytes(text);

                NetStream encoded = BuildMessage(123u, SyncProtocol.PermissionHost, SyncProtocol.SubType.Text, payload);
                if (encoded == null)
                {
                    Logger.Error("SyncChannel 自测失败：BuildMessage 返回 null");
                    return false;
                }

                try
                {
                    // 从流头开始：先读 MsgId，再读消息体。
                    encoded.Seek(0);
                    NetMsgId msgId = encoded.ReadMsgId();
                    if ((int)msgId != SyncProtocol.CustomMsgId)
                    {
                        Logger.Error("SyncChannel 自测失败：MsgId 往返不一致");
                        return false;
                    }

                    SyncProtocol.Message decoded = ReadMessage(encoded);
                    string decodedText = decoded.Payload != null
                        ? System.Text.Encoding.UTF8.GetString(decoded.Payload)
                        : null;

                    bool ok = decoded.SenderId == 123u &&
                              decoded.Permission == SyncProtocol.PermissionHost &&
                              decoded.SubType == SyncProtocol.SubType.Text &&
                              string.Equals(decodedText, text, StringComparison.Ordinal);

                    Logger.Info("SyncChannel 自测 " + (ok ? "通过" : "失败") +
                                "（sender=" + decoded.SenderId +
                                ", permission=" + decoded.Permission +
                                ", subType=" + decoded.SubType +
                                ", payload=" + decodedText + "）");
                    return ok;
                }
                finally
                {
                    encoded = encoded.Release();
                }
            }
            catch (Exception exception)
            {
                Logger.Error("SyncChannel 自测异常 -> " + exception);
                return false;
            }
        }
    }
}
