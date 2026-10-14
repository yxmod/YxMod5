// ===========================================================================
// YxMod5 原生启动层 —— AheadLib 转发实现
// ===========================================================================
#include "forward.h"
#include "logger.h"

#include "generated/winhttp_forwards.h"

// 为每个 winhttp 导出定义一个 C 链接的函数指针，供 asm 跳转桩引用。
#define YXMOD5_DEFINE_PFN(name) extern "C" FARPROC g_pfn_##name = nullptr;
YXMOD5_WINHTTP_FORWARD_LIST(YXMOD5_DEFINE_PFN)
#undef YXMOD5_DEFINE_PFN

namespace yxmod5 {

namespace {
HMODULE g_realWinhttp = nullptr;
}

bool ProxyInit()
{
    wchar_t systemDir[MAX_PATH] = { 0 };
    const UINT length = GetSystemDirectoryW(systemDir, MAX_PATH);
    if (length == 0 || length >= MAX_PATH)
    {
        logging::Error("获取系统目录失败，无法加载真实 winhttp.dll");
        return false;
    }

    if (wcscat_s(systemDir, L"\\winhttp.dll") != 0)
    {
        logging::Error("拼接 winhttp.dll 路径失败");
        return false;
    }

    g_realWinhttp = LoadLibraryW(systemDir);
    if (g_realWinhttp == nullptr)
    {
        logging::Error("加载真实 winhttp.dll 失败，GetLastError=%lu", GetLastError());
        return false;
    }

    bool allResolved = true;

#define YXMOD5_RESOLVE_PFN(name)                                                 \
    g_pfn_##name = GetProcAddress(g_realWinhttp, #name);                         \
    if (g_pfn_##name == nullptr)                                                 \
    {                                                                            \
        logging::Warn("winhttp 导出缺失：%s", #name);                            \
        allResolved = false;                                                     \
    }

    YXMOD5_WINHTTP_FORWARD_LIST(YXMOD5_RESOLVE_PFN)
#undef YXMOD5_RESOLVE_PFN

    if (!allResolved)
    {
        logging::Warn("部分 winhttp 导出未解析，相关调用可能失败");
    }

    logging::Info("AheadLib 转发已就绪（真实 winhttp.dll 已加载）");
    return true;
}

void ProxyShutdown()
{
    if (g_realWinhttp != nullptr)
    {
        FreeLibrary(g_realWinhttp);
        g_realWinhttp = nullptr;
    }
}

} // namespace yxmod5
