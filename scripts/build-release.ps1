<#
.SYNOPSIS
    Builds the WRApperture release packages (host and client) as self-contained x64 executables.

.EXAMPLE
    .\scripts\build-release.ps1
    .\scripts\build-release.ps1 -Version 0.2.0 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutDir,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Runtime = 'win-x64'

function Invoke-Native([string]$Description, [scriptblock]$Command) {
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed (exit code $LASTEXITCODE)." }
}

# Windows PowerShell 5.1 reads BOM-less scripts as ANSI, which garbles PT-BR messages.
function Copy-ScriptWithBom([string]$Source, [string]$Destination) {
    $text = [IO.File]::ReadAllText($Source, [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText($Destination, $text, (New-Object Text.UTF8Encoding($true)))
}

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
}

# $PSScriptRoot is not available in param defaults on Windows PowerShell 5.1.
if (-not $OutDir) { $OutDir = Join-Path $root 'dist' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
$publishDir = Join-Path $OutDir 'publish'
$hostDir = Join-Path $OutDir "WRApperture-Host-$Version"
$clientDir = Join-Path $OutDir "WRApperture-Client-$Version"
Remove-Item -Recurse -Force $publishDir, $hostDir, $clientDir, "$hostDir.zip", "$clientDir.zip" -ErrorAction SilentlyContinue

if (-not $SkipTests) {
    Invoke-Native 'Running tests' { dotnet test (Join-Path $root 'WRApperture.sln') -c Release --nologo -v q }
}

$projects = @(
    @{ Name = 'wrahost'; Path = 'src\WRApperture.Host.Cli\WRApperture.Host.Cli.csproj' },
    @{ Name = 'agent'; Path = 'src\WRApperture.Host.Agent\WRApperture.Host.Agent.csproj' },
    @{ Name = 'client'; Path = 'src\WRApperture.Client\WRApperture.Client.csproj' }
)
foreach ($p in $projects) {
    $out = Join-Path $publishDir $p.Name
    Invoke-Native "Publishing $($p.Name)" {
        dotnet publish (Join-Path $root $p.Path) -c Release -r $Runtime --self-contained true -o $out --nologo -v q `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
            -p:DebugType=none -p:Version=$Version
    }
}

Write-Host '==> Assembling host package' -ForegroundColor Cyan
New-Item -ItemType Directory -Force $hostDir | Out-Null
Copy-Item (Join-Path $publishDir 'wrahost\wrahost.exe') $hostDir
Copy-Item (Join-Path $publishDir 'agent\WRApperture.Agent.exe'), (Join-Path $publishDir 'agent\appsettings.json') $hostDir
Copy-Item (Join-Path $root 'packaging\host\Instalar.cmd'), (Join-Path $root 'packaging\host\Desinstalar.cmd'), (Join-Path $root 'packaging\host\LEIA-ME.txt') $hostDir
foreach ($script in 'Install-WRAppertureHost.ps1', 'Uninstall-WRAppertureHost.ps1') {
    Copy-ScriptWithBom (Join-Path $root "packaging\host\$script") (Join-Path $hostDir $script)
}

Write-Host '==> Assembling client package' -ForegroundColor Cyan
New-Item -ItemType Directory -Force $clientDir | Out-Null
Copy-Item (Join-Path $publishDir 'client\WRApperture.exe') $clientDir
Copy-Item (Join-Path $root 'packaging\client\Instalar.cmd'), (Join-Path $root 'packaging\client\Desinstalar.cmd') $clientDir
foreach ($script in 'Install-WRAppertureClient.ps1', 'Uninstall-WRAppertureClient.ps1') {
    Copy-ScriptWithBom (Join-Path $root "packaging\client\$script") (Join-Path $clientDir $script)
}

Compress-Archive -Path (Join-Path $hostDir '*') -DestinationPath "$hostDir.zip"
Compress-Archive -Path (Join-Path $clientDir '*') -DestinationPath "$clientDir.zip"
Remove-Item -Recurse -Force $publishDir

Write-Host ''
Write-Host "Release $Version ready:" -ForegroundColor Green
Get-ChildItem $OutDir -Filter '*.zip' | ForEach-Object { '  {0}  ({1:N1} MB)' -f $_.FullName, ($_.Length / 1MB) }
