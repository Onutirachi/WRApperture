# Pester 3.4. The installers' translation loader (packaging\Localization.ps1), which mirrors
# WRApperture.Shared.Localization.Strings.
$repo = Join-Path $PSScriptRoot '..\..'
$loader = Join-Path $repo 'packaging\Localization.ps1'

$previousLanguage = $env:WRAPPERTURE_LANG

function Get-Catalog([string]$Language) {
    $json = Get-Content -Raw -Encoding UTF8 (Join-Path $repo "localization\$Language\installer.json") | ConvertFrom-Json
    return @($json.PSObject.Properties | ForEach-Object { $_.Name })
}

Describe 'Localization.ps1' {
    $env:WRAPPERTURE_LANG = 'en'
    . $loader

    It 'regional language falls back to its parent, then English' {
        (Get-WRLanguageChain 'pt-BR') -join ',' | Should Be 'pt-BR,pt,en'

        Use-WRLanguage 'pt-BR'
        T 'installer.dryRun.done' | Should Be 'Simulação concluída.'
    }

    It 'unknown language ends in English' {
        @(Get-WRLanguageChain 'xx-YY')[-1] | Should Be 'en'
        @(Get-WRLanguageChain 'not a culture!')[-1] | Should Be 'en'

        Use-WRLanguage 'xx-YY'
        T 'installer.dryRun.done' | Should Be 'Dry run complete.'
    }

    It 'missing key returns the key, with or without arguments' {
        Use-WRLanguage 'pt-BR'
        T 'installer.nope.missing' | Should Be 'installer.nope.missing'
        T 'installer.nope.missing' 1 2 | Should Be 'installer.nope.missing'
    }

    It 'formats {0} placeholders' {
        Use-WRLanguage 'en'
        T 'installer.deleteFolder' 'C:\x' | Should Be 'Delete C:\x'
        T 'installer.host.install.firewall' 5080 'Domain,Private' | Should Be 'Open TCP port 5080 in the firewall (Domain,Private)'
    }

    It 'follows WRAPPERTURE_LANG when no language is given' {
        $env:WRAPPERTURE_LANG = 'pt'
        try { Use-WRLanguage } finally { $env:WRAPPERTURE_LANG = 'en' }
        T 'installer.pressEnter' | Should Be 'Pressione Enter para fechar'

        Use-WRLanguage
        T 'installer.pressEnter' | Should Be 'Press Enter to close'
    }

    It 'accepts the local yes letters and always y/Y' {
        Use-WRLanguage 'pt'
        Test-WRYes 's' | Should Be $true
        Test-WRYes '  Sim' | Should Be $true
        Test-WRYes 'y' | Should Be $true
        Test-WRYes 'n' | Should Be $false
        Test-WRYes '' | Should Be $false

        Use-WRLanguage 'en'
        Test-WRYes 'Yes' | Should Be $true
        Test-WRYes 's' | Should Be $false
        Test-WRYes 'n' | Should Be $false
    }

    It 'Portuguese has exactly the English keys' {
        $english = Get-Catalog 'en'
        $portuguese = Get-Catalog 'pt'

        @($english | Where-Object { $portuguese -notcontains $_ }) | Should BeNullOrEmpty
        @($portuguese | Where-Object { $english -notcontains $_ }) | Should BeNullOrEmpty
    }

    It 'every T key used by the packaging scripts exists in English' {
        $english = Get-Catalog 'en'
        $used = Get-ChildItem (Join-Path $repo 'packaging') -Filter '*.ps1' -Recurse |
            Select-String -Pattern "(?<![\w-])T\s+'installer\.([A-Za-z0-9.]+)'" -AllMatches |
            ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

        $used.Count | Should BeGreaterThan 50
        @($used | Where-Object { $english -notcontains $_ }) | Should BeNullOrEmpty
    }
}

Describe 'Localization.ps1 with a new language folder' {
    # A copy of the loader next to its own catalog: en complete, es partial (no list to edit anywhere).
    $dir = Join-Path $TestDrive 'pkg'
    New-Item -ItemType Directory (Join-Path $dir 'localization\en'), (Join-Path $dir 'localization\es') -Force | Out-Null
    Copy-Item $loader $dir
    Set-Content -Encoding UTF8 (Join-Path $dir 'localization\en\installer.json') '{ "a.hello": "Hello", "a.bye": "Bye {0}" }'
    Set-Content -Encoding UTF8 (Join-Path $dir 'localization\es\installer.json') '{ "a.hello": "Hola" }'
    $env:WRAPPERTURE_LANG = 'es-MX'
    try { . (Join-Path $dir 'Localization.ps1') } finally { $env:WRAPPERTURE_LANG = $previousLanguage }

    It 'is picked up from the folder, and a missing key falls back to English' {
        (T 'installer.a.hello') | Should Be 'Hola'
        (T 'installer.a.bye' 'Ana') | Should Be 'Bye Ana'
        (T 'installer.a.none') | Should Be 'installer.a.none'
    }

    It 'with no yes letters in the catalog, only y/Y mean yes' {
        Test-WRYes 'i' | Should Be $false
        Test-WRYes 'n' | Should Be $false
        Test-WRYes 'y' | Should Be $true
    }
}

$env:WRAPPERTURE_LANG = $previousLanguage
