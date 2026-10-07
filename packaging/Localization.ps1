<#
.SYNOPSIS
    Translations for the installer scripts. Dot-source it, then write texts as T 'installer.<key>' [args...].

.DESCRIPTION
    Same rules as WRApperture.Shared.Localization.Strings:
    - Catalog: localization\<language>\<area>.json (flat JSON); a key is "<area>.<key>".
      Looked up next to this file (packages, install folder) or one level up (repo: packaging\..\localization).
    - Language: WRAPPERTURE_LANG, else HKCU\Software\WRApperture\Ui\Language, else the Windows display language.
    - "pt-BR" looks in pt-BR, then pt, then en; a missing key falls back the same way and finally to the key itself.
    - Languages are just the folders present: there is no list to edit.
    The catalog is read once, up front, so an uninstaller can still talk after deleting its own folder.
#>

$WRLocalization = @{
    Default  = 'en'
    Root     = $null
    Chain    = @('en')
    Catalogs = @{}
}

foreach ($candidate in 'localization', '..\localization') {
    $dir = Join-Path $PSScriptRoot $candidate
    if (Test-Path -LiteralPath $dir -PathType Container) { $WRLocalization.Root = [IO.Path]::GetFullPath($dir); break }
}

# "pt-BR" -> pt-BR, pt, en. Always ends in the default language.
function Get-WRLanguageChain([string]$Name) {
    $chain = New-Object System.Collections.Generic.List[string]
    if ($Name -and $Name.Trim()) {
        try {
            for ($culture = [Globalization.CultureInfo]::GetCultureInfo($Name.Trim()); $culture.Name.Length -gt 0; $culture = $culture.Parent) {
                $chain.Add($culture.Name)
            }
        }
        catch {
            # Unknown name: just the default language.
        }
    }
    $chain.Add($WRLocalization.Default)

    $seen = @{}   # case-insensitive
    $result = @()
    foreach ($language in $chain) {
        if (-not $seen.ContainsKey($language)) { $seen[$language] = $true; $result += $language }
    }
    return $result
}

function Get-WRRequestedLanguage {
    $value = $env:WRAPPERTURE_LANG
    if (-not ($value -and $value.Trim())) {
        try { $value = [string](Get-ItemProperty -Path 'HKCU:\Software\WRApperture\Ui' -Name 'Language' -ErrorAction Stop).Language }
        catch { $value = $null }
    }
    if ($value -and $value.Trim()) { return $value.Trim() }
    return $null
}

# Every <area>.json of one language folder, keyed "<area>.<key>". Empty when the folder doesn't exist.
function Read-WRCatalog([string]$Language) {
    $entries = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    if ($WRLocalization.Root) {
        $dir = Join-Path $WRLocalization.Root $Language
        if (Test-Path -LiteralPath $dir -PathType Container) {
            foreach ($file in Get-ChildItem -LiteralPath $dir -Filter '*.json' -File) {
                try { $json = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json }
                catch { continue }
                foreach ($property in $json.PSObject.Properties) { $entries["$($file.BaseName).$($property.Name)"] = [string]$property.Value }
            }
        }
    }
    return , $entries
}

# Picks the language (automatic when $Language is empty) and loads its chain.
function Use-WRLanguage([string]$Language) {
    if (-not $Language) { $Language = Get-WRRequestedLanguage }
    if (-not $Language) { $Language = (Get-UICulture).Name }
    $WRLocalization.Chain = @(Get-WRLanguageChain $Language)
    $WRLocalization.Catalogs = @{}
    foreach ($name in $WRLocalization.Chain) { $WRLocalization.Catalogs[$name] = Read-WRCatalog $name }
}

# The text for a key following the chain; the key itself when no language has it.
function Get-WRText([string]$Key) {
    foreach ($name in $WRLocalization.Chain) {
        $catalog = $WRLocalization.Catalogs[$name]
        if ($null -ne $catalog -and $catalog.ContainsKey($Key)) { return $catalog[$Key] }
    }
    return $Key
}

# Translation with {0}-style placeholders (like -f), formatted with the current culture.
function T([string]$Key) {
    $text = Get-WRText $Key
    if ($args.Count -eq 0) { return $text }
    return [string]::Format([Globalization.CultureInfo]::CurrentCulture, $text, [object[]]$args)
}

# True when the answer starts with a local "yes" letter (catalog) or y/Y.
function Test-WRYes([string]$Answer) {
    $answer = "$Answer".Trim()
    if ($answer.Length -eq 0) { return $false }
    $letters = Get-WRText 'installer.yesLetters'
    if ($letters -eq 'installer.yesLetters') { $letters = '' }   # never treat the key's own letters as "yes"
    return ($letters + 'yY').Contains($answer.Substring(0, 1))
}

Use-WRLanguage
