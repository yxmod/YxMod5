# 阶段 0：工程初始化与启动链 — 开发计划（Plan）

> - 所属分支：`feat/stage-0-engineering-init-launch-chain`
> - 对应 roadmap：阶段 0（里程碑 M0），任务 `0.1`–`0.11`
> - 编写依据：`specs/mission.md`、`specs/tech-stack.md`

## 分组总览

按功能模块分为 4 个任务组，顺序执行：

| 组 | 名称 | 覆盖 roadmap 任务 | 阶段目标 |
| --- | --- | --- | --- |
| A | 工程骨架与目录结构 | 0.1、0.2、0.8 | 构建基线可用 |
| B | 原生启动层 | 0.3、0.4、0.5、0.6、0.7 | 可注入代理 DLL |
| C | C# 模组层闭环 | 0.9 | 模组宿主可挂载 |
| D | 日志系统与注入验证 | 0.10、0.11 | 达成 M0 |

依赖顺序：**A → B → C → D**（D 组贯穿全程，最终收口）。

---

## A 组：工程骨架与目录结构

### 任务

- `0.1` 建立仓库目录结构（`specs/`、`src/launcher/`、`src/mod/`、`build/`、`release/`）。
- `0.2` 创建原生启动层 C++ 工程（`.vcxproj`，DLL，Win32/x64 均配置，C++20，PlatformToolset v143+）。
- `0.8` 创建 C# 模组工程（`.csproj`，.NET Framework 4.8，引用游戏程序集且 `Private=False`）。

### 交付物

- 完整目录结构落地于仓库。
- 可被 MSBuild/Visual Studio 打开的 `src/launcher/*.vcxproj`（Win32 与 x64 配置齐全）。
- 可编译的 `src/mod/*.csproj`，游戏程序集引用均为「仅编译期、不随包分发」。

### 验收点

- 两个工程在 VS 2022（或更高）中打开无缺失引用、无配置错误。
- 空工程（无业务代码）能够成功生成 DLL 骨架。

---

## B 组：原生启动层

### 任务

- `0.3` 实现 AheadLib 转发框架：加载真实系统 DLL 并逐项转发导出。
- `0.4` 实现宿主：默认 `winhttp.dll`（本阶段**仅实现此宿主**，不做可配置宿主名、不做 `version.dll` 备选）。
- `0.5` 实现进程名校验：仅对 `human.exe` 生效，其余进程不注入、不动作。
- `0.6` 实现 Mono 运行时加载：`GetProcAddress` 获取 `mono-2.0-bdwgc.dll` 的 `mono_*` 函数。
- `0.7` 实现 C# 方法调用：加载 `YxMod5.dll` → 调 `Doorstop.Entrypoint.Start`。

### 交付物

- `winhttp.dll`（AheadLib 转发 + 注入逻辑）。
- 真实 `winhttp.dll` 导出被正确转发，游戏网络栈可正常使用。
- Mono 运行时加载与 `Doorstop.Entrypoint.Start` 调用链完整。

### 验收点

- 放入 `human.exe` 所在目录后，仅 `human.exe` 触发注入；其它进程加载该 DLL 时静默转发、不注入。
- 进程名校验、Mono 函数地址获取、C# 入口调用三者在日志中均有可观测输出（依赖 D 组日志）。

---

## C 组：C# 模组层闭环

### 任务

- `0.9` 实现 `Entrypoint` + `YxMod5Host` GameObject + 最小 `YxMod` MonoBehaviour 闭环，`DontDestroyOnLoad`。

### 交付物

- `YxMod5.dll`：`Doorstop.Entrypoint.Start` 入口可用。
- `YxMod5Host` GameObject 与最小 `YxMod` MonoBehaviour 挂载成功。

### 验收点

- 由 B 组调用后，模组宿主能在场景切换后保持存在（`DontDestroyOnLoad`）。
- 入口被重复调用时不会产生重复宿主实例。

---

## D 组：日志系统与注入验证

### 任务

- `0.10` 实现日志系统与调试控制台（原生层 `AllocConsole` + UTF-8 输出；C# 层统一日志接口，输出到 `Debug.Log` 与控制台）。
- `0.11` 注入验证：确认链路「原生启动层 → Mono → `Entrypoint` → 空宿主」打通，达成 M0。

### 交付物

- 统一日志接口及双通道输出（控制台 + `Debug.Log`）。
- 完整注入链路的可观测日志。

### 验收点

- 游戏目录放入 `winhttp.dll` + `YxMod5.dll` 后，控制台出现模组日志、空宿主挂载成功、进程名校验生效（由维护者手动验收，见 `validation.md`）。
