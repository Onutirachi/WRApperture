<#
.SYNOPSIS
    Builds the WRApperture icons from the SVG drawings in assets/icons.

.DESCRIPTION
    Renders each SVG with WPF at every icon size (sharper than scaling one big image down) and writes:
      assets/icons/WRApperture.ico       client app (WRApperture.exe)
      assets/icons/WRApperture.Host.ico  host app, agent and wrahost
      assets/icons/badge.xaml            corner badge on shortcuts created by the client (vector, drawn at each size)
      assets/icons/desktop.xaml          "Desktop" art: grid tile, tray item and desktop shortcut (vector)
      assets/icons/*.png                 256 px previews of the vector art, for reviewing the drawings
    The outputs are committed, so a normal build does not need this script: run it after changing a drawing.

    Supported SVG subset (what CorelDRAW exports for these drawings): <path d>, <polygon points>, <circle>,
    nested <g transform="translate(..) scale(..)">, fills from class rules in <style> or a fill attribute,
    and fill-rule evenodd/nonzero.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\build-icons.ps1
#>
[CmdletBinding()]
param(
    [string]$IconsDir
)

$ErrorActionPreference = 'Stop'
if (-not $IconsDir) { $IconsDir = Join-Path $PSScriptRoot '..\assets\icons' }
$IconsDir = (Resolve-Path $IconsDir).Path

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    # WPF rendering needs an STA thread (Windows PowerShell 5.1 already is; pwsh may not be).
    & powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -IconsDir $IconsDir
    exit $LASTEXITCODE
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$IconSizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$Invariant = [Globalization.CultureInfo]::InvariantCulture

function Get-ClassStyles([xml]$Svg) {
    $styles = @{}
    $css = ($Svg.svg.ChildNodes | Where-Object { $_.LocalName -eq 'style' } | ForEach-Object { $_.InnerText }) -join "`n"
    foreach ($rule in [regex]::Matches($css, '\.([\w-]+)\s*\{([^}]*)\}')) {
        $props = @{}
        foreach ($decl in $rule.Groups[2].Value.Split(';')) {
            $pair = $decl.Split(':', 2)
            if ($pair.Count -eq 2) { $props[$pair[0].Trim()] = $pair[1].Trim() }
        }
        $styles[$rule.Groups[1].Value] = $props
    }
    return $styles
}

function Get-Property($Node, [hashtable]$Styles, [string]$Name, [string]$Inherited) {
    $value = $Node.GetAttribute($Name)
    if (-not $value -and $Node.GetAttribute('class')) {
        foreach ($class in $Node.GetAttribute('class').Split(' ', [StringSplitOptions]::RemoveEmptyEntries)) {
            if ($Styles.ContainsKey($class) -and $Styles[$class].ContainsKey($Name)) { $value = $Styles[$class][$Name] }
        }
    }
    if (-not $value -and $Node.GetAttribute('style') -match "$Name\s*:\s*([^;]+)") { $value = $Matches[1].Trim() }
    if ($value) { return $value } else { return $Inherited }
}

function ConvertTo-Transform([string]$Text) {
    $group = New-Object System.Windows.Media.TransformGroup
    foreach ($op in [regex]::Matches($Text, '(translate|scale)\s*\(([^)]*)\)')) {
        $n = @($op.Groups[2].Value.Split(@(' ', ','), [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [double]::Parse($_, $Invariant) })
        if ($op.Groups[1].Value -eq 'translate') {
            $y = if ($n.Count -gt 1) { $n[1] } else { 0 }
            $group.Children.Insert(0, (New-Object System.Windows.Media.TranslateTransform($n[0], $y)))
        } else {
            $sy = if ($n.Count -gt 1) { $n[1] } else { $n[0] }
            $group.Children.Insert(0, (New-Object System.Windows.Media.ScaleTransform($n[0], $sy)))
        }
    }
    # SVG applies the rightmost operation first: inserting at 0 gives WPF that order.
    return $group
}

function Add-Shapes($Node, [System.Windows.Media.DrawingGroup]$Target, [hashtable]$Styles, [string]$Fill, [string]$FillRule) {
    foreach ($child in $Node.ChildNodes) {
        if ($child.NodeType -ne 'Element') { continue }
        $fill = Get-Property $child $Styles 'fill' $Fill
        $rule = Get-Property $child $Styles 'fill-rule' $FillRule
        $geometry = $null
        # WPF path syntax takes the fill rule as a prefix: F0 = even-odd, F1 = nonzero.
        $prefix = if ($rule -eq 'nonzero') { 'F1 ' } else { 'F0 ' }
        switch ($child.LocalName) {
            'g' {
                $group = New-Object System.Windows.Media.DrawingGroup
                if ($child.GetAttribute('transform')) { $group.Transform = ConvertTo-Transform $child.GetAttribute('transform') }
                Add-Shapes $child $group $Styles $fill $rule
                $Target.Children.Add($group)
            }
            'path' { $geometry = [System.Windows.Media.Geometry]::Parse($prefix + $child.GetAttribute('d')) }
            'polygon' {
                $points = $child.GetAttribute('points').Trim()
                $geometry = [System.Windows.Media.Geometry]::Parse("$($prefix)M $points Z")
            }
            'circle' {
                $c = 'cx', 'cy', 'r' | ForEach-Object { [double]::Parse($child.GetAttribute($_), $Invariant) }
                $geometry = New-Object System.Windows.Media.EllipseGeometry((New-Object System.Windows.Point($c[0], $c[1])), $c[2], $c[2])
            }
        }
        if ($geometry -and $fill -and $fill -ne 'none') {
            $brush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($fill))
            $Target.Children.Add((New-Object System.Windows.Media.GeometryDrawing($brush, $null, $geometry)))
        }
    }
}

function Read-Drawing([string]$Path) {
    [xml]$svg = Get-Content -Raw -Encoding UTF8 $Path
    $styles = Get-ClassStyles $svg
    $box = @($svg.svg.viewBox.Split(@(' ', ','), [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [double]::Parse($_, $Invariant) })
    $rootRule = if ($svg.svg.GetAttribute('style') -match 'fill-rule\s*:\s*(\w+)') { $Matches[1] } else { 'nonzero' }
    $drawing = New-Object System.Windows.Media.DrawingGroup
    Add-Shapes $svg.DocumentElement $drawing $styles 'black' $rootRule
    return [pscustomobject]@{ Drawing = $drawing; X = $box[0]; Y = $box[1]; Width = $box[2]; Height = $box[3] }
}

function ConvertTo-Png($Art, [int]$Size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $scale = $Size / [Math]::Max($Art.Width, $Art.Height)
    $context.PushTransform((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
    $context.PushTransform((New-Object System.Windows.Media.TranslateTransform(-$Art.X, -$Art.Y)))
    $context.DrawDrawing($Art.Drawing)
    $context.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    return , $stream.ToArray()
}

# ICO container with one PNG entry per size (supported since Windows Vista).
function Write-Ico([string]$Path, [object[]]$Images) {
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($stream)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$Images.Count)
    $offset = 6 + 16 * $Images.Count
    foreach ($image in $Images) {
        $side = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $writer.Write([byte]$side); $writer.Write([byte]$side); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Png.Length); $writer.Write([uint32]$offset)
        $offset += $image.Png.Length
    }
    foreach ($image in $Images) { $writer.Write([byte[]]$image.Png) }
    $writer.Flush()
    [IO.File]::WriteAllBytes($Path, $stream.ToArray())
}

# Vector copy of a drawing (XAML), for art the apps draw at arbitrary sizes, like the shortcut badge:
# scaling a bitmap down to 14-20 px looks blurry, drawing the shapes at that size does not.
function Write-DrawingXaml($Art, [string]$Path) {
    $group = New-Object System.Windows.Media.DrawingGroup
    $group.Children.Add($Art.Drawing)
    # Normalize to a 1x1 box so the app can scale it to any rectangle.
    $scale = 1 / [Math]::Max($Art.Width, $Art.Height)
    $transform = New-Object System.Windows.Media.TransformGroup
    $transform.Children.Add((New-Object System.Windows.Media.TranslateTransform(-$Art.X, -$Art.Y)))
    $transform.Children.Add((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
    $group.Transform = $transform
    $group.Freeze()
    [IO.File]::WriteAllText($Path, [System.Windows.Markup.XamlWriter]::Save($group), (New-Object Text.UTF8Encoding($false)))
}

foreach ($icon in @(
        @{ Svg = 'client.svg'; Ico = 'WRApperture.ico'; Png = $null; Xaml = $null },
        @{ Svg = 'host.svg'; Ico = 'WRApperture.Host.ico'; Png = $null; Xaml = $null },
        @{ Svg = 'badge.svg'; Ico = $null; Png = 'badge.png'; Xaml = 'badge.xaml' },
        @{ Svg = 'desktop.svg'; Ico = $null; Png = 'desktop.png'; Xaml = 'desktop.xaml' })) {
    $art = Read-Drawing (Join-Path $IconsDir $icon.Svg)
    if ($icon.Ico) {
        $images = foreach ($size in $IconSizes) { [pscustomobject]@{ Size = $size; Png = (ConvertTo-Png $art $size) } }
        Write-Ico (Join-Path $IconsDir $icon.Ico) $images
        Write-Host "$($icon.Ico): $($IconSizes -join ', ') px"
    }
    if ($icon.Png) {
        [IO.File]::WriteAllBytes((Join-Path $IconsDir $icon.Png), (ConvertTo-Png $art 256))
        Write-Host "$($icon.Png): 256 px preview"
    }
    if ($icon.Xaml) {
        Write-DrawingXaml $art (Join-Path $IconsDir $icon.Xaml)
        Write-Host "$($icon.Xaml): vector"
    }
}
