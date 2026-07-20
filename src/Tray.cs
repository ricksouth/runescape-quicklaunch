using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// Tray mode (RuneScapeQuickLaunch.exe --tray): launch, pick characters and
// switch game or client from a tray menu.

class Tray {
	static NotifyIcon trayIcon;
	static volatile bool launching;

	public static int Run() {
		bool firstInstance;
		using (var mutex = new Mutex(true, "RuneScapeQuickLaunchTray", out firstInstance)) {
			if (!firstInstance)
				return 0;

			Application.EnableVisualStyles();

			trayIcon = new NotifyIcon();
			trayIcon.Icon = LoadIcon();
			trayIcon.Text = QuickLaunch.Title;
			trayIcon.ContextMenuStrip = new ContextMenuStrip();
			trayIcon.ContextMenuStrip.Opening += delegate { RebuildMenu(); };
			trayIcon.DoubleClick += delegate { LaunchInBackground(null); };
			RebuildMenu();
			trayIcon.Visible = true;

			Application.Run();

			trayIcon.Visible = false;
			trayIcon.Dispose();
			return 0;
		}
	}

	static Icon LoadIcon() {
		string ico = Path.Combine(QuickLaunch.ExeDir, "quicklaunch.ico");
		try {
			if (File.Exists(ico))
				return new Icon(ico);
			return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		}
		catch {
			return SystemIcons.Application;
		}
	}

	static void RebuildMenu() {
		var menu = trayIcon.ContextMenuStrip;
		menu.Items.Clear();

		string game = QuickLaunch.ConfiguredGame();

		// The worker lingers to tag windows after a launch; only grey the menu
		// while the launcher phase itself is running.
		bool busy = launching && !QuickLaunch.AnyClientRunning();

		var play = new ToolStripMenuItem("Play");
		play.Font = new Font(play.Font, FontStyle.Bold);
		play.Enabled = !busy;
		play.Click += delegate { LaunchInBackground(null); };
		menu.Items.Add(play);

		foreach (string name in QuickLaunch.SavedCharacterNames(game)) {
			string character = name;
			var item = new ToolStripMenuItem("Play as " + character);
			item.Enabled = !busy;
			item.Click += delegate { LaunchInBackground(character); };
			menu.Items.Add(item);
		}

		menu.Items.Add(new ToolStripSeparator());

		if (QuickLaunch.Games.Length > 1) {
			var gameMenu = new ToolStripMenuItem("Game");
			foreach (var g in QuickLaunch.Games) {
				string gameId = g.Id;
				var item = new ToolStripMenuItem(g.Label);
				item.Checked = gameId == game;
				item.Click += delegate { QuickLaunch.SetConfiguredGame(gameId); };
				gameMenu.DropDownItems.Add(item);
			}
			menu.Items.Add(gameMenu);
		}

		var clientMenu = new ToolStripMenuItem("Client");
		string selected = QuickLaunch.SelectedClientId(game);
		bool anyClient = false;
		foreach (var c in QuickLaunch.Clients) {
			if (c.Game != game)
				continue;
			anyClient = true;
			var client = c;
			var item = new ToolStripMenuItem(client.Label);
			item.Checked = client.Id == selected;
			item.Click += delegate {
				string error = QuickLaunch.ApplyClientId(client.Game, client.Id);
				if (error != null)
					Balloon(error);
			};
			clientMenu.DropDownItems.Add(item);
		}
		if (anyClient)
			menu.Items.Add(clientMenu);

		var remember = new ToolStripMenuItem("Remember current character...");
		remember.Click += delegate { RememberViaDialog(); };
		menu.Items.Add(remember);

		menu.Items.Add(new ToolStripSeparator());

		var exit = new ToolStripMenuItem("Exit");
		exit.Click += delegate { Application.Exit(); };
		menu.Items.Add(exit);
	}

	static void LaunchInBackground(string character) {
		if (launching || QuickLaunch.AnyClientRunning())
			return;
		launching = true;
		var worker = new Thread(delegate() {
			string failure;
			int code = QuickLaunch.Launch(character, out failure);
			launching = false;
			if (failure != null)
				Balloon(failure);
			else if (code == 2)
				Balloon("The game didn't start. Check the launcher and try again.");
		});
		worker.IsBackground = true;
		worker.Start();
	}

	static void RememberViaDialog() {
		string name = Prompt(
			"Name for the character the launcher currently has selected.\n" +
			"(Pick it in the launcher first and close the launcher.)");
		if (string.IsNullOrEmpty(name))
			return;
		string error = QuickLaunch.SaveCharacter(name, true);
		if (error != null)
			Balloon(error);
	}

	static void Balloon(string message) {
		trayIcon.ShowBalloonTip(4000, QuickLaunch.Title, message, ToolTipIcon.Warning);
	}

	static string Prompt(string label) {
		using (var form = new Form()) {
			form.Text = QuickLaunch.Title;
			form.FormBorderStyle = FormBorderStyle.FixedDialog;
			form.StartPosition = FormStartPosition.CenterScreen;
			form.MinimizeBox = false;
			form.MaximizeBox = false;
			form.ClientSize = new Size(360, 110);
			form.TopMost = true;

			var text = new Label();
			text.Text = label;
			text.SetBounds(12, 10, 336, 45);

			var input = new TextBox();
			input.SetBounds(12, 55, 336, 22);

			var ok = new Button();
			ok.Text = "Save";
			ok.DialogResult = DialogResult.OK;
			ok.SetBounds(192, 82, 75, 24);

			var cancel = new Button();
			cancel.Text = "Cancel";
			cancel.DialogResult = DialogResult.Cancel;
			cancel.SetBounds(273, 82, 75, 24);

			form.Controls.Add(text);
			form.Controls.Add(input);
			form.Controls.Add(ok);
			form.Controls.Add(cancel);
			form.AcceptButton = ok;
			form.CancelButton = cancel;

			return form.ShowDialog() == DialogResult.OK ? input.Text.Trim() : null;
		}
	}
}
