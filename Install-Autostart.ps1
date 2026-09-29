$ErrorActionPreference = 'Stop'

$projectDirectory = $PSScriptRoot
$buildDirectory = Join-Path $projectDirectory 'bin\Release\net48'
$installDirectory = Join-Path $projectDirectory 'App'
$startupDirectory = [Environment]::GetFolderPath('Startup')
$desktopDirectory = [Environment]::GetFolderPath('Desktop')
$programsDirectory = [Environment]::GetFolderPath('Programs')
$startupShortcutPath = Join-Path $startupDirectory 'DV5Direct.lnk'
$desktopShortcutPath = Join-Path $desktopDirectory 'DV5Direct.lnk'
$startShortcutPath = Join-Path $programsDirectory 'DV5Direct.lnk'
$stopShortcutPath = Join-Path $programsDirectory 'Stop DV5Direct.lnk'

dotnet build (Join-Path $projectDirectory 'DV5Direct.csproj') -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'DV5Direct build failed.'
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $buildDirectory -File |
    Where-Object Extension -ne '.pdb' |
    Copy-Item -Destination $installDirectory -Force
$shell = New-Object -ComObject WScript.Shell
function New-DV5Shortcut($path, $arguments, $description) {
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = Join-Path $installDirectory 'DV5Direct.exe'
    $shortcut.Arguments = $arguments
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.Description = $description
    $shortcut.Save()
}

New-DV5Shortcut $startupShortcutPath '--run' 'Start the DV5Direct iRacing dashboard automatically'
New-DV5Shortcut $desktopShortcutPath '' 'Open DV5Direct settings'
New-DV5Shortcut $startShortcutPath '' 'Open DV5Direct settings'
New-DV5Shortcut $stopShortcutPath '--stop' 'Stop DV5Direct and clear the display'

foreach ($obsoleteLauncher in @('Run-DV5Direct.vbs', 'Stop-DV5Direct.vbs')) {
    $obsoletePath = Join-Path $installDirectory $obsoleteLauncher
    if (Test-Path -LiteralPath $obsoletePath) { Remove-Item -LiteralPath $obsoletePath }
}

Write-Host "Installed DV5Direct to $installDirectory"
Write-Host "Enabled auto-start shortcut at $startupShortcutPath"
Write-Host "Created desktop and Start Menu shortcuts."
