; ===========================================================================
; YxMod5 原生启动层 —— 同步协议接收钩子汇编桩（Win32 / x86）
; ---------------------------------------------------------------------------
; Mono x86 托管调用约定（本模块假设）：this 在 ECX，其余参数在栈上（右到左）。
;   方法入口：ECX = this（NetGame），[ESP+4] = arg1，[ESP+8] = arg2。
;
; 流程：
;   1. 保存被调用方寄存器与 this；
;   2. 调用 YxMod5SyncDispatch(arg1, arg2, kind)，由它通过 mono_runtime_invoke
;      调用托管 SyncChannel.NativeOnServerReceive / NativeOnClientReceive；
;   3. 返回 1 = 已消费自定义消息 -> ret；
;      返回 0 = 非自定义消息 -> 以原始寄存器/栈状态跳回 trampoline 继续原逻辑。
; ===========================================================================

.386
.model flat
option casemap:none

extern _YxMod5SyncDispatch:PROC
extern _g_pfnOnServerReceiveTrampoline:DWORD
extern _g_pfnOnClientReceiveTrampoline:DWORD

PUBLIC _YxMod5OnServerReceiveHook
PUBLIC _YxMod5OnClientReceiveHook

.code

_YxMod5OnServerReceiveHook PROC
    push ebp
    mov ebp, esp
    push ebx
    push esi
    push edi
    mov ebx, ecx                        ; 保存 this（NetGame），供 trampoline 使用
    push 0                              ; kind = server
    push dword ptr [ebp+12]             ; arg2 = stream
    push dword ptr [ebp+8]              ; arg1 = client
    call _YxMod5SyncDispatch
    add esp, 12
    mov ecx, ebx                        ; 恢复 this
    test eax, eax
    jnz server_handled
    pop edi
    pop esi
    pop ebx
    pop ebp
    jmp dword ptr [_g_pfnOnServerReceiveTrampoline]
server_handled:
    pop edi
    pop esi
    pop ebx
    pop ebp
    ret
_YxMod5OnServerReceiveHook ENDP

_YxMod5OnClientReceiveHook PROC
    push ebp
    mov ebp, esp
    push ebx
    push esi
    push edi
    mov ebx, ecx                        ; 保存 this（NetGame）
    push 1                              ; kind = client
    push dword ptr [ebp+12]             ; arg2 = stream
    push dword ptr [ebp+8]              ; arg1 = connection
    call _YxMod5SyncDispatch
    add esp, 12
    mov ecx, ebx                        ; 恢复 this
    test eax, eax
    jnz client_handled
    pop edi
    pop esi
    pop ebx
    pop ebp
    jmp dword ptr [_g_pfnOnClientReceiveTrampoline]
client_handled:
    pop edi
    pop esi
    pop ebx
    pop ebp
    ret
_YxMod5OnClientReceiveHook ENDP

END
