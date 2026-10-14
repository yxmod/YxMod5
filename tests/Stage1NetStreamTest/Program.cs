// ===========================================================================
// YxMod5 阶段 1 离线自测 —— NetStream 编码往返（validation.md 自主验收项 3）
// ---------------------------------------------------------------------------
// 直接引用游戏 Assembly-CSharp.dll + UnityEngine.dll，在不启动游戏的情况下
// 验证自定义消息经 NetStream 编码后解码，MsgId / NetId / 字符串 / 子流一致。
// ===========================================================================
using System;
using System.Text;
using Multiplayer;

internal static class Program
{
    private static int Main()
    {
        try
        {
            const uint senderId = 123u;
            const byte permission = 1;
            const byte subType = 5;
            const string text = "YxMod5 同步协议自测";

            // ---- 编码 -----------------------------------------------------
            NetStream stream = NetStream.AllocStream();
            stream.WriteMsgId((NetMsgId)0);       // 自定义 MsgId = 0
            stream.WriteNetId(senderId);
            stream.Write(permission);
            stream.Write(subType);

            byte[] payload = Encoding.UTF8.GetBytes(text);
            NetStream sub = NetStream.AllocStream(payload.Length + 4);
            sub.WriteArray(payload);
            stream.WriteStream(sub);
            sub = sub.Release();

            // ---- 解码 -----------------------------------------------------
            stream.Seek(0);
            NetMsgId msgId = stream.ReadMsgId();
            uint readSender = stream.ReadNetId();
            byte readPermission = stream.ReadByte();
            byte readSubType = stream.ReadByte();
            NetStream readSub = stream.ReadStream(forceIndependent: true);
            byte[] readPayload = readSub != null ? readSub.ReadArray() : null;
            if (readSub != null)
            {
                readSub = readSub.Release();
            }

            string readText = readPayload != null ? Encoding.UTF8.GetString(readPayload) : null;
            stream = stream.Release();

            bool ok = (int)msgId == 0 &&
                      readSender == senderId &&
                      readPermission == permission &&
                      readSubType == subType &&
                      string.Equals(readText, text, StringComparison.Ordinal);

            Console.WriteLine("MsgId=" + (int)msgId +
                              ", NetId=" + readSender +
                              ", permission=" + readPermission +
                              ", subType=" + readSubType +
                              ", payload=" + readText);
            Console.WriteLine(ok ? "PASS" : "FAIL");
            return ok ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.WriteLine("EXCEPTION: " + exception);
            return 2;
        }
    }
}
