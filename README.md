# YxMod5

《人类一败涂地》（Human: Fall Flat，Unity + Mono）游戏的模组插件。

- **功能/UI 参照**：`F:\VS项目\人类一败涂地\github\YxModDll`
- **启动方式参照**：`github.com/yxmod/HumanModLauncher`
- **核心约束**：完全自研，不依赖 HarmonyX / Mono.Cecil / MonoMod 等第三方注入或补丁库。

项目使命、范围与路线图见 [`specs/`](specs/)：

- [`specs/mission.md`](specs/mission.md) —— 项目使命与范围
- [`specs/tech-stack.md`](specs/tech-stack.md) —— 技术栈
- [`specs/roadmap.md`](specs/roadmap.md) —— 分阶段实施顺序
- [`specs/2026-10-14-engineering-init-launch-chain/`](specs/2026-10-14-engineering-init-launch-chain/) —— 阶段 0（工程初始化与启动链）需求 / 计划 / 验证
- [`specs/2026-10-15-runtime-foundation-multiplayer-sync/`](specs/2026-10-15-runtime-foundation-multiplayer-sync/) —— 阶段 1（自研运行时基础与多人同步设计）需求 / 计划 / 验证 / 协议

## 当前进度

- 阶段 0（里程碑 **M0 可注入**）已完成：启动链「原生代理 DLL → Mono → C# 入口 → 模组宿主」打通。
- 阶段 1（**自研运行时基础与多人同步设计**）已完成：反射访问器核心 + 安全防护、Human / NetGame / NetPlayer 访问器、配置系统（INI + PlayerPrefs）、热键系统、自定义消息（MsgId=0）同步协议与最小收发通道（发送走反射，接收走自研原生钩子）。

后续阶段（IMGUI UI 框架、核心能力等）见 roadmap。

## 架构

```
┌─────────────────────────────────────────────────────────┐
│  C# 模组层（YxMod5.dll，.NET Framework 4.8）              │
│  Doorstop.Entrypoint → YxMod5Host(GameObject) → YxMod     │
└─────────────────────────────────────────────────────────┘
                          ▲ mono_runtime_invoke(...)
┌─────────────────────────────────────────────────────────┐
│  原生启动层（winhttp.dll，C++20，AheadLib 代理）           │
│  转发真实 winhttp 导出 → 进程名校验 → 等待 Mono → 调 C# 入口 │
│  → 安装同步钩子（拦截 NetGame.OnServerReceive/OnClientReceive）│
└─────────────────────────────────────────────────────────┘
```

## 目录结构

```
specs/                          需求 / 计划 / 验证文档
src/
  launcher/                     原生启动层（C++，输出 winhttp.dll）
    YxMod5Launcher.vcxproj
    src/                        dllmain / forward / mono_loader / logger / sync_hook
    generated/                  自动生成的 winhttp 转发代码
  mod/                          C# 模组层（输出 YxMod5.dll）
    YxMod5.csproj
    src/                        Entrypoint / YxMod / YxModHost / Logger / SelfTest
      Runtime/                  反射访问器 + 安全防护 + Human/NetGame/NetPlayer 访问器
      Config/                   目录 / INI / 分层配置
      Input/                    热键系统
      Net/                      同步协议 + 收发通道
build/                          构建脚本与中间产物
release/                        发布产物（winhttp.dll、YxMod5.dll）
tests/                          离线自测（NetStream 编码往返）
YxMod5.sln                      解决方案
```

## 构建

### 环境要求

- Windows 10 / 11
- Visual Studio 2022 或更高版本（需 **C++ 桌面开发** 与 **.NET 桌面开发** 负载；PlatformToolset 自动选取 v143+）
- 《人类一败涂地》游戏（提供编译期引用的游戏程序集）

C# 工程默认引用：

```
F:\SteamLibrary\steamapps\common\Human Fall Flat\Human_Data\Managed\
```

如游戏安装路径不同，可在构建时覆盖：

```powershell
# 方式一：命令行参数
powershell -ExecutionPolicy Bypass -File build\build.ps1 `
    -GameManagedDir "D:\Steam\steamapps\common\Human Fall Flat\Human_Data\Managed"

# 方式二：环境变量
$env:YXMOD5_GAME_MANAGED = "D:\Steam\...\Human_Data\Managed"
```

> 游戏程序集均为「仅编译期引用」（`Private=False`），不会随发布包分发。

### 一键构建

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

产物位于 `release\`：`winhttp.dll`、`YxMod5.dll`（以及可选调试符号）。

常用参数：

> **重要**：当前《人类一败涂地》为 **32 位（x86）** 游戏，代理 DLL 必须使用 `Win32`（默认）构建。

| 参数 | 说明 |
| --- | --- |
| `-Configuration Debug\|Release` | 构建配置，默认 `Release` |
| `-Platform Win32\|x64` | 原生层平台，默认 `Win32`（游戏为 32 位） |
| `-Clean` | 构建前清理产物 |
| `-RegenerateForwards` | 重新生成 winhttp 转发代码 |
| `-GameManagedDir <dir>` | 指定游戏托管程序集目录 |
| `-Deploy -GameDir <dir>` | 构建后直接部署到游戏目录 |

也可以直接用 Visual Studio 打开 `YxMod5.sln` 构建。

### 重新生成 winhttp 转发代码

`src/launcher/generated/` 下的文件由脚本生成（导出清单内嵌于脚本）：

```powershell
powershell -ExecutionPolicy Bypass -File build\Generate-WinHttpForwards.ps1
```

## 安装与验证

> **重要**：当前《人类一败涂地》为 **32 位（x86）** 游戏，代理 DLL 必须使用 `-Platform Win32`（脚本默认值）构建，否则游戏无法加载。

将 `release\` 下的 `winhttp.dll` 与 `YxMod5.dll` 放入游戏根目录（与 `Human.exe` 同级），启动游戏即可自动注入。

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1 `
    -Platform Win32 `
    -Deploy -GameDir "F:\SteamLibrary\steamapps\common\Human Fall Flat"
```

部署时脚本会读取 `Human.exe` 的 PE 架构并校验平台是否匹配。

更快捷的方式（固定 Win32、默认游戏目录）：

```powershell
powershell -ExecutionPolicy Bypass -File build\deploy.ps1
```

详细验收步骤见 [`docs/安装与验证.md`](docs/安装与验证.md)。

## 许可

见仓库内相关声明。游戏程序集版权归原作者 / 发行商所有，本仓库仅作编译期引用。
