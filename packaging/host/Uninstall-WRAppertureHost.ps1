<#
.SYNOPSIS
    Remove o WRApperture do PC host.

.DESCRIPTION
    Remove o serviço WRAppertureAgent, a regra de firewall e os arquivos do programa.
    Por padrão mantém os apps publicados e as configurações do agente (token), para facilitar uma reinstalação.

.PARAMETER Purge
    Também desfaz a preparação do host (wrahost revert) e apaga token, configurações e certificado do agente.
    Os apps publicados continuam registrados no Windows; remova-os antes com "wrahost unpublish <alias>" se quiser.

.PARAMETER DryRun
    Mostra o que seria feito, sem alterar nada.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'WRApperture'),
    [switch]$Purge,
    [switch]$DryRun
)

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
    Write-Host 'Execute a desinstalação como administrador.' -ForegroundColor Red
    exit 2
}

try {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Invoke-Step "Parar e remover o serviço $ServiceName" {
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
            sc.exe delete $ServiceName | Out-Null
        }
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
    if ($DryRun) { Write-Host 'Simulação concluída.' -ForegroundColor DarkYellow; exit 0 }
    Write-Host 'WRApperture removido deste computador.' -ForegroundColor Green
    if (-not $Purge) {
        Write-Host 'Apps publicados, preparação do host e token foram mantidos (use -Purge para limpar tudo).'
    }
    exit 0
}
catch {
    Write-Host "Falha na desinstalação: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
