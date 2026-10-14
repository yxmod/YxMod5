// ===========================================================================
// YxMod5 原生启动层 —— 入口（winhttp.dll 代理宿主）
// ---------------------------------------------------------------------------
// 流程：
//   DllMain(DLL_PROCESS_ATTACH)
//     -> ProxyInit()                加载真实 winhttp.dll 并解析转发地址
//     -> 进程名校验 (human.exe)
//     -> CreateThread(InjectionThread)
//          -> ConsoleInit()         分配调试控制台
//          -> 等待 YxMod5.dll
//          -> LoadMono()            解析 mono_* 函数
//          -> InvokeStatic("Doorstop", "Entrypoint", "Start")
// ===========================================================================
#include <windows.h>
#include <string>

#include "forward.h"
#include "logger.h"
#include "mono_loader.h"

namespace {

// 仅对《人类一败涂地》主进程生效，其余进程静默转发。
bool IsHostProcess()
{
    wchar_t path[MAX_PATH] = { 0 };
    const DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH)
    {
        return false;
    }

    const wchar_t* name = wcsrchr(path, L'\\');
    name = (name != nullptr) ? (name + 1) : path;
    return _wcsicmp(name, L"human.exe") == 0;
}

// 获取宿主 exe 所在目录（不带结尾反斜杠）。
bool GetHostDirectory(std::wstring& outDir)
{
    wchar_t path[MAX_PATH] = { 0 };
    const DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH)
    {
        return false;
    }

    std::wstring full(path, length);
    const size_t pos = full.find_last_of(L"\\/");
    if (pos == std::wstring::npos)
    {
        return false;
    }

    outDir = full.substr(0, pos);
    return true;
}

// 等待模组 DLL 就绪（部署延迟场景下可能稍晚出现）。
bool WaitForModDll(const std::wstring& modPath, DWORD timeoutMs)
{
    const DWORD start = GetTickCount();
    while ((GetTickCount() - start) < timeoutMs)
    {
        if (GetFileAttributesW(modPath.c_str()) != INVALID_FILE_ATTRIBUTES)
        {
            return true;
        }
        Sleep(100);
    }
    return GetFileAttributesW(modPath.c_str()) != INVALID_FILE_ATTRIBUTES;
}

DWORD WINAPI InjectionThread(LPVOID)
{
    // 避免与 DllMain 内的加载器锁竞争。
    Sleep(50);

    yxmod5::logging::ConsoleInit();
    yxmod5::logging::Info("注入线程启动，进程 PID=%lu", GetCurrentProcessId());

    std::wstring hostDir;
    if (!GetHostDirectory(hostDir))
    {
        yxmod5::logging::Error("获取宿主目录失败");
        return 1;
    }

    const std::wstring modPath = hostDir + L"\\YxMod5.dll";
    yxmod5::logging::Info("模组程序集路径：%ls", modPath.c_str());

    if (!WaitForModDll(modPath, 30000))
    {
        yxmod5::logging::Error("未找到 YxMod5.dll，放弃注入");
        return 2;
    }

    yxmod5::MonoApi mono;
    if (!yxmod5::LoadMono(mono, 30000))
    {
        return 3;
    }

    const std::string utf8Path = yxmod5::WideToUtf8(modPath);
    if (!yxmod5::InvokeStatic(mono, utf8Path.c_str(), "Doorstop", "Entrypoint", "Start"))
    {
        return 4;
    }

    yxmod5::logging::Info("已调用 Doorstop.Entrypoint.Start，注入链路完成");
    return 0;
}

} // namespace

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);

        // 无论是否宿主进程，都必须先完成转发，保证游戏网络栈可用。
        yxmod5::ProxyInit();

        if (IsHostProcess())
        {
            HANDLE thread = CreateThread(nullptr, 0, InjectionThread, nullptr, 0, nullptr);
            if (thread != nullptr)
            {
                CloseHandle(thread);
            }
        }
        break;

    case DLL_PROCESS_DETACH:
        yxmod5::logging::ConsoleShutdown();
        yxmod5::ProxyShutdown();
        break;

    default:
        break;
    }

    return TRUE;
}
