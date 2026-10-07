<#
.SYNOPSIS
    Remove o cliente WRApperture do usuário atual.

.PARAMETER RemoveUserData
    Também apaga hosts pareados, ícones, logs, as credenciais salvas (TERMSRV/<host>) e os
    atalhos da área de trabalho criados pelo WRApperture.

.PARAMETER KeepSigning
    Com -RemoveUserData, mantém a configuração "abrir apps sem alerta" (certificado de assinatura) neste PC.

.PARAMETER Interactive
    Usado pelo Desinstalar.cmd: pergunta se deve apagar os dados do usuário e espera Enter antes de fechar.

.PARAMETER DryRun
    Mostra o que seria feito, sem alterar nada.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\WRApperture'),
    [string]$StartMenuDir = [Environment]::GetFolderPath('Programs'),
    [string]$UserDataDir = (Join-Path $env:LOCALAPPDATA 'WRApperture'),
    [string]$DesktopDir = [Environment]::GetFolderPath('DesktopDirectory'),
    [switch]$RemoveUserData,
    [switch]$KeepSigning,
    [switch]$Interactive,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$ExeName = 'WRApperture.exe'

# Translations: Localization.ps1 sits next to this script (package, install folder) or in packaging\ (repo).
# It reads the catalog up front, so messages keep working after the install folder is deleted.
$localizer = @('Localization.ps1', '..\Localization.ps1') | ForEach-Object { Join-Path $PSScriptRoot $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($localizer) { . $localizer }
else {
    function T([string]$Key) { (@($Key) + $args) -join ' ' }
    function Test-WRYes([string]$Answer) { $Answer -match '^\s*[yY]' }
}

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($DryRun) { Write-Host (T 'installer.dryRun.step' $Description) -ForegroundColor DarkYellow; return }
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Action
}

function Exit-Script([int]$Code) {
    if ($Interactive) { [void](Read-Host (T 'installer.pressEnter')) }
    exit $Code
}

if ($Interactive -and -not $RemoveUserData) {
    Write-Host (T 'installer.client.uninstall.title') -ForegroundColor Cyan
    $answer = Read-Host (T 'installer.client.uninstall.askUserData')
    $RemoveUserData = Test-WRYes $answer
}

try {
    $target = Join-Path $InstallDir $ExeName
    $running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $target }
    if ($running) {
        Invoke-Step (T 'installer.client.uninstall.closeRunning') {
            $running | ForEach-Object { $null = $_.CloseMainWindow(); if (-not $_.WaitForExit(5000)) { $_.Kill() } }
        }
    }

    # "WRApperture" and every other Start Menu shortcut pointing into the install folder: the uninstall
    # shortcut's name ("Desinstalar WRApperture", "Uninstall WRApperture"…) depends on the install language.
    if (Test-Path $StartMenuDir) {
        $prefix = $InstallDir.TrimEnd('\') + '\'
        $shell = New-Object -ComObject WScript.Shell
        $links = @(Get-ChildItem -Path $StartMenuDir -Filter '*.lnk' | Where-Object {
            $_.Name -eq 'WRApperture.lnk' -or $shell.CreateShortcut($_.FullName).TargetPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
        })
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        foreach ($link in $links) {
            $startMenuLink = $link.FullName
            Invoke-Step (T 'installer.client.uninstall.removeShortcut' $link.Name) { Remove-Item -Force $startMenuLink }
        }
    }

    # Must run before the program folder is deleted: the client itself removes the signing setup.
    $signing = Get-ChildItem Cert:\LocalMachine\My -ErrorAction SilentlyContinue | Where-Object { $_.Subject -like 'CN=WRApperture RDP Signing*' }
    if ($RemoveUserData -and -not $KeepSigning -and $signing -and (Test-Path $target)) {
        Invoke-Step (T 'installer.client.uninstall.removeSigning') {
            try {
                $p = Start-Process -FilePath $target -ArgumentList '--remove-signing' -Verb RunAs -Wait -PassThru
                if ($p.ExitCode -ne 0) { throw (T 'installer.exitCode' $p.ExitCode) }
            }
            catch {
                # Never block the uninstall; the setup can be removed later by reinstalling and uninstalling again.
                Write-Host (T 'installer.client.uninstall.signingFailed' $_.Exception.Message) -ForegroundColor DarkYellow
            }
        }
    }
    if (Test-Path $InstallDir) {
        Invoke-Step (T 'installer.deleteFolder' $InstallDir) {
            Set-Location $env:TEMP   # this script may be running from inside InstallDir
            Remove-Item -Recurse -Force $InstallDir
        }
    }

    if ($RemoveUserData) {
        $stateFile = Join-Path $UserDataDir 'client\hosts.json'
        if (Test-Path $stateFile) {
            $hosts = (Get-Content -Raw -Encoding UTF8 $stateFile | ConvertFrom-Json).hosts
            foreach ($h in $hosts) {
                Invoke-Step (T 'installer.client.uninstall.removeCredential' $h.address) {
                    cmdkey.exe /delete:"TERMSRV/$($h.address)" 2>&1 | Out-Null
                }
            }
        }

        $rdpRoot = Join-Path $UserDataDir 'client\rdp'
        if ((Test-Path $DesktopDir) -and (Test-Path $rdpRoot)) {
            $shell = New-Object -ComObject WScript.Shell
            Get-ChildItem -Path $DesktopDir -Filter '*.lnk' | ForEach-Object {
                if ($shell.CreateShortcut($_.FullName).TargetPath -like "$rdpRoot\*") {
                    $link = $_.FullName
                    Invoke-Step (T 'installer.client.uninstall.removeDesktopShortcut' $_.Name) { Remove-Item -Force $link }
                }
            }
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }

        if (Test-Path $UserDataDir) {
            Invoke-Step (T 'installer.client.uninstall.deleteUserData' $UserDataDir) { Remove-Item -Recurse -Force $UserDataDir }
        }
    }

    Write-Host ''
    if ($DryRun) { Write-Host (T 'installer.dryRun.done') -ForegroundColor DarkYellow; Exit-Script 0 }
    Write-Host (T 'installer.client.uninstall.done') -ForegroundColor Green
    if (-not $RemoveUserData) { Write-Host (T 'installer.client.uninstall.dataKept' $UserDataDir) }
    Exit-Script 0
}
catch {
    Write-Host (T 'installer.uninstallFailed' $_.Exception.Message) -ForegroundColor Red
    Exit-Script 1
}
