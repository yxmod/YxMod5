// ===========================================================================
// YxMod5 原生启动层 —— 日志系统
// ---------------------------------------------------------------------------
// 统一日志接口，双通道输出：
//   * stdout（由 ConsoleInit 分配的控制台，UTF-8 编码）
//   * OutputDebugStringA（供调试器 / DebugView 观察）
// 在控制台尚未就绪时（例如代理转发初始化阶段），仅输出到调试器，避免写入
// 无效的 stdout 句柄。
// ===========================================================================
#pragma once

#include <windows.h>
#include <cstdio>
#include <cstdarg>
#include <cstring>

namespace yxmod5 {
namespace logging {

// 控制台是否已经初始化完成。
inline bool& ConsoleReady()
{
    static bool ready = false;
    return ready;
}

// 分配并初始化调试控制台（UTF-8 输出）。
inline void ConsoleInit()
{
    // 若进程已有控制台，AllocConsole 会失败，此时直接复用。
    if (AllocConsole())
    {
        SetConsoleTitleW(L"YxMod5 Debug Console");
    }

    FILE* f = nullptr;
    if (freopen_s(&f, "CONOUT$", "w", stdout) != 0)
    {
        // 忽略：无控制台时仅依赖调试器输出。
    }
    if (freopen_s(&f, "CONOUT$", "w", stderr) != 0)
    {
    }
    setvbuf(stdout, nullptr, _IONBF, 0);
    setvbuf(stderr, nullptr, _IONBF, 0);

    // Unity Mono 对 UTF-8 兼容性最好。
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    ConsoleReady() = true;
}

inline void ConsoleShutdown()
{
    ConsoleReady() = false;
    FreeConsole();
}

inline void Emit(const char* level, const char* fmt, va_list args)
{
    char body[2048];
    int written = _vsnprintf_s(body, sizeof(body), _TRUNCATE, fmt, args);
    if (written < 0)
    {
        body[sizeof(body) - 1] = '\0';
    }

    SYSTEMTIME st;
    GetLocalTime(&st);

    char line[2304];
    _snprintf_s(line, sizeof(line), _TRUNCATE,
                "[%02d:%02d:%02d][YxMod5][%s] %s\n",
                st.wHour, st.wMinute, st.wSecond, level, body);

    if (ConsoleReady())
    {
        fputs(line, stdout);
        fflush(stdout);
    }
    OutputDebugStringA(line);
}

inline void Info(const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    Emit("INFO", fmt, args);
    va_end(args);
}

inline void Warn(const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    Emit("WARN", fmt, args);
    va_end(args);
}

inline void Error(const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    Emit("ERROR", fmt, args);
    va_end(args);
}

} // namespace logging
} // namespace yxmod5
