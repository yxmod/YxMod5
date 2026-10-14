# 阶段 0：工程初始化与启动链 — 验证标准（Validation）

> - 所属分支：`feat/stage-0-engineering-init-launch-chain`
> - 对应 roadmap：阶段 0（里程碑 M0），任务 `0.1`–`0.11`
> - 编写依据：`specs/mission.md`、`specs/tech-stack.md`

## 1. 验证方式

- 由**维护者（用户）手动验收**。
- 前置条件：将构建产物 `winhttp.dll` + `YxMod5.dll` 放入游戏目录。
- **本流程不自动启动 `human.exe`**，游戏启动与真机观察由维护者自行完成。

## 2. 验收清单（手动逐项确认）

1. **目录结构**：`specs/`、`src/launcher/`、`src/mod/`、`build/`、`release/` 均已建立。
2. **构建通过**：原生工程 `src/launcher/*.vcxproj` 与 C# 工程 `src/mod/*.csproj` 均可在 VS 2022（或更高）中成功生成 DLL。
3. **进程名校验**：`winhttp.dll` 仅对 `human.exe` 生效；其它进程加载时静默转发真实系统 DLL，不注入。
4. **转发正确**：真实 `winhttp.dll` 导出被完整转发，游戏网络功能不受影响。
5. **注入链路**：启动游戏后，链路「原生启动层 → Mono 运行时 → `Doorstop.Entrypoint.Start` → `YxMod5Host` 挂载」完整走通。
6. **日志可观测**：控制台出现模组日志（`AllocConsole` + UTF-8 输出），且 `YxMod5.dll` 侧日志输出到 `Debug.Log` 与控制台。
7. **宿主稳定**：空宿主 `YxMod5Host` GameObject 挂载成功并 `DontDestroyOnLoad`，场景切换后仍存在，无重复实例。

## 3. 完成定义（DoD）

阶段 0 **完成** = 上述验收清单第 1–7 项**全部通过**，且维护者明确表示「**验收通过**」。

## 4. 合并标准

- 仅在维护者**明确表示「验收通过」后**，才允许将本分支 `feat/stage-0-engineering-init-launch-chain` 合并回 `main`。
- 在维护者给出「验收通过」之前，本分支保持独立，**不得合并**。
- 若验收中发现问题，记录问题并修复后重新执行验收清单，直至全部通过。
