<#
.SYNOPSIS
    Instala ou atualiza o cliente WRApperture para o usuário atual (não precisa de administrador).

.PARAMETER InstallDir
    Padrão: "$env:LOCALAPPDATA\Programs\WRApperture".

.PARAMETER StartMenuDir
    Pasta onde o atalho "WRApperture" é criado. Padrão: Menu Iniciar do usuário.

.PARAMETER DesktopDir
    Pasta do atalho "WRApperture" na área de trabalho. Padrão: área de trabalho do usuário.

.PARAMETER Interactive
    Pergunta se deve criar o atalho na área de trabalho (Enter = sim). Usado pelo Instalar.cmd.

.PARAMETER NoDesktopShortcut
    Não cria o atalho na área de trabalho.

.PARAMETER Launch
    Abre o cliente ao final.

.PARAMETER DryRun
    Mostra o que seria feito, sem alterar nada.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\WRApperture'),
    [string]$StartMenuDir = [Environment]::GetFolderPath('Programs'),
    [string]$DesktopDir = [Environment]::GetFolderPath('DesktopDirectory'),
    [switch]$Interactive,
    [switch]$NoDesktopShortcut,
    [switch]$Launch,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$ExeName = 'WRApperture.exe'

# Translations: Localization.ps1 sits next to this script (package, install folder) or in packaging\ (repo).
$localizer = @('Localization.ps1', '..\Localization.ps1') | ForEach-Object { Join-Path $PSScriptRoot $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($localizer) { . $localizer }
else {
    function T([string]$Key) { (@($Key) + $args) -join ' ' }
    function Test-WRYes([string]$Answer) { $Answer -match '^\s*[yY]' }
}

# Asked once by Instalar.cmd (-Interactive); Enter means yes. Without -Interactive the default is yes.
function Get-DesktopShortcutChoice([string]$Path) {
    if ($NoDesktopShortcut) { return $false }
    if (-not $Interactive -or (Test-Path $Path)) { return $true }   # an update just refreshes it
    $answer = Read-Host (T 'installer.desktopShortcut.question')
    return [string]::IsNullOrWhiteSpace($answer) -or (Test-WRYes $answer)
}

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($DryRun) { Write-Host (T 'installer.dryRun.step' $Description) -ForegroundColor DarkYellow; return }
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Action
}

# Start Menu shortcuts that point into the install folder (their names depend on the language used at install time).
function Get-OwnShortcuts {
    if (-not (Test-Path $StartMenuDir)) { return }
    $prefix = $InstallDir.TrimEnd('\') + '\'
    $shell = New-Object -ComObject WScript.Shell
    Get-ChildItem -Path $StartMenuDir -Filter '*.lnk' | Where-Object {
        $shell.CreateShortcut($_.FullName).TargetPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}

try {
    $source = Join-Path $PSScriptRoot $ExeName
    if (-not $DryRun -and -not (Test-Path $source)) {
        throw (T 'installer.missingFile' $ExeName)
    }

    $target = Join-Path $InstallDir $ExeName
    $desktopShortcut = Join-Path $DesktopDir 'WRApperture.lnk'
    $createDesktopShortcut = Get-DesktopShortcutChoice $desktopShortcut

    $running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $target }
    if ($running) {
        Invoke-Step (T 'installer.client.install.closeRunning') {
            $running | ForEach-Object { $null = $_.CloseMainWindow(); if (-not $_.WaitForExit(5000)) { $_.Kill() } }
        }
    }

    Invoke-Step (T 'installer.client.install.copy' $InstallDir) {
        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
        Copy-Item -Path $source -Destination $InstallDir -Force
        foreach ($file in 'Uninstall-WRAppertureClient.ps1', 'Desinstalar.cmd', 'Localization.ps1') {
            $extra = Join-Path $PSScriptRoot $file
            if (Test-Path $extra) { Copy-Item -Path $extra -Destination $InstallDir -Force }
        }
        # Translations, so the uninstaller run from the install folder speaks the user's language.
        $catalog = Join-Path $PSScriptRoot 'localization'
        if (Test-Path $catalog) {
            Remove-Item -Recurse -Force (Join-Path $InstallDir 'localization') -ErrorAction SilentlyContinue
            Copy-Item -Path $catalog -Destination $InstallDir -Recurse -Force
        }
    }

    $uninstallName = T 'installer.client.shortcut.uninstallName'
    Invoke-Step (T 'installer.client.install.shortcuts' $uninstallName) {
        # Drops shortcuts from a previous install, possibly named in another language.
        Get-OwnShortcuts | Remove-Item -Force

        New-Item -ItemType Directory -Force -Path $StartMenuDir | Out-Null
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Join-Path $StartMenuDir 'WRApperture.lnk'))
        $shortcut.TargetPath = $target
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.Description = T 'installer.client.shortcut.description'
        $shortcut.Save()

        $uninstallCmd = Join-Path $InstallDir 'Desinstalar.cmd'
        if (Test-Path $uninstallCmd) {
            $uninstall = $shell.CreateShortcut((Join-Path $StartMenuDir "$uninstallName.lnk"))
            $uninstall.TargetPath = $uninstallCmd
            $uninstall.WorkingDirectory = $env:TEMP   # never inside the folder being deleted
            $uninstall.IconLocation = "$target,0"
            $uninstall.Description = T 'installer.client.shortcut.uninstallDescription'
            $uninstall.Save()
        }

        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }

    if ($createDesktopShortcut) {
        Invoke-Step (T 'installer.desktopShortcut.create' $desktopShortcut) {
            New-Item -ItemType Directory -Force -Path $DesktopDir | Out-Null
            $shell = New-Object -ComObject WScript.Shell
            $shortcut = $shell.CreateShortcut($desktopShortcut)
            $shortcut.TargetPath = $target
            $shortcut.WorkingDirectory = $InstallDir
            $shortcut.Description = T 'installer.client.shortcut.description'
            $shortcut.Save()
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }

    Write-Host ''
    if ($DryRun) { Write-Host (T 'installer.dryRun.done') -ForegroundColor DarkYellow; exit 0 }
    Write-Host (T 'installer.client.install.done') -ForegroundColor Green
    Write-Host (T 'installer.client.install.howToUninstall' $uninstallName)
    if ($Launch) { Start-Process -FilePath $target }
    exit 0
}
catch {
    Write-Host (T 'installer.installFailed' $_.Exception.Message) -ForegroundColor Red
    exit 1
}
