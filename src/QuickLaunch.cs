using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

// Starts the selected game client through the Jagex Launcher without showing
// the launcher, using its own --launch and --silent flags. Never touches
// credentials; login stays inside the official launcher.
//
// Modes:
//   (no arguments)      launch whatever the launcher has selected
//   --character NAME    launch as a character saved with --remember
//   --remember NAME     save the currently selected character under a name
//   --tray              persistent tray icon

class QuickLaunch {
	public const string AppId = "RuneScape.QuickLaunch";
	public const string Title = "RuneScape Quick Launch";

	public class GameDef {
		public string Id, Label;
		public GameDef(string id, string label) { Id = id; Label = label; }
	}

	public static readonly GameDef[] Games = {
		new GameDef("osrs", "Old School RuneScape"),
		new GameDef("runescape", "RuneScape 3"),
	};

	public class ClientDef {
		public string Game, Label, Id;
		public string[] Processes;
		public ClientDef(string game, string label, string id, params string[] processes) {
			Game = game; Label = label; Id = id; Processes = processes;
		}
	}

	public static readonly ClientDef[] Clients = {
		new ClientDef("osrs", "RuneLite", "osrs_runelite", "RuneLite"),
		new ClientDef("osrs", "Official Client", "osrs_ehc", "osclient"),
		new ClientDef("osrs", "HDOS", "osrs_hdos", "HDOS"),
		new ClientDef("runescape", "RuneScape (NXT)", "rs_nxt", "rs2client", "RuneScape"),
	};

	static readonly string JagexData = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jagex Launcher");

	public static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }

	[STAThread]
	static int Main(string[] args) {
		SetCurrentProcessExplicitAppUserModelID(AppId);

		if (args.Length == 1 && args[0] == "--tray")
			return Tray.Run();

		if (args.Length == 2 && args[0] == "--remember") {
			string game = ConfiguredGame();
			string existing = NameForId(game, SelectedAccountId(game));
			if (existing != null) {
				MessageBoxW(IntPtr.Zero,
					"The selected character is already saved as '" + existing + "'.", Title, 0x40);
				return 0;
			}
			string error = SaveCharacter(args[1], true);
			if (error != null) {
				MessageBoxW(IntPtr.Zero, error, Title, 0x10);
				return 1;
			}
			MessageBoxW(IntPtr.Zero,
				"Saved '" + args[1] + "'. A shortcut for it was placed on your desktop.",
				Title, 0x40);
			return 0;
		}

		string character = args.Length == 2 && args[0] == "--character" ? args[1] : null;

		string failure;
		int code = Launch(character, out failure);
		if (failure != null)
			MessageBoxW(IntPtr.Zero, failure, Title, 0x10);
		return code;
	}

	public static int Launch(string character, out string failure) {
		failure = null;

		if (AnyClientRunning())
			return 0;

		string launcher = FindLauncherExe();
		if (launcher == null) {
			failure = "Couldn't find the Jagex Launcher. Is it installed?";
			return 1;
		}

		string game = ConfiguredGame();

		if (character != null) {
			string error = ApplyCharacter(game, character);
			if (error != null) {
				failure = error;
				return 1;
			}
		}

		bool up;
		IntPtr existing = FindLauncherWindow();

		if (existing != IntPtr.Zero && IsWindowVisible(existing)) {
			// Single instance: a second --launch is ignored, so surface the
			// open launcher and take over once the user hits Play.
			if (IsIconic(existing))
				ShowWindow(existing, SW_RESTORE);
			if (!SetForegroundWindow(existing))
				FlashUntilFocused(existing);
			up = WaitForClient(600);
		}
		else {
			if (existing != IntPtr.Zero)
				CloseLauncher();

			// Without a login or client selection, --silent would sit on an
			// invisible window. Launch visibly once; the launcher remembers.
			bool ready = LoggedIn() && SelectedClientId(game) != null;

			Process.Start(launcher, ready ? "--launch=" + game + " --silent" : "");
			up = WaitForClient(ready ? 60 : 600);

			if (!up && ready) {
				// A stuck --silent launcher can't be unhidden, replace it.
				CloseLauncher();
				Process.Start(launcher);
				up = WaitForClient(600);
			}
		}

		if (!up)
			return 2;

		CloseLauncher();

		// Clients swap windows during startup, so keep tagging a while after
		// the first confirmed game window.
		int confirmedPolls = 0;
		for (int i = 0; i < 1000 && confirmedPolls < 400; i++) {
			if (!AnyClientRunning())
				break;
			if (TagClientWindows())
				confirmedPolls++;
			Thread.Sleep(300);
		}

		return 0;
	}

	static string ConfigFile { get { return Path.Combine(ExeDir, "quicklaunch.ini"); } }

	public static string ConfiguredGame() {
		if (File.Exists(ConfigFile)) {
			foreach (string line in File.ReadAllLines(ConfigFile)) {
				if (line.StartsWith("game=", StringComparison.Ordinal)) {
					string value = line.Substring(5).Trim();
					foreach (var g in Games) {
						if (g.Id == value)
							return value;
					}
				}
			}
		}
		return "osrs";
	}

	public static void SetConfiguredGame(string game) {
		File.WriteAllText(ConfigFile, "game=" + game + "\r\n");
	}

	// Characters map a name to the id the launcher stores in
	// "lastGameAccountByGameId", saved as name=game=id lines in characters.txt.
	// Older name=id lines are read as osrs.

	static string CharactersFile { get { return Path.Combine(ExeDir, "characters.txt"); } }

	static List<string[]> ReadCharacters() {
		var list = new List<string[]>();
		if (File.Exists(CharactersFile)) {
			foreach (string line in File.ReadAllLines(CharactersFile)) {
				string[] p = line.Split('=');
				if (p.Length == 2)
					list.Add(new[] { p[0], "osrs", p[1] });
				else if (p.Length >= 3)
					list.Add(new[] { p[0], p[1], p[2] });
			}
		}
		return list;
	}

	public static string[] SavedCharacterNames(string game) {
		var names = new List<string>();
		foreach (var c in ReadCharacters()) {
			if (c[1] == game)
				names.Add(c[0]);
		}
		names.Sort(StringComparer.OrdinalIgnoreCase);
		return names.ToArray();
	}

	static string FindSavedCharacterId(string game, string name) {
		foreach (var c in ReadCharacters()) {
			if (c[1] == game && c[0].Equals(name, StringComparison.OrdinalIgnoreCase))
				return c[2];
		}
		return null;
	}

	public static string SelectedAccountId(string game) {
		string settings = ReadSettings();
		if (settings == null)
			return null;
		Match match = Regex.Match(settings,
			"\"lastGameAccountByGameId\"\\s*:\\s*\\{[^}]*\"" + game + "\"\\s*:\\s*\"([^\"]+)\"");
		return match.Success ? match.Groups[1].Value : null;
	}

	public static string NameForId(string game, string id) {
		if (id == null)
			return null;
		foreach (var c in ReadCharacters()) {
			if (c[1] == game && c[2] == id)
				return c[0];
		}
		return null;
	}

	public static string SaveCharacter(string name, bool makeShortcut) {
		if (name.IndexOf('=') >= 0)
			return "Character names can't contain '='.";

		string game = ConfiguredGame();
		string id = SelectedAccountId(game);
		if (id == null)
			return "Couldn't find a selected character in the launcher settings.\n" +
				"Open the Jagex Launcher, pick the character, close the launcher, then try again.";

		var lines = File.Exists(CharactersFile)
			? new List<string>(File.ReadAllLines(CharactersFile))
			: new List<string>();
		lines.RemoveAll(delegate(string line) {
			return line.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase);
		});
		lines.Add(name + "=" + game + "=" + id);
		File.WriteAllLines(CharactersFile, lines.ToArray());

		if (makeShortcut) {
			string shortcut = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
				Title + " - " + name + ".lnk");
			string icon = Path.Combine(ExeDir, "quicklaunch.ico");
			var make = Process.Start(new ProcessStartInfo {
				FileName = Path.Combine(ExeDir, "MakeShortcut.exe"),
				Arguments = Quote(shortcut) + " " + Quote(Path.Combine(ExeDir, "RuneScapeQuickLaunch.exe")) + " " +
					Quote(ExeDir.TrimEnd('\\')) + " " + Quote(File.Exists(icon) ? icon : "") + " " +
					Quote(AppId) + " " + Quote("Straight into the game as " + name) + " " +
					Quote("--character " + name),
				UseShellExecute = false,
				CreateNoWindow = true,
			});
			make.WaitForExit();
		}
		return null;
	}

	static string ApplyCharacter(string game, string name) {
		string id = FindSavedCharacterId(game, name);
		if (id == null)
			return "No saved character named '" + name + "' for " + game + ".\n" +
				"Select it in the launcher once, close the launcher, then run:\n" +
				"RuneScapeQuickLaunch.exe --remember " + name;

		// A running launcher would overwrite the edit; the already-open path
		// takes over instead.
		if (Process.GetProcessesByName("JagexLauncher").Length > 0)
			return null;

		string settings = ReadSettings();
		if (settings == null)
			return null;
		string updated = Regex.Replace(settings,
			"(\"lastGameAccountByGameId\"\\s*:\\s*\\{[^}]*\"" + game + "\"\\s*:\\s*)(\"[^\"]*\"|null)",
			"$1\"" + id + "\"");
		if (updated != settings)
			File.WriteAllText(SettingsPath, updated, new UTF8Encoding(false));
		return null;
	}

	static string Quote(string value) {
		return "\"" + value + "\"";
	}

	static string SettingsPath { get { return Path.Combine(JagexData, "settings.json"); } }

	static string ReadSettings() {
		return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
	}

	public static string FindLauncherExe() {
		// The jagex: protocol handler holds the full install path.
		foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine }) {
			using (var key = root.OpenSubKey(@"Software\Classes\jagex\shell\open\command")) {
				string command = key == null ? null : key.GetValue("") as string;
				if (command == null)
					continue;
				int start = command.IndexOf('"');
				int end = start < 0 ? -1 : command.IndexOf('"', start + 1);
				if (end > start) {
					string exe = command.Substring(start + 1, end - start - 1);
					if (File.Exists(exe))
						return exe;
				}
			}
		}

		string usual = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			@"Jagex Launcher\JagexLauncher.exe");
		return File.Exists(usual) ? usual : null;
	}

	static bool LoggedIn() {
		string creds = Path.Combine(JagexData, Path.Combine("auth", "creds"));
		return File.Exists(creds) && new FileInfo(creds).Length > 0;
	}

	public static string SelectedClientId(string game) {
		string settings = ReadSettings();
		if (settings == null)
			return null;
		Match match = Regex.Match(settings,
			"\"lastGameClientByGameId\"\\s*:\\s*\\{[^}]*\"" + game + "\"\\s*:\\s*\"([^\"]+)\"");
		return match.Success ? match.Groups[1].Value : null;
	}

	public static string ApplyClientId(string game, string id) {
		if (Process.GetProcessesByName("JagexLauncher").Length > 0)
			return "Close the Jagex Launcher first, it would overwrite the change.";
		string settings = ReadSettings();
		if (settings == null)
			return "No launcher settings found yet. Open the launcher once first.";
		string updated = Regex.Replace(settings,
			"(\"lastGameClientByGameId\"\\s*:\\s*\\{[^}]*\"" + game + "\"\\s*:\\s*)(\"[^\"]*\"|null)",
			"$1\"" + id + "\"");
		if (updated == settings && SelectedClientId(game) != id)
			return "The launcher hasn't seen this game yet. Open it once and pick a client there.";
		File.WriteAllText(SettingsPath, updated, new UTF8Encoding(false));
		return null;
	}

	public static bool AnyClientRunning() {
		foreach (var client in Clients) {
			foreach (string process in client.Processes) {
				if (Process.GetProcessesByName(process).Length > 0)
					return true;
			}
		}
		return false;
	}

	static bool WaitForClient(int seconds) {
		for (int i = 0; i < seconds * 2; i++) {
			if (AnyClientRunning())
				return true;
			Thread.Sleep(500);
		}
		return false;
	}

	static IntPtr FindLauncherWindow() {
		// The launcher window class is "<hash>_SSNSkinWindow"; EnumWindows sees
		// it even while hidden by --silent.
		IntPtr found = IntPtr.Zero;
		EnumWindows(delegate(IntPtr hwnd, IntPtr lparam) {
			uint pid;
			GetWindowThreadProcessId(hwnd, out pid);
			if (!PidBelongsTo(pid, "JagexLauncher"))
				return true;
			if (ClassNameOf(hwnd).IndexOf("SSNSkinWindow", StringComparison.Ordinal) < 0)
				return true;
			found = hwnd;
			return false;
		}, IntPtr.Zero);
		return found;
	}

	static void CloseLauncher() {
		IntPtr window = FindLauncherWindow();
		if (window != IntPtr.Zero)
			PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

		// WM_CLOSE may just minimize it to the tray, force it after a while.
		for (int i = 0; i < 10; i++) {
			Thread.Sleep(500);
			if (Process.GetProcessesByName("JagexLauncher").Length == 0)
				return;
		}
		foreach (var p in Process.GetProcessesByName("JagexLauncher")) {
			try { p.Kill(); } catch { }
		}
	}

	static bool TagClientWindows() {
		// True once a real game window is tagged (RuneLite's bootstrap window
		// is titled "RuneLite Launcher", so anything without "Launcher" counts).
		bool gameWindowTagged = false;
		EnumWindows(delegate(IntPtr hwnd, IntPtr lparam) {
			uint pid;
			GetWindowThreadProcessId(hwnd, out pid);
			bool isClient = false;
			foreach (var client in Clients) {
				foreach (string process in client.Processes) {
					if (PidBelongsTo(pid, process)) { isClient = true; break; }
				}
				if (isClient)
					break;
			}
			if (!isClient || !IsWindowVisible(hwnd))
				return true;
			SetWindowAppId(hwnd);
			string title = TitleOf(hwnd);
			if (title.Length > 0 && title.IndexOf("Launcher", StringComparison.Ordinal) < 0)
				gameWindowTagged = true;
			return true;
		}, IntPtr.Zero);
		return gameWindowTagged;
	}

	static void SetWindowAppId(IntPtr hwnd) {
		// InitPropVariantFromString is missing from propsys.dll on some
		// installs, so the VT_LPWSTR is built by hand.
		try {
			IPropertyStore store;
			Guid iid = typeof(IPropertyStore).GUID;
			if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store == null)
				return;
			var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
			var value = new PROPVARIANT { vt = 31, pointerValue = Marshal.StringToCoTaskMemUni(AppId) };
			store.SetValue(ref key, ref value);
			store.Commit();
			PropVariantClear(ref value);
			Marshal.ReleaseComObject(store);
		}
		catch { }
	}

	static bool PidBelongsTo(uint pid, string processName) {
		foreach (var p in Process.GetProcessesByName(processName)) {
			if ((uint)p.Id == pid)
				return true;
		}
		return false;
	}

	static string ClassNameOf(IntPtr hwnd) {
		var sb = new StringBuilder(256);
		GetClassName(hwnd, sb, 256);
		return sb.ToString();
	}

	static string TitleOf(IntPtr hwnd) {
		var sb = new StringBuilder(256);
		GetWindowText(hwnd, sb, 256);
		return sb.ToString();
	}

	static void FlashUntilFocused(IntPtr hwnd) {
		var info = new FLASHWINFO {
			cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)),
			hwnd = hwnd,
			dwFlags = 3 | 12,
			uCount = 0,
			dwTimeout = 0,
		};
		FlashWindowEx(ref info);
	}

	const uint WM_CLOSE = 0x10;
	const int SW_RESTORE = 9;

	delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lparam);

	[StructLayout(LayoutKind.Sequential)]
	struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

	[StructLayout(LayoutKind.Sequential)]
	struct PROPERTYKEY { public Guid fmtid; public int pid; }

	[StructLayout(LayoutKind.Sequential)]
	struct PROPVARIANT {
		public ushort vt;
		public ushort reserved1, reserved2, reserved3;
		public IntPtr pointerValue;
		public IntPtr pointerValue2;
	}

	[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IPropertyStore {
		int GetCount(out uint count);
		int GetAt(uint index, out PROPERTYKEY key);
		int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
		int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
		int Commit();
	}

	[DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lparam);
	[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
	[DllImport("user32.dll")] static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int max);
	[DllImport("user32.dll")] static extern int GetWindowText(IntPtr hwnd, StringBuilder buffer, int max);
	[DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
	[DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
	[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
	[DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
	[DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO info);
	[DllImport("user32.dll")] static extern IntPtr PostMessage(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr owner, string text, string title, uint type);
	[DllImport("shell32.dll")] static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
	[DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);
	[DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT value);
}
