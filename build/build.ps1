#Requires -Version 5.1
<#
.SYNOPSIS
    构建 YxMod5（原生启动层 + C# 模组层）并收集发布产物。

.DESCRIPTION
    1. 按需生成 winhttp 转发代码（AheadLib 跳转桩 / DEF / 头文件）；
    2. 构建原生启动层 winhttp.dll；
    3. 构建 C# 模组 YxMod5.dll；
    4. 将产物复制到 release/ 目录；
    5. 可选：直接部署到游戏目录（-Deploy）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build.ps1 -Deploy `
        -GameDir "F:\SteamLibrary\steamapps\common\Human Fall Flat"
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # 注意：《人类一败涂地》为 32 位（x86）游戏，故默认 Win32。
    [ValidateSet('x64', 'Win32')]
    [string]$Platform = 'Win32',

    # 游戏托管程序集目录；缺省时由 csproj 中的默认值决定。
    [string]$GameManagedDir,

    # 游戏根目录（配合 -Deploy 使用）。
    [string]$GameDir,

    [switch]$Deploy,
    [switch]$RegenerateForwards,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = [System.IO.Path]::GetFullPath((Join-Path $scriptDir '..'))
$releaseDir = Join-Path $repoRoot 'release'

function Write-Step([string]$message) {
    Write-Host ("`n=== {0} ===" -f $message) -ForegroundColor Cyan
}

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $found = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($found) { return $found }
    }
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw '未找到 MSBuild，请安装 Visual Studio（含 C++ 与 .NET 桌面开发负载）。'
}

# 读取 PE 可执行文件架构（x86 / x64）。
function Get-PEArchitecture([string]$exePath) {
    try {
        $bytes = [System.IO.File]::ReadAllBytes($exePath)
        if ($bytes.Length -lt 0x40) { return $null }
        $peOffset = [System.BitConverter]::ToInt32($bytes, 0x3C)
        if ($peOffset -le 0 -or ($peOffset + 6) -ge $bytes.Length) { return $null }
        $machine = [System.BitConverter]::ToUInt16($bytes, $peOffset + 4)
        switch ($machine) {
            0x014c { return 'x86' }
            0x8664 { return 'x64' }
            default { return ('0x{0:X}' -f $machine) }
        }
    }
    catch { return $null }
}

# ---------------------------------------------------------------------------
# 1) 生成 winhttp 转发代码
# ---------------------------------------------------------------------------
$forwardsHeader = Join-Path $repoRoot 'src\launcher\generated\winhttp_forwards.h'
if ($RegenerateForwards -or -not (Test-Path -LiteralPath $forwardsHeader)) {
    Write-Step '生成 winhttp 转发代码'
    & (Join-Path $scriptDir 'Generate-WinHttpForwards.ps1')
}

# ---------------------------------------------------------------------------
# 2) 清理
# ---------------------------------------------------------------------------
if ($Clean) {
    Write-Step '清理构建产物'
    foreach ($path in @(
        (Join-Path $repoRoot 'build\launcher'),
        (Join-Path $repoRoot 'build\obj'),
        (Join-Path $repoRoot 'build\mod'),
        (Join-Path $repoRoot 'src\mod\bin'),
        (Join-Path $repoRoot 'src\mod\obj')
    )) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}

# ---------------------------------------------------------------------------
# 3) 构建
# ---------------------------------------------------------------------------
$msbuild = Find-MSBuild
Write-Host ("MSBuild: {0}" -f $msbuild) -ForegroundColor DarkGray

Write-Step '构建原生启动层 winhttp.dll'
$launcherProj = Join-Path $repoRoot 'src\launcher\YxMod5Launcher.vcxproj'
& $msbuild $launcherProj "/p:Configuration=$Configuration" "/p:Platform=$Platform" /m /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "原生启动层构建失败（退出码 $LASTEXITCODE）" }

Write-Step '构建 C# 模组 YxMod5.dll'
$modProj = Join-Path $repoRoot 'src\mod\YxMod5.csproj'
$modArgs = @($modProj, "/p:Configuration=$Configuration", '/p:Platform=AnyCPU', '/m', '/v:minimal', '/nologo')
if ($GameManagedDir) { $modArgs += "/p:GameManagedDir=$GameManagedDir" }
& $msbuild @modArgs
if ($LASTEXITCODE -ne 0) { throw "C# 模组构建失败（退出码 $LASTEXITCODE）" }

# ---------------------------------------------------------------------------
# 4) 收集发布产物
# ---------------------------------------------------------------------------
Write-Step '收集发布产物'
if (-not (Test-Path -LiteralPath $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}

$nativeOut = Join-Path $repoRoot ("build\launcher\{0}\{1}" -f $Platform, $Configuration)
$modOut    = Join-Path $repoRoot ("build\mod\{0}" -f $Configuration)

$artifacts = @(
    (Join-Path $nativeOut 'winhttp.dll'),
    (Join-Path $modOut 'YxMod5.dll')
)
foreach ($artifact in $artifacts) {
    if (-not (Test-Path -LiteralPath $artifact)) {
        throw "缺少构建产物：$artifact"
    }
    Copy-Item -LiteralPath $artifact -Destination $releaseDir -Force
    Write-Host ("  + {0}" -f (Split-Path -Leaf $artifact)) -ForegroundColor Green
}

# 可选：复制调试符号，便于排查问题。
foreach ($pdb in @(
    (Join-Path $nativeOut 'winhttp.pdb'),
    (Join-Path $modOut 'YxMod5.pdb')
)) {
    if (Test-Path -LiteralPath $pdb) {
        Copy-Item -LiteralPath $pdb -Destination $releaseDir -Force
    }
}

# ---------------------------------------------------------------------------
# 5) 可选部署
# ---------------------------------------------------------------------------
if ($Deploy) {
    if (-not $GameDir) {
        throw '使用 -Deploy 时必须提供 -GameDir <游戏根目录>。'
    }
    if (-not (Test-Path -LiteralPath $GameDir)) {
        throw "游戏目录不存在：$GameDir"
    }

    $running = Get-Process -Name 'Human' -ErrorAction SilentlyContinue
    if ($running) {
        throw '检测到 Human.exe 正在运行，请先关闭游戏再部署。'
    }

    # 校验架构：代理 DLL 必须与游戏进程位数一致。
    $hostExe = Join-Path $GameDir 'Human.exe'
    if (Test-Path -LiteralPath $hostExe) {
        $arch = Get-PEArchitecture $hostExe
        $required = if ($arch -eq 'x86') { 'Win32' } elseif ($arch -eq 'x64') { 'x64' } else { $null }
        if ($required -and $required -ne $Platform) {
            throw ("游戏为 {0} 架构，需要 -Platform {1}（当前为 {2}）。" -f $arch, $required, $Platform)
        }
        Write-Host ("  游戏架构：{0}（{1}）" -f $arch, $Platform) -ForegroundColor DarkGray
    }

    Write-Step "部署到 $GameDir"
    foreach ($name in @('winhttp.dll', 'YxMod5.dll')) {
        $src = Join-Path $releaseDir $name
        Copy-Item -LiteralPath $src -Destination (Join-Path $GameDir $name) -Force
        Write-Host ("  -> {0}" -f $name) -ForegroundColor Green
    }
}

Write-Host "`n构建完成，产物目录：$releaseDir" -ForegroundColor Cyan
