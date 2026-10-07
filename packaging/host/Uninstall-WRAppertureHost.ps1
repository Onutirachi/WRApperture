<#
.SYNOPSIS
    Remove o WRApperture do PC host.

.DESCRIPTION
    Remove o serviço WRAppertureAgent, a regra de firewall e os arquivos do programa.
    Por padrão mantém os apps publicados e as configurações do agente (token), para facilitar uma reinstalação.

.PARAMETER Purge
    Também desfaz a preparação do host (wrahost revert) e apaga token, configurações e certificado do agente.
    Os apps publicados continuam registrados no Windows; remova-os antes com "wrahost unpublish <alias>" se quiser.

.PARAMETER Interactive
    Usado pelo Desinstalar.cmd: pergunta se deve limpar tudo e espera Enter antes de fechar.

.PARAMETER DryRun
    Mostra o que seria feito, sem alterar nada.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'WRApperture'),
    [switch]$Purge,
    [switch]$Interactive,
    [switch]$DryRun
)

function Exit-Script([int]$Code) {
    if ($Interactive) { [void](Read-Host 'Pressione Enter para fechar') }
    exit $Code
}

$ErrorActionPreference = 'Stop'
$ServiceName = 'WRAppertureAgent'
$FirewallRule = 'WRAppertureAgent-TCP'

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($DryRun) { Write-Host "[simulação] $Description" -ForegroundColor DarkYellow; return }
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Action
}

function Test-IsAdministrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not $DryRun -and -not (Test-IsAdministrator)) {
    Write-Host 'Execute a desinstalação como administrador (use o Desinstalar.cmd).' -ForegroundColor Red
    Exit-Script 2
}

if ($Interactive -and -not $Purge) {
    Write-Host 'Desinstalar o WRApperture deste PC (host).' -ForegroundColor Cyan
    $answer = Read-Host 'Desfazer também a preparação do PC e apagar o token? Os clientes precisarão parear de novo. (s/N)'
    $Purge = $answer -match '^\s*[sSyY]'
}

try {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Invoke-Step "Parar e remover o serviço $ServiceName" {
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
            sc.exe delete $ServiceName | Out-Null
        }
    }

    foreach ($manager in @(Get-Process -Name 'WRApperture.Host' -ErrorAction SilentlyContinue)) {
        Invoke-Step 'Fechar o WRApperture (Host) em execução' { $manager.Kill(); $manager.WaitForExit(5000) | Out-Null }
    }

    $managerShortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'WRApperture (Host).lnk'
    if (Test-Path $managerShortcut) {
        Invoke-Step 'Remover atalho "WRApperture (Host)" do Menu Iniciar' { Remove-Item -Force $managerShortcut }
    }

    if (Get-NetFirewallRule -Name $FirewallRule -ErrorAction SilentlyContinue) {
        Invoke-Step 'Remover a regra de firewall do agente' { Remove-NetFirewallRule -Name $FirewallRule }
    }

    $wrahost = Join-Path $InstallDir 'wrahost.exe'
    if ($Purge) {
        if ($DryRun -or (Test-Path $wrahost)) {
            Invoke-Step 'Desfazer a preparação do host (wrahost revert)' { & $wrahost revert | Out-Host }
            Invoke-Step 'Apagar token, configurações e certificado do agente (wrahost agent reset)' { & $wrahost agent reset | Out-Host }
        }
        else {
            Write-Host "wrahost.exe não encontrado em $InstallDir; preparação e configurações não foram revertidas." -ForegroundColor DarkYellow
        }
    }

    Invoke-Step 'Remover a pasta de instalação do PATH do sistema' {
        $parts = [Environment]::GetEnvironmentVariable('Path', 'Machine') -split ';' | Where-Object { $_ -and $_ -ne $InstallDir }
        [Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'Machine')
    }

    if (Test-Path $InstallDir) {
        Invoke-Step "Apagar $InstallDir" {
            # This script may be running from inside InstallDir.
            Set-Location $env:TEMP
            Remove-Item -Recurse -Force $InstallDir
        }
    }

    Write-Host ''
    if ($DryRun) { Write-Host 'Simulação concluída.' -ForegroundColor DarkYellow; Exit-Script 0 }
    Write-Host 'WRApperture removido deste computador.' -ForegroundColor Green
    if (-not $Purge) {
        Write-Host 'Apps publicados, preparação do host e token foram mantidos (uma reinstalação os reaproveita).'
    }
    Exit-Script 0
}
catch {
    Write-Host "Falha na desinstalação: $($_.Exception.Message)" -ForegroundColor Red
    Exit-Script 1
}
