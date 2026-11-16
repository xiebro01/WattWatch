<#
.SYNOPSIS
    Builds a runnable WattWatch build.

.DESCRIPTION
    Default output is a self-contained single-file win-x64 build under .\publish.
    Pass -FrameworkDependent for a small build that reuses the installed .NET 8
    Desktop Runtime.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
    powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -FrameworkDependent
#>
param(
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$output = Join-Path $root 'publish'

function Resolve-Dotnet {
    $candidates = @(
        (Join-Path $root '.dotnet\dotnet.exe'),
        'D:\tools\dotnet\dotnet.exe',
        'C:\Program Files\dotnet\dotnet.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw '找不到 .NET 8 SDK，请先安装：https://dotnet.microsoft.com/download/dotnet/8.0'
}

$dotnet = Resolve-Dotnet
$project = Join-Path $root 'src\WattWatch.App\WattWatch.App.csproj'

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

$arguments = @('publish', $project, '-c', 'Release', '-r', $Runtime, '-o', $output, '-p:Platform=x64')

if ($FrameworkDependent) {
    $arguments += '--self-contained'
    $arguments += 'false'
} else {
    $arguments += '--self-contained'
    $arguments += 'true'
    $arguments += '-p:PublishSingleFile=true'
    $arguments += '-p:IncludeNativeLibrariesForSelfExtract=true'
    $arguments += '-p:EnableCompressionInSingleFile=true'
}

Write-Host "使用 $dotnet" -ForegroundColor DarkGray
& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "publish 失败（exit $LASTEXITCODE）" }

Write-Host ''
Write-Host "输出：$output\WattWatch.exe" -ForegroundColor Green
