// ===========================================================================
// YxMod5 原生启动层 —— 多人同步协议接收钩子（roadmap 1.8）
// ---------------------------------------------------------------------------
// 目标：拦截 Multiplayer.NetGame::OnServerReceive / OnClientReceive 的接收
// 分发，在游戏 switch 静默忽略自定义 MsgId(=0) 之前分流自定义消息。
//
// 原理（自研，不依赖第三方）：
//   1. 用 mono_compile_method 取得原方法与托管分发方法的原生代码地址；
//   2. 在原方法入口写入 5 字节相对跳转（E9 rel32），跳到原生汇编桩；
//   3. 汇编桩调用 C++ 分发器（arg1/arg2/kind），再由分发器通过
//      mono_runtime_invoke 调用托管 SyncChannel.NativeOnServerReceive /
//      NativeOnClientReceive；
//   4. 托管返回 1 = 已消费自定义消息；返回 0 = 非自定义消息，汇编桩
//      以原始寄存器/栈状态跳回「原方法首指令副本 + 跳回原方法」的 trampoline，
//      继续走游戏原逻辑。
//
// 注意：仅对 32 位游戏（Win32）生效；x64 构建下此模块编译为 no-op。
// ===========================================================================
#pragma once

#include <windows.h>

#include "mono_loader.h"

namespace yxmod5 {

// 安装同步协议接收钩子。在 Mono 就绪且 YxMod5.dll 已加载后调用。
// 返回是否成功安装（部分失败会记录日志）。
bool InstallSyncHooks(MonoApi& mono, const char* modAssemblyPath);

} // namespace yxmod5
