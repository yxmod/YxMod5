# 阶段 0：工程初始化与启动链 — 需求文档（Requirements）

> - 所属分支：`feat/stage-0-engineering-init-launch-chain`
> - 对应 roadmap：阶段 0（里程碑 M0），任务 `0.1`–`0.11`
> - 编写依据：`specs/mission.md`、`specs/tech-stack.md`

## 1. 功能范围（In Scope）

**完整覆盖 roadmap 阶段 0 的任务 `0.1`–`0.11`**：

- `0.1` 建立仓库目录结构（`specs/`、`src/launcher/`、`src/mod/`、`build/`、`release/`）。
- `0.2` 创建原生启动层 C++ 工程（`.vcxproj`，DLL，Win32/x64）。
- `0.3` 实现 AheadLib 转发框架：加载真实系统 DLL 并转发导出。
- `0.4` 实现宿主名：默认 `winhttp.dll`。
- `0.5` 实现进程名校验（仅对 `human.exe` 生效）。
- `0.6` 实现 Mono 运行时加载：获取 `mono-2.0-bdwgc.dll` 及 `mono_*` 函数地址。
- `0.7` 实现 C# 方法调用：加载 `YxMod5.dll` → 调 `Doorstop.Entrypoint.Start`。
- `0.8` 创建 C# 模组工程（`.csproj`，.NET Framework 4.8，引用游戏程序集，`Private=False`）。
- `0.9` 实现 `Entrypoint` + `YxMod5Host` GameObject + 最小 `YxMod` MonoBehaviour 闭环。
- `0.10` 实现日志系统与调试控制台。
- `0.11` 注入验证：确认链路打通，达成 M0。

### 范围特化说明

- **`0.4` 本阶段仅实现 `winhttp.dll`**：暂不做可配置宿主名切换，也不实现 `version.dll` 备选宿主（后续阶段再评估）。
- **`0.9` 仅为最小闭环**：本阶段不实现 UI 框架、功能模块等（对应 roadmap 阶段 1 及以后）。

## 2. 技术决策

| 决策点 | 决定 | 说明 |
| --- | --- | --- |
| 原生语言 | C++（C++20） | 参照 `HumanModLauncher` |
| 原生工程 | VS C++ `.vcxproj`，DLL，Win32/x64 | 动态库 |
| 工具集 | PlatformToolset v143+ | 兼容 VS 2022 |
| 宿主 DLL | `winhttp.dll` | UnityDoorstop 同款，Unity 网络栈会加载 |
| 代理模式 | AheadLib 转发 | 加载真实系统 DLL 并逐项转发导出 |
| Mono 交互 | `GetProcAddress` 调用 `mono_*` | 自研，无第三方 |
| C# 语言 | LangVersion 12（按编译器能力） | — |
| C# 目标框架 | .NET Framework 4.8 | 匹配游戏 Unity Mono 运行时 |
| C# 输出 | 类库 DLL（`YxMod5.dll`） | 由原生启动层加载 |
| 入口 | `Doorstop.Entrypoint.Start` | 场景切换后挂载宿主 |
| 宿主 | `GameObject("YxMod5Host")` + `YxMod : MonoBehaviour` | `DontDestroyOnLoad` |
| 游戏程序集引用 | `Assembly-CSharp.dll`、`Assembly-CSharp-firstpass.dll`、`HumanAPI.dll`、`UnityEngine*.dll`、`UnityEngine.UI.dll` | 仅编译期引用，`Private=False` |
| 控制台 | `AllocConsole` + UTF-8 输出 | 调试用 |
| 第三方依赖 | **禁用**（无 NuGet / Harmony / Cecil / MonoMod） | 仅 Windows SDK 与系统 API |

## 3. 业务背景

本阶段为 YxMod5 的**启动链与工程初始化**，是整个项目的根基：打通「启动 → 注入 → C# 入口 → 模组宿主」链路，达成里程碑 **M0 可注入**。后续所有功能阶段均建立在本阶段交付物之上。

> 详细项目使命、定位与范围见 `specs/mission.md`，技术栈全貌见 `specs/tech-stack.md`。本阶段不额外补充背景说明。

## 4. 非目标（Out of Scope）

- 可配置宿主名与 `version.dll` 备选宿主（本阶段只做 `winhttp.dll`）。
- UI 框架（IMGUI 主框架，roadmap 阶段 2）。
- 自研运行时基础（反射访问器、配置、热键等，roadmap 阶段 1）。
- 任何功能模块（能力、分身、聊天、皮肤、HUD、娱乐、管理等）。
- 任何第三方方法注入/补丁库。
