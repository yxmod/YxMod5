# YxMod5 技术栈清单

## 总体架构

YxMod5 采用**两层架构**：

```
┌─────────────────────────────────────────────────────────┐
│  C# 模组层（YxMod5.dll）                                  │
│  - Entrypoint 入口                                        │
│  - YxMod 宿主 MonoBehaviour                                │
│  - 反射访问器层 / 生命周期驱动 / 软补丁 / 事件订阅           │
│  - IMGUI UI 框架                                          │
│  - 功能模块（能力/分身/聊天/皮肤/HUD/娱乐/管理）             │
└─────────────────────────────────────────────────────────┘
                          ▲  mono_runtime_invoke(...)
┌─────────────────────────────────────────────────────────┐
│  原生启动层（winhttp.dll，代理 DLL）                       │
│  - AheadLib 转发真实系统 DLL 导出                          │
│  - 定位 human.exe 进程                                    │
│  - 获取 mono-2.0-bdwgc.dll 的 mono_* 函数                 │
│  - 加载并反射调用 YxMod5.dll::Doorstop.Entrypoint.Start    │
└─────────────────────────────────────────────────────────┘
```

## 原生启动层

| 项目 | 选型 | 说明 |
| --- | --- | --- |
| 语言 | C++（C++20） | 参照 HumanModLauncher |
| 工程 | Visual Studio C++（`.vcxproj`） | 动态库（DLL），Win32/x64 均配置 |
| 工具集 | PlatformToolset v143+ | 参照工程使用 v145（VS 2026），兼容 VS 2022 |
| 代理模式 | AheadLib 转发 | 加载真实系统 DLL 并逐项转发导出 |
| **默认宿主 DLL** | **`winhttp.dll`** | UnityDoorstop 同款，Unity 网络栈会加载，HFF 验证充分 |
| 备选宿主 | `version.dll` | 导出简单、依赖少；宿主名可配置 |
| 弃用宿主 | `cryptbase.dll` | 参照方案，本次不再采用 |
| Mono 交互 | 直接 `GetProcAddress` 调用 `mono_get_root_domain` / `mono_thread_attach` / `mono_domain_assembly_open` / `mono_assembly_get_image` / `mono_class_from_name` / `mono_class_get_method_from_name` / `mono_runtime_invoke` | 自研，无第三方 |
| 控制台 | `AllocConsole` + UTF-8 输出 | 调试用 |

> 说明：原生启动层不引入任何第三方库，仅使用 Windows SDK 与系统 API。

## C# 模组层

| 项目 | 选型 | 说明 |
| --- | --- | --- |
| 语言 | C# | LangVersion 可选 12（按编译器能力） |
| 目标框架 | .NET Framework 4.8 | 匹配游戏 Unity Mono 运行时 |
| 输出 | 类库 DLL（`YxMod5.dll`） | 由原生启动层加载 |
| UI | Unity IMGUI（`OnGUI`） | 与参照项目一致，实现自定义皮肤/样式 |
| 入口 | `Doorstop.Entrypoint.Start` | 场景切换后挂载宿主 |
| 宿主 | `GameObject("YxMod5Host")` + `YxMod : MonoBehaviour` | `DontDestroyOnLoad` |

### 游戏程序集引用（仅编译期引用，不随包分发）

- `Assembly-CSharp.dll`
- `Assembly-CSharp-firstpass.dll`
- `HumanAPI.dll`
- `UnityEngine.dll` 及所需 `UnityEngine.*Module`（Core/UI/IMGUI/Physics/Audio/TextRendering/ImageConversion 等）
- `UnityEngine.UI.dll`

> 这些引用在工程中设置为 `Private=False`，只用于编译，运行时由游戏进程提供。

### 明确禁用清单（重要约束）

| 禁用 | 替代方案 |
| --- | --- |
| Harmony / HarmonyX | 自研「软补丁 + 生命周期驱动 + 事件订阅」 |
| Mono.Cecil | 不做静态 IL 改写；如需动态代码用 BCL 自带 `System.Reflection.Emit` |
| MonoMod（RuntimeDetour/Utils） | 自研反射访问器 + 按需自研原生钩子 |

## 自研组件清单

1. **反射访问器层（Reflection Accessor）**
   - 基于 `System.Reflection`，缓存 `Type/FieldInfo/PropertyInfo/MethodInfo`。
   - 关键访问点用 `System.Reflection.Emit` 或 `Delegate.CreateDelegate` 委托化，避免每帧 `Invoke` 开销。
   - 封装对 `Human` / `NetGame` / `NetPlayer` 等私有成员的读写。

2. **生命周期驱动（Lifecycle Driver）**
   - 以 `MonoBehaviour` 的 `Awake/Start/Update/FixedUpdate/LateUpdate/OnGUI` 为执行骨架。
   - 功能模块以组件形式注册到宿主 GameObject。

3. **软补丁（Soft Patch / 状态修正）**
   - 以「定时/每帧检测目标状态并覆写」替代 Harmony 的 Prefix/Postfix 拦截。
   - 适用于常显名字、光标控制、跳过启动画面等非时序敏感场景。

4. **事件订阅（Event Subscription）**
   - 优先使用 Unity 自带事件（如 `SceneManager.activeSceneChanged`）与游戏自身回调。

5. **自研原生钩子（Native Hook，按需启用）**
   - 仅对少数必须「先于/后于」原方法执行的时序敏感点，在原生启动层对 Mono 方法做地址级拦截。
   - v1 尽量少用，避免不稳定；确需时再逐步引入。

6. **配置系统**
   - 自研 INI 读写 + `PlayerPrefs` 封装（按 Steam 作用域隔离）。

7. **热键系统（HotKey）**
   - 自研，支持单键/组合键/数字编号热键。

8. **日志系统**
   - 统一日志接口，输出到 Unity `Debug.Log` 与控制台。

9. **聊天指令框架**
   - 自研注册/分发，替代参照项目中对 `NetChat.RegisterCommand` 的依赖。

## 构建与发布

| 项目 | 选型 |
| --- | --- |
| 构建工具 | MSBuild / Visual Studio（`.sln` + `.csproj` / `.vcxproj`） |
| NuGet 还原 | 不使用第三方 NuGet 包（无 Harmony/Cecil/MonoMod） |
| PostBuild | 构建后自动拷贝 `YxMod5.dll` 与 `winhttp.dll` 到游戏目录 |
| 打包 | 脚本化打包为发布压缩包（含 DLL、配置模板、安装说明） |
| 自动更新 | 基于 GitHub Release 版本检查 + 下载（自研） |
| 版本管理 | Git，仓库 `github.com/yxmod/YxMod5` |

## 开发环境

- 操作系统：Windows 10 / 11
- IDE：Visual Studio 2022（或更高，含 C++ 桌面开发负载与 .NET 桌面开发负载）
- 游戏：Steam 版《人类一败涂地》
- 目标进程：`human.exe`

## 风险与备选

| 风险 | 应对 |
| --- | --- |
| `winhttp.dll` 未按预期被游戏加载 | 备选宿主 `version.dll`；宿主名做成可配置，AheadLib 转发逻辑通用 |
| 无 Harmony 导致某些拦截难以实现 | 优先软补丁/事件；确需时序拦截再启用自研原生钩子 |
| 反射访问随游戏版本更新失效 | 反射访问器层集中封装，随版本只改这一层 |
| 多人同步复杂度 | 架构层统一同步协议（聊天指令通道 + 房主权威），功能模块遵循同一模式 |
