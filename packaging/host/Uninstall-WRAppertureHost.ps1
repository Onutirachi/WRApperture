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
    [string]$DesktopDir = [Environment]::GetFolderPath('CommonDesktopDirectory'),
    [switch]$Purge,
    [switch]$KeepFiles,
    [switch]$Interactive,
    [switch]$DryRun
)

# Translations: Localization.ps1 sits next to this script (package, install folder) or in packaging\ (repo).
# It reads the catalog up front, so messages keep working after the install folder is deleted.
$localizer = @('Localization.ps1', '..\Localization.ps1') | ForEach-Object { Join-Path $PSScriptRoot $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($localizer) { . $localizer }
else {
    function T([string]$Key) { (@($Key) + $args) -join ' ' }
    function Test-WRYes([string]$Answer) { $Answer -match '^\s*[yY]' }
}

function Exit-Script([int]$Code) {
    if ($Interactive) { [void](Read-Host (T 'installer.pressEnter')) }
    exit $Code
}

$ErrorActionPreference = 'Stop'
$ServiceName = 'WRAppertureAgent'
$FirewallRule = 'WRAppertureAgent-TCP'
$DiscoveryRule = 'WRAppertureAgent-Discovery-UDP'

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($DryRun) { Write-Host (T 'installer.dryRun.step' $Description) -ForegroundColor DarkYellow; return }
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Action
}

function Test-IsAdministrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not $DryRun -and -not (Test-IsAdministrator)) {
    Write-Host (T 'installer.host.uninstall.needsAdmin') -ForegroundColor Red
    Exit-Script 2
}

if ($Interactive -and -not $Purge) {
    Write-Host (T 'installer.host.uninstall.title') -ForegroundColor Cyan
    $answer = Read-Host (T 'installer.host.uninstall.askPurge')
    $Purge = Test-WRYes $answer
}

try {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Invoke-Step (T 'installer.host.uninstall.removeService' $ServiceName) {
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
            sc.exe delete $ServiceName | Out-Null
        }
    }

    foreach ($manager in @(Get-Process -Name 'WRApperture.Host' -ErrorAction SilentlyContinue)) {
        Invoke-Step (T 'installer.host.uninstall.closeManager') { $manager.Kill(); $manager.WaitForExit(5000) | Out-Null }
    }

    $managerShortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'WRApperture (Host).lnk'
    if (Test-Path $managerShortcut) {
        Invoke-Step (T 'installer.host.uninstall.removeShortcut') { Remove-Item -Force $managerShortcut }
    }

    $desktopShortcut = Join-Path $DesktopDir 'WRApperture (Host).lnk'
    if (Test-Path $desktopShortcut) {
        Invoke-Step (T 'installer.host.uninstall.removeDesktopShortcut' $desktopShortcut) { Remove-Item -Force $desktopShortcut }
    }

    if (Get-NetFirewallRule -Name $FirewallRule -ErrorAction SilentlyContinue) {
        Invoke-Step (T 'installer.host.uninstall.removeFirewall') { Remove-NetFirewallRule -Name $FirewallRule }
    }

    if (Get-NetFirewallRule -Name $DiscoveryRule -ErrorAction SilentlyContinue) {
        Invoke-Step (T 'installer.host.uninstall.removeFirewall') { Remove-NetFirewallRule -Name $DiscoveryRule }
    }

    $wrahost = Join-Path $InstallDir 'wrahost.exe'
    if ($Purge) {
        if ($DryRun -or (Test-Path $wrahost)) {
            Invoke-Step (T 'installer.host.uninstall.revert') { & $wrahost revert | Out-Host }
            Invoke-Step (T 'installer.host.uninstall.reset') { & $wrahost agent reset | Out-Host }
        }
        else {
            Write-Host (T 'installer.host.uninstall.wrahostMissing' $InstallDir) -ForegroundColor DarkYellow
        }
    }

    Invoke-Step (T 'installer.host.uninstall.removePath') {
        $parts = [Environment]::GetEnvironmentVariable('Path', 'Machine') -split ';' | Where-Object { $_ -and $_ -ne $InstallDir }
        [Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'Machine')
    }

    if (-not $KeepFiles -and (Test-Path $InstallDir)) {
        Invoke-Step (T 'installer.deleteFolder' $InstallDir) {
            # This script may be running from inside InstallDir.
            Set-Location $env:TEMP
            Remove-Item -Recurse -Force $InstallDir
        }
    }

    Write-Host ''
    if ($DryRun) { Write-Host (T 'installer.dryRun.done') -ForegroundColor DarkYellow; Exit-Script 0 }
    Write-Host (T 'installer.host.uninstall.done') -ForegroundColor Green
    if (-not $Purge) {
        Write-Host (T 'installer.host.uninstall.kept')
    }
    Exit-Script 0
}
catch {
    Write-Host (T 'installer.uninstallFailed' $_.Exception.Message) -ForegroundColor Red
    Exit-Script 1
}
