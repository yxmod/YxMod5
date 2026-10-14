#Requires -Version 5.1
<#
.SYNOPSIS
    生成 winhttp.dll 代理转发所需的源码（AheadLib 风格）。

.DESCRIPTION
    YxMod5 的原生启动层以 winhttp.dll 作为代理宿主（AheadLib 转发）。
    本脚本读取 winhttp.dll 的导出清单（名称 + 序号），生成：
      * generated/winhttp_forwards.h     —— 导出 X-Macro 清单、函数指针声明、解析宏
      * generated/winhttp_thunks_x64.asm —— x64 跳转桩（jmp qword ptr [g_pfn_X]）
      * generated/winhttp_thunks_x86.asm —— x86 跳转桩（jmp dword ptr [_g_pfn_X]）
      * generated/winhttp_x64.def        —— x64 导出别名（Name=AheadLib_Name @ord）
      * generated/winhttp_x86.def        —— x86 导出别名（Name=AheadLib_Name @ord）

    注意：32 位（SysWOW64）与 64 位（System32）winhttp.dll 的导出名称一致，
    但个别序号不同（本脚本记录 x86 序号覆盖表）。请以目标系统实际导出为准，
    可用 `dumpbin /exports` 核对后维护 $exports / $x86OrdinalOverrides。

.NOTES
    导出清单必须与目标系统 winhttp.dll 完全一致（含序号），否则依赖 ordinal
    导入 winhttp 的程序可能解析失败。名称导入不受影响。
#>
[CmdletBinding()]
param(
    [string]$OutputDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# winhttp.dll 导出清单（名称, 序号）。序号默认取 64 位 System32 的值。
# ---------------------------------------------------------------------------
$exports = @(
    @{ Name = 'WinHttpPacJsWorkerMain';                  Ordinal = 1  },
    @{ Name = 'WinHttpSetSecureLegacyServersAppCompat';  Ordinal = 2  },
    @{ Name = 'DllCanUnloadNow';                         Ordinal = 3  },
    @{ Name = 'DllGetClassObject';                       Ordinal = 4  },
    @{ Name = 'Private1';                                Ordinal = 5  },
    @{ Name = 'SvchostPushServiceGlobals';               Ordinal = 6  },
    @{ Name = 'WinHttpAddRequestHeaders';                Ordinal = 7  },
    @{ Name = 'WinHttpAddRequestHeadersEx';              Ordinal = 8  },
    @{ Name = 'WinHttpAutoProxySvcMain';                 Ordinal = 9  },
    @{ Name = 'WinHttpCheckPlatform';                    Ordinal = 10 },
    @{ Name = 'WinHttpCloseHandle';                      Ordinal = 11 },
    @{ Name = 'WinHttpConnect';                          Ordinal = 12 },
    @{ Name = 'WinHttpConnectionDeletePolicyEntries';    Ordinal = 13 },
    @{ Name = 'WinHttpConnectionDeleteProxyInfo';        Ordinal = 14 },
    @{ Name = 'WinHttpConnectionFreeNameList';           Ordinal = 15 },
    @{ Name = 'WinHttpConnectionFreeProxyInfo';          Ordinal = 16 },
    @{ Name = 'WinHttpConnectionFreeProxyList';          Ordinal = 17 },
    @{ Name = 'WinHttpConnectionGetNameList';            Ordinal = 18 },
    @{ Name = 'WinHttpConnectionGetProxyInfo';           Ordinal = 19 },
    @{ Name = 'WinHttpConnectionGetProxyList';           Ordinal = 20 },
    @{ Name = 'WinHttpConnectionSetPolicyEntries';       Ordinal = 21 },
    @{ Name = 'WinHttpConnectionSetProxyInfo';           Ordinal = 22 },
    @{ Name = 'WinHttpConnectionUpdateIfIndexTable';     Ordinal = 23 },
    @{ Name = 'WinHttpCrackUrl';                         Ordinal = 24 },
    @{ Name = 'WinHttpCreateProxyResolver';              Ordinal = 25 },
    @{ Name = 'WinHttpCreateUrl';                        Ordinal = 26 },
    @{ Name = 'WinHttpDetectAutoProxyConfigUrl';         Ordinal = 27 },
    @{ Name = 'WinHttpFreeProxyResult';                  Ordinal = 28 },
    @{ Name = 'WinHttpFreeProxyResultEx';                Ordinal = 29 },
    @{ Name = 'WinHttpFreeProxySettings';                Ordinal = 30 },
    @{ Name = 'WinHttpGetDefaultProxyConfiguration';     Ordinal = 31 },
    @{ Name = 'WinHttpGetIEProxyConfigForCurrentUser';   Ordinal = 32 },
    @{ Name = 'WinHttpGetProxyForUrl';                   Ordinal = 33 },
    @{ Name = 'WinHttpGetProxyForUrlEx';                 Ordinal = 34 },
    @{ Name = 'WinHttpGetProxyForUrlEx2';                Ordinal = 35 },
    @{ Name = 'WinHttpGetProxyForUrlHvsi';               Ordinal = 36 },
    @{ Name = 'WinHttpGetProxyResult';                   Ordinal = 37 },
    @{ Name = 'WinHttpGetProxyResultEx';                 Ordinal = 38 },
    @{ Name = 'WinHttpGetProxySettingsVersion';          Ordinal = 39 },
    @{ Name = 'WinHttpGetTunnelSocket';                  Ordinal = 40 },
    @{ Name = 'WinHttpOpen';                             Ordinal = 41 },
    @{ Name = 'WinHttpOpenRequest';                      Ordinal = 42 },
    @{ Name = 'WinHttpProbeConnectivity';                Ordinal = 43 },
    @{ Name = 'WinHttpQueryAuthSchemes';                 Ordinal = 44 },
    @{ Name = 'WinHttpQueryDataAvailable';               Ordinal = 45 },
    @{ Name = 'WinHttpQueryHeaders';                     Ordinal = 46 },
    @{ Name = 'WinHttpQueryOption';                      Ordinal = 47 },
    @{ Name = 'WinHttpReadData';                         Ordinal = 48 },
    @{ Name = 'WinHttpReadProxySettings';                Ordinal = 49 },
    @{ Name = 'WinHttpReadProxySettingsHvsi';            Ordinal = 50 },
    @{ Name = 'WinHttpReceiveResponse';                  Ordinal = 51 },
    @{ Name = 'WinHttpResetAutoProxy';                   Ordinal = 52 },
    @{ Name = 'WinHttpSaveProxyCredentials';             Ordinal = 53 },
    @{ Name = 'WinHttpSendRequest';                      Ordinal = 54 },
    @{ Name = 'WinHttpSetCredentials';                   Ordinal = 55 },
    @{ Name = 'WinHttpSetDefaultProxyConfiguration';     Ordinal = 56 },
    @{ Name = 'WinHttpSetOption';                        Ordinal = 57 },
    @{ Name = 'WinHttpSetProxySettingsPerUser';          Ordinal = 58 },
    @{ Name = 'WinHttpSetStatusCallback';                Ordinal = 59 },
    @{ Name = 'WinHttpSetTimeouts';                      Ordinal = 60 },
    @{ Name = 'WinHttpTimeFromSystemTime';               Ordinal = 61 },
    @{ Name = 'WinHttpTimeToSystemTime';                 Ordinal = 62 },
    @{ Name = 'WinHttpWebSocketClose';                   Ordinal = 63 },
    @{ Name = 'WinHttpWebSocketCompleteUpgrade';         Ordinal = 64 },
    @{ Name = 'WinHttpWebSocketQueryCloseStatus';        Ordinal = 65 },
    @{ Name = 'WinHttpWebSocketReceive';                 Ordinal = 66 },
    @{ Name = 'WinHttpWebSocketSend';                    Ordinal = 67 },
    @{ Name = 'WinHttpWebSocketShutdown';                Ordinal = 68 },
    @{ Name = 'WinHttpWriteData';                        Ordinal = 69 },
    @{ Name = 'WinHttpWriteProxySettings';               Ordinal = 70 }
)

# x86（SysWOW64）与 x64（System32）序号差异项。经 Windows 10/11 实测：
#   x86:  34 = WinHttpGetProxyForUrlEx2, 35 = WinHttpGetProxyForUrlEx
$x86OrdinalOverrides = @{
    'WinHttpGetProxyForUrlEx2' = 34
    'WinHttpGetProxyForUrlEx'  = 35
}

if ([string]::IsNullOrEmpty($OutputDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutputDir = Join-Path $scriptDir '..\src\launcher\generated'
}
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

function Get-Ordinal {
    param([hashtable]$Export, [switch]$X64)
    if (-not $X64 -and $x86OrdinalOverrides.ContainsKey($Export.Name)) {
        return $x86OrdinalOverrides[$Export.Name]
    }
    return $Export.Ordinal
}

# ---------------------------------------------------------------------------
# 1) winhttp_forwards.h
# ---------------------------------------------------------------------------
$h = New-Object System.Text.StringBuilder
[void]$h.AppendLine('// ===========================================================================')
[void]$h.AppendLine('// AUTO-GENERATED by build/Generate-WinHttpForwards.ps1. DO NOT EDIT.')
[void]$h.AppendLine('// winhttp.dll 导出转发清单（X-Macro）。')
[void]$h.AppendLine('// ===========================================================================')
[void]$h.AppendLine('#pragma once')
[void]$h.AppendLine()
[void]$h.AppendLine('// X(Name)')
[void]$h.AppendLine('#define YXMOD5_WINHTTP_FORWARD_LIST(X) \')
for ($i = 0; $i -lt $exports.Count; $i++) {
    $suffix = if ($i -eq $exports.Count - 1) { '' } else { ' \' }
    [void]$h.AppendLine(("    X({0}){1}" -f $exports[$i].Name, $suffix))
}
[void]$h.AppendLine()
[void]$h.AppendLine('// 真实 winhttp.dll 中的函数地址（由 forward.cpp 在 DllMain 中解析）。')
[void]$h.AppendLine('#define YXMOD5_DECLARE_PFN(name) extern "C" FARPROC g_pfn_##name;')
[void]$h.AppendLine('YXMOD5_WINHTTP_FORWARD_LIST(YXMOD5_DECLARE_PFN)')
[void]$h.AppendLine('#undef YXMOD5_DECLARE_PFN')
[void]$h.AppendLine()
[void]$h.AppendLine('// 说明：导出别名由 generated/winhttp_x64.def 与 winhttp_x86.def 提供，')
[void]$h.AppendLine('//       链接器据此把 winhttp 的导出名指向 asm 中的 AheadLib_* 跳转桩。')
Set-Content -LiteralPath (Join-Path $OutputDir 'winhttp_forwards.h') -Value $h.ToString() -Encoding UTF8

# ---------------------------------------------------------------------------
# 2) asm thunks
# ---------------------------------------------------------------------------
function New-Thunks {
    param([switch]$X64)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('; ===========================================================================')
    [void]$sb.AppendLine('; AUTO-GENERATED by build/Generate-WinHttpForwards.ps1. DO NOT EDIT.')
    if ($X64) {
        [void]$sb.AppendLine('; x64 thunks: jmp qword ptr [g_pfn_Name]')
    } else {
        [void]$sb.AppendLine('; x86 thunks: jmp dword ptr [_g_pfn_Name]')
    }
    [void]$sb.AppendLine('; ===========================================================================')
    [void]$sb.AppendLine()
    if (-not $X64) {
        [void]$sb.AppendLine('.386')
        [void]$sb.AppendLine('.model flat')
        [void]$sb.AppendLine('option casemap:none')
        [void]$sb.AppendLine()
    }
    foreach ($e in $exports) {
        if ($X64) {
            [void]$sb.AppendLine(("extern g_pfn_{0}:QWORD" -f $e.Name))
        } else {
            [void]$sb.AppendLine(("extern _g_pfn_{0}:DWORD" -f $e.Name))
        }
    }
    [void]$sb.AppendLine()
    foreach ($e in $exports) {
        if ($X64) {
            [void]$sb.AppendLine(("PUBLIC AheadLib_{0}" -f $e.Name))
        } else {
            [void]$sb.AppendLine(("PUBLIC _AheadLib_{0}" -f $e.Name))
        }
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('.code')
    [void]$sb.AppendLine()
    foreach ($e in $exports) {
        if ($X64) {
            [void]$sb.AppendLine(("AheadLib_{0} PROC" -f $e.Name))
            [void]$sb.AppendLine(("    jmp qword ptr [g_pfn_{0}]" -f $e.Name))
            [void]$sb.AppendLine(("AheadLib_{0} ENDP" -f $e.Name))
        } else {
            [void]$sb.AppendLine(("_AheadLib_{0} PROC" -f $e.Name))
            [void]$sb.AppendLine(("    jmp dword ptr [_g_pfn_{0}]" -f $e.Name))
            [void]$sb.AppendLine(("_AheadLib_{0} ENDP" -f $e.Name))
        }
        [void]$sb.AppendLine()
    }
    [void]$sb.AppendLine('END')
    return $sb.ToString()
}

Set-Content -LiteralPath (Join-Path $OutputDir 'winhttp_thunks_x64.asm') -Value (New-Thunks -X64) -Encoding ASCII
Set-Content -LiteralPath (Join-Path $OutputDir 'winhttp_thunks_x86.asm') -Value (New-Thunks) -Encoding ASCII

# ---------------------------------------------------------------------------
# 3) def files
# ---------------------------------------------------------------------------
function New-Def {
    param([switch]$X64)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('; ===========================================================================')
    [void]$sb.AppendLine('; AUTO-GENERATED by build/Generate-WinHttpForwards.ps1. DO NOT EDIT.')
    [void]$sb.AppendLine('; ===========================================================================')
    [void]$sb.AppendLine('LIBRARY winhttp')
    [void]$sb.AppendLine('EXPORTS')
    foreach ($e in $exports) {
        # 注意：DEF 中的目标符号不要写前导下划线。MSVC 链接器会按自身规则
        # 为 x86 目标自动解析 _AheadLib_Name，写了下划线反而会去找
        # __AheadLib_Name 从而报 LNK2001。
        $ordinal = Get-Ordinal -Export $e -X64:$X64
        [void]$sb.AppendLine(("    {0}=AheadLib_{0} @{1}" -f $e.Name, $ordinal))
    }
    return $sb.ToString()
}

Set-Content -LiteralPath (Join-Path $OutputDir 'winhttp_x64.def') -Value (New-Def -X64) -Encoding ASCII
Set-Content -LiteralPath (Join-Path $OutputDir 'winhttp_x86.def') -Value (New-Def) -Encoding ASCII

Write-Host ("[YxMod5] 已生成 {0} 个 winhttp 导出的转发代码 -> {1}" -f $exports.Count, $OutputDir)
