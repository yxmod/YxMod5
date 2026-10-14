// ===========================================================================
// YxMod5 原生启动层 —— 入口（winhttp.dll 代理宿主）
// ---------------------------------------------------------------------------
// 流程：
//   DllMain(DLL_PROCESS_ATTACH)
//     -> SetupFileLogging()         在游戏目录建立日志文件
//     -> ProxyInit()                加载真实 winhttp.dll 并解析转发地址
//     -> 进程名校验 (human.exe)
//     -> CreateThread(InjectionThread)
//          -> ConsoleInit()         分配调试控制台
//          -> SetUnhandledExceptionFilter（崩溃时记录异常地址/模块）
//          -> 等待 YxMod5.dll
//          -> LoadMono()            解析 mono_* 函数
//          -> WaitForMonoRuntime()  等待 Mono 运行时初始化完成
//          -> InvokeStatic("Doorstop", "Entrypoint", "Start")
// ===========================================================================
#include <windows.h>
#include <string>

#include "forward.h"
#include "logger.h"
#include "mono_loader.h"
#include "sync_hook.h"

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

// 记录异常地址所属模块，便于定位崩溃。
void LogFaultModule(const void* address)
{
    if (address == nullptr)
    {
        return;
    }

    HMODULE module = nullptr;
    const DWORD flags = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT;
    if (GetModuleHandleExW(flags, reinterpret_cast<LPCWSTR>(address), &module) && module != nullptr)
    {
        wchar_t name[MAX_PATH] = { 0 };
        GetModuleFileNameW(module, name, MAX_PATH);
        const std::string utf8 = yxmod5::WideToUtf8(name);
        yxmod5::logging::Error("异常地址位于模块：%s", utf8.c_str());
    }
}

// 链式调用进程原有的未处理异常过滤器（可能是 Unity 的崩溃报告器）。
LPTOP_LEVEL_EXCEPTION_FILTER g_previousExceptionFilter = nullptr;

LONG WINAPI CrashHandler(EXCEPTION_POINTERS* info)
{
    if (info != nullptr && info->ExceptionRecord != nullptr)
    {
        const EXCEPTION_RECORD* record = info->ExceptionRecord;
        yxmod5::logging::Error("检测到未处理异常：code=0x%08lX address=0x%p flags=0x%08lX",
                               static_cast<unsigned long>(record->ExceptionCode),
                               record->ExceptionAddress,
                               static_cast<unsigned long>(record->ExceptionFlags));
        LogFaultModule(record->ExceptionAddress);
    }
    else
    {
        yxmod5::logging::Error("检测到未处理异常（无异常记录）");
    }

    if (g_previousExceptionFilter != nullptr)
    {
        return g_previousExceptionFilter(info);
    }

    // 无原有处理器时按默认方式终止进程，避免无限递归。
    return EXCEPTION_EXECUTE_HANDLER;
}

DWORD WINAPI InjectionThread(LPVOID)
{
    // 避免与 DllMain 内的加载器锁竞争。
    Sleep(50);

    yxmod5::logging::ConsoleInit();
    g_previousExceptionFilter = SetUnhandledExceptionFilter(&CrashHandler);

    yxmod5::logging::Info("注入线程启动，PID=%lu", static_cast<unsigned long>(GetCurrentProcessId()));
    yxmod5::logging::Info("日志文件：%ls", yxmod5::logging::LogFilePath().c_str());

    std::wstring hostDir = yxmod5::logging::GetProcessDirectory();
    if (hostDir.empty())
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

    if (!yxmod5::WaitForMonoRuntime(mono, 30000))
    {
        return 5;
    }

    const std::string utf8Path = yxmod5::WideToUtf8(modPath);
    if (!yxmod5::InvokeStatic(mono, utf8Path.c_str(), "Doorstop", "Entrypoint", "Start"))
    {
        return 4;
    }

    // 阶段 1.8：安装自定义消息接收钩子（拦截 OnServerReceive / OnClientReceive）。
    yxmod5::InstallSyncHooks(mono, utf8Path.c_str());

    yxmod5::logging::Info("已调用 Doorstop.Entrypoint.Start，注入链路完成");
    return 0;
}

} // namespace

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
    {
        DisableThreadLibraryCalls(hModule);

        const bool isHost = IsHostProcess();
        if (isHost)
        {
            yxmod5::logging::SetupFileLogging();
            yxmod5::logging::Info("===== YxMod5 启动层已加载（host=human.exe）=====");
        }

        // 无论是否宿主进程，都必须先完成转发，保证游戏网络栈可用。
        if (!yxmod5::ProxyInit())
        {
            yxmod5::logging::Error("AheadLib 转发初始化失败");
        }

        if (isHost)
        {
            HANDLE thread = CreateThread(nullptr, 0, InjectionThread, nullptr, 0, nullptr);
            if (thread != nullptr)
            {
                CloseHandle(thread);
            }
        }
        break;
    }

    case DLL_PROCESS_DETACH:
        if (yxmod5::logging::ConsoleReady())
        {
            yxmod5::logging::Info("进程卸载启动层（DLL_PROCESS_DETACH）");
        }
        yxmod5::logging::ConsoleShutdown();
        yxmod5::ProxyShutdown();
        break;

    default:
        break;
    }

    return TRUE;
}
