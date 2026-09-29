$ErrorActionPreference = 'Stop'

$startupDirectory = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupDirectory 'DV5Direct.lnk'
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DV5Direct.lnk'
$programsDirectory = [Environment]::GetFolderPath('Programs')
$startShortcutPath = Join-Path $programsDirectory 'DV5Direct.lnk'
$stopShortcutPath = Join-Path $programsDirectory 'Stop DV5Direct.lnk'

if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath
    Write-Host 'DV5Direct auto-start disabled.'
}
else {
    Write-Host 'DV5Direct auto-start was not enabled.'
}

foreach ($path in @($desktopShortcutPath, $startShortcutPath, $stopShortcutPath)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
}
