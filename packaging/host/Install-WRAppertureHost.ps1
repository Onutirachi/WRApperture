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
$Binaries = @('wrahost.exe', 'WRApperture.Agent.exe', 'appsettings.json')

function Write-Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($DryRun) {
        Write-Host "[simulação] $Description" -ForegroundColor DarkYellow
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
        throw [System.InvalidOperationException]::new("wrahost $($Arguments -join ' ') falhou (código $LASTEXITCODE): $output")
    }
    return ($output | Out-String)
}

if (-not $DryRun -and -not (Test-IsAdministrator)) {
    Write-Host 'Este instalador precisa ser executado como administrador (use Instalar.cmd ou um terminal elevado).' -ForegroundColor Red
    exit 2
}

if ($DryRun) { Write-Host "Modo simulação: nada será alterado. Destino: $InstallDir" -ForegroundColor DarkYellow }

$newToken = $null
try {
    foreach ($file in $Binaries) {
        if (-not $DryRun -and -not (Test-Path (Join-Path $PSScriptRoot $file))) {
            throw "Arquivo '$file' não encontrado ao lado do instalador. Extraia o pacote completo antes de instalar."
        }
    }

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Invoke-Step "Parar o serviço $ServiceName para atualização" { Stop-Service -Name $ServiceName -Force; (Get-Service $ServiceName).WaitForStatus('Stopped', '00:00:30') } | Out-Null
    }

    Invoke-Step "Copiar binários para $InstallDir" {
        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
        foreach ($file in $Binaries + 'Uninstall-WRAppertureHost.ps1', 'Desinstalar.cmd') {
            $source = Join-Path $PSScriptRoot $file
            if (Test-Path $source) { Copy-Item -Path $source -Destination $InstallDir -Force }
        }
    } | Out-Null

    Invoke-Step 'Adicionar a pasta de instalação ao PATH do sistema' {
        $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
        if (($machinePath -split ';') -notcontains $InstallDir) {
            [Environment]::SetEnvironmentVariable('Path', ($machinePath.TrimEnd(';') + ';' + $InstallDir), 'Machine')
        }
    } | Out-Null

    Invoke-Step 'Preparar o host para RemoteApp (wrahost prepare)' {
        $exe = Join-Path $InstallDir 'wrahost.exe'
        & $exe prepare | Out-Host
        if ($LASTEXITCODE -eq 3) { throw 'Esta edição do Windows não suporta RemoteApp (use Pro, Enterprise ou Education).' }
        if ($LASTEXITCODE -ne 0) { throw "wrahost prepare falhou (código $LASTEXITCODE)." }
    } | Out-Null

    $settings = [pscustomobject]@{ port = 5080; isPaired = $false; certificateFingerprint = $null }
    if (-not $DryRun) { $settings = Invoke-Wrahost @('agent', 'show', '--json') | ConvertFrom-Json }

    if (-not $settings.isPaired) {
        $newToken = Invoke-Step 'Gerar token de pareamento' { (Invoke-Wrahost @('agent', 'token', '--json') | ConvertFrom-Json).token }
    }
    else {
        Write-Step 'Host já pareado: token existente mantido'
    }

    $binPath = '"' + (Join-Path $InstallDir 'WRApperture.Agent.exe') + '"'
    if ($service) {
        Invoke-Step "Atualizar o serviço $ServiceName" { sc.exe config $ServiceName binPath= $binPath start= auto | Out-Null } | Out-Null
    }
    else {
        Invoke-Step "Registrar o serviço $ServiceName (automático, LocalSystem)" {
            New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName 'WRApperture Agent' `
                -Description 'Publica a lista de RemoteApps deste PC para clientes pareados.' -StartupType Automatic | Out-Null
        } | Out-Null
    }

    Invoke-Step 'Configurar reinício automático em caso de falha' {
        sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null
    } | Out-Null

    $profiles = if ($AllowPublicNetwork) { 'Domain,Private,Public' } else { 'Domain,Private' }
    Invoke-Step "Liberar a porta TCP $($settings.port) no firewall ($profiles)" {
        Get-NetFirewallRule -Name $FirewallRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        New-NetFirewallRule -Name $FirewallRule -DisplayName 'WRApperture Agent' -Direction Inbound -Protocol TCP `
            -LocalPort $settings.port -Action Allow -Profile $profiles.Split(',') | Out-Null
    } | Out-Null

    Invoke-Step "Iniciar o serviço $ServiceName" { Start-Service -Name $ServiceName } | Out-Null

    $healthy = $false
    if (-not $DryRun) {
        Write-Step 'Verificando se o agente responde'
        for ($i = 0; $i -lt 30 -and -not $healthy; $i++) {
            Start-Sleep -Seconds 1
            $code = & curl.exe -k -s -o NUL -w '%{http_code}' "https://localhost:$($settings.port)/health" 2>$null
            $healthy = $code -eq '200'
        }
        $settings = Invoke-Wrahost @('agent', 'show', '--json') | ConvertFrom-Json
    }

    Write-Host ''
    if ($DryRun) { Write-Host 'Simulação concluída.' -ForegroundColor DarkYellow; exit 0 }

    if (-not $healthy) {
        Write-Host "O agente não respondeu na porta $($settings.port)." -ForegroundColor Red
        $log = Get-ChildItem (Join-Path $env:ProgramData 'WRApperture\logs') -Filter 'agent*.log' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime | Select-Object -Last 1
        if ($log) { Write-Host "Últimas linhas de $($log.FullName):"; Get-Content $log.FullName -Tail 15 | Out-Host }
        Write-Host 'Se a porta estiver em uso, troque com "wrahost agent port <n>" e rode o instalador de novo.'
        exit 1
    }

    $addresses = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        Select-Object -ExpandProperty IPAddress
    $fingerprint = ($settings.certificateFingerprint -split '(.{4})' | Where-Object { $_ }) -join ' '

    Write-Host 'WRApperture instalado. Agente respondendo.' -ForegroundColor Green
    Write-Host ''
    Write-Host "  Computador:   $env:COMPUTERNAME"
    Write-Host "  Endereço(s):  $($addresses -join ', ')"
    Write-Host "  Porta:        $($settings.port)"
    Write-Host "  Fingerprint:  $fingerprint"
    if ($newToken) {
        Write-Host ''
        Write-Host '  Token de pareamento (exibido apenas agora):' -ForegroundColor Yellow
        Write-Host "      $newToken" -ForegroundColor Yellow
    }
    else {
        Write-Host '  Token:        o atual foi mantido (gere outro com "wrahost agent token")'
    }

    Write-Host ''
    Write-Host 'Próximos passos:'
    Write-Host '  1. Publique apps (novo terminal como administrador): wrahost discover / wrahost publish --path "..." --name "..."'
    Write-Host '  2. No outro computador, abra o WRApperture e adicione este host com o endereço, a porta e o token acima.'
    Write-Host "  Para desinstalar: duplo clique em $InstallDir\Desinstalar.cmd"
    Write-Host '  Atenção: no Windows Pro só há uma sessão por vez; abrir um app remoto desconecta quem estiver usando este PC.' -ForegroundColor DarkYellow

    $public = Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object NetworkCategory -eq 'Public'
    if ($public -and -not $AllowPublicNetwork) {
        Write-Host "  Aviso: a rede '$($public.Name -join ', ')' está como Pública; o agente não aceita conexões nela." -ForegroundColor DarkYellow
        Write-Host '  Altere a rede para Privada nas configurações do Windows ou reinstale com -AllowPublicNetwork.' -ForegroundColor DarkYellow
    }
    exit 0
}
catch {
    Write-Host "Falha na instalação: $($_.Exception.Message)" -ForegroundColor Red
    if ($newToken) {
        # The token is only shown once; never lose it because a later step failed.
        Write-Host "Token de pareamento gerado (guarde-o): $newToken" -ForegroundColor Yellow
    }
    exit 1
}
