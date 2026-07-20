# Installer for runescape-quicklaunch. Compiles the sources from src/ locally
# with the csc.exe that ships with Windows, nothing pre-compiled is downloaded.
# Works from a cloned repo or via irm | iex (which fetches the sources first).

param(
	[string]$InstallDir,
	[switch]$NoDesktopShortcut,
	[switch]$NoStartMenuShortcut,
	[switch]$NoTray
)

$ErrorActionPreference = 'Stop'

$rawBase = 'https://raw.githubusercontent.com/ricksouth/runescape-quicklaunch/main'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'RuneScape Quick Launch.lnk'
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft\Windows\Start Menu\Programs\RuneScape Quick Launch.lnk'
$appId = 'RuneScape.QuickLaunch'

function Fail($message) {
	Write-Host ''
	Write-Host $message -ForegroundColor Red
	exit 1
}

function AskYesNo($question) {
	# Enter means yes; so does having no console to ask on.
	try { $answer = Read-Host "$question [Y/n]" } catch { return $true }
	return $answer -eq '' -or $answer -match '^[yY]'
}

Write-Host 'runescape-quicklaunch installer'
Write-Host ''

# --- install location: default to where it already is, or localappdata ---

$defaultDir = Join-Path $env:LOCALAPPDATA 'RuneScapeQuickLaunch'
foreach ($existingLnk in $desktopShortcut, $startMenuShortcut) {
	if (Test-Path $existingLnk) {
		$target = (New-Object -ComObject WScript.Shell).CreateShortcut($existingLnk).TargetPath
		if ($target -and (Test-Path $target)) {
			$defaultDir = Split-Path $target
			break
		}
	}
}

if ($InstallDir) {
	$installDir = $InstallDir
}
else {
	$typed = $null
	try { $typed = Read-Host "Install location? [enter = $defaultDir]" } catch { }
	$installDir = if ($typed) { $typed } else { $defaultDir }
}

$wantDesktop = -not $NoDesktopShortcut
if ($wantDesktop -and -not $PSBoundParameters.ContainsKey('NoDesktopShortcut')) {
	$wantDesktop = AskYesNo 'Put a shortcut on the desktop?'
}
$wantStartMenu = -not $NoStartMenuShortcut
if ($wantStartMenu -and -not $PSBoundParameters.ContainsKey('NoStartMenuShortcut')) {
	$wantStartMenu = AskYesNo 'Add it to the Start Menu (so Windows search finds it)?'
}
$wantTray = -not $NoTray
if ($wantTray -and -not $PSBoundParameters.ContainsKey('NoTray')) {
	$wantTray = AskYesNo 'Start the tray icon with Windows (menu with characters, client switching)?'
}

# --- compiler ---

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
	$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
	Fail 'No .NET Framework 4 compiler found (csc.exe). It ships with Windows, so this is unexpected.'
}

# --- find the Jagex Launcher (via the jagex: protocol it registers) ---

$launcherExe = $null
foreach ($hive in 'HKCU:', 'HKLM:') {
	$key = "$hive\Software\Classes\jagex\shell\open\command"
	if (Test-Path $key) {
		$command = (Get-ItemProperty $key).'(default)'
		if ($command -match '"([^"]+)"') {
			$candidate = $Matches[1]
			if (Test-Path $candidate) { $launcherExe = $candidate; break }
		}
	}
}
if (-not $launcherExe) {
	$usual = "${env:ProgramFiles(x86)}\Jagex Launcher\JagexLauncher.exe"
	if (Test-Path $usual) { $launcherExe = $usual }
}
if (-not $launcherExe) {
	Fail 'Could not find the Jagex Launcher. Install it first: https://www.jagex.com/launcher'
}
Write-Host "Jagex Launcher: $launcherExe"

# --- heads-up checks (not fatal, the first run opens the launcher for both) ---

$settingsPath = Join-Path $env:LOCALAPPDATA 'Jagex Launcher\settings.json'
$clientSelected = $false
if (Test-Path $settingsPath) {
	$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
	if ($settings.gamePreferences -and $settings.gamePreferences.lastGameClientByGameId) {
		$clientSelected = $null -ne $settings.gamePreferences.lastGameClientByGameId.osrs
	}
}
if (-not $clientSelected) {
	Write-Warning 'No game client selected in the Jagex Launcher yet.'
	Write-Warning 'The first launch will open the launcher so you can pick one.'
}

$credsPath = Join-Path $env:LOCALAPPDATA 'Jagex Launcher\auth\creds'
if (-not ((Test-Path $credsPath) -and (Get-Item $credsPath).Length -gt 0)) {
	Write-Warning 'Not logged into the Jagex Launcher yet.'
	Write-Warning 'The first launch will open the launcher so you can log in.'
}

# --- sources: local checkout if present, otherwise fetch from GitHub ---

New-Item -ItemType Directory -Force $installDir | Out-Null
$sourceNames = 'QuickLaunch.cs', 'Tray.cs', 'MakeShortcut.cs'

$localSrc = $null
if ($PSScriptRoot) { $localSrc = Join-Path $PSScriptRoot 'src' }
foreach ($name in $sourceNames) {
	$destination = Join-Path $installDir $name
	if ($localSrc -and (Test-Path (Join-Path $localSrc $name))) {
		Copy-Item (Join-Path $localSrc $name) $destination -Force
	}
	else {
		Write-Host "Fetching src/$name from GitHub..."
		Invoke-RestMethod "$rawBase/src/$name" -OutFile $destination
	}
}

# --- icon: repo icon.png packed into a multi-size ico ---

$iconPath = Join-Path $installDir 'quicklaunch.ico'
$haveIcon = $false
Add-Type -AssemblyName System.Drawing
try {
	$pngPath = Join-Path $installDir 'icon.png'
	$localPng = if ($PSScriptRoot) { Join-Path $PSScriptRoot 'icon.png' } else { $null }
	if ($localPng -and (Test-Path $localPng)) {
		Copy-Item $localPng $pngPath -Force
	}
	else {
		Write-Host 'Fetching icon.png from GitHub...'
		Invoke-RestMethod "$rawBase/icon.png" -OutFile $pngPath
	}
	$master = [Drawing.Image]::FromFile($pngPath)

	$sizes = 16, 24, 32, 48, 64, 128, 256
	$frames = foreach ($size in $sizes) {
		$bitmap = New-Object Drawing.Bitmap($size, $size)
		$scale = [Drawing.Graphics]::FromImage($bitmap)
		$scale.InterpolationMode = 'HighQualityBicubic'
		$scale.DrawImage($master, 0, 0, $size, $size)
		$scale.Dispose()
		$frameStream = New-Object IO.MemoryStream
		$bitmap.Save($frameStream, [Drawing.Imaging.ImageFormat]::Png)
		$bitmap.Dispose()
		, $frameStream.ToArray()
	}
	$master.Dispose()

	$file = [IO.File]::Create($iconPath)
	$writer = New-Object IO.BinaryWriter($file)
	$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
	$offset = 6 + 16 * $sizes.Count
	for ($i = 0; $i -lt $sizes.Count; $i++) {
		$dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
		$writer.Write([byte]$dim); $writer.Write([byte]$dim)
		$writer.Write([byte]0); $writer.Write([byte]0)
		$writer.Write([uint16]1); $writer.Write([uint16]32)
		$writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
		$offset += $frames[$i].Length
	}
	foreach ($frame in $frames) { $writer.Write($frame) }
	$writer.Close()
	$haveIcon = $true
	Write-Host 'Icon built from icon.png.'
}
catch {
	Write-Warning "Couldn't build the icon ($_). Continuing without one."
}

# --- compile ---

# A running exe from a previous install would keep the output file locked.
Get-Process RuneScapeQuickLaunch, MakeShortcut -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host 'Compiling...'
$exePath = Join-Path $installDir 'RuneScapeQuickLaunch.exe'
$cscArgs = @('/nologo', '/target:winexe', "/out:$exePath",
	'/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
if ($haveIcon) { $cscArgs += "/win32icon:$iconPath" }
& $csc @cscArgs (Join-Path $installDir 'QuickLaunch.cs') (Join-Path $installDir 'Tray.cs')
if ($LASTEXITCODE -ne 0) { Fail 'Compiling the sources failed, see errors above.' }

# MakeShortcut.exe stays installed, --remember reuses it.
$makeShortcut = Join-Path $installDir 'MakeShortcut.exe'
& $csc /nologo /target:exe "/out:$makeShortcut" (Join-Path $installDir 'MakeShortcut.cs')
if ($LASTEXITCODE -ne 0) { Fail 'Compiling MakeShortcut.cs failed, see errors above.' }

# --- shortcuts ---

$iconArg = if ($haveIcon) { $iconPath } else { '' }
$description = 'Straight into the game, no launcher window'
if ($wantDesktop) {
	& $makeShortcut $desktopShortcut $exePath $installDir $iconArg $appId $description
	if ($LASTEXITCODE -ne 0) { Fail 'Creating the desktop shortcut failed.' }
}
if ($wantStartMenu) {
	& $makeShortcut $startMenuShortcut $exePath $installDir $iconArg $appId $description
	if ($LASTEXITCODE -ne 0) { Fail 'Creating the start menu shortcut failed.' }
}

# --- tray: a shortcut in the Startup folder, and start it right away ---

$trayShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'RuneScape Quick Launch Tray.lnk'
if ($wantTray) {
	& $makeShortcut $trayShortcut $exePath $installDir $iconArg $appId 'RuneScape Quick Launch tray icon' '--tray'
	if ($LASTEXITCODE -ne 0) { Fail 'Creating the tray startup shortcut failed.' }
	Start-Process $exePath -ArgumentList '--tray'
}
elseif (Test-Path $trayShortcut) {
	Remove-Item $trayShortcut
}

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Write-Host "Installed to: $installDir"
if ($wantDesktop) { Write-Host "Desktop shortcut: $desktopShortcut" }
if ($wantStartMenu) { Write-Host 'Added to the Start Menu, so Windows search will find it.' }
if ($wantTray) { Write-Host 'Tray icon is running and will start with Windows.' }
if ($wantDesktop -or $wantStartMenu) {
	Write-Host 'Pin the shortcut to your taskbar (right-click it > Pin to taskbar) and use that from now on.'
}
else {
	Write-Host "No shortcuts created. The exe is at: $exePath"
}
Write-Host 'First run after a fresh login may show the launcher once; after that it stays out of sight.'
