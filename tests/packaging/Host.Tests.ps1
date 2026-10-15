# Pester 3.4 (ships with Windows PowerShell 5.1). Run: Invoke-Pester tests\packaging
$scripts = Join-Path $PSScriptRoot '..\..\packaging\host'

# Output assertions are in English; the scripts run as child processes and inherit this.
$previousLanguage = $env:WRAPPERTURE_LANG
$env:WRAPPERTURE_LANG = 'en'

function Invoke-Script([string]$Name, [string[]]$Arguments) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts $Name) @Arguments *>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

Describe 'Install-WRAppertureHost' {
    if (-not $isAdmin) {
        It 'refuses to run without administrator rights and changes nothing' {
            $dir = Join-Path $TestDrive 'refuse'
            $r = Invoke-Script 'Install-WRAppertureHost.ps1' @('-InstallDir', $dir)

            $r.ExitCode | Should Be 2
            $r.Output | Should Match 'administrator'
            $dir | Should Not Exist
        }
    }

    It 'dry run lists every step without touching the system' {
        $dir = Join-Path $TestDrive 'dry'
        $desktop = Join-Path $TestDrive 'desktop'
        # The host may really be installed on the machine running the tests: compare, don't assume absence.
        $serviceBefore = [bool](Get-Service WRAppertureAgent -ErrorAction SilentlyContinue)
        $r = Invoke-Script 'Install-WRAppertureHost.ps1' @('-InstallDir', $dir, '-DesktopDir', $desktop, '-DryRun')

        $r.ExitCode | Should Be 0
        $r.Output | Should Match '\[dry run\] Copy binaries'
        $r.Output | Should Match '\[dry run\] Create the desktop shortcut .*WRApperture \(Host\)\.lnk'
        $desktop | Should Not Exist
        $r.Output | Should Match '\[dry run\] Create the "WRApperture \(Host\)" shortcut'
        $r.Output | Should Match '\[dry run\] Prepare the host'
        $r.Output | Should Match '\[dry run\] Generate a pairing token'
        $r.Output | Should Match '\[dry run\] (Register|Update) the WRAppertureAgent service'
        $r.Output | Should Match 'automatic restart'
        $r.Output | Should Match 'TCP port 5080 in the firewall \(Domain,Private\)'
        $r.Output | Should Match 'UDP port 5081 in the firewall .*\(Domain,Private\)'
        $r.Output | Should Match '\[dry run\] Start the WRAppertureAgent service'
        $dir | Should Not Exist
        [bool](Get-Service WRAppertureAgent -ErrorAction SilentlyContinue) | Should Be $serviceBefore
    }

    It 'from the setup wizard (run inside the install folder) only configures the system' {
        # The wizard has copied the files and made the shortcuts; pairing is done in the host app.
        $dir = Join-Path $TestDrive 'setup'
        New-Item -ItemType Directory $dir | Out-Null
        Copy-Item (Join-Path $scripts 'Install-WRAppertureHost.ps1') $dir
        Copy-Item (Join-Path $scripts '..\Localization.ps1') $dir
        Copy-Item (Join-Path $scripts '..\..\localization') $dir -Recurse

        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dir 'Install-WRAppertureHost.ps1') `
            -InstallDir $dir -FromSetup -DryRun *>&1 | Out-String

        $LASTEXITCODE | Should Be 0
        $output | Should Not Match 'Copy binaries'
        $output | Should Not Match 'shortcut'
        $output | Should Not Match 'pairing token'
        $output | Should Match '\[dry run\] Prepare the host'
        $output | Should Match '\[dry run\] Start the WRAppertureAgent service'
    }

    It 'dry run with -NoDesktopShortcut skips the desktop shortcut' {
        $r = Invoke-Script 'Install-WRAppertureHost.ps1' @('-InstallDir', (Join-Path $TestDrive 'dry2'), '-NoDesktopShortcut', '-DryRun')

        $r.ExitCode | Should Be 0
        $r.Output | Should Not Match 'desktop shortcut'
    }

    It 'dry run with -AllowPublicNetwork includes the Public profile' {
        $r = Invoke-Script 'Install-WRAppertureHost.ps1' @('-InstallDir', (Join-Path $TestDrive 'pub'), '-DryRun', '-AllowPublicNetwork')

        $r.Output | Should Match 'Domain,Private,Public'
    }

    It 'speaks Portuguese when the language is pt-BR' {
        $env:WRAPPERTURE_LANG = 'pt-BR'
        try { $r = Invoke-Script 'Install-WRAppertureHost.ps1' @('-InstallDir', (Join-Path $TestDrive 'pt'), '-DryRun') }
        finally { $env:WRAPPERTURE_LANG = 'en' }

        $r.ExitCode | Should Be 0
        $r.Output | Should Match '\[simulação\] Copiar binários'
        $r.Output | Should Match 'Simulação concluída'
    }
}

Describe 'Uninstall-WRAppertureHost' {
    if (-not $isAdmin) {
        It 'refuses to run without administrator rights' {
            (Invoke-Script 'Uninstall-WRAppertureHost.ps1' @('-InstallDir', (Join-Path $TestDrive 'x'))).ExitCode | Should Be 2
        }
    }

    It 'dry run with -Purge plans revert, reset and removal but keeps files' {
        $dir = Join-Path $TestDrive 'installed'
        New-Item -ItemType Directory $dir | Out-Null
        Set-Content (Join-Path $dir 'wrahost.exe') 'stub'

        $r = Invoke-Script 'Uninstall-WRAppertureHost.ps1' @('-InstallDir', $dir, '-Purge', '-DryRun')

        $r.ExitCode | Should Be 0
        $r.Output | Should Match 'wrahost revert'
        $r.Output | Should Match 'wrahost agent reset'
        $r.Output | Should Match "\[dry run\] Delete"
        Join-Path $dir 'wrahost.exe' | Should Exist
    }

    It 'with -KeepFiles leaves the folder to the setup uninstaller' {
        $dir = Join-Path $TestDrive 'kept'
        New-Item -ItemType Directory $dir | Out-Null

        $r = Invoke-Script 'Uninstall-WRAppertureHost.ps1' @('-InstallDir', $dir, '-KeepFiles', '-DryRun')

        $r.ExitCode | Should Be 0
        $r.Output | Should Not Match '\[dry run\] Delete'
    }

    It 'dry run without -Purge does not plan revert' {
        $r = Invoke-Script 'Uninstall-WRAppertureHost.ps1' @('-InstallDir', (Join-Path $TestDrive 'none'), '-DryRun')

        $r.ExitCode | Should Be 0
        $r.Output | Should Not Match 'wrahost revert'
    }
}

$env:WRAPPERTURE_LANG = $previousLanguage
