// ===========================================================================
// YxMod5 原生启动层 —— 多人同步协议接收钩子实现（roadmap 1.8）
// ---------------------------------------------------------------------------
// 仅 32 位（Win32）生效；x64 编译为 no-op。
// 详见 sync_hook.h。
// ===========================================================================
#include "sync_hook.h"

#include <cstdint>
#include <cstring>

#include "logger.h"

namespace yxmod5 {
namespace {

// 当前 Mono API 引用（安装时赋值，供分发器使用）。
MonoApi g_mono;

// 托管分发方法（YxMod5.Net.SyncChannel::NativeOnServerReceive / NativeOnClientReceive）。
void* g_hookServerMethod = nullptr;
void* g_hookClientMethod = nullptr;

// 原方法 trampoline（汇编桩在「未消费」时跳回）。
extern "C" void* g_pfnOnServerReceiveTrampoline = nullptr;
extern "C" void* g_pfnOnClientReceiveTrampoline = nullptr;

// 汇编桩入口（定义于 sync_hooks_x86.asm，仅 Win32）。
#if defined(_M_IX86)
extern "C" void YxMod5OnServerReceiveHook();
extern "C" void YxMod5OnClientReceiveHook();
#endif

// ---- 最小 x86 指令长度解码（用于 trampoline 对齐指令边界） ----------------

// 解码 ModRM + SIB + 位移（不含 opcode 本身），返回字节数；失败返回 0。
size_t DecodeModRM(const uint8_t* p, size_t remaining)
{
    if (remaining < 1)
    {
        return 0;
    }

    uint8_t modrm = p[0];
    uint8_t mod = (modrm >> 6) & 3;
    uint8_t rm = modrm & 7;

    size_t len = 1;
    if (mod == 3)
    {
        return len; // 寄存器寻址，无位移 / SIB。
    }

    if (rm == 4)
    {
        // SIB。
        if (remaining < 2)
        {
            return 0;
        }
        len++;
        uint8_t base = p[1] & 7;
        if (base == 5 && mod == 0)
        {
            len += 4; // disp32。
        }
    }
    else if (rm == 5 && mod == 0)
    {
        len += 4; // disp32。
    }

    if (mod == 1)
    {
        len += 1; // disp8。
    }
    else if (mod == 2)
    {
        len += 4; // disp32。
    }

    return len;
}

// 解码一条 x86 指令长度；失败返回 0。
size_t DecodeX86Len(const uint8_t* p, size_t remaining)
{
    if (remaining == 0)
    {
        return 0;
    }

    size_t i = 0;
    while (i < remaining)
    {
        uint8_t b = p[i];
        if (b == 0x66 || b == 0x67 || b == 0xF0 || b == 0xF2 || b == 0xF3 ||
            b == 0x2E || b == 0x36 || b == 0x3E || b == 0x26 || b == 0x64 || b == 0x65)
        {
            i++;
            continue;
        }
        break;
    }

    if (i >= remaining)
    {
        return 0;
    }

    uint8_t op = p[i];

    if (op == 0x0F)
    {
        if (i + 1 >= remaining)
        {
            return 0;
        }
        uint8_t op2 = p[i + 1];
        if (op2 >= 0x80 && op2 <= 0x8F)
        {
            return i + 6; // jcc rel32。
        }
        // movzx / movsx 等带 ModRM 的指令。
        size_t modrm = DecodeModRM(p + i + 2, remaining - i - 2);
        if (modrm == 0)
        {
            return 0;
        }
        return i + 2 + modrm;
    }

    // 需要 ModRM 的 ALU / mov 类指令。
    auto ModRmLen = [&](size_t extraImm = 0) -> size_t {
        size_t modrm = DecodeModRM(p + i + 1, remaining - i - 1);
        return modrm == 0 ? 0 : i + 1 + modrm + extraImm;
    };

    switch (op)
    {
    // push / pop 通用寄存器。
    case 0x50: case 0x51: case 0x52: case 0x53: case 0x54: case 0x55: case 0x56: case 0x57:
    case 0x58: case 0x59: case 0x5A: case 0x5B: case 0x5C: case 0x5D: case 0x5E: case 0x5F:
        return i + 1;

    case 0x68: return i + 5; // push imm32。
    case 0x6A: return i + 2; // push imm8。

    // jcc rel8。
    case 0x70: case 0x71: case 0x72: case 0x73: case 0x74: case 0x75: case 0x76: case 0x77:
    case 0x78: case 0x79: case 0x7A: case 0x7B: case 0x7C: case 0x7D: case 0x7E: case 0x7F:
        return i + 2;

    case 0xE8: case 0xE9: return i + 5; // call / jmp rel32。
    case 0xEB: return i + 2;            // jmp rel8。

    case 0xC3: return i + 1; // ret。
    case 0xC2: return i + 3; // ret imm16。
    case 0xC9: return i + 1; // leave。
    case 0x90: return i + 1; // nop。
    case 0xCC: return i + 1; // int3。

    // mov r32, imm32。
    case 0xB8: case 0xB9: case 0xBA: case 0xBB: case 0xBC: case 0xBD: case 0xBE: case 0xBF:
        return i + 5;

    case 0xA1: case 0xA3: return i + 5; // moffs。

    // 组 1：imm32。
    case 0x81: return ModRmLen(4);
    // 组 1：imm8。
    case 0x83: return ModRmLen(1);
    // mov r/m, imm（8/32）。
    case 0xC6: return ModRmLen(1);
    case 0xC7: return ModRmLen(4);

    // 常见 ALU / mov / test / lea / cmp / sub / add / xor / and / or 等（ModRM）。
    case 0x00: case 0x01: case 0x02: case 0x03:
    case 0x08: case 0x09: case 0x0A: case 0x0B:
    case 0x20: case 0x21: case 0x22: case 0x23:
    case 0x28: case 0x29: case 0x2A: case 0x2B:
    case 0x30: case 0x31: case 0x32: case 0x33:
    case 0x38: case 0x39: case 0x3A: case 0x3B:
    case 0x84: case 0x85: case 0x86: case 0x87:
    case 0x88: case 0x89: case 0x8A: case 0x8B:
    case 0x8C: case 0x8D: case 0x8E: case 0x8F:
    case 0xD0: case 0xD1: case 0xD2: case 0xD3:
    case 0xF6: case 0xF7: case 0xFE: case 0xFF:
        return ModRmLen();

    default:
        return 0; // 未知指令，安装失败（安全降级）。
    }
}

// ---- 地址可执行性检查 -----------------------------------------------------

bool IsExecutableMemory(void* p)
{
    if (p == nullptr)
    {
        return false;
    }
    MEMORY_BASIC_INFORMATION mbi;
    if (VirtualQuery(p, &mbi, sizeof(mbi)) == 0)
    {
        return false;
    }
    if (mbi.State != MEM_COMMIT)
    {
        return false;
    }
    return (mbi.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;
}

// ---- 写入相对跳转 ---------------------------------------------------------

bool WriteRelJump(uint8_t* from, void* to)
{
    DWORD oldProtect = 0;
    if (!VirtualProtect(from, 5, PAGE_EXECUTE_READWRITE, &oldProtect))
    {
        return false;
    }

    from[0] = 0xE9;
    int32_t rel = static_cast<int32_t>(static_cast<uint8_t*>(to) - (from + 5));
    std::memcpy(from + 1, &rel, sizeof(rel));

    VirtualProtect(from, 5, oldProtect, &oldProtect);
    FlushInstructionCache(GetCurrentProcess(), from, 5);
    return true;
}

// ---- 安装单个 detour ------------------------------------------------------

bool InstallDetour(void* target, void* hook, void** outTrampoline)
{
    if (target == nullptr || hook == nullptr || outTrampoline == nullptr)
    {
        return false;
    }

    if (!IsExecutableMemory(target))
    {
        logging::Warn("同步钩子：目标地址不可执行 0x%p", target);
        return false;
    }

    uint8_t* code = static_cast<uint8_t*>(target);
    logging::Info("同步钩子：安装 detour 0x%p -> 0x%p（首字节 0x%02X）",
                  target, hook, code[0]);

    // 复制至少 5 字节的完整指令（对齐指令边界）。
    size_t total = 0;
    while (total < 5)
    {
        size_t len = DecodeX86Len(code + total, 16 - total);
        if (len == 0)
        {
            logging::Warn("同步钩子：无法解码原方法序言（offset=%zu）", total);
            return false;
        }
        total += len;
        if (total > 32)
        {
            logging::Warn("同步钩子：原方法序言异常过长（%zu 字节）", total);
            return false;
        }
    }

    uint8_t* trampoline = static_cast<uint8_t*>(
        VirtualAlloc(nullptr, total + 5, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (trampoline == nullptr)
    {
        return false;
    }

    std::memcpy(trampoline, code, total);
    trampoline[total] = 0xE9;
    int32_t backRel = static_cast<int32_t>((code + total) - (trampoline + total + 5));
    std::memcpy(trampoline + total + 1, &backRel, sizeof(backRel));
    FlushInstructionCache(GetCurrentProcess(), trampoline, total + 5);

    if (!WriteRelJump(code, hook))
    {
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return false;
    }

    *outTrampoline = trampoline;
    return true;
}

// ---- 查找托管方法 ---------------------------------------------------------

void* FindMethod(void* image, const char* nameSpace, const char* className, const char* methodName, int paramCount)
{
    if (image == nullptr)
    {
        return nullptr;
    }

    void* klass = g_mono.class_from_name(image, nameSpace, className);
    if (klass == nullptr)
    {
        logging::Warn("同步钩子：未找到类型 %s.%s", nameSpace, className);
        return nullptr;
    }

    void* method = g_mono.class_get_method(klass, methodName, paramCount);
    if (method == nullptr)
    {
        logging::Warn("同步钩子：未找到方法 %s.%s::%s/%d", nameSpace, className, methodName, paramCount);
        return nullptr;
    }
    return method;
}

} // namespace

// ---- 分发器（由汇编桩调用） ------------------------------------------------

extern "C" int __cdecl YxMod5SyncDispatch(void* arg1, void* arg2, int kind)
{
#if defined(_M_IX86)
    if (!g_mono.IsHookReady())
    {
        return 0;
    }

    // 接收可能发生在网络工作线程，需先附加到 Mono。
    void* domain = g_mono.get_root_domain();
    if (domain != nullptr)
    {
        g_mono.thread_attach(domain);
    }

    void* method = (kind == 0) ? g_hookServerMethod : g_hookClientMethod;
    if (method == nullptr)
    {
        return 0;
    }

    void* params[2] = { arg1, arg2 };
    void* exception = nullptr;
    void* result = g_mono.runtime_invoke(method, nullptr, params, &exception);
    if (exception != nullptr)
    {
        logging::Error("同步钩子：托管分发抛出异常");
        return 0;
    }
    if (result == nullptr || g_mono.object_unbox == nullptr)
    {
        return 0;
    }

    // 托管返回 int（0 = 未消费，1 = 已消费自定义消息）。
    int handled = *static_cast<int*>(g_mono.object_unbox(result));
    return handled ? 1 : 0;
#else
    (void)arg1; (void)arg2; (void)kind;
    return 0;
#endif
}

// ---- 安装入口 -------------------------------------------------------------

bool InstallSyncHooks(MonoApi& mono, const char* modAssemblyPath)
{
#if !defined(_M_IX86)
    logging::Info("同步钩子：非 32 位构建，跳过安装（当前游戏为 32 位）");
    return false;
#else
    if (!mono.IsHookReady())
    {
        logging::Warn("同步钩子：Mono 钩子所需 API 不可用，跳过安装");
        return false;
    }

    g_mono = mono;

    void* domain = mono.get_root_domain();
    if (domain == nullptr)
    {
        logging::Error("同步钩子：获取根域失败");
        return false;
    }
    mono.thread_attach(domain);

    // 1) 打开游戏程序集（Assembly-CSharp.dll）。
    std::wstring gameDir = logging::GetProcessDirectory();
    std::wstring gameAssemblyPath = gameDir + L"\\Human_Data\\Managed\\Assembly-CSharp.dll";
    std::string gameAssemblyUtf8 = WideToUtf8(gameAssemblyPath);

    void* gameAssembly = mono.domain_assembly_open(domain, gameAssemblyUtf8.c_str());
    if (gameAssembly == nullptr)
    {
        logging::Error("同步钩子：打开游戏程序集失败：%s", gameAssemblyUtf8.c_str());
        return false;
    }
    void* gameImage = mono.assembly_get_image(gameAssembly);

    // 2) 打开模组程序集（YxMod5.dll，此前已由 Entrypoint 加载）。
    void* modAssembly = mono.domain_assembly_open(domain, modAssemblyPath);
    if (modAssembly == nullptr)
    {
        logging::Error("同步钩子：打开模组程序集失败：%s", modAssemblyPath);
        return false;
    }
    void* modImage = mono.assembly_get_image(modAssembly);

    // 3) 解析原方法与托管分发方法。
    void* serverMethod = FindMethod(gameImage, "Multiplayer", "NetGame", "OnServerReceive", 2);
    void* clientMethod = FindMethod(gameImage, "Multiplayer", "NetGame", "OnClientReceive", 2);
    g_hookServerMethod = FindMethod(modImage, "YxMod5.Net", "SyncChannel", "NativeOnServerReceive", 2);
    g_hookClientMethod = FindMethod(modImage, "YxMod5.Net", "SyncChannel", "NativeOnClientReceive", 2);

    if (serverMethod == nullptr || clientMethod == nullptr ||
        g_hookServerMethod == nullptr || g_hookClientMethod == nullptr)
    {
        logging::Error("同步钩子：解析方法失败，跳过安装");
        return false;
    }

    // 4) 编译并取得原生代码地址。
    void* serverCode = mono.compile_method(serverMethod);
    void* clientCode = mono.compile_method(clientMethod);
    void* hookServerCode = mono.compile_method(g_hookServerMethod);
    void* hookClientCode = mono.compile_method(g_hookClientMethod);

    if (serverCode == nullptr || clientCode == nullptr ||
        hookServerCode == nullptr || hookClientCode == nullptr)
    {
        logging::Error("同步钩子：编译方法失败，跳过安装");
        return false;
    }

    logging::Info("同步钩子：方法地址 OnServerReceive=0x%p, OnClientReceive=0x%p",
                  serverCode, clientCode);

    // 5) 安装 detour（汇编桩为跳转目标）。
    bool serverOk = InstallDetour(serverCode, reinterpret_cast<void*>(&YxMod5OnServerReceiveHook),
                                  &g_pfnOnServerReceiveTrampoline);
    bool clientOk = InstallDetour(clientCode, reinterpret_cast<void*>(&YxMod5OnClientReceiveHook),
                                  &g_pfnOnClientReceiveTrampoline);

    if (serverOk)
    {
        logging::Info("同步钩子：OnServerReceive 已挂钩");
    }
    if (clientOk)
    {
        logging::Info("同步钩子：OnClientReceive 已挂钩");
    }
    if (!serverOk || !clientOk)
    {
        logging::Warn("同步钩子：部分安装失败（server=%d client=%d）", serverOk ? 1 : 0, clientOk ? 1 : 0);
    }

    return serverOk || clientOk;
#endif
}

} // namespace yxmod5
