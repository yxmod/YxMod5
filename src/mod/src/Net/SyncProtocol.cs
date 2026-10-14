// ===========================================================================
// YxMod5 C# 模组层 —— 多人同步协议设计（roadmap 1.7）
// ---------------------------------------------------------------------------
// 自定义消息类型（模组专用 MsgId = 0，避开游戏 NetMsgId 1-15 已占用值）。
// 与游戏原生消息同构：沿用 NetStream 位级序列化，复用 NetGame 传输层。
//
// 消息布局（自定义 MsgId 之后）：
//   senderId    NetId（WriteNetId / ReadNetId）    标识发送者（0 = 房主）
//   permission  byte  （权限标记）                  0 普通 / 1 房主 / 2 授权
//   subType     byte  （子消息类型）                见 SyncSubType
//   payload     NetStream 子流（WriteStream / ReadStream）
//
// 权威模型：房主权威。客户端只发请求（request），房主校验 senderId/permission
// 后广播（broadcast）。本地（isLocal）无需网络，直接本地生效。
// 非法 senderId / 无权限 / 未知 subType 一律丢弃并记录日志，不抛异常。
// ===========================================================================
using Multiplayer;

namespace YxMod5.Net
{
    /// <summary>模组自定义消息类型（NetMsgId 4-bit 空间中的未占用值 0）。</summary>
    public static class SyncProtocol
    {
        /// <summary>自定义消息 MsgId。</summary>
        public const int CustomMsgId = 0;

        public static NetMsgId CustomNetMsgId => (NetMsgId)CustomMsgId;

        // 权限标记
        public const byte PermissionNormal = 0;
        public const byte PermissionHost = 1;
        public const byte PermissionAuthorized = 2;

        /// <summary>子消息类型（阶段 1 最小闭环：握手 / 请求 / 广播 / 文本）。</summary>
        public static class SubType
        {
            /// <summary>Ping（连通性自测）。</summary>
            public const byte Ping = 1;

            /// <summary>Pong（Ping 应答）。</summary>
            public const byte Pong = 2;

            /// <summary>客户端 → 房主请求（request，携带任意 payload）。</summary>
            public const byte Request = 3;

            /// <summary>房主 → 全体广播（broadcast）。</summary>
            public const byte Broadcast = 4;

            /// <summary>文本消息（最小可读 payload，便于真机观测）。</summary>
            public const byte Text = 5;
        }

        /// <summary>协议消息（已从流中解析）。</summary>
        public struct Message
        {
            public uint SenderId;
            public byte Permission;
            public byte SubType;
            public byte[] Payload;
        }
    }
}
