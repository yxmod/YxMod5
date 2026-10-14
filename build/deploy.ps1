#Requires -Version 5.1
<#
.SYNOPSIS
    构建并部署 YxMod5 到游戏目录（快捷方式）。

.DESCRIPTION
    《人类一败涂地》为 32 位（x86）游戏，本脚本固定使用 Win32 平台，
    构建后自动复制 winhttp.dll 与 YxMod5.dll 到游戏根目录。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\deploy.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\deploy.ps1 `
        -GameDir "D:\Steam\steamapps\common\Human Fall Flat"
#>
[CmdletBinding()]
param(
    [string]$GameDir = 'F:\SteamLibrary\steamapps\common\Human Fall Flat',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'build.ps1') `
    -Configuration $Configuration `
    -Platform Win32 `
    -Deploy `
    -GameDir $GameDir
