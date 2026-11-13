// Open Keydial — the tray application: connection, frames from the Keydial, actions, tray menu.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OpenKeydial
{
    sealed class TrayApp : ApplicationContext
    {
        const int BitCenter = 18;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem configItem, statusItem, dialMenu, startupItem;
        string menuLanguage;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 3000 };
        readonly KeydialBle ble;
        readonly Osd osd = new Osd();
        readonly KeyOverlay overlay = new KeyOverlay();
        readonly SynchronizationContext ui;
        readonly EventWaitHandle showSignal, quitSignal;
        readonly RegisteredWaitHandle showWait, quitWait;
        ConfigForm configForm;
        string configLanguage;
        DateTime quietReloadUntil = DateTime.MinValue;
        int battery = -1;
        bool lowBatteryWarned;
        FileSystemWatcher watcher;
        System.Windows.Forms.Timer reloadDebounce;
        Config cfg;
        uint lastButtons;
        readonly Dictionary<int, Act> activeActs = new Dictionary<int, Act>();
        int dialIndex;
        bool connected, paused;
        bool? lastActive;
        DateTime lastActivation = DateTime.MinValue;
        bool driverModeLogged;
        Icon currentIcon;

        bool ConfigActive => configForm != null && !configForm.IsDisposed && configForm.ContainsFocus;

        public TrayApp()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            LoadConfig(false);
            ble = new KeydialBle(ui);
            ble.DataFrame += OnData;
            ble.CommandFrame += OnCommand;

            menu.Renderer = new DarkMenuRenderer();
            menu.Opening += (s, e) => PrepareMenu();
            BuildMenu();
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowConfig(); };

            // A second launch asks us to show the settings window; the uninstaller asks us to quit.
            showSignal = Signals.Open(Signals.Show);
            quitSignal = Signals.Open(Signals.Quit);
            showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (s, t) => ui.Post(_ => ShowConfig(), null), null, -1, false);
            quitWait = ThreadPool.RegisterWaitForSingleObject(quitSignal, (s, t) => ui.Post(_ => ExitThread(), null), null, -1, false);

            poll.Tick += (s, e) => Poll();
            poll.Start();
            Poll();
        }

        // ---- settings window

        public void ShowConfig()
        {
            if (configForm != null && !configForm.IsDisposed && configLanguage != L.Code) configForm.Close();
            if (configForm == null || configForm.IsDisposed)
            {
                // Saving from the window reloads the settings without the "reloaded" balloon.
                var f = new ConfigForm(() => quietReloadUntil = DateTime.Now.AddSeconds(3));
                f.LanguageChanged += () => ui.Post(_ => { BuildMenu(); ShowConfig(); }, null);
                f.FormClosed += (s, e) => { if (configForm == f) configForm = null; };
                configForm = f;
                configLanguage = L.Code;
                f.Show();
            }
            if (configForm.WindowState == FormWindowState.Minimized) configForm.WindowState = FormWindowState.Normal;
            configForm.Activate();
        }

        // ---- tray menu

        void BuildMenu()
        {
            if (menuLanguage == L.Code) return;
            menuLanguage = L.Code;
            menu.Items.Clear();
            configItem = new ToolStripMenuItem(L.T("Configure keys and profiles…", "Configurer les touches et profils…"), null, (s, e) => ShowConfig());
            statusItem = new ToolStripMenuItem { Enabled = false };
            dialMenu = new ToolStripMenuItem(L.T("Dial mode", "Mode de la molette"));
            dialMenu.DropDown.Renderer = menu.Renderer;
            startupItem = new ToolStripMenuItem(L.T("Start with Windows", "Lancer au démarrage de Windows"));
            startupItem.Click += (s, e) => Setup.SetStartup(!Setup.IsStartup());
            menu.Items.Add(configItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(dialMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Edit the settings file…", "Modifier le fichier de configuration…"), null, (s, e) => Process.Start("notepad.exe", "\"" + AppPaths.ConfigFile + "\""));
            menu.Items.Add(L.T("Reload the settings", "Recharger la configuration"), null, (s, e) => LoadConfig(true));
            menu.Items.Add(L.T("Open the log", "Ouvrir le journal"), null, (s, e) => Process.Start("notepad.exe", "\"" + AppPaths.LogFile + "\""));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(startupItem);
            menu.Items.Add(L.T("About " + AppInfo.Name + "…", "À propos d'" + AppInfo.Name + "…"), null, (s, e) => About());
            menu.Items.Add(L.T("Uninstall…", "Désinstaller…"), null, (s, e) => StartUninstall());
            menu.Items.Add(L.T("Quit", "Quitter"), null, (s, e) => ExitThread());
            lastActive = null;
            UpdateTray();
        }

        // Fonts at the DPI of the monitor where the menu opens.
        void PrepareMenu()
        {
            int dpi = Theme.DpiAtCursor();
            menu.Font = dialMenu.DropDown.Font = Theme.Regular(9f, FontStyle.Regular, dpi);
            configItem.Font = Theme.Regular(9f, FontStyle.Bold, dpi);
            startupItem.Checked = Setup.IsStartup();
            dialMenu.DropDownItems.Clear();
            for (int i = 0; i < cfg.DialModes.Count; i++)
            {
                int k = i;
                var it = new ToolStripMenuItem(cfg.DialModes[i]) { Checked = i == dialIndex };
                it.Click += (s, e) => SetDialMode(cfg.DialModes[k]);
                dialMenu.DropDownItems.Add(it);
            }
        }

        void About()
        {
            var r = MessageBox.Show(
                AppInfo.Name + " " + AppInfo.VersionText + "\n" +
                L.T("Open-source driver for the Huion Keydial Mini (K20).\n\n" +
                    "Free software under the MIT License, provided \"as is\", without warranty of any kind.\n" +
                    "Not affiliated with or endorsed by Huion. Huion and Keydial are trademarks of their respective owners.\n" +
                    "Montserrat font: SIL Open Font License 1.1.\n\nOpen the project page?",
                    "Pilote libre pour le Huion Keydial Mini (K20).\n\n" +
                    "Logiciel libre sous licence MIT, fourni « tel quel », sans aucune garantie.\n" +
                    "Sans lien avec Huion, ni approuvé par Huion. Huion et Keydial sont des marques de leurs propriétaires respectifs.\n" +
                    "Police Montserrat : SIL Open Font License 1.1.\n\nOuvrir la page du projet ?"),
                AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) Process.Start(new ProcessStartInfo(AppInfo.Url) { UseShellExecute = true });
        }

        void StartUninstall()
        {
            if (Setup.Uninstall()) ExitThread();
        }

        // ---- settings file

        void LoadConfig(bool notify)
        {
            try
            {
                var c = Config.Load(AppPaths.ConfigFile);
                cfg = c;
                L.Set(c.Language);
                if (dialIndex >= cfg.DialModes.Count) dialIndex = 0;
                foreach (var w in c.Warnings) Log.W("Settings: " + w);
                Log.W($"Settings loaded: {c.ByExe.Count} application profile(s), dial = {string.Join(", ", c.DialModes)}, language = {L.Code}");
                if (menuLanguage != null && menuLanguage != L.Code) BuildMenu();
                if (notify && (DateTime.Now > quietReloadUntil || c.Warnings.Count > 0))
                    tray.ShowBalloonTip(2500, AppInfo.Name,
                        c.Warnings.Count == 0 ? L.T("Settings reloaded.", "Configuration rechargée.")
                                              : L.T($"Settings reloaded with {c.Warnings.Count} error(s): see the log.", $"Configuration rechargée avec {c.Warnings.Count} erreur(s) : voir le journal."),
                        c.Warnings.Count == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
            }
            catch (Exception e)
            {
                Log.W("Unreadable settings: " + e.Message);
                if (cfg == null) cfg = new Config();
                tray.ShowBalloonTip(4000, AppInfo.Name, L.T("Unreadable settings: ", "Configuration illisible : ") + e.Message, ToolTipIcon.Error);
            }
            if (watcher == null)
            {
                watcher = new FileSystemWatcher(AppPaths.DataDir, Path.GetFileName(AppPaths.ConfigFile)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
                reloadDebounce = new System.Windows.Forms.Timer { Interval = 400 };
                reloadDebounce.Tick += (s, e) => { reloadDebounce.Stop(); LoadConfig(true); };
                watcher.Changed += (s, e) => ui.Post(_ => { reloadDebounce.Stop(); reloadDebounce.Start(); }, null);
            }
        }

        // ---- connection state

        void Poll()
        {
            bool huionRunning = Process.GetProcessesByName("TabletDriver").Length > 0 || Process.GetProcessesByName("HuionTablet").Length > 0;
            bool nowPaused = cfg.PauseIfHuion && huionRunning;
            bool resumed = false;
            if (nowPaused != paused)
            {
                paused = nowPaused;
                resumed = !paused;
                Log.W(paused ? "Huion driver detected: paused (frames are only logged)." : "Huion driver gone: active.");
            }
            bool nowConnected = KeydialPresence.Connected();
            if (!ble.IsOpen || (nowConnected && !connected))
            {
                if (nowConnected && !connected) { Log.W("Keydial connected."); driverModeLogged = false; }
                if (ble.TryOpen(nowConnected) && nowConnected) Activate("connection");
            }
            else if (!nowConnected && connected) { Log.W("Keydial disconnected (sleep)."); ReleaseEverything(); }
            else if (nowConnected && resumed) Activate("Huion driver gone");
            else if (nowConnected && !paused && (DateTime.Now - lastActivation).TotalSeconds >= 60) KeepDriverMode();
            connected = nowConnected;
            UpdateTray();
        }

        void Activate(string why)
        {
            if (paused || cfg.ActivationCommands.Count == 0) return;
            if ((DateTime.Now - lastActivation).TotalSeconds < 2) return;
            lastActivation = DateTime.Now;
            Log.W("Switching to driver mode (" + why + ")");
            foreach (var c in cfg.ActivationCommands) { ble.SendCommand(c, true); Thread.Sleep(300); } // same delay as Huion's driver
        }

        // Driver mode can be lost without a disconnection: repeat its command (the last one) every minute.
        // It costs one tiny Bluetooth write and has no effect when the mode is already on.
        void KeepDriverMode()
        {
            lastActivation = DateTime.Now;
            if (cfg.ActivationCommands.Count > 0) ble.SendCommand(cfg.ActivationCommands[cfg.ActivationCommands.Count - 1], cfg.VerboseLog);
        }

        void OnCommand(byte[] b)
        {
            if (cfg.VerboseLog) Log.W("FFE2 <- [" + Log.Hex(b) + "]");
            if (b.Length < 2) return;
            switch (b[1])
            {
                case 0xC8:
                    if (!driverModeLogged || cfg.VerboseLog) Log.W("Keydial: driver mode confirmed.");
                    driverModeLogged = true;
                    break;
                case 0xC9:
                    Log.W("Keydial: model " + Encoding.ASCII.GetString(b, 2, b.Length - 2));
                    break;
                case 0xD1 when b.Length >= 4 && b[3] <= 100: // periodic status (~3 min): byte 3 = battery %
                    if (battery != b[3]) Log.W("Keydial battery: " + b[3] + " %");
                    battery = b[3];
                    if (battery <= 15 && !lowBatteryWarned)
                    {
                        lowBatteryWarned = true;
                        tray.ShowBalloonTip(5000, AppInfo.Name, L.T($"Keydial battery low: {battery} %", $"Batterie du Keydial faible : {battery} %"), ToolTipIcon.Warning);
                    }
                    if (battery > 20) lowBatteryWarned = false;
                    UpdateTray();
                    break;
            }
        }

        public void ToggleKeyOverlay()
        {
            if (overlay.Visible) { overlay.Hide(); return; }
            KeyOverlay.Fill(overlay, cfg, Foreground.ExeName(), cfg.DialModes.Count > 0 ? cfg.DialModes[dialIndex] : "");
            overlay.ShowNearCursor();
        }

        void UpdateTray()
        {
            if (statusItem == null) return;
            string st = !ble.IsOpen ? L.T("Keydial not found (paired?)", "Keydial introuvable (appairé ?)")
                      : !connected ? L.T("Keydial asleep", "Keydial en veille")
                      : paused ? L.T("Paused (Huion driver running)", "En pause (pilote Huion actif)")
                      : L.T("Keydial active", "Keydial actif");
            string bat = battery >= 0 ? L.T($"battery {battery} %", $"batterie {battery} %") : null;
            statusItem.Text = bat == null ? st : st + " — " + bat;
            string mode = cfg.DialModes.Count > 0 ? cfg.DialModes[dialIndex] : "-";
            string tip = AppInfo.Name + "\n" + st + "\n" + L.T("Dial: ", "Molette : ") + mode + (bat == null ? "" : " · " + bat);
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip; // Windows Forms limit: 63 characters
            bool active = ble.IsOpen && connected && !paused;
            if (lastActive == active && currentIcon != null) return;
            lastActive = active;
            var icon = AppIcon.ForTray(active);
            tray.Icon = icon;
            currentIcon?.Dispose();
            currentIcon = icon;
        }

        public void SetDialMode(string target)
        {
            if (cfg.DialModes.Count == 0) return;
            if (target == null) dialIndex = (dialIndex + 1) % cfg.DialModes.Count;
            else
            {
                int i = cfg.DialModes.FindIndex(m => Str.Norm(m) == Str.Norm(target));
                if (i < 0) { Log.W("Unknown dial mode: " + target); return; }
                dialIndex = i;
            }
            Log.W("Dial mode: " + cfg.DialModes[dialIndex]);
            if (cfg.ShowOsd) osd.Flash(L.T("Dial: ", "Molette : ") + cfg.DialModes[dialIndex]);
            UpdateTray();
        }

        // ---- frames

        void OnData(byte[] f)
        {
            if (cfg.VerboseLog) Log.W("FFE1 <- [" + Log.Hex(f) + "]");
            if (f.Length < 4 || f[0] != 0x55 || f[1] != 0x54) { Log.W("Unknown frame: [" + Log.Hex(f) + "]"); return; }
            int sum = 0;
            for (int i = 2; i < f.Length - 1; i++) sum += f[i];
            if ((sum & 0xFF) != f[f.Length - 1]) Log.W("Unexpected checksum: [" + Log.Hex(f) + "]");
            switch (f[2])
            {
                case 0xE0 when f.Length >= 8:
                    OnButtons((uint)(f[5] | f[6] << 8 | f[7] << 16));
                    break;
                case 0xF1 when f.Length >= 7:
                    int dir = f[6] == 1 ? 1 : f[6] == 2 ? -1 : (sbyte)f[6];
                    if (dir != 0) OnDial(cfg.ReverseDial ? -dir : dir);
                    break;
                default:
                    Log.W("Unhandled frame type: [" + Log.Hex(f) + "]");
                    break;
            }
        }

        static string ButtonName(int bit) => bit == BitCenter ? "Center" : "T" + (bit + 1);

        void OnButtons(uint mask)
        {
            uint changed = mask ^ lastButtons;
            lastButtons = mask;
            for (int bit = 0; bit < 24; bit++)
            {
                if ((changed & (1u << bit)) == 0) continue;
                bool down = (mask & (1u << bit)) != 0;
                string name = ButtonName(bit);
                if (ConfigActive)
                {
                    // Settings window in front: the key selects itself in the window.
                    if (down) configForm.SelectFromDevice(name);
                    continue;
                }
                if (paused) { if (down) Log.W("Button " + name + " (paused: nothing sent)"); continue; }
                if (down)
                {
                    string exe = Foreground.ExeName();
                    string rawAct = cfg.ButtonAction(exe, name);
                    var act = Actions.Parse(rawAct, out string err);
                    if (err != null) Log.W($"{name}: invalid action \"{rawAct}\" ({err})");
                    if (cfg.VerboseLog) Log.W($"Button {name} down ({exe ?? "?"}) -> {rawAct ?? "none"}");
                    if (act == null) continue;
                    activeActs[bit] = act;
                    act.Down(this);
                }
                else if (activeActs.TryGetValue(bit, out var act))
                {
                    activeActs.Remove(bit);
                    act.Up(this);
                }
            }
        }

        void OnDial(int dir)
        {
            if (cfg.DialModes.Count == 0 || ConfigActive) return;
            string mode = cfg.DialModes[dialIndex];
            if (paused) { Log.W("Dial " + (dir > 0 ? "+" : "-") + " (paused: nothing sent)"); return; }
            string exe = Foreground.ExeName();
            string rawAct = cfg.DialAction(exe, mode, dir > 0);
            if (cfg.VerboseLog) Log.W($"Dial {(dir > 0 ? "clockwise" : "counterclockwise")} [{mode}] ({exe ?? "?"}) -> {rawAct ?? "none"}");
            var act = Actions.Parse(rawAct, out string err);
            if (err != null) Log.W($"Dial {mode}: invalid action \"{rawAct}\" ({err})");
            if (act == null) return;
            act.Down(this);
            act.Up(this);
        }

        void ReleaseEverything()
        {
            foreach (var a in activeActs.Values) try { a.Up(this); } catch { }
            activeActs.Clear();
            lastButtons = 0;
            Input.ReleaseAll();
        }

        protected override void ExitThreadCore()
        {
            Log.W("Exit.");
            showWait.Unregister(null);
            quitWait.Unregister(null);
            showSignal.Dispose();
            quitSignal.Dispose();
            ReleaseEverything();
            poll.Stop();
            ble.Dispose();
            tray.Visible = false;
            tray.Dispose();
            osd.Dispose();
            overlay.Dispose();
            if (configForm != null && !configForm.IsDisposed) configForm.Dispose();
            base.ExitThreadCore();
        }
    }
}
