// Open Keydial — first launch, start with Windows, Start menu shortcut, removal.
// Open Keydial is portable: it runs from wherever its file is and never copies itself.
//   settings, log ... %APPDATA%\OpenKeydial\
//   shortcuts ....... Start menu, and the Startup folder when "Start with Windows" is on

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;

namespace OpenKeydial
{
    static class Setup
    {
        // Welcome window on the very first launch. Returns false if it was closed.
        public static bool FirstRun()
        {
            using var w = new WelcomeForm();
            if (w.ShowDialog() != DialogResult.OK) return false;
            L.Set(w.Language);
            try
            {
                Config.CreateDefault(AppPaths.ConfigFile, w.Language);
                SetStartup(w.StartWithWindows);
                SetStartMenu(w.StartMenu);
            }
            catch (Exception e)
            {
                Log.W("First run failed: " + e);
                MessageBox.Show(L.T("Setup failed: ", "La configuration a échoué : ") + e.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }

        // The program file was moved: point the existing shortcuts at its new place.
        public static void RepairShortcuts()
        {
            foreach (var link in new[] { AppPaths.StartupLink, AppPaths.StartMenuLink })
                try
                {
                    if (!File.Exists(link)) continue;
                    string target = Shortcut.Target(link);
                    if (target != null && AppPaths.SamePath(target, AppPaths.CurrentExe)) continue;
                    Shortcut.Create(link, AppPaths.CurrentExe);
                    Log.W("Shortcut updated to the program's new place: " + link);
                }
                catch (Exception e) { Log.W("Cannot update " + link + ": " + e.Message); }
        }

        // ---- start with Windows: a shortcut in the user's Startup folder (listed in Task Manager > Startup apps)

        public static bool IsStartup() => File.Exists(AppPaths.StartupLink);

        public static void SetStartup(bool on)
        {
            if (on) Shortcut.Create(AppPaths.StartupLink, AppPaths.CurrentExe);
            else if (File.Exists(AppPaths.StartupLink)) File.Delete(AppPaths.StartupLink);
            Log.W("Start with Windows: " + (on ? "on" : "off"));
        }

        public static void SetStartMenu(bool on)
        {
            if (on) Shortcut.Create(AppPaths.StartMenuLink, AppPaths.CurrentExe);
            else if (File.Exists(AppPaths.StartMenuLink)) File.Delete(AppPaths.StartMenuLink);
        }

        // ---- removal (tray menu): shortcuts, and the settings if asked. The program file is the user's to delete.

        public static bool Uninstall()
        {
            var r = MessageBox.Show(
                L.T($"Remove {AppInfo.Name} from this computer?\n\nYes: remove its shortcuts and its settings.\nNo: remove its shortcuts, keep the settings.",
                    $"Retirer {AppInfo.Name} de cet ordinateur ?\n\nOui : supprimer ses raccourcis et ses réglages.\nNon : supprimer ses raccourcis, garder les réglages."),
                AppInfo.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
            if (r == DialogResult.Cancel) return false;
            foreach (var f in new[] { AppPaths.StartupLink, AppPaths.StartMenuLink })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            Log.W("Removed" + (r == DialogResult.Yes ? ", settings deleted" : ", settings kept"));
            if (r == DialogResult.Yes) try { Directory.Delete(AppPaths.DataDir, true); } catch { }
            MessageBox.Show(
                L.T($"{AppInfo.Name} no longer starts with Windows. It will now quit: you can then delete its file, shown in the window that opens.",
                    $"{AppInfo.Name} ne se lance plus avec Windows. Il va maintenant se fermer : tu pourras ensuite supprimer son fichier, affiché dans la fenêtre qui s'ouvre."),
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start("explorer.exe", "/select,\"" + AppPaths.CurrentExe + "\"");
            return true;
        }
    }

    // Windows shortcuts (.lnk) through the standard IShellLink API.
    static class Shortcut
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        public static void Create(string link, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link));
            var sl = (IShellLinkW)new ShellLink();
            try
            {
                sl.SetPath(target);
                sl.SetWorkingDirectory(Path.GetDirectoryName(target));
                sl.SetDescription(AppInfo.Name + " — Huion Keydial Mini driver");
                sl.SetIconLocation(target, 0);
                ((IPersistFile)sl).Save(link, true);
            }
            finally { Marshal.ReleaseComObject(sl); }
        }

        public static string Target(string link)
        {
            var sl = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)sl).Load(link, 0);
                var sb = new StringBuilder(1024);
                sl.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.Length > 0 ? sb.ToString() : null;
            }
            finally { Marshal.ReleaseComObject(sl); }
        }
    }

    // First-launch window: language, start with Windows, Start menu shortcut.
    sealed class WelcomeForm : ThemedForm
    {
        readonly HeaderControl header = new HeaderControl();
        readonly Label intro = new Label { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(3, 4, 3, 14) };
        readonly Label languageLabel = new Label { AutoSize = true, Margin = new Padding(3, 5, 10, 3) };
        readonly ComboBox language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        readonly DarkCheck startup = new DarkCheck { AutoSize = true, Checked = true, Margin = new Padding(3, 12, 3, 0) };
        readonly DarkCheck startMenu = new DarkCheck { AutoSize = true, Checked = true };
        readonly Label placeHint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(440, 0), Margin = new Padding(3, 10, 3, 3) };
        readonly Label legal = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(440, 0), Margin = new Padding(3, 14, 3, 3) };
        readonly FlowLayoutPanel body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22, 18, 22, 6) };
        readonly FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(14, 12, 18, 12) };
        readonly Button ok = new Button { AutoSize = true, Tag = "primary", MinimumSize = new Size(120, 30), DialogResult = DialogResult.OK };
        readonly Button cancel = new Button { AutoSize = true, MinimumSize = new Size(100, 30), DialogResult = DialogResult.Cancel };

        public string Language => L.Codes[Math.Max(0, language.SelectedIndex)];
        public bool StartWithWindows => startup.Checked;
        public bool StartMenu => startMenu.Checked;

        public WelcomeForm(int dpi = 0)
        {
            Text = AppInfo.Name;
            Font = Theme.Light(9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Icon = AppIcon.ForWindows();
            ClientSize = new Size(488, 420);

            language.Items.AddRange(L.Names);
            language.SelectedIndex = L.Fr ? 1 : 0;
            var langRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            langRow.Controls.AddRange(new Control[] { languageLabel, language });
            body.Controls.AddRange(new Control[] { header, intro, langRow, startup, startMenu, placeHint, legal });
            bottom.Controls.AddRange(new Control[] { ok, cancel });
            Controls.Add(body);
            Controls.Add(bottom);
            AcceptButton = ok;
            CancelButton = cancel;
            UpdateTexts();

            language.SelectedIndexChanged += (s, e) => { L.Set(Language); UpdateTexts(); Fit(); };
            Theme.Dress(this, dpi > 0 ? dpi : Theme.DpiAtCursor());
            Fit();
        }

        void UpdateTexts()
        {
            header.Title = L.T("Welcome to " + AppInfo.Name, "Bienvenue dans " + AppInfo.Name);
            intro.Text = L.T("Open Keydial drives the Huion Keydial Mini (K20) over Bluetooth: its 18 keys, the dial and per-application profiles, without Huion's driver.\n\nPair the Keydial in the Windows Bluetooth settings first.",
                             "Open Keydial pilote le Huion Keydial Mini (K20) en Bluetooth : ses 18 touches, la molette et des profils par application, sans le pilote Huion.\n\nAppaire d'abord le Keydial dans les paramètres Bluetooth de Windows.");
            languageLabel.Text = L.T("Language", "Langue");
            startup.Text = L.T("Start with Windows", "Lancer au démarrage de Windows");
            startMenu.Text = L.T("Add to the Start menu", "Ajouter au menu Démarrer");
            placeHint.Text = L.T("Open Keydial runs from where this file is:\n", "Open Keydial fonctionne depuis l'emplacement de ce fichier :\n")
                + AppPaths.Friendly(Path.GetDirectoryName(AppPaths.CurrentExe))
                + L.T("\nKeep it there. If you move it later, launch it once from its new place.", "\nGarde-le à cet endroit. Si tu le déplaces plus tard, lance-le une fois depuis son nouvel emplacement.");
            legal.Text = L.T("Free software under the MIT License, provided without warranty. Not affiliated with or endorsed by Huion.",
                             "Logiciel libre sous licence MIT, fourni sans garantie. Sans lien avec Huion, ni approuvé par Huion.");
            ok.Text = L.T("Start", "Démarrer");
            cancel.Text = L.T("Cancel", "Annuler");
        }

        void Fit()
        {
            body.PerformLayout();
            ClientSize = new Size(ClientSize.Width, body.PreferredSize.Height + bottom.Height);
        }

        // Icon and title at the top of the window.
        sealed class HeaderControl : Control
        {
            string title = "";
            public HeaderControl()
            {
                Size = new Size(440, 64);
                Margin = new Padding(0, 0, 0, 8);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            public string Title { get => title; set { title = value; Invalidate(); } }
            protected override void OnPaint(PaintEventArgs e)
            {
                int dpi = Theme.DpiOf(this);
                e.Graphics.Clear(BackColor);
                int s = Theme.Px(56, dpi);
                using (var icon = AppIcon.Draw(s, true)) e.Graphics.DrawImageUnscaled(icon, 0, (Height - s) / 2);
                TextRenderer.DrawText(e.Graphics, title, Theme.Regular(15f, FontStyle.Regular, dpi),
                    new Rectangle(s + Theme.Px(14, dpi), 0, Width - s - Theme.Px(14, dpi), Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
