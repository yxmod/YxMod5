// ===========================================================================
// YxMod5 原生启动层 —— AheadLib 转发框架
// ---------------------------------------------------------------------------
// 以 winhttp.dll 作为代理宿主：
//   1. 从系统目录加载真实 winhttp.dll；
//   2. 解析其全部导出地址，保存到 g_pfn_* 函数指针；
//   3. asm 中的 AheadLib_* 跳转桩转发到对应 g_pfn_*；
//   4. 链接器（winhttp_x64.def / winhttp_x86.def）把 winhttp 导出名绑定到跳转桩。
// ===========================================================================
#pragma once

#include <windows.h>

namespace yxmod5 {

// 加载真实 winhttp.dll 并解析全部转发函数地址。
// 返回 false 表示代理不可用（此时依赖 winhttp 的功能会失效）。
bool ProxyInit();

// 释放真实 winhttp.dll。
void ProxyShutdown();

} // namespace yxmod5
