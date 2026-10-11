$ErrorActionPreference = 'Stop'

$projectDirectory = $PSScriptRoot
$buildDirectory = Join-Path $projectDirectory 'bin\Release\net48'
$installDirectory = Join-Path $projectDirectory 'App'
$startupDirectory = [Environment]::GetFolderPath('Startup')
$desktopDirectory = [Environment]::GetFolderPath('Desktop')
$programsDirectory = [Environment]::GetFolderPath('Programs')
$startupShortcutPath = Join-Path $startupDirectory 'Almost Fast Overlays.lnk'
$desktopShortcutPath = Join-Path $desktopDirectory 'Almost Fast Overlays.lnk'
$startShortcutPath = Join-Path $programsDirectory 'Almost Fast Overlays.lnk'
$stopShortcutPath = Join-Path $programsDirectory 'Stop Almost Fast Overlays.lnk'

dotnet build (Join-Path $projectDirectory 'AlmostFastOverlays.csproj') -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'Almost Fast Overlays build failed.'
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
# The app is a single self-contained exe; settings.json and Logs are created next to it.
Copy-Item -LiteralPath (Join-Path $buildDirectory 'AlmostFastOverlays.exe') -Destination $installDirectory -Force
$shell = New-Object -ComObject WScript.Shell
function New-AppShortcut($path, $arguments, $description) {
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = Join-Path $installDirectory 'AlmostFastOverlays.exe'
    $shortcut.Arguments = $arguments
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.Description = $description
    $shortcut.Save()
}

New-AppShortcut $startupShortcutPath '--run' 'Start the Almost Fast Overlays iRacing dashboard automatically'
New-AppShortcut $desktopShortcutPath '' 'Open Almost Fast Overlays settings'
New-AppShortcut $startShortcutPath '' 'Open Almost Fast Overlays settings'
New-AppShortcut $stopShortcutPath '--stop' 'Stop Almost Fast Overlays and clear the display'

# Files left over from earlier versions, including the DV5Direct name.
$obsoleteFiles = @(
    (Join-Path $installDirectory 'Run-DV5Direct.vbs'),
    (Join-Path $installDirectory 'Stop-DV5Direct.vbs'),
    (Join-Path $installDirectory 'DV5Direct.exe'),
    (Join-Path $installDirectory 'DV5Direct.exe.config'),
    (Join-Path $installDirectory 'AlmostFastOverlays.exe.config'),
    (Join-Path $startupDirectory 'DV5Direct.lnk'),
    (Join-Path $desktopDirectory 'DV5Direct.lnk'),
    (Join-Path $programsDirectory 'DV5Direct.lnk'),
    (Join-Path $programsDirectory 'Stop DV5Direct.lnk'))
foreach ($obsoletePath in $obsoleteFiles) {
    if (Test-Path -LiteralPath $obsoletePath) { Remove-Item -LiteralPath $obsoletePath }
}

Write-Host "Installed Almost Fast Overlays to $installDirectory"
Write-Host "Enabled auto-start shortcut at $startupShortcutPath"
Write-Host "Created desktop and Start Menu shortcuts."
