# Pester 3.4. Installs and uninstalls the client for real, inside $TestDrive (no admin needed).
$scripts = Join-Path $PSScriptRoot '..\..\packaging\client'

function New-Package {
    $pkg = Join-Path $TestDrive ('pkg-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $pkg | Out-Null
    Copy-Item (Join-Path $scripts '*.ps1') $pkg
    Copy-Item (Join-Path $scripts 'Desinstalar.cmd') $pkg
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
    }
}

function Invoke-Script([string]$Path, [string[]]$Arguments) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments *>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Install($pkg, $l, [string[]]$Extra = @()) {
    Invoke-Script (Join-Path $pkg 'Install-WRAppertureClient.ps1') (@('-InstallDir', $l.Install, '-StartMenuDir', $l.Start) + $Extra)
}

function Uninstall($l, [string[]]$Extra = @()) {
    $uninstaller = Join-Path $TestDrive 'Uninstall-WRAppertureClient.ps1'
    Copy-Item (Join-Path $scripts 'Uninstall-WRAppertureClient.ps1') $uninstaller -Force
    Invoke-Script $uninstaller (@('-InstallDir', $l.Install, '-StartMenuDir', $l.Start, '-UserDataDir', $l.UserData, '-DesktopDir', $l.Desktop, '-KeepSigning') + $Extra)
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
        Get-LinkTarget (Join-Path $l.Start 'Desinstalar WRApperture.lnk') | Should Be (Join-Path $l.Install 'Desinstalar.cmd')
    }

    It 'reinstalling updates the binary and keeps user data' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-Item -ItemType Directory (Join-Path $l.UserData 'client') -Force | Out-Null
        Set-Content (Join-Path $l.UserData 'client\hosts.json') '{"hosts":[]}'
        Set-Content (Join-Path $pkg 'WRApperture.exe') 'stub-exe-v2'

        (Install $pkg $l).ExitCode | Should Be 0

        Get-Content (Join-Path $l.Install 'WRApperture.exe') | Should Be 'stub-exe-v2'
        Join-Path $l.UserData 'client\hosts.json' | Should Exist
    }

    It 'fails clearly when the package is incomplete' {
        $pkg = New-Package; $l = New-Layout
        Remove-Item (Join-Path $pkg 'WRApperture.exe')

        $r = Install $pkg $l

        $r.ExitCode | Should Be 1
        $r.Output | Should Match 'não encontrado'
        $l.Install | Should Not Exist
    }

    It 'dry run changes nothing' {
        $pkg = New-Package; $l = New-Layout

        $r = Install $pkg $l @('-DryRun')

        $r.ExitCode | Should Be 0
        $r.Output | Should Match '\[simulação\] Copiar o cliente'
        $l.Install | Should Not Exist
        $l.Start | Should Not Exist
    }
}

Describe 'Uninstall-WRAppertureClient' {
    It 'removes program and shortcut but keeps user data by default' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-Item -ItemType Directory (Join-Path $l.UserData 'client') -Force | Out-Null
        Set-Content (Join-Path $l.UserData 'client\hosts.json') '{"hosts":[]}'

        $r = Uninstall $l

        $r.ExitCode | Should Be 0
        $l.Install | Should Not Exist
        Join-Path $l.Start 'WRApperture.lnk' | Should Not Exist
        Join-Path $l.Start 'Desinstalar WRApperture.lnk' | Should Not Exist
        Join-Path $l.UserData 'client\hosts.json' | Should Exist
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

    It 'interactive mode asks about user data and removes it when the answer is yes' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-Item -ItemType Directory (Join-Path $l.UserData 'client') -Force | Out-Null
        Set-Content (Join-Path $l.UserData 'client\hosts.json') '{"hosts":[]}'
        $uninstaller = Join-Path $TestDrive 'Uninstall-WRAppertureClient.ps1'
        Copy-Item (Join-Path $scripts 'Uninstall-WRAppertureClient.ps1') $uninstaller -Force

        $output = "s`n`n" | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $uninstaller -InstallDir $l.Install `
            -StartMenuDir $l.Start -UserDataDir $l.UserData -DesktopDir $l.Desktop -KeepSigning -Interactive *>&1 | Out-String

        $LASTEXITCODE | Should Be 0
        $l.UserData | Should Not Exist
        $l.Install | Should Not Exist
    }

    It 'interactive mode keeps user data by default' {
        $pkg = New-Package; $l = New-Layout
        Install $pkg $l | Out-Null
        New-Item -ItemType Directory (Join-Path $l.UserData 'client') -Force | Out-Null
        Set-Content (Join-Path $l.UserData 'client\hosts.json') '{"hosts":[]}'
        $uninstaller = Join-Path $TestDrive 'Uninstall-WRAppertureClient.ps1'
        Copy-Item (Join-Path $scripts 'Uninstall-WRAppertureClient.ps1') $uninstaller -Force

        "`n`n" | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $uninstaller -InstallDir $l.Install `
            -StartMenuDir $l.Start -UserDataDir $l.UserData -DesktopDir $l.Desktop -KeepSigning -Interactive *>&1 | Out-Null

        Join-Path $l.UserData 'client\hosts.json' | Should Exist
        $l.Install | Should Not Exist
    }
    It 'is idempotent' {
        $l = New-Layout

        (Uninstall $l).ExitCode | Should Be 0
        (Uninstall $l @('-RemoveUserData')).ExitCode | Should Be 0
    }
}
