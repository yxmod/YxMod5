# 阶段 1：自研运行时基础与多人同步设计 — 需求文档（Requirements）

> - 所属分支：`feat/stage-1-runtime-foundation-multiplayer-sync`
> - 对应 roadmap：阶段 1，任务 `1.1`–`1.8`
> - 编写依据：`specs/mission.md`、`specs/tech-stack.md`
> - 参考源码：`F:\VS项目\人类一败涂地\Assembly-CSharp(原始)`（`Multiplayer/` 目录反编译源码）

## 1. 功能范围（In Scope）

**完整覆盖 roadmap 阶段 1 的任务 `1.1`–`1.8`**：

- `1.1` 反射访问器核心：`MemberInfo` 缓存 + 委托化调用。
- `1.2` 封装 `Human` 访问器（状态、刚体、扩展属性）。
- `1.3` 封装 `NetGame` / `NetPlayer` 访问器（房主/客户端/本地玩家）。
- `1.4` 空引用/异常安全防护层。
- `1.5` 配置系统（INI 读写 + `PlayerPrefs` 封装）。
- `1.6` 热键系统（单键/组合键/数字编号）。
- `1.7` 设计多人同步协议：自定义消息类型、消息编码、房主权威模型、权限标记。
- `1.8` 实现同步协议收发基础（自定义消息类型的最小收发通道）。

### 范围特化说明

- **`1.8` 仅做最小收发通道**：本阶段完成「自定义消息类型」的发送与接收闭环（供 `1.7` 协议消息使用），不实现完整聊天指令框架（对应 roadmap `5.4`，届时统一）。
- **`1.5` 配置落地目录**：在游戏根目录创建 `YxMod5` 文件夹，本模组产生的文件（配置文件、日志等）统一放入该目录（详见第 2 节）。
- **`1.6` 仅框架 + 少量示例热键**：本阶段只交付热键注册/触发框架与少量示例；具体功能键位由后续功能阶段各自注册。
- **`1.7` 采用自定义消息类型，不走聊天通道**：不使用 `NetMsgId.Chat`（值 `13`）及 `NetChat` 通道，另立模组专用消息类型（详见第 2 节）。

## 2. 技术决策

| 决策点 | 决定 | 说明 |
| --- | --- | --- |
| 反射访问器委托化 | **混合**：优先 `Delegate.CreateDelegate`；对热路径/无法直接建委托的成员按需使用 `System.Reflection.Emit` | 避免每帧 `Invoke` 开销，见 `tech-stack.md` 自研组件清单 1 |
| 访问器字段策略 | **以当前游戏版本反射到的真实字段为准，文档只写接口语义** | 字段名/类型随游戏版本集中封装在访问器层，版本更新只改这一层 |
| 配置存储 | 自研 INI 读写（UTF-8 无 BOM）+ `PlayerPrefs` 封装 | INI 与 PlayerPrefs 分层：热键/显示等进 INI，功能状态进 PlayerPrefs |
| 配置文件目录 | `<游戏根目录>\YxMod5\` | 游戏根目录下创建 `YxMod5` 文件夹，模组产生的文件（INI、日志等）统一放入 |
| PlayerPrefs 隔离 | 按 Steam 作用域隔离（沿用 Unity 默认 Company/Product 命名空间） | PlayerPrefs 由 Unity 写注册表，不额外改目录 |
| 热键系统 | 自研，支持单键/组合键/数字编号；v1 只交付框架 + 注册 API + 少量示例 | 具体键位由后续功能阶段注册 |
| 消息类型 | **自定义消息类型**（模组专用 `MsgId`），**不走 `NetChat`/`Chat(13)` 聊天通道** | 参照反编译源码 `Multiplayer.NetMsgId`，在 4-bit 空间中预留未占用值（当前 `0` 未被使用，实现时以真机程序集确认） |
| 消息编码 | 沿用游戏的 `NetStream` 位级序列化 | `WriteMsgId/ReadMsgId`（4 bit）、`WriteNetId/ReadNetId`、`Write(string)/ReadString`（UTF-8）、`WriteArray/ReadArray`、`WriteStream/ReadStream`，与游戏现有消息同构 |
| 消息发送 | 通过反射调用 `NetGame.BeginMessage(id)` + `SendReliable(host, stream)` / `SendReliableToServer(stream)` | 复用游戏传输层 `NetTransport`，不自行建连接 |
| 消息接收 | 拦截 `NetGame.OnServerReceive` / `OnClientReceive` 分发，在 `ReadMsgId` 后、游戏 `switch` 前分流自定义消息 id | 游戏 `switch` 对未知 id 会静默忽略，故必须自研拦截（见第 4 节风险） |
| 权威模型 | **房主权威**：客户端只发请求（request），房主校验（`senderId` / `permission` 标记）后广播（broadcast） | 参照 `NetGame.SendChatMessage` / `OnReceiveChatServer` 的 client→server→广播范式，但走独立消息类型 |
| 第三方依赖 | **禁用**（无 Harmony / Cecil / MonoMod / NuGet） | 仅 `System.Reflection`、`System.Reflection.Emit`、Windows SDK |

### 多人同步协议设计要点（对应 `1.7`）

- **消息头**：`MsgId`（自定义类型）+ `senderId`（`NetId`，标识发送者）+ `permission`（权限标记，如房主/授权者/普通）。
- **消息体**：协议消息体用 `NetStream` 子流（`WriteStream/ReadStream`）承载，便于与游戏 `Container(12)` 的「子流」机制对齐。
- **收发范式**：
  - 客户端 → 房主：`SendReliableToServer`，仅请求，不直接改全局状态。
  - 房主 → 全体/指定：房主校验后 `SendReliable` 广播（向其他 `readyclients` 转发，模式同 `OnReceiveChatServer`）。
  - 本地（单机）：`NetGame.isLocal` 时无需网络，直接本地生效。
- **失败兜底**：非法 `senderId`、无权限、未知 `MsgId` 一律丢弃并记录日志，不抛异常、不影响游戏网络层。

## 3. 业务背景

阶段 0 已打通「启动 → 注入 → C# 入口 → 模组宿主」链路（M0 可注入）。本阶段构建**自研运行时基础**（反射访问器、安全防护、配置、热键）与**多人同步协议设计**，是后续所有功能阶段（能力、分身、聊天、皮肤、HUD、娱乐、管理）的共同地基：

- 反射访问器层让功能模块通过**接口语义**读写 `Human` / `NetGame` / `NetPlayer` 等私有成员，把游戏版本差异收敛到单一层次（`mission.md` 核心原则 4）。
- 多人同步协议**跨切面先行设计**（`roadmap.md` 说明：同步协议、房主/客户端权威、权限校验在阶段 1 完成设计，随后贯穿每个功能阶段），避免「单机可用、多人崩溃」的半成品（`mission.md` 核心原则 3）。
- 消息协议参照游戏原生实现（`NetMsgId` / `NetStream` / `NetGame` 收发分发），保证与游戏传输层同构、可复用其可靠/不可靠通道，且**不引入任何第三方库**（`mission.md` 核心原则 1）。

> 详细项目使命与范围见 `specs/mission.md`，技术栈全貌见 `specs/tech-stack.md`。

## 4. 非目标（Out of Scope）

- 完整聊天指令框架（roadmap `5.4`）。
- IMGUI UI 框架（roadmap 阶段 2）。
- 任何具体功能模块（能力、分身、聊天、皮肤、HUD、娱乐、管理等）。
- 皮肤/动画、AI 机器人、脚本管理器等 YxModDll 全量功能（`mission.md` Out of Scope）。
- 任何第三方方法注入/补丁库。

## 5. 关键风险

| 风险 | 应对 |
| --- | --- |
| 游戏 `switch` 对未知 `MsgId` 静默忽略，接收侧无法收到自定义消息 | `1.8` 在原生启动层对 `Multiplayer.NetGame::OnServerReceive` / `OnClientReceive` 做地址级自研钩子，分流自定义消息 id；这是本阶段最高风险项，先离线验证编码往返，再真机验证收发 |
| 消息 id 占用冲突（`NetMsgId` 4-bit 空间 `1`–`15` 已用） | 预留未占用值（当前 `0`），实现时以真机程序集确认；钩子侧自行维护 id 常量，不依赖游戏枚举扩展 |
| 反射访问随游戏版本更新失效 | 访问器层集中封装，版本更新只改该层；所有访问做空引用/异常防护 |
| 配置/日志目录变化（由阶段 0 的根目录 `YxMod5.log` 迁至 `YxMod5\` 子目录） | 统一「所有模组文件放 `YxMod5\`」约定；旧日志路径不再写入，验收时一并确认 |
