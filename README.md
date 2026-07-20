# Runescape Quick Launch

<img src="icon.png" width="96" align="right">

Launch Old School RuneScape (OSRS) or RuneScape 3 in one click, straight into the game, skipping the Jagex Launcher window. No Play button, no second taskbar icon. Works with RuneLite, the official client and HDOS.

Since Jagex accounts arrived, the game has to start through the Jagex Launcher. Every session turns into the same routine: open the launcher, wait, click Play, wait, then close the launcher it leaves behind.

This does that part for you. It starts the launcher hidden with its own `--launch` and `--silent` flags, waits for the client to appear, closes the launcher, and tags the game window so it groups under your pinned shortcut. One taskbar icon, game on screen, launcher never shown.

It never reads or writes your password or tokens. The official launcher handles login, same as clicking Play yourself.

<img src="/.github/social-preview.png">

## Install

Windows 10 or 11, the [Jagex Launcher](https://www.jagex.com/launcher), and a game client installed.

Open PowerShell, then clone and run the installer:

```
git clone https://github.com/ricksouth/runescape-quicklaunch
cd runescape-quicklaunch
powershell -ExecutionPolicy Bypass -File install.ps1
```

Or in one line:

```
irm https://raw.githubusercontent.com/ricksouth/runescape-quicklaunch/main/install.ps1 | iex
```

The one-liner fetches `install.ps1` and the sources from this repo instead of a local clone. Either way you're running a script from the internet, so skim `install.ps1` first. It's short.

There's no .exe to download. The installer compiles the sources on your machine with the C# compiler that ships with Windows, so what runs is the code in [`src/`](src/) and nothing else. That also avoids the SmartScreen warning an unsigned downloaded exe would trip.

The installer asks where to install, whether you want a desktop shortcut and Start Menu entry, and whether the tray icon starts with Windows. Scripted installs can pass `-InstallDir`, `-NoDesktopShortcut`, `-NoStartMenuShortcut` and `-NoTray`.

When it finishes, pin the **RuneScape Quick Launch** shortcut to your taskbar. Run the installer again to update.

## The tray icon

Optional. A tray icon whose menu covers everything without shortcuts: **Play** (or double-click the icon), **Play as ...** for each saved character, a **Game** picker for Old School or RS3, a **Client** picker that switches the launcher's selected client, and **Remember current character**. It's the same exe run with `--tray`; the installer adds it to your Startup folder if you say yes.

## Multiple characters

The launcher remembers your last character, so with a single character every launch is already silent.

For more than one, give each its own shortcut:

1. Select the character in the Jagex Launcher, then close the launcher.
2. Run `RuneScapeQuickLaunch.exe --remember Bob` from the install folder.

That saves the selection and drops a "RuneScape Quick Launch - Bob" shortcut on your desktop, which launches straight into that character. The tray's **Remember current character** does the same without a terminal. Saved characters live in `characters.txt` next to the exe.

## How it works

The installer finds your Jagex Launcher through the registry, compiles the sources into the install folder, and creates each shortcut with an explicit AppUserModelID. Windows groups taskbar windows by that id: the shortcut carries one, and after launch the tool stamps the same id onto the game window, so the pinned icon and the running game share a single taskbar button.

Clicking the shortcut:

1. If a client is already running, it exits.
2. Otherwise it runs `JagexLauncher.exe --launch=<game> --silent`, the launcher's own flags for starting the selected client without showing a window.
3. Once the client appears, it closes the launcher.
4. It tags the client's windows until the real game window has the id (clients swap their startup window for the game window partway through loading).

If you're not logged in, or no client is selected, hiding the launcher would hide the screen you need, so it opens the launcher visibly and waits. The launcher remembers login and client choice, so later runs are silent.

If the launcher is already open when you click the shortcut, the tool brings it to the front and takes over once you hit Play. A second silent launch is ignored, since the launcher is single instance.

## Uninstall

Run `uninstall.ps1`, or by hand: unpin and delete the shortcuts and the install folder. Your launcher and game clients are left alone.

## Is this allowed?

It doesn't automate or bypass anything in the game. It runs the official launcher with flags the launcher provides, closes it once the game is up, and never sees your credentials. It's the same sequence you'd do by hand. Unofficial, and not affiliated with Jagex or RuneLite.

## Notes

- Launches whatever client the launcher has selected. RuneLite, the official client, HDOS and the RS3 (NXT) client are all tested.
- If your launcher closes to the tray instead of exiting, the tool asks it to close, waits a few seconds, then ends the process. The game is already running by then.
- If a silent launch gets stuck (expired login, a launcher update), the tool reopens the launcher visibly after a minute.
- Steam: add `RuneScapeQuickLaunch.exe` from the install folder as a non-Steam game for the overlay.
- No signed releases: signing certificates cost money every year, and an unsigned download trips SmartScreen anyway. Compiling locally avoids both.
