# Pester 3.4. Installs and uninstalls the client for real, inside $TestDrive (no admin needed).
$repo = Join-Path $PSScriptRoot '..\..'
$scripts = Join-Path $repo 'packaging\client'

# Output assertions are in English; the scripts run as child processes and inherit this.
$previousLanguage = $env:WRAPPERTURE_LANG
$env:WRAPPERTURE_LANG = 'en'

# Same layout as scripts\build-release.ps1: Localization.ps1 + localization\<language>\installer.json.
function Copy-Localization([string]$Destination) {
    Copy-Item (Join-Path $repo 'packaging\Localization.ps1') $Destination -Force
    foreach ($language in Get-ChildItem (Join-Path $repo 'localization') -Directory) {
        $target = Join-Path $Destination "localization\$($language.Name)"
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item (Join-Path $language.FullName 'installer.json') $target -Force
    }
}

function New-Package {
    $pkg = Join-Path $TestDrive ('pkg-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $pkg | Out-Null
    Copy-Item (Join-Path $scripts '*.ps1') $pkg
    Copy-Item (Join-Path $scripts 'Desinstalar.cmd') $pkg
    Copy-Localization $pkg
    Set-Content (Join-Path $pkg 'WRApperture.exe') 'stub-exe'
    return $pkg
}

function New-Layout {
    $base = Join-Path $TestDrive ('env-' + [guid]::NewGuid().ToString('N'))
    return [pscustomobject]@{
        Install  = Join-Path $base 'Programs\WRApperture'
        Start    = Join-Path $base 'StartMenu'
        UserData = Join-Path $base 'UserData'
        Desktop  = Join-Path $base 'Desktop'
        # A throwaway key: the real Run key is never touched.
        RunKey   = 'HKCU:\Software\WRApperture.Tests.' + [guid]::NewGuid().ToString('N')
    }
}

function Invoke-Script([string]$Path, [string[]]$Arguments) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments *>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Install($pkg, $l, [string[]]$Extra = @()) {
    Invoke-Script (Join-Path $pkg 'Install-WRAppertureClient.ps1') (@('-InstallDir', $l.Install, '-StartMenuDir', $l.Start) + $Extra)
}

function Get-Uninstaller {
    $uninstaller = Join-Path $TestDrive 'Uninstall-WRAppertureClient.ps1'
    Copy-Item (Join-Path $scripts 'Uninstall-WRAppertureClient.ps1') $uninstaller -Force
    Copy-Localization $TestDrive
    return $uninstaller
}

function Uninstall($l, [string[]]$Extra = @()) {
    Invoke-Script (Get-Uninstaller) (@('-InstallDir', $l.Install, '-StartMenuDir', $l.Start, '-UserDataDir', $l.UserData, '-DesktopDir', $l.Desktop, '-RunKey', $l.RunKey, '-KeepSigning') + $Extra)
}

function Invoke-Interactive([string]$Uninstaller, $l, [string]$Answer) {
    "$Answer`n`n" | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Uninstaller -InstallDir $l.Install `
        -StartMenuDir $l.Start -UserDataDir $l.UserData -DesktopDir $l.Desktop -RunKey $l.RunKey -KeepSigning -Interactive *>&1 | Out-String
}

function New-UserData($l) {
    New-Item -ItemType Directory (Join-Path $l.UserData 'client') -Force | Out-Null
    Set-Content (Join-Path $l.UserData 'client\hosts.json') '{"hosts":[]}'
}

function Get-LinkTarget([string]$Path) {
    $shell = New-Object -ComObject WScript.Shell
    return $shell.CreateShortcut($Path).TargetPath
}

Describe 'Install-WRAppertureClient' {
    It 'copies the client, the uninstaller and creates a Start Menu shortcut' {
        $pkg = New-Package; $l = New-Layout

        $r = Install $pkg $l

        $r.ExitCode | Should Be 0
        Join-Path $l.Install 'WRApperture.exe' | Should Exist
        Join-Path $l.Install 'Uninstall-WRAppertureClient.ps1' | Should Exist
        Get-LinkTarget (Join-Path $l.Start 'WRApperture.lnk') | Should Be (Join-Path $l.Install 'WRApperture.exe')
        Join-Path $l.Install 'Desinstalar.cmd' | Should Exist
        Get-LinkTarget (Join-Path $l.Start 'Uninstall WRApperture.lnk') | Should Be (Join-Path $l.Install 'Desinstalar.cmd')
        $r.Output | Should Match 'WRApperture client installed'
    }

    It 'copies the translations into the install folder' {
        $pkg = New-Package; $l = New-Layout

        (Install $pkg $l).ExitCode | Should Be 0

        Join-Path $l.Install 'Localization.ps1' | Should Exist
        foreach ($language in Get-ChildItem (Join-Path $repo 'localization') -Directory) {
            Join-Path $l.Install "localization\$($language.Name)\installer.json" | Should Exist
        }
        Join-Path $l.Install 'localization\localization' | Should Not Exist
    }

    It 'reinstalling updates the binary and keeps user data' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-UserData $l
        Set-Content (Join-Path $pkg 'WRApperture.exe') 'stub-exe-v2'

        (Install $pkg $l).ExitCode | Should Be 0

        Get-Content (Join-Path $l.Install 'WRApperture.exe') | Should Be 'stub-exe-v2'
        Join-Path $l.UserData 'client\hosts.json' | Should Exist
        Join-Path $l.Install 'localization\localization' | Should Not Exist
    }

    It 'names the uninstall shortcut in the install language and replaces one from another language' {
        $pkg = New-Package; $l = New-Layout
        $env:WRAPPERTURE_LANG = 'pt-BR'
        try { Install $pkg $l | Out-Null } finally { $env:WRAPPERTURE_LANG = 'en' }
        Join-Path $l.Start 'Desinstalar WRApperture.lnk' | Should Exist

        Install $pkg $l | Out-Null

        Join-Path $l.Start 'Desinstalar WRApperture.lnk' | Should Not Exist
        Join-Path $l.Start 'Uninstall WRApperture.lnk' | Should Exist
    }

    It 'fails clearly when the package is incomplete' {
        $pkg = New-Package; $l = New-Layout
        Remove-Item (Join-Path $pkg 'WRApperture.exe')

        $r = Install $pkg $l

        $r.ExitCode | Should Be 1
        $r.Output | Should Match 'not found next to the installer'
        $l.Install | Should Not Exist
    }

    It 'dry run changes nothing' {
        $pkg = New-Package; $l = New-Layout

        $r = Install $pkg $l @('-DryRun')

        $r.ExitCode | Should Be 0
        $r.Output | Should Match '\[dry run\] Copy the client'
        $l.Install | Should Not Exist
        $l.Start | Should Not Exist
    }
}

Describe 'Uninstall-WRAppertureClient' {
    It 'removes program and shortcut but keeps user data by default' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-UserData $l

        $r = Uninstall $l

        $r.ExitCode | Should Be 0
        $l.Install | Should Not Exist
        Join-Path $l.Start 'WRApperture.lnk' | Should Not Exist
        Join-Path $l.Start 'Uninstall WRApperture.lnk' | Should Not Exist
        Join-Path $l.UserData 'client\hosts.json' | Should Exist
    }

    It 'removes the uninstall shortcut whatever language it was created in' {
        $pkg = New-Package; $l = New-Layout
        $env:WRAPPERTURE_LANG = 'pt'
        try { Install $pkg $l | Out-Null } finally { $env:WRAPPERTURE_LANG = 'en' }

        (Uninstall $l).ExitCode | Should Be 0

        Join-Path $l.Start 'Desinstalar WRApperture.lnk' | Should Not Exist
        Join-Path $l.Start 'WRApperture.lnk' | Should Not Exist
    }

    It 'run from the install folder, still finds its translations after deleting that folder' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        $env:WRAPPERTURE_LANG = 'pt-BR'
        try {
            $r = Invoke-Script (Join-Path $l.Install 'Uninstall-WRAppertureClient.ps1') @('-InstallDir', $l.Install, '-StartMenuDir', $l.Start,
                '-UserDataDir', $l.UserData, '-DesktopDir', $l.Desktop, '-KeepSigning')
        }
        finally { $env:WRAPPERTURE_LANG = 'en' }

        $r.ExitCode | Should Be 0
        $l.Install | Should Not Exist
        $r.Output | Should Match 'Cliente WRApperture removido\.'
    }

    It '-RemoveUserData also removes data and only the desktop shortcuts created by WRApperture' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        $rdpDir = Join-Path $l.UserData 'client\rdp\abc'
        New-Item -ItemType Directory $rdpDir -Force | Out-Null
        Set-Content (Join-Path $rdpDir 'notepad.rdp') 'x'
        Set-Content (Join-Path $l.UserData 'client\hosts.json') ('{"hosts":[{"address":"wrapperture-pester-' + [guid]::NewGuid().ToString('N') + '.invalid"}]}')
        New-Item -ItemType Directory $l.Desktop | Out-Null
        $other = Join-Path $l.Desktop 'Other.txt'; Set-Content $other 'x'
        $shell = New-Object -ComObject WScript.Shell
        $ours = $shell.CreateShortcut((Join-Path $l.Desktop 'Bloco de Notas.lnk')); $ours.TargetPath = (Join-Path $rdpDir 'notepad.rdp'); $ours.Save()
        $theirs = $shell.CreateShortcut((Join-Path $l.Desktop 'Outro.lnk')); $theirs.TargetPath = $other; $theirs.Save()

        $r = Uninstall $l @('-RemoveUserData')

        $r.ExitCode | Should Be 0
        $l.UserData | Should Not Exist
        Join-Path $l.Desktop 'Bloco de Notas.lnk' | Should Not Exist
        Join-Path $l.Desktop 'Outro.lnk' | Should Exist
        $r.Output | Should Match 'TERMSRV/wrapperture-pester-'
    }

    It '-RemoveUserData removes the app shortcuts from the WRApperture Start menu folder, and the folder when empty' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        $rdpDir = Join-Path $l.UserData 'client\rdp\abc'
        New-Item -ItemType Directory $rdpDir -Force | Out-Null
        Set-Content (Join-Path $rdpDir 'calc.rdp') 'x'
        $appsMenu = Join-Path $l.Start 'WRApperture'
        New-Item -ItemType Directory $appsMenu | Out-Null
        $shell = New-Object -ComObject WScript.Shell
        $ours = $shell.CreateShortcut((Join-Path $appsMenu 'Calculadora.lnk')); $ours.TargetPath = (Join-Path $rdpDir 'calc.rdp'); $ours.Save()

        $r = Uninstall $l @('-RemoveUserData')

        $r.ExitCode | Should Be 0
        $appsMenu | Should Not Exist
        $r.Output | Should Match 'Start menu shortcut'
    }

    It 'removes "start with Windows" only when it points at this installation' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-Item -Path $l.RunKey -Force | Out-Null
        try {
            Set-ItemProperty -Path $l.RunKey -Name 'WRApperture' -Value ('"' + (Join-Path $l.Install 'WRApperture.exe') + '" --tray')
            $r = Uninstall $l
            $r.ExitCode | Should Be 0
            (Get-ItemProperty -Path $l.RunKey -Name 'WRApperture' -ErrorAction SilentlyContinue) | Should BeNullOrEmpty

            Install $pkg $l | Out-Null
            Set-ItemProperty -Path $l.RunKey -Name 'WRApperture' -Value '"C:\Elsewhere\WRApperture.exe" --tray'
            (Uninstall $l).ExitCode | Should Be 0
            (Get-ItemProperty -Path $l.RunKey -Name 'WRApperture').WRApperture | Should Be '"C:\Elsewhere\WRApperture.exe" --tray'
        }
        finally {
            Remove-Item -Path $l.RunKey -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'keeps the WRApperture Start menu folder when it holds something else' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-Item -ItemType Directory (Join-Path $l.UserData 'client\rdp') -Force | Out-Null
        $appsMenu = Join-Path $l.Start 'WRApperture'
        New-Item -ItemType Directory $appsMenu | Out-Null
        Set-Content (Join-Path $appsMenu 'notes.txt') 'x'

        (Uninstall $l @('-RemoveUserData')).ExitCode | Should Be 0

        Join-Path $appsMenu 'notes.txt' | Should Exist
    }

    It 'interactive mode asks about user data and removes it when the answer is yes' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-UserData $l

        $output = Invoke-Interactive (Get-Uninstaller) $l 'y'

        $LASTEXITCODE | Should Be 0
        $output | Should Match 'Uninstall WRApperture from this computer'
        $l.UserData | Should Not Exist
        $l.Install | Should Not Exist
    }

    It 'interactive mode keeps user data by default' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-UserData $l

        Invoke-Interactive (Get-Uninstaller) $l '' | Out-Null

        Join-Path $l.UserData 'client\hosts.json' | Should Exist
        $l.Install | Should Not Exist
    }

    It 'interactive mode in English does not take the Portuguese "s" as yes' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-UserData $l

        Invoke-Interactive (Get-Uninstaller) $l 's' | Out-Null

        Join-Path $l.UserData 'client\hosts.json' | Should Exist
    }

    It 'interactive mode in Portuguese accepts "s" and still accepts "y"' {
        foreach ($answer in 's', 'Y') {
            $pkg = New-Package; $l = New-Layout
            Install $pkg $l | Out-Null
            New-UserData $l
            $env:WRAPPERTURE_LANG = 'pt-BR'
            try { $output = Invoke-Interactive (Get-Uninstaller) $l $answer }
            finally { $env:WRAPPERTURE_LANG = 'en' }

            $output | Should Match 'Desinstalar o WRApperture deste computador'
            $l.UserData | Should Not Exist
        }
    }

    It 'is idempotent' {
        $l = New-Layout

        (Uninstall $l).ExitCode | Should Be 0
        (Uninstall $l @('-RemoveUserData')).ExitCode | Should Be 0
    }
}

$env:WRAPPERTURE_LANG = $previousLanguage
