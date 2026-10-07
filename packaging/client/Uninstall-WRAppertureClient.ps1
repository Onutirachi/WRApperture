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

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($DryRun) { Write-Host "[simulação] $Description" -ForegroundColor DarkYellow; return }
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Action
}

function Exit-Script([int]$Code) {
    if ($Interactive) { [void](Read-Host 'Pressione Enter para fechar') }
    exit $Code
}

if ($Interactive -and -not $RemoveUserData) {
    Write-Host 'Desinstalar o WRApperture deste computador.' -ForegroundColor Cyan
    $answer = Read-Host 'Apagar também os PCs pareados, ícones, senhas salvas e a configuração "abrir sem alerta"? (s/N)'
    $RemoveUserData = $answer -match '^\s*[sSyY]'
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

    foreach ($name in 'WRApperture.lnk', 'Desinstalar WRApperture.lnk') {
        $startMenuLink = Join-Path $StartMenuDir $name
        if (Test-Path $startMenuLink) {
            Invoke-Step "Remover atalho '$name' do Menu Iniciar" { Remove-Item -Force $startMenuLink }
        }
    }

    # Must run before the program folder is deleted: the client itself removes the signing setup.
    $signing = Get-ChildItem Cert:\LocalMachine\My -ErrorAction SilentlyContinue | Where-Object { $_.Subject -like 'CN=WRApperture RDP Signing*' }
    if ($RemoveUserData -and -not $KeepSigning -and $signing -and (Test-Path $target)) {
        Invoke-Step 'Remover a configuração "abrir apps sem alerta" (pede administrador)' {
            try {
                $p = Start-Process -FilePath $target -ArgumentList '--remove-signing' -Verb RunAs -Wait -PassThru
                if ($p.ExitCode -ne 0) { throw "código $($p.ExitCode)" }
            }
            catch {
                # Never block the uninstall; the setup can be removed later by reinstalling and uninstalling again.
                Write-Host "Não foi possível remover a configuração de assinatura: $($_.Exception.Message)" -ForegroundColor DarkYellow
            }
        }
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
    if ($DryRun) { Write-Host 'Simulação concluída.' -ForegroundColor DarkYellow; Exit-Script 0 }
    Write-Host 'Cliente WRApperture removido.' -ForegroundColor Green
    if (-not $RemoveUserData) { Write-Host "PCs pareados e ícones foram mantidos em $UserDataDir (uma reinstalação os reaproveita)." }
    Exit-Script 0
}
catch {
    Write-Host "Falha na desinstalação: $($_.Exception.Message)" -ForegroundColor Red
    Exit-Script 1
}
