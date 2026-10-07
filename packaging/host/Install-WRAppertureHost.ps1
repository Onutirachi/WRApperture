<#
.SYNOPSIS
    Instala ou atualiza o WRApperture no PC host (agente como serviço do Windows).

.DESCRIPTION
    1. Copia wrahost.exe e WRApperture.Agent.exe para Program Files.
    2. Prepara o host para RemoteApp (wrahost prepare).
    3. Gera o token de pareamento, se o host ainda não estiver pareado.
    4. Registra o serviço WRAppertureAgent (automático, LocalSystem, reinício em falha).
    5. Libera a porta do agente no firewall (redes de domínio e privadas).
    6. Inicia o serviço e verifica se o agente responde.
    Pode ser executado de novo para atualizar: o token e os apps publicados são preservados.

.PARAMETER InstallDir
    Pasta de instalação. Padrão: "$env:ProgramFiles\WRApperture".

.PARAMETER AllowPublicNetwork
    Libera o agente também em redes classificadas como Públicas.

.PARAMETER DryRun
    Mostra o que seria feito, sem alterar nada.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'WRApperture'),
    [switch]$AllowPublicNetwork,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'WRAppertureAgent'
$FirewallRule = 'WRAppertureAgent-TCP'
$Binaries = @('wrahost.exe', 'WRApperture.Agent.exe', 'WRApperture.Host.exe', 'appsettings.json')
$ManagerShortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'WRApperture (Host).lnk'

# Translations: Localization.ps1 sits next to this script (package, install folder) or in packaging\ (repo).
$localizer = @('Localization.ps1', '..\Localization.ps1') | ForEach-Object { Join-Path $PSScriptRoot $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($localizer) { . $localizer }
else {
    function T([string]$Key) { (@($Key) + $args) -join ' ' }
}

function Write-Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($DryRun) {
        Write-Host (T 'installer.dryRun.step' $Description) -ForegroundColor DarkYellow
        return $null
    }
    Write-Step $Description
    return & $Action
}

function Test-IsAdministrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Wrahost([string[]]$Arguments) {
    $exe = Join-Path $InstallDir 'wrahost.exe'
    $output = & $exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw [System.InvalidOperationException]::new((T 'installer.host.install.wrahostFailed' ($Arguments -join ' ') $LASTEXITCODE "$output"))
    }
    return ($output | Out-String)
}

if (-not $DryRun -and -not (Test-IsAdministrator)) {
    Write-Host (T 'installer.host.install.needsAdmin') -ForegroundColor Red
    exit 2
}

if ($DryRun) { Write-Host (T 'installer.host.install.dryRunMode' $InstallDir) -ForegroundColor DarkYellow }

$newToken = $null
try {
    foreach ($file in $Binaries) {
        if (-not $DryRun -and -not (Test-Path (Join-Path $PSScriptRoot $file))) {
            throw (T 'installer.missingFile' $file)
        }
    }

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Invoke-Step (T 'installer.host.install.stopService' $ServiceName) { Stop-Service -Name $ServiceName -Force; (Get-Service $ServiceName).WaitForStatus('Stopped', '00:00:30') } | Out-Null
    }

    Invoke-Step (T 'installer.host.install.copy' $InstallDir) {
        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
        foreach ($file in $Binaries + 'Uninstall-WRAppertureHost.ps1', 'Desinstalar.cmd', 'Localization.ps1') {
            $source = Join-Path $PSScriptRoot $file
            if (Test-Path $source) { Copy-Item -Path $source -Destination $InstallDir -Force }
        }
        # Translations, so the uninstaller run from the install folder speaks the user's language.
        $catalog = Join-Path $PSScriptRoot 'localization'
        if (Test-Path $catalog) {
            Remove-Item -Recurse -Force (Join-Path $InstallDir 'localization') -ErrorAction SilentlyContinue
            Copy-Item -Path $catalog -Destination $InstallDir -Recurse -Force
        }
    } | Out-Null

    Invoke-Step (T 'installer.host.install.shortcut') {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($ManagerShortcut)
        $shortcut.TargetPath = Join-Path $InstallDir 'WRApperture.Host.exe'
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.Description = T 'installer.host.shortcut.description'
        $shortcut.Save()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    } | Out-Null

    Invoke-Step (T 'installer.host.install.addPath') {
        $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
        if (($machinePath -split ';') -notcontains $InstallDir) {
            [Environment]::SetEnvironmentVariable('Path', ($machinePath.TrimEnd(';') + ';' + $InstallDir), 'Machine')
        }
    } | Out-Null

    Invoke-Step (T 'installer.host.install.prepare') {
        $exe = Join-Path $InstallDir 'wrahost.exe'
        & $exe prepare | Out-Host
        if ($LASTEXITCODE -eq 3) { throw (T 'installer.host.install.unsupportedEdition') }
        if ($LASTEXITCODE -ne 0) { throw (T 'installer.host.install.prepareFailed' $LASTEXITCODE) }
    } | Out-Null

    $settings = [pscustomobject]@{ port = 5080; isPaired = $false; certificateFingerprint = $null }
    if (-not $DryRun) { $settings = Invoke-Wrahost @('agent', 'show', '--json') | ConvertFrom-Json }

    if (-not $settings.isPaired) {
        $newToken = Invoke-Step (T 'installer.host.install.token') { (Invoke-Wrahost @('agent', 'token', '--json') | ConvertFrom-Json).token }
    }
    else {
        Write-Step (T 'installer.host.install.alreadyPaired')
    }

    $binPath = '"' + (Join-Path $InstallDir 'WRApperture.Agent.exe') + '"'
    if ($service) {
        Invoke-Step (T 'installer.host.install.updateService' $ServiceName) { sc.exe config $ServiceName binPath= $binPath start= auto | Out-Null } | Out-Null
    }
    else {
        Invoke-Step (T 'installer.host.install.registerService' $ServiceName) {
            New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName 'WRApperture Agent' `
                -Description (T 'installer.host.service.description') -StartupType Automatic | Out-Null
        } | Out-Null
    }

    Invoke-Step (T 'installer.host.install.restartOnFailure') {
        sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null
    } | Out-Null

    $profiles = if ($AllowPublicNetwork) { 'Domain,Private,Public' } else { 'Domain,Private' }
    Invoke-Step (T 'installer.host.install.firewall' $settings.port $profiles) {
        Get-NetFirewallRule -Name $FirewallRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        New-NetFirewallRule -Name $FirewallRule -DisplayName 'WRApperture Agent' -Direction Inbound -Protocol TCP `
            -LocalPort $settings.port -Action Allow -Profile $profiles.Split(',') | Out-Null
    } | Out-Null

    Invoke-Step (T 'installer.host.install.startService' $ServiceName) { Start-Service -Name $ServiceName } | Out-Null

    $healthy = $false
    if (-not $DryRun) {
        Write-Step (T 'installer.host.install.checking')
        for ($i = 0; $i -lt 30 -and -not $healthy; $i++) {
            Start-Sleep -Seconds 1
            $code = & curl.exe -k -s -o NUL -w '%{http_code}' "https://localhost:$($settings.port)/health" 2>$null
            $healthy = $code -eq '200'
        }
        $settings = Invoke-Wrahost @('agent', 'show', '--json') | ConvertFrom-Json
    }

    Write-Host ''
    if ($DryRun) { Write-Host (T 'installer.dryRun.done') -ForegroundColor DarkYellow; exit 0 }

    if (-not $healthy) {
        Write-Host (T 'installer.host.install.notResponding' $settings.port) -ForegroundColor Red
        $log = Get-ChildItem (Join-Path $env:ProgramData 'WRApperture\logs') -Filter 'agent*.log' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime | Select-Object -Last 1
        if ($log) { Write-Host (T 'installer.host.install.lastLines' $log.FullName); Get-Content $log.FullName -Tail 15 | Out-Host }
        Write-Host (T 'installer.host.install.portHint')
        exit 1
    }

    $addresses = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        Select-Object -ExpandProperty IPAddress
    $fingerprint = ($settings.certificateFingerprint -split '(.{4})' | Where-Object { $_ }) -join ' '

    Write-Host (T 'installer.host.install.done') -ForegroundColor Green
    Write-Host ''
    Write-Host (T 'installer.host.install.computer' $env:COMPUTERNAME)
    Write-Host (T 'installer.host.install.addresses' ($addresses -join ', '))
    Write-Host (T 'installer.host.install.port' $settings.port)
    Write-Host (T 'installer.host.install.fingerprint' $fingerprint)
    if ($newToken) {
        Write-Host ''
        Write-Host (T 'installer.host.install.newToken') -ForegroundColor Yellow
        Write-Host "      $newToken" -ForegroundColor Yellow
    }
    else {
        Write-Host (T 'installer.host.install.tokenKept')
    }

    Write-Host ''
    Write-Host (T 'installer.host.install.nextSteps')
    Write-Host (T 'installer.host.install.step1')
    Write-Host (T 'installer.host.install.step2')
    Write-Host (T 'installer.host.install.howToUninstall' (Join-Path $InstallDir 'Desinstalar.cmd'))
    Write-Host (T 'installer.host.install.singleSession') -ForegroundColor DarkYellow

    $public = Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object NetworkCategory -eq 'Public'
    if ($public -and -not $AllowPublicNetwork) {
        Write-Host (T 'installer.host.install.publicNetwork' ($public.Name -join ', ')) -ForegroundColor DarkYellow
        Write-Host (T 'installer.host.install.publicNetworkHint') -ForegroundColor DarkYellow
    }
    exit 0
}
catch {
    Write-Host (T 'installer.installFailed' $_.Exception.Message) -ForegroundColor Red
    if ($newToken) {
        # The token is only shown once; never lose it because a later step failed.
        Write-Host (T 'installer.host.install.tokenOnFailure' $newToken) -ForegroundColor Yellow
    }
    exit 1
}
