# Removes everything install.ps1 created: the install folder and all shortcuts.
# Taskbar pins have to be unpinned by hand, Windows doesn't let scripts touch them.

Get-Process RuneScapeQuickLaunch, MakeShortcut -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$desktop = [Environment]::GetFolderPath('Desktop')
$startMenu = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft\Windows\Start Menu\Programs'

# The shortcut knows the install location if it was customized.
$installDir = Join-Path $env:LOCALAPPDATA 'RuneScapeQuickLaunch'
foreach ($lnk in (Join-Path $desktop 'RuneScape Quick Launch.lnk'), (Join-Path $startMenu 'RuneScape Quick Launch.lnk')) {
	if (Test-Path $lnk) {
		$target = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk).TargetPath
		if ($target) { $installDir = Split-Path $target; break }
	}
}

$targets = @($installDir)
$targets += Get-ChildItem $desktop -Filter 'RuneScape Quick Launch*.lnk' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
$targets += Get-ChildItem $startMenu -Filter 'RuneScape Quick Launch*.lnk' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
$targets += Join-Path ([Environment]::GetFolderPath('Startup')) 'RuneScape Quick Launch Tray.lnk'

foreach ($target in $targets) {
	if (Test-Path $target) {
		Remove-Item $target -Recurse -Force
		Write-Host "removed $target"
	}
}

Write-Host 'Done. Your Jagex Launcher and game clients were not touched.'
