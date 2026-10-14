// ===========================================================================
// YxMod5 原生启动层 —— 日志系统
// ---------------------------------------------------------------------------
// 三通道输出：
//   * 文件：<游戏目录>\YxMod5_launcher.log（追加、UTF-8、每条立即落盘）
//           若游戏目录不可写，则回退到 %TEMP%\YxMod5_launcher.log
//   * stdout：由 ConsoleInit 分配的控制台（UTF-8）
//   * OutputDebugStringA：供调试器 / DebugView 观察
// 控制台尚未就绪时（例如代理转发初始化阶段）仅写文件与调试器，避免写入
// 无效的 stdout 句柄。
// ===========================================================================
#pragma once

#include <windows.h>
#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <string>

namespace yxmod5 {
namespace logging {

// 日志文件路径（DllMain 早期调用 SetupFileLogging 设定）。
inline std::wstring& LogFilePath()
{
    static std::wstring path;
    return path;
}

// 控制台是否已经初始化完成。
inline bool& ConsoleReady()
{
    static bool ready = false;
    return ready;
}

// 获取当前进程 exe 所在目录（不带结尾反斜杠）。
inline std::wstring GetProcessDirectory()
{
    wchar_t path[MAX_PATH] = { 0 };
    const DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH)
    {
        return std::wstring();
    }

    std::wstring full(path, length);
    const size_t pos = full.find_last_of(L"\\/");
    if (pos == std::wstring::npos)
    {
        return std::wstring();
    }
    return full.substr(0, pos);
}

// 在游戏目录建立日志文件；不可写时回退到临时目录。
inline void SetupFileLogging()
{
    const std::wstring dir = GetProcessDirectory();
    if (!dir.empty())
    {
        const std::wstring candidate = dir + L"\\YxMod5_launcher.log";
        FILE* probe = nullptr;
        if (_wfopen_s(&probe, candidate.c_str(), L"ab") == 0 && probe != nullptr)
        {
            fclose(probe);
            LogFilePath() = candidate;
            return;
        }
    }

    wchar_t temp[MAX_PATH] = { 0 };
    const DWORD length = GetTempPathW(MAX_PATH, temp);
    if (length > 0 && length < MAX_PATH)
    {
        LogFilePath() = std::wstring(temp) + L"YxMod5_launcher.log";
    }
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
        // 忽略：无控制台时仅依赖文件与调试器输出。
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
                "[%04d-%02d-%02d %02d:%02d:%02d.%03d][T%05lu][YxMod5][%s] %s\n",
                st.wYear, st.wMonth, st.wDay,
                st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
                static_cast<unsigned long>(GetCurrentThreadId()), level, body);

    // 1) 文件（每条立即打开/写入/关闭，确保崩溃前的日志落盘）。
    const std::wstring& path = LogFilePath();
    if (!path.empty())
    {
        FILE* file = nullptr;
        if (_wfopen_s(&file, path.c_str(), L"ab") == 0 && file != nullptr)
        {
            fwrite(line, 1, std::strlen(line), file);
            fclose(file);
        }
    }

    // 2) 控制台。
    if (ConsoleReady())
    {
        fputs(line, stdout);
        fflush(stdout);
    }

    // 3) 调试器。
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
