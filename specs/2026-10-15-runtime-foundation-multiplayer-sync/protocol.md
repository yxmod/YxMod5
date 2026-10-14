# 阶段 1：多人同步协议设计说明（Protocol）

> 对应 roadmap 任务 `1.7`。本文是 `requirements.md` 第 2 节「多人同步协议设计要点」的落地细化，
> 与代码 `src/mod/src/Net/SyncProtocol.cs`、`SyncChannel.cs` 一一对应。

## 1. 消息类型

| 项 | 值 | 说明 |
| --- | --- | --- |
| 自定义 MsgId | `0` | 游戏 `NetMsgId` 4-bit 空间中当前未占用的值（`1`–`15` 已用）。钩子侧自行维护该常量，不依赖游戏枚举扩展。 |

## 2. 消息编码（沿用游戏 NetStream 位级序列化）

自定义消息在 4-bit `MsgId(=0)` 之后按如下布局写入：

| 字段 | 编码 | 说明 |
| --- | --- | --- |
| `senderId` | `WriteNetId` / `ReadNetId` | 发送者 host id；`0` = 房主 |
| `permission` | `Write(byte)` / `ReadByte` | 权限标记：`0` 普通 / `1` 房主 / `2` 授权者 |
| `subType` | `Write(byte)` / `ReadByte` | 子消息类型（见下） |
| `payload` | `WriteStream` / `ReadStream` | 消息体，NetStream 子流；子流内为 `WriteArray`/`ReadArray` 字节数组 |

### 子消息类型（阶段 1 最小闭环）

| subType | 方向 | 含义 |
| --- | --- | --- |
| `1` Ping | 客户端 → 房主 | 连通性自测 |
| `2` Pong | 房主 → 客户端 | Ping 应答 |
| `3` Request | 客户端 → 房主 | 客户端请求（request） |
| `4` Broadcast | 房主 → 全体 | 房主校验后的广播 |
| `5` Text | 双向 | 最小可读文本（真机观测用） |

## 3. 收发范式（房主权威模型）

- **客户端 → 房主**：`SendReliableToServer`，仅发请求，不直接改全局状态。
- **房主 → 全体 / 指定**：房主校验（`senderId` / `permission`）后 `SendReliable` 广播。
  - Ping → 房主回 Pong（按 `senderId` 解析目标）。
  - Request → 校验 `permission` 为房主/授权后，`Broadcast` 原样广播（示例，后续阶段扩展具体业务）。
- **本地（单机）**：`NetGame.isLocal` 时无需网络，`DispatchLocal` 直接入队分发。
- **失败兜底**：非法 `senderId`、无权限、未知 `subType` 一律丢弃并记录日志，不抛异常、不影响游戏网络层。

## 4. 接收分流（自研钩子）

游戏 `NetGame.OnServerReceive` / `OnClientReceive` 对未知 `MsgId` 会静默忽略，故在原生启动层对这两个方法做地址级自研钩子：

1. 原生层用 `mono_compile_method` 取得原方法与 `SyncChannel.NativeOnServerReceive` / `NativeOnClientReceive` 的原生代码地址；
2. 在原方法入口写 5 字节相对跳转（`E9 rel32`）到汇编桩；
3. 汇编桩调用 C++ 分发器，分发器经 `mono_runtime_invoke` 调用托管分流方法；
4. 托管分流方法读 `MsgId`：
   - 非自定义（`!= 0`）：回退流位置并返回 `0`，汇编桩以原始状态跳回 trampoline 继续原逻辑；
   - 自定义（`== 0`）：解析消息并入队（线程安全），返回 `1`。
5. 入站消息由主线程 `SyncChannel.Drain()` 分发到按 `subType` 注册的处理器。

### 线程模型

- 接收可能发生在游戏网络工作线程：托管分流方法只做「位级解析 + 入队」，不触碰 Unity API。
- 发送与分发在主线程进行（`NetStream` / `NetGame` 发送路径线程安全）。

## 5. 实现文件

| 文件 | 职责 |
| --- | --- |
| `src/mod/src/Net/SyncProtocol.cs` | 消息 id / 权限 / 子类型常量与 `Message` 结构 |
| `src/mod/src/Net/SyncChannel.cs` | 收发、注册、分发、自测 |
| `src/launcher/src/sync_hook.h/.cpp` | 原生地址级钩子（Win32） |
| `src/launcher/src/sync_hooks_x86.asm` | 汇编桩（x86） |
