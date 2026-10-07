<#
.SYNOPSIS
    Instala ou atualiza o cliente WRApperture para o usuário atual (não precisa de administrador).

.PARAMETER InstallDir
    Padrão: "$env:LOCALAPPDATA\Programs\WRApperture".

.PARAMETER StartMenuDir
    Pasta onde o atalho "WRApperture" é criado. Padrão: Menu Iniciar do usuário.

.PARAMETER Launch
    Abre o cliente ao final.

.PARAMETER DryRun
    Mostra o que seria feito, sem alterar nada.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\WRApperture'),
    [string]$StartMenuDir = [Environment]::GetFolderPath('Programs'),
    [switch]$Launch,
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
    $source = Join-Path $PSScriptRoot $ExeName
    if (-not $DryRun -and -not (Test-Path $source)) {
        throw "Arquivo '$ExeName' não encontrado ao lado do instalador. Extraia o pacote completo antes de instalar."
    }

    $target = Join-Path $InstallDir $ExeName
    $running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $target }
    if ($running) {
        Invoke-Step 'Fechar o WRApperture em execução para atualizar' {
            $running | ForEach-Object { $null = $_.CloseMainWindow(); if (-not $_.WaitForExit(5000)) { $_.Kill() } }
        }
    }

    Invoke-Step "Copiar o cliente para $InstallDir" {
        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
        Copy-Item -Path $source -Destination $InstallDir -Force
        foreach ($file in 'Uninstall-WRAppertureClient.ps1', 'Desinstalar.cmd') {
            $extra = Join-Path $PSScriptRoot $file
            if (Test-Path $extra) { Copy-Item -Path $extra -Destination $InstallDir -Force }
        }
    }

    Invoke-Step 'Criar atalhos "WRApperture" e "Desinstalar WRApperture" no Menu Iniciar' {
        New-Item -ItemType Directory -Force -Path $StartMenuDir | Out-Null
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Join-Path $StartMenuDir 'WRApperture.lnk'))
        $shortcut.TargetPath = $target
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.Description = 'Abrir apps de outro computador (WRApperture)'
        $shortcut.Save()

        $uninstallCmd = Join-Path $InstallDir 'Desinstalar.cmd'
        if (Test-Path $uninstallCmd) {
            $uninstall = $shell.CreateShortcut((Join-Path $StartMenuDir 'Desinstalar WRApperture.lnk'))
            $uninstall.TargetPath = $uninstallCmd
            $uninstall.WorkingDirectory = $env:TEMP   # never inside the folder being deleted
            $uninstall.IconLocation = "$target,0"
            $uninstall.Description = 'Remover o WRApperture deste computador'
            $uninstall.Save()
        }

        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }

    Write-Host ''
    if ($DryRun) { Write-Host 'Simulação concluída.' -ForegroundColor DarkYellow; exit 0 }
    Write-Host 'Cliente WRApperture instalado. Abra "WRApperture" pelo Menu Iniciar.' -ForegroundColor Green
    Write-Host 'Para desinstalar: Menu Iniciar → "Desinstalar WRApperture".'
    if ($Launch) { Start-Process -FilePath $target }
    exit 0
}
catch {
    Write-Host "Falha na instalação: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
