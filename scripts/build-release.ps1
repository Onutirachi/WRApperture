<#
.SYNOPSIS
    Builds the WRApperture release: self-contained x64 executables packed into two graphical setups
    (Inno Setup) plus SHA256SUMS.txt — the files the GitHub release publishes and the apps update from.

.DESCRIPTION
    dist\WRApperture-Client-Setup-<v>.exe   per-user client setup
    dist\WRApperture-Host-Setup-<v>.exe     host setup (administrator)
    dist\SHA256SUMS.txt                     "<sha256>  <file>" per setup, checked by the automatic updates

    Needs the Inno Setup 6 compiler (ISCC.exe): on PATH, in its default folders, or given with -Iscc.

.EXAMPLE
    .\scripts\build-release.ps1
    .\scripts\build-release.ps1 -Version 0.2.0 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutDir,
    [string]$Iscc,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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

function Find-Iscc {
    if ($Iscc) { return $Iscc }
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))
    $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $found) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it (winget install JRSoftware.InnoSetup) or pass -Iscc.' }
    return $found
}

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
}

# $PSScriptRoot is not available in param defaults on Windows PowerShell 5.1.
if (-not $OutDir) { $OutDir = Join-Path $root 'dist' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
$publishDir = Join-Path $OutDir 'publish'
$stageDir = Join-Path $OutDir 'stage'
$hostDir = Join-Path $stageDir 'host'
$clientDir = Join-Path $stageDir 'client'
$hostSetup = Join-Path $OutDir "WRApperture-Host-Setup-$Version.exe"
$clientSetup = Join-Path $OutDir "WRApperture-Client-Setup-$Version.exe"
$sums = Join-Path $OutDir 'SHA256SUMS.txt'
Remove-Item -Recurse -Force $publishDir, $stageDir, $hostSetup, $clientSetup, $sums -ErrorAction SilentlyContinue
$isccPath = Find-Iscc

if (-not $SkipTests) {
    Invoke-Native 'Running tests' { dotnet test (Join-Path $root 'WRApperture.sln') -c Release --nologo -v q }
}

$projects = @(
    @{ Name = 'wrahost'; Path = 'src\WRApperture.Host.Cli\WRApperture.Host.Cli.csproj' },
    @{ Name = 'agent'; Path = 'src\WRApperture.Host.Agent\WRApperture.Host.Agent.csproj' },
    @{ Name = 'manager'; Path = 'src\WRApperture.Host.Manager\WRApperture.Host.Manager.csproj' },
    @{ Name = 'client'; Path = 'src\WRApperture.Client\WRApperture.Client.csproj' }
)
foreach ($p in $projects) {
    $out = Join-Path $publishDir $p.Name
    Invoke-Native "Publishing $($p.Name)" {
        dotnet publish (Join-Path $root $p.Path) -c Release -r $Runtime --self-contained true -o $out --nologo -v q `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false `
            -p:DebugType=none -p:Version=$Version
    }
}

# Script translations: the shared loader plus localization\<language>\installer.json for every language folder.
function Copy-InstallerLocalization([string]$Destination) {
    Copy-ScriptWithBom (Join-Path $root 'packaging\Localization.ps1') (Join-Path $Destination 'Localization.ps1')
    foreach ($language in Get-ChildItem (Join-Path $root 'localization') -Directory) {
        $catalog = Join-Path $language.FullName 'installer.json'
        if (Test-Path $catalog) {
            $target = Join-Path $Destination "localization\$($language.Name)"
            New-Item -ItemType Directory -Force $target | Out-Null
            Copy-Item $catalog $target
        }
    }
}

Write-Host '==> Staging host files' -ForegroundColor Cyan
New-Item -ItemType Directory -Force $hostDir | Out-Null
Copy-Item (Join-Path $publishDir 'wrahost\wrahost.exe'), (Join-Path $publishDir 'manager\WRApperture.Host.exe') $hostDir
Copy-Item (Join-Path $publishDir 'agent\WRApperture.Agent.exe'), (Join-Path $publishDir 'agent\appsettings.json') $hostDir
foreach ($script in 'Install-WRAppertureHost.ps1', 'Uninstall-WRAppertureHost.ps1') {
    Copy-ScriptWithBom (Join-Path $root "packaging\host\$script") (Join-Path $hostDir $script)
}
Copy-InstallerLocalization $hostDir

Write-Host '==> Staging client files' -ForegroundColor Cyan
New-Item -ItemType Directory -Force $clientDir | Out-Null
Copy-Item (Join-Path $publishDir 'client\WRApperture.exe') $clientDir
Copy-ScriptWithBom (Join-Path $root 'packaging\client\Uninstall-WRAppertureClient.ps1') (Join-Path $clientDir 'Uninstall-WRAppertureClient.ps1')
Copy-InstallerLocalization $clientDir

# Setup languages: every localization folder Inno Setup has a translation for, English first (the fallback).
# Their "setup.*" texts become Inno custom messages ("{0}" -> "%1", line breaks -> "%n").
$innoLanguages = [ordered]@{
    'en' = @('English', 'compiler:Default.isl'); 'pt' = @('BrazilianPortuguese', 'compiler:Languages\BrazilianPortuguese.isl')
    'pt-PT' = @('Portuguese', 'compiler:Languages\Portuguese.isl'); 'es' = @('Spanish', 'compiler:Languages\Spanish.isl')
    'fr' = @('French', 'compiler:Languages\French.isl'); 'de' = @('German', 'compiler:Languages\German.isl')
    'it' = @('Italian', 'compiler:Languages\Italian.isl'); 'nl' = @('Dutch', 'compiler:Languages\Dutch.isl')
    'pl' = @('Polish', 'compiler:Languages\Polish.isl'); 'ru' = @('Russian', 'compiler:Languages\Russian.isl')
    'uk' = @('Ukrainian', 'compiler:Languages\Ukrainian.isl'); 'tr' = @('Turkish', 'compiler:Languages\Turkish.isl')
    'cs' = @('Czech', 'compiler:Languages\Czech.isl'); 'da' = @('Danish', 'compiler:Languages\Danish.isl')
    'fi' = @('Finnish', 'compiler:Languages\Finnish.isl'); 'sv' = @('Swedish', 'compiler:Languages\Swedish.isl')
    'nb' = @('Norwegian', 'compiler:Languages\Norwegian.isl'); 'hu' = @('Hungarian', 'compiler:Languages\Hungarian.isl')
    'ja' = @('Japanese', 'compiler:Languages\Japanese.isl'); 'ko' = @('Korean', 'compiler:Languages\Korean.isl')
    'he' = @('Hebrew', 'compiler:Languages\Hebrew.isl'); 'ar' = @('Arabic', 'compiler:Languages\Arabic.isl')
    'ca' = @('Catalan', 'compiler:Languages\Catalan.isl'); 'sk' = @('Slovak', 'compiler:Languages\Slovak.isl')
    'sl' = @('Slovenian', 'compiler:Languages\Slovenian.isl'); 'bg' = @('Bulgarian', 'compiler:Languages\Bulgarian.isl')
}
function Read-SetupTexts([string]$Language) {
    $catalog = Join-Path $root "localization\$Language\installer.json"
    $texts = @{}
    if (Test-Path $catalog) {
        (Get-Content -Raw -Encoding UTF8 $catalog | ConvertFrom-Json).PSObject.Properties |
            Where-Object { $_.Name -like 'setup.*' } | ForEach-Object { $texts[$_.Name] = $_.Value }
    }
    return $texts
}
$english = Read-SetupTexts 'en'
$languageLines = @('[Languages]')
$messageLines = @('[CustomMessages]')
foreach ($code in $innoLanguages.Keys) {
    if (-not (Test-Path (Join-Path $root "localization\$code"))) { continue }
    $inno = $innoLanguages[$code]
    $languageLines += "Name: `"$($inno[0])`"; MessagesFile: `"$($inno[1])`""
    $texts = Read-SetupTexts $code
    foreach ($key in $english.Keys | Sort-Object) {
        $text = if ($texts.ContainsKey($key)) { $texts[$key] } else { $english[$key] }
        $text = [regex]::Replace($text, '\{(\d+)\}', { param($m) '%' + ([int]$m.Groups[1].Value + 1) }) -replace "`r?`n", '%n'
        $messageLines += "$($inno[0]).$($key.Replace('.', '_'))=$text"
    }
}
$languagesFile = Join-Path $stageDir 'languages.iss'
[IO.File]::WriteAllText($languagesFile, (($languageLines + '' + $messageLines) -join "`r`n"), (New-Object Text.UTF8Encoding($true)))

foreach ($setup in @(@{ Name = 'client'; Source = $clientDir }, @{ Name = 'host'; Source = $hostDir })) {
    Invoke-Native "Building the $($setup.Name) setup" {
        & $isccPath /Q "/DAppVersion=$Version" "/DRepoRoot=$root" "/DSourceDir=$($setup.Source)" "/DOutputDir=$OutDir" `
            "/DLanguagesFile=$languagesFile" (Join-Path $root "packaging\installer\$($setup.Name).iss")
    }
}

# Checked by the automatic updates before running a downloaded setup.
$lines = foreach ($file in $clientSetup, $hostSetup) {
    '{0}  {1}' -f (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $file -Leaf)
}
[IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
Remove-Item -Recurse -Force $publishDir, $stageDir

Write-Host ''
Write-Host "Release $Version ready:" -ForegroundColor Green
Get-Item $clientSetup, $hostSetup, $sums | ForEach-Object { '  {0}  ({1:N1} MB)' -f $_.FullName, ($_.Length / 1MB) }
