<#
.SYNOPSIS
    Remove o cliente WRApperture do usuário atual.

.PARAMETER RemoveUserData
    Também apaga hosts pareados, ícones, logs, as credenciais salvas (TERMSRV/<host>) e os
    atalhos da área de trabalho criados pelo WRApperture.

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
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$ExeName = 'WRApperture.exe'

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($DryRun) { Write-Host "[simulação] $Description" -ForegroundColor DarkYellow; return }
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Action
}

try {
    $target = Join-Path $InstallDir $ExeName
    $running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $target }
    if ($running) {
        Invoke-Step 'Fechar o WRApperture em execução' {
            $running | ForEach-Object { $null = $_.CloseMainWindow(); if (-not $_.WaitForExit(5000)) { $_.Kill() } }
        }
    }

    $startMenuLink = Join-Path $StartMenuDir 'WRApperture.lnk'
    if (Test-Path $startMenuLink) {
        Invoke-Step 'Remover atalho do Menu Iniciar' { Remove-Item -Force $startMenuLink }
    }

    if (Test-Path $InstallDir) {
        Invoke-Step "Apagar $InstallDir" {
            Set-Location $env:TEMP   # this script may be running from inside InstallDir
            Remove-Item -Recurse -Force $InstallDir
        }
    }

    if ($RemoveUserData) {
        $stateFile = Join-Path $UserDataDir 'client\hosts.json'
        if (Test-Path $stateFile) {
            $hosts = (Get-Content -Raw -Encoding UTF8 $stateFile | ConvertFrom-Json).hosts
            foreach ($h in $hosts) {
                Invoke-Step "Remover credencial salva TERMSRV/$($h.address)" {
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
                    Invoke-Step "Remover atalho da área de trabalho '$($_.Name)'" { Remove-Item -Force $link }
                }
            }
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }

        if (Test-Path $UserDataDir) {
            Invoke-Step "Apagar dados do usuário em $UserDataDir" { Remove-Item -Recurse -Force $UserDataDir }
        }
    }

    Write-Host ''
    if ($DryRun) { Write-Host 'Simulação concluída.' -ForegroundColor DarkYellow; exit 0 }
    Write-Host 'Cliente WRApperture removido.' -ForegroundColor Green
    if (-not $RemoveUserData) { Write-Host "Hosts pareados e ícones foram mantidos em $UserDataDir (use -RemoveUserData para apagar)." }
    exit 0
}
catch {
    Write-Host "Falha na desinstalação: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
