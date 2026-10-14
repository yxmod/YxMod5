// ===========================================================================
// YxMod5 原生启动层 —— Mono 运行时加载与托管方法调用
// ---------------------------------------------------------------------------
// 自研实现，仅使用 Windows API + 游戏自带 mono-2.0-bdwgc.dll，不引入任何
// 第三方注入 / 补丁库。
//
// 调用链：
//   WaitForMonoModule -> GetProcAddress(mono_*) -> mono_get_root_domain
//   -> mono_thread_attach -> mono_domain_assembly_open -> mono_assembly_get_image
//   -> mono_class_from_name -> mono_class_get_method_from_name -> mono_runtime_invoke
// ===========================================================================
#pragma once

#include <windows.h>
#include <string>

#include "logger.h"

namespace yxmod5 {

// ---- Mono C API 签名（与 mono-2.0-bdwgc 导出一致） -------------------------
typedef void* (__cdecl* t_mono_get_root_domain)();
typedef void* (__cdecl* t_mono_thread_attach)(void* domain);
typedef void* (__cdecl* t_mono_domain_assembly_open)(void* domain, const char* path);
typedef void* (__cdecl* t_mono_assembly_get_image)(void* assembly);
typedef void* (__cdecl* t_mono_class_from_name)(void* image, const char* name_space, const char* class_name);
typedef void* (__cdecl* t_mono_class_get_method_from_name)(void* klass, const char* name, int param_count);
typedef void* (__cdecl* t_mono_runtime_invoke)(void* method, void* obj, void** params, void** exc);

struct MonoApi
{
    HMODULE module = nullptr;

    t_mono_get_root_domain           get_root_domain    = nullptr;
    t_mono_thread_attach             thread_attach      = nullptr;
    t_mono_domain_assembly_open      domain_assembly_open = nullptr;
    t_mono_assembly_get_image        assembly_get_image = nullptr;
    t_mono_class_from_name           class_from_name    = nullptr;
    t_mono_class_get_method_from_name class_get_method = nullptr;
    t_mono_runtime_invoke            runtime_invoke     = nullptr;

    bool IsValid() const
    {
        return get_root_domain && thread_attach && domain_assembly_open &&
               assembly_get_image && class_from_name && class_get_method &&
               runtime_invoke;
    }
};

// 等待 mono 模块出现（宿主游戏启动早期 mono 尚未加载），随后解析所需函数。
inline bool LoadMono(MonoApi& api, DWORD timeoutMs)
{
    const DWORD start = GetTickCount();
    logging::Info("等待 Mono 运行时模块 mono-2.0-bdwgc.dll ...");

    while ((GetTickCount() - start) < timeoutMs)
    {
        api.module = GetModuleHandleW(L"mono-2.0-bdwgc.dll");
        if (api.module != nullptr)
        {
            break;
        }
        Sleep(100);
    }

    if (api.module == nullptr)
    {
        logging::Error("等待超时：未能获取 mono-2.0-bdwgc.dll 模块句柄");
        return false;
    }

    logging::Info("已获取 Mono 模块句柄：0x%p", static_cast<void*>(api.module));

    api.get_root_domain     = reinterpret_cast<t_mono_get_root_domain>(GetProcAddress(api.module, "mono_get_root_domain"));
    api.thread_attach       = reinterpret_cast<t_mono_thread_attach>(GetProcAddress(api.module, "mono_thread_attach"));
    api.domain_assembly_open = reinterpret_cast<t_mono_domain_assembly_open>(GetProcAddress(api.module, "mono_domain_assembly_open"));
    api.assembly_get_image  = reinterpret_cast<t_mono_assembly_get_image>(GetProcAddress(api.module, "mono_assembly_get_image"));
    api.class_from_name     = reinterpret_cast<t_mono_class_from_name>(GetProcAddress(api.module, "mono_class_from_name"));
    api.class_get_method    = reinterpret_cast<t_mono_class_get_method_from_name>(GetProcAddress(api.module, "mono_class_get_method_from_name"));
    api.runtime_invoke      = reinterpret_cast<t_mono_runtime_invoke>(GetProcAddress(api.module, "mono_runtime_invoke"));

    if (!api.IsValid())
    {
        logging::Error("解析 Mono 函数地址失败（mono_* 导出缺失）");
        return false;
    }

    logging::Info("Mono 函数地址解析完成");
    return true;
}

// 等待 Mono 运行时真正初始化完成（root domain 可用）。
// Unity 在启动过程中才初始化 Mono，若在模块刚加载时就调用 mono_*，
// 可能因运行时未就绪而崩溃。这里先等待主窗口进入空闲，再轮询 root domain。
inline bool WaitForMonoRuntime(MonoApi& api, DWORD timeoutMs)
{
    logging::Info("等待游戏初始化完成（WaitForInputIdle，最多 %lu ms）...",
                  static_cast<unsigned long>(timeoutMs));
    WaitForInputIdle(GetCurrentProcess(), timeoutMs);

    const DWORD start = GetTickCount();
    while ((GetTickCount() - start) < timeoutMs)
    {
        if (api.get_root_domain() != nullptr)
        {
            logging::Info("Mono 运行时已就绪（root domain 可用）");
            return true;
        }
        Sleep(100);
    }

    logging::Error("等待 Mono 运行时就绪超时");
    return false;
}

// 加载托管程序集并调用其静态无参方法。
inline bool InvokeStatic(MonoApi& api,
                         const char* assemblyPath,
                         const char* nameSpace,
                         const char* className,
                         const char* methodName)
{
    void* domain = api.get_root_domain();
    if (domain == nullptr)
    {
        logging::Error("获取 Mono 根域失败");
        return false;
    }

    // 当前线程是原生线程，需先附加到 Mono 运行时。
    logging::Info("附加线程到 Mono 运行时...");
    api.thread_attach(domain);

    logging::Info("打开托管程序集：%s", assemblyPath);
    void* assembly = api.domain_assembly_open(domain, assemblyPath);
    if (assembly == nullptr)
    {
        logging::Error("打开托管程序集失败：%s", assemblyPath);
        return false;
    }

    void* image = api.assembly_get_image(assembly);
    if (image == nullptr)
    {
        logging::Error("获取程序集 Image 失败：%s", assemblyPath);
        return false;
    }

    logging::Info("查找类型：%s.%s", nameSpace, className);
    void* klass = api.class_from_name(image, nameSpace, className);
    if (klass == nullptr)
    {
        logging::Error("查找类型失败：%s.%s", nameSpace, className);
        return false;
    }

    void* method = api.class_get_method(klass, methodName, 0);
    if (method == nullptr)
    {
        logging::Error("查找方法失败：%s.%s::%s()", nameSpace, className, methodName);
        return false;
    }

    logging::Info("调用托管方法：%s.%s::%s()", nameSpace, className, methodName);
    void* exception = nullptr;
    api.runtime_invoke(method, nullptr, nullptr, &exception);
    if (exception != nullptr)
    {
        logging::Error("调用 %s.%s::%s() 时抛出托管异常", nameSpace, className, methodName);
        return false;
    }

    logging::Info("托管方法调用返回");
    return true;
}

// 宽字符路径 -> UTF-8（mono_domain_assembly_open 需要 UTF-8 路径）。
inline std::string WideToUtf8(const std::wstring& wide)
{
    if (wide.empty())
    {
        return std::string();
    }

    const int size = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(),
                                         static_cast<int>(wide.size()),
                                         nullptr, 0, nullptr, nullptr);
    if (size <= 0)
    {
        return std::string();
    }

    std::string result(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, wide.c_str(),
                        static_cast<int>(wide.size()),
                        &result[0], size, nullptr, nullptr);
    return result;
}

} // namespace yxmod5
