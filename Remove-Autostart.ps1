$ErrorActionPreference = 'Stop'

$startupDirectory = [Environment]::GetFolderPath('Startup')
$desktopDirectory = [Environment]::GetFolderPath('Desktop')
$programsDirectory = [Environment]::GetFolderPath('Programs')
$disabled = $false

# Current names first, then the shortcuts created under the old DV5Direct name.
foreach ($name in @('Almost Fast Overlays', 'DV5Direct')) {
    $shortcutPath = Join-Path $startupDirectory "$name.lnk"
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath
        $disabled = $true
    }
    foreach ($path in @((Join-Path $desktopDirectory "$name.lnk"), (Join-Path $programsDirectory "$name.lnk"), (Join-Path $programsDirectory "Stop $name.lnk"))) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
}

if ($disabled) { Write-Host 'Almost Fast Overlays auto-start disabled.' }
else { Write-Host 'Almost Fast Overlays auto-start was not enabled.' }
