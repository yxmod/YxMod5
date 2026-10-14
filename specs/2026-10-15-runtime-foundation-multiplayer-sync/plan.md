# 阶段 1：自研运行时基础与多人同步设计 — 开发计划（Plan）

> - 所属分支：`feat/stage-1-runtime-foundation-multiplayer-sync`
> - 对应 roadmap：阶段 1，任务 `1.1`–`1.8`
> - 编写依据：`specs/mission.md`、`specs/tech-stack.md`

## 分组总览

按功能模块分为 4 个任务组，顺序执行：

| 组 | 名称 | 覆盖 roadmap 任务 | 阶段目标 |
| --- | --- | --- | --- |
| A | 反射访问器核心与安全防护 | 1.1、1.4 | 可安全读写游戏私有成员 |
| B | Human / NetGame / NetPlayer 访问器 | 1.2、1.3 | 玩家与多人状态可用 |
| C | 配置系统与热键系统 | 1.5、1.6 | 可持久化、可绑键 |
| D | 多人同步协议设计与收发 | 1.7、1.8 | 自定义消息最小收发闭环 |

依赖顺序：**A → B → C → D**（A 为地基；D 依赖 A/B 提供的 `NetGame`/`NetPlayer` 访问能力）。

---

## A 组：反射访问器核心与安全防护

### 任务

- `1.1` 实现反射访问器核心：`MemberInfo`（`Type/FieldInfo/PropertyInfo/MethodInfo`）缓存 + 委托化调用。
- `1.4` 实现空引用/异常安全防护层：所有反射访问统一走防护，单点故障不拖垮模组。

### 交付物

- 访问器核心 API：按类型/成员名解析并缓存，返回可复用委托（优先 `Delegate.CreateDelegate`，必要时 `System.Reflection.Emit`）。
- 统一防护层：目标为 `null`、成员缺失、类型不匹配、调用异常时均安全降级（返回默认值/记录日志）。

### 验收点

- 对测试类型做读写往返，委托调用与 `Invoke` 结果一致且无每帧反射开销。
- 对不存在的成员/空对象/类型错误做防护，模组不崩溃、日志有记录。

---

## B 组：Human / NetGame / NetPlayer 访问器

### 任务

- `1.2` 封装 `Human` 访问器（状态、刚体、扩展属性），只暴露接口语义（不硬编码字段名到业务层）。
- `1.3` 封装 `NetGame` / `NetPlayer` 访问器（房主/客户端/本地玩家、玩家列表、`netId`/`host`/`human` 等）。

### 交付物

- `HumanAccessor`：状态读写、刚体读写、扩展属性读写。
- `NetGameAccessor`：`isServer` / `isClient` / `isLocal`，`server` / `local` / `allclients` / `readyclients` / `players`，`BeginMessage` / `SendReliable` / `SendReliableToServer` 等收发入口。
- `NetPlayerAccessor`：`netId` / `host` / `human` / `isLocalPlayer` / `localCoopIndex` 等。

### 验收点

- 真机（`human.exe`）下读取本地 `Human` 状态/刚体不崩溃，空引用防护生效。
- 多人场景下能区分房主/客户端/本地玩家并遍历玩家列表。

---

## C 组：配置系统与热键系统

### 任务

- `1.5` 实现配置系统：自研 INI 读写（UTF-8 无 BOM）+ `PlayerPrefs` 封装；落地目录 `<游戏根目录>\YxMod5\`。
- `1.6` 实现热键系统：单键/组合键/数字编号，注册/触发框架 + 少量示例热键。

### 交付物

- 配置系统：`YxMod5\` 目录自动创建；INI 读写与 `PlayerPrefs` 封装按分层约定（热键/显示进 INI，功能状态进 PlayerPrefs）。
- 热键系统：注册 API（键位、回调、是否组合键/数字编号）、`Update` 轮询触发、少量示例热键。

### 验收点

- 配置写入后重启游戏可读回（持久化生效）；`YxMod5\` 目录及配置文件存在。
- 注册示例热键后，按下/组合键触发回调，日志可观测；非法键位不崩溃。

---

## D 组：多人同步协议设计与收发

### 任务

- `1.7` 设计多人同步协议：自定义消息类型、消息编码、房主权威模型、权限标记，产出协议设计说明（可写入本阶段需求文档或独立 `protocol.md`）。
- `1.8` 实现同步协议收发基础：自定义消息类型的最小收发通道（发送走反射调用 `NetGame.BeginMessage` + `SendReliable/SendReliableToServer`；接收走自研钩子分流）。

### 交付物

- 协议设计：消息头（`MsgId` + `senderId` + `permission`）、消息体（`NetStream` 子流）、收发范式（客户端请求 → 房主校验 → 广播）、非法消息兜底。
- 最小收发通道：自定义 `MsgId` 的发送与接收闭环，接收侧在 `OnServerReceive` / `OnClientReceive` 分流。

### 验收点

- 离线：自定义消息经 `NetStream` 编码/解码往返一致（`MsgId`/`NetId`/字符串/子流）。
- 真机：单人（本地）场景收发闭环不崩溃；多人（房主+客户端）场景自定义消息能按房主权威模型广播到其他客户端（维护者验收，见 `validation.md`）。
