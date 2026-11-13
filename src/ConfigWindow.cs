// Open Keydial — settings window: profiles, keys, dial modes, options.
// It edits OpenKeydial.ini (a .bak copy is made before each save); the driver reloads it automatically.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace OpenKeydial
{
    // Editable INI document that keeps the order and spelling of sections and keys.
    sealed class IniDocument
    {
        public sealed class Section
        {
            public string Name = "";
            public readonly List<KeyValuePair<string, string>> Entries = new List<KeyValuePair<string, string>>();
        }

        public readonly List<Section> Sections = new List<Section>();

        public static IniDocument Load(string path)
        {
            var doc = new IniDocument();
            Section cur = null;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = Ini.StripComment(raw).Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    string name = line.Substring(1, line.Length - 2).Trim();
                    cur = doc.Find(name) ?? doc.Add(name);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0 || cur == null) continue;
                doc.Set(cur.Name, line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
            return doc;
        }

        public Section Find(string name) => Sections.FirstOrDefault(s => Str.Norm(s.Name) == Str.Norm(name));

        Section Add(string name)
        {
            var s = new Section { Name = name };
            Sections.Add(s);
            return s;
        }

        public string Get(string section, string key)
        {
            var s = Find(section);
            if (s == null) return null;
            foreach (var kv in s.Entries)
                if (Str.Norm(kv.Key) == Str.Norm(key)) return kv.Value;
            return null;
        }

        public void Set(string section, string key, string value)
        {
            var s = Find(section) ?? Add(section);
            int i = s.Entries.FindIndex(kv => Str.Norm(kv.Key) == Str.Norm(key));
            var entry = new KeyValuePair<string, string>(i >= 0 ? s.Entries[i].Key : key, value ?? "");
            if (i >= 0) s.Entries[i] = entry; else s.Entries.Add(entry);
        }

        public void Remove(string section, string key) => Find(section)?.Entries.RemoveAll(kv => Str.Norm(kv.Key) == Str.Norm(key));

        public void RemoveSection(string name) => Sections.RemoveAll(s => Str.Norm(s.Name) == Str.Norm(name));

        public void RenameSection(string name, string newName)
        {
            if (Find(name) is Section s) s.Name = newName;
        }

        // Renames a dial mode everywhere it is referred to: its rotation actions ([Dial name]), the profiles'
        // own rotation actions ("name.Clockwise") and the keys that switch to it ("DialMode:name").
        // The [General] DialModes list is the caller's.
        public void RenameDialMode(string old, string name)
        {
            if (Str.Norm(name) != Str.Norm(old)) RemoveSection(Config.DialPrefix + name);   // leftover of a removed mode
            RenameSection(Config.DialPrefix + old, Config.DialPrefix + name);

            string oldNorm = Str.Norm(old), prefix = Str.Norm(Config.ProfilePrefix);
            string[] turns = { Str.Norm(Config.Clockwise), Str.Norm(Config.CounterClockwise) };
            foreach (var s in Sections.Where(s => Str.Norm(s.Name).StartsWith(prefix)))
                for (int k = 0; k < s.Entries.Count; k++)
                {
                    string key = s.Entries[k].Key, value = s.Entries[k].Value;
                    int dot = key.LastIndexOf('.');
                    if (dot > 0 && Str.Norm(key.Substring(0, dot)) == oldNorm && turns.Contains(Str.Norm(key.Substring(dot + 1))))
                        key = name + key.Substring(dot);
                    int colon = value.IndexOf(':');
                    if (colon > 0 && Str.Norm(value.Substring(0, colon)) == "dialmode" && Str.Norm(value.Substring(colon + 1)) == oldNorm)
                        value = value.Substring(0, colon + 1) + name;
                    s.Entries[k] = new KeyValuePair<string, string>(key, value);
                }
        }

        public void Save(string path, string header)
        {
            var sb = new StringBuilder();
            sb.Append(header.TrimEnd()).Append("\r\n\r\n");
            foreach (var s in Sections)
            {
                sb.Append('[').Append(s.Name).Append("]\r\n");
                foreach (var kv in s.Entries) sb.Append(kv.Key).Append(" = ").Append(kv.Value).Append("\r\n");
                sb.Append("\r\n");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }

    static class KeyNames
    {
        static readonly Dictionary<string, ushort> OemVk = new Dictionary<string, ushort>
        {
            ["oem1"] = 0xBA, ["oem2"] = 0xBF, ["oem3"] = 0xC0, ["oem4"] = 0xDB, ["oem5"] = 0xDC, ["oem6"] = 0xDD,
            ["oem7"] = 0xDE, ["oem8"] = 0xDF, ["oem102"] = 0xE2, ["oemplus"] = 0xBB, ["oemcomma"] = 0xBC,
            ["oemminus"] = 0xBD, ["oemperiod"] = 0xBE,
        };

        static string Friendly(string n)
        {
            switch (n)
            {
                case "dialmode": return L.T("Next dial mode", "Mode molette suivant");
                case "showkeys": return L.T("Key map", "Carte des touches");
                case "leftclick": return L.T("Left click", "Clic gauche");
                case "rightclick": return L.T("Right click", "Clic droit");
                case "middleclick": return L.T("Middle click", "Clic milieu");
                case "wheelup": return L.T("Wheel up", "Molette haut");
                case "wheeldown": return L.T("Wheel down", "Molette bas");
                case "wheelleft": return L.T("Wheel left", "Molette gauche");
                case "wheelright": return L.T("Wheel right", "Molette droite");
                case "back": return L.T("Back", "Précédent");
                case "forward": return L.T("Forward", "Suivant");
                case "numplus": return "Num +";
                case "numminus": return "Num -";
                case "nummul": return "Num *";
                case "numdiv": return "Num /";
                case "numdot": return "Num .";
                case "space": return L.T("Space", "Espace");
                case "enter": return L.T("Enter", "Entrée");
                case "del": return L.T("Del", "Suppr");
                case "backspace": return L.T("Backspace", "Retour arr.");
                case "esc": return L.T("Esc", "Échap");
            }
            return null;
        }

        // INI name of a combination typed on the keyboard.
        public static string FromKeys(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            var parts = new List<string>();
            bool isCtrl = key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey;
            bool isShift = key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey;
            bool isAlt = key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu;
            bool isWin = key == Keys.LWin || key == Keys.RWin;
            if ((keyData & Keys.Control) != 0 && !isCtrl) parts.Add("Ctrl");
            if ((keyData & Keys.Shift) != 0 && !isShift) parts.Add("Shift");
            if ((keyData & Keys.Alt) != 0 && !isAlt) parts.Add("Alt");
            parts.Add(isCtrl ? "Ctrl" : isShift ? "Shift" : isAlt ? "Alt" : isWin ? "Win" : Name(key));
            return string.Join("+", parts);
        }

        static string Name(Keys k)
        {
            switch (k)
            {
                case Keys.Space: return "Space";
                case Keys.Enter: return "Enter";
                case Keys.Escape: return "Esc";
                case Keys.Tab: return "Tab";
                case Keys.Back: return "Backspace";
                case Keys.Delete: return "Del";
                case Keys.Insert: return "Ins";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "PgUp";
                case Keys.PageDown: return "PgDn";
                case Keys.Left: return "Left";
                case Keys.Right: return "Right";
                case Keys.Up: return "Up";
                case Keys.Down: return "Down";
                case Keys.Add: return "NumPlus";
                case Keys.Subtract: return "NumMinus";
                case Keys.Multiply: return "NumMul";
                case Keys.Divide: return "NumDiv";
                case Keys.Decimal: return "NumDot";
                case Keys.PrintScreen: return "PrintScreen";
                case Keys.Pause: return "Pause";
                case Keys.CapsLock: return "CapsLock";
                case Keys.Apps: return "Menu";
                case Keys.VolumeUp: return "VolumeUp";
                case Keys.VolumeDown: return "VolumeDown";
                case Keys.VolumeMute: return "Mute";
                case Keys.MediaPlayPause: return "PlayPause";
                case Keys.MediaNextTrack: return "NextTrack";
                case Keys.MediaPreviousTrack: return "PrevTrack";
            }
            if (k >= Keys.A && k <= Keys.Z) return k.ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + (k - Keys.NumPad0);
            if (k >= Keys.F1 && k <= Keys.F24) return "F" + (k - Keys.F1 + 1);
            switch ((int)k)
            {
                case 0xBA: return "Oem1";
                case 0xBF: return "Oem2";
                case 0xC0: return "Oem3";
                case 0xDB: return "Oem4";
                case 0xDC: return "Oem5";
                case 0xDD: return "Oem6";
                case 0xDE: return "Oem7";
                case 0xDF: return "Oem8";
                case 0xE2: return "Oem102";
                case 0xBB: return "OemPlus";
                case 0xBC: return "OemComma";
                case 0xBD: return "OemMinus";
                case 0xBE: return "OemPeriod";
            }
            return "vk:0x" + ((int)k).ToString("X2");
        }

        // Readable label: OEM keys show the character of the current keyboard layout.
        public static string Pretty(string action)
        {
            if (string.IsNullOrWhiteSpace(action) || Str.Norm(action) == "none") return "—";
            string n = Str.Norm(action);
            if (n.StartsWith("dialmode:")) return L.T("Dial: ", "Molette : ") + action.Substring(action.IndexOf(':') + 1).Trim();
            if (n.StartsWith("run:")) return L.T("Run ", "Lancer ") + Path.GetFileNameWithoutExtension(action.Substring(action.IndexOf(':') + 1).Trim());
            var parts = action.Split('+').Select(p =>
            {
                string pn = Str.Norm(p);
                if (OemVk.TryGetValue(pn, out var vk))
                {
                    uint ch = Native.MapVirtualKey(vk, 2) & 0x7FFFFFFF;
                    return ch > 32 ? L.T("\"" + (char)ch + "\"", "« " + (char)ch + " »") : p.Trim();
                }
                return Friendly(pn) ?? p.Trim();
            });
            return string.Join("+", parts);
        }
    }

    // Input box: pressing a combination records it (Ctrl+Z, Shift, F5, "^"...).
    sealed class CaptureBox : TextBox
    {
        public event Action Captured;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public CaptureBox()
        {
            ReadOnly = true;
            Cursor = Cursors.IBeam;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SendMessage(Handle, 0x1501, (IntPtr)1, L.T("Click here, then press the key or combination", "Clique ici, puis appuie sur la touche ou la combinaison")); // EM_SETCUEBANNER
        }
        protected override bool IsInputKey(Keys keyData) => true;
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Text = KeyNames.FromKeys(keyData);
            Captured?.Invoke();
            return true;
        }
    }

    // Editor for one action (a key, or one direction of the dial).
    sealed class ActionEditor : UserControl
    {
        static readonly string[] MouseNames = { "LeftClick", "RightClick", "MiddleClick", "WheelUp", "WheelDown", "WheelLeft", "WheelRight", "Back", "Forward" };

        readonly ComboBox type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        readonly CaptureBox capture = new CaptureBox { Width = 300 };
        readonly ComboBox mouse = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        readonly DarkCheck mCtrl = new DarkCheck { Text = "Ctrl", AutoSize = true }, mShift = new DarkCheck { Text = "Shift", AutoSize = true }, mAlt = new DarkCheck { Text = "Alt", AutoSize = true };
        readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        readonly TextBox path = new TextBox { Width = 200 };
        readonly Button browse = new Button { AutoSize = true };
        readonly Label error = new Label { AutoSize = true, ForeColor = Color.Firebrick, Visible = false, MaximumSize = new Size(300, 0) };
        readonly FlowLayoutPanel body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
        bool allowInherit, loading;

        public event Action Changed;

        enum Kind { Inherit, None, Keys, Mouse, DialMode, ShowKeys, Run }
        readonly List<Kind> kinds = new List<Kind>();

        public ActionEditor()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            browse.Text = L.T("Browse…", "Parcourir…");
            mouse.Items.AddRange(new object[] {
                L.T("Left click", "Clic gauche"), L.T("Right click", "Clic droit"), L.T("Middle click", "Clic milieu"),
                L.T("Wheel up", "Molette vers le haut"), L.T("Wheel down", "Molette vers le bas"),
                L.T("Wheel left", "Molette à gauche"), L.T("Wheel right", "Molette à droite"),
                L.T("Back button", "Bouton Précédent"), L.T("Forward button", "Bouton Suivant") });
            var mods = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            mods.Controls.AddRange(new Control[] { mCtrl, mShift, mAlt });
            var runRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            runRow.Controls.AddRange(new Control[] { path, browse });
            foreach (var c in new Control[] { capture, mouse, mods, mode, runRow }) c.Margin = new Padding(3, 8, 3, 3);
            body.Controls.AddRange(new Control[] { type, capture, mouse, mods, mode, runRow, error });
            Controls.Add(body);
            BuildTypes();

            type.SelectedIndexChanged += (s, e) => { UpdateVisibility(); Fire(); };
            capture.Captured += Fire;
            mouse.SelectedIndexChanged += (s, e) => Fire();
            foreach (var c in new[] { mCtrl, mShift, mAlt }) c.CheckedChanged += (s, e) => Fire();
            mode.SelectedIndexChanged += (s, e) => Fire();
            path.TextChanged += (s, e) => Fire();
            browse.Click += (s, e) =>
            {
                using var dlg = new OpenFileDialog { Filter = L.T("Programs|*.exe;*.lnk;*.bat;*.cmd|All files|*.*", "Programmes|*.exe;*.lnk;*.bat;*.cmd|Tous les fichiers|*.*") };
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK) path.Text = dlg.FileName;
            };
            UpdateVisibility();
        }

        public bool AllowInherit
        {
            get => allowInherit;
            set { allowInherit = value; BuildTypes(); }
        }

        void BuildTypes()
        {
            loading = true;
            type.Items.Clear();
            kinds.Clear();
            void Add(Kind k, string label) { kinds.Add(k); type.Items.Add(label); }
            if (allowInherit) Add(Kind.Inherit, L.T("Same as the Global profile", "Comme le profil Global"));
            Add(Kind.None, L.T("Nothing", "Rien"));
            Add(Kind.Keys, L.T("Keyboard shortcut", "Raccourci clavier"));
            Add(Kind.Mouse, L.T("Mouse (click or wheel)", "Souris (clic ou molette)"));
            Add(Kind.DialMode, L.T("Change the dial mode", "Changer le mode de la molette"));
            Add(Kind.ShowKeys, L.T("Show the key map", "Afficher la carte des touches"));
            Add(Kind.Run, L.T("Run a program", "Lancer un programme"));
            type.SelectedIndex = 0;
            loading = false;
        }

        public void SetModes(IEnumerable<string> modes)
        {
            int cur = mode.SelectedIndex;
            string curName = cur > 0 ? mode.SelectedItem as string : null;
            loading = true;
            mode.Items.Clear();
            mode.Items.Add(L.T("Next mode", "Mode suivant"));
            foreach (var m in modes) mode.Items.Add(m);
            int idx = curName == null ? 0 : mode.Items.IndexOf(curName);
            mode.SelectedIndex = Math.Max(0, idx);
            loading = false;
        }

        Kind Current => type.SelectedIndex >= 0 ? kinds[type.SelectedIndex] : Kind.None;

        void UpdateVisibility()
        {
            var k = Current;
            capture.Visible = k == Kind.Keys;
            mouse.Visible = mCtrl.Parent.Visible = k == Kind.Mouse;
            mode.Visible = k == Kind.DialMode;
            path.Parent.Visible = k == Kind.Run;
            if (k == Kind.Mouse && mouse.SelectedIndex < 0) mouse.SelectedIndex = 0;
            if (k == Kind.DialMode && mode.SelectedIndex < 0 && mode.Items.Count > 0) mode.SelectedIndex = 0;
        }

        void SetError(string t) { error.Text = t; error.Visible = t.Length > 0; }   // empty = hidden, so it leaves no gap

        void Fire()
        {
            if (loading) return;
            string v = Value;
            SetError("");
            if (v != null && Actions.Parse(v, out string err) == null && err != null) SetError(err);
            Changed?.Invoke();
        }

        // null = inherit from the Global profile; "" = nothing.
        public string Value
        {
            get
            {
                switch (Current)
                {
                    case Kind.Inherit: return null;
                    case Kind.None: return "";
                    case Kind.Keys: return capture.Text;
                    case Kind.Mouse:
                        var parts = new List<string>();
                        if (mCtrl.Checked) parts.Add("Ctrl");
                        if (mShift.Checked) parts.Add("Shift");
                        if (mAlt.Checked) parts.Add("Alt");
                        parts.Add(MouseNames[Math.Max(0, mouse.SelectedIndex)]);
                        return string.Join("+", parts);
                    case Kind.DialMode:
                        return mode.SelectedIndex <= 0 ? "DialMode" : "DialMode:" + mode.SelectedItem;
                    case Kind.ShowKeys: return "ShowKeys";
                    case Kind.Run: return "Run:" + path.Text.Trim();
                }
                return "";
            }
            set
            {
                loading = true;
                string n = value == null ? null : Str.Norm(value);
                Kind k;
                if (value == null) k = allowInherit ? Kind.Inherit : Kind.None;
                else if (n.Length == 0 || n == "none") k = Kind.None;
                else if (n.StartsWith("dialmode")) k = Kind.DialMode;
                else if (n == "showkeys") k = Kind.ShowKeys;
                else if (n.StartsWith("run:")) k = Kind.Run;
                else if (MouseNames.Any(m => n.EndsWith(Str.Norm(m)))) k = Kind.Mouse;
                else k = Kind.Keys;

                type.SelectedIndex = Math.Max(0, kinds.IndexOf(k));
                capture.Text = k == Kind.Keys ? value : "";
                mCtrl.Checked = mShift.Checked = mAlt.Checked = false;
                if (k == Kind.Mouse)
                {
                    var toks = value.Split('+').Select(t => Str.Norm(t)).ToList();
                    mCtrl.Checked = toks.Contains("ctrl") || toks.Contains("control");
                    mShift.Checked = toks.Contains("shift") || toks.Contains("maj");
                    mAlt.Checked = toks.Contains("alt");
                    mouse.SelectedIndex = Math.Max(0, Array.FindIndex(MouseNames, m => Str.Norm(m) == toks.Last()));
                }
                if (k == Kind.DialMode)
                {
                    string target = n.Contains(":") ? value.Substring(value.IndexOf(':') + 1).Trim() : null;
                    int idx = target == null ? 0 : mode.Items.Cast<object>().ToList().FindIndex(o => Str.Norm(o.ToString()) == Str.Norm(target));
                    mode.SelectedIndex = idx < 0 ? 0 : idx;
                }
                path.Text = k == Kind.Run ? value.Substring(value.IndexOf(':') + 1).Trim() : "";
                SetError("");
                UpdateVisibility();
                loading = false;
            }
        }
    }

    // Clickable map of the Keydial. Its size is given in 100 % pixels (the window scales it);
    // drawing and clicks use real pixels, hence Theme.Px.
    sealed class KeypadControl : Control
    {
        public const int K = 78, Gap = 8, Pad = 12, HeaderH = 120;
        public static readonly (string name, int col, int row, int w, int h)[] KeyLayout =
        {
            ("T1", 0, 0, 1, 1), ("T2", 1, 0, 1, 1), ("T3", 2, 0, 1, 1), ("T4", 3, 0, 1, 1),
            ("T5", 0, 1, 1, 1), ("T6", 1, 1, 1, 1), ("T7", 2, 1, 1, 1), ("T8", 3, 1, 1, 1),
            ("T9", 0, 2, 1, 1), ("T10", 1, 2, 1, 1), ("T11", 2, 2, 1, 1), ("T12", 3, 2, 1, 1),
            ("T13", 0, 3, 1, 1), ("T14", 1, 3, 1, 1), ("T15", 2, 3, 1, 1), ("T16", 3, 3, 1, 2),
            ("T17", 0, 4, 2, 1), ("T18", 2, 4, 1, 1),
        };
        public Func<string, (string label, bool inherited)> Describe = n => ("", false);
        public string Selected { get; private set; } = "T1";
        public event Action<string> SelectedChanged;

        public KeypadControl()
        {
            DoubleBuffered = true;
            Size = new Size(Pad * 2 + 4 * K + 3 * Gap, HeaderH + 5 * K + 4 * Gap + Pad);
            BackColor = Theme.Back;
        }

        Rectangle DialRect => Theme.Px(new Rectangle(Pad + 4, 14, 92, 92), Theme.DpiOf(this));

        Rectangle KeyRect((string name, int col, int row, int w, int h) k) =>
            Theme.Px(new Rectangle(Pad + k.col * (K + Gap), HeaderH + k.row * (K + Gap), k.w * K + (k.w - 1) * Gap, k.h * K + (k.h - 1) * Gap), Theme.DpiOf(this));

        public void Select(string name)
        {
            if (name == Selected) return;
            Selected = name;
            Invalidate();
            SelectedChanged?.Invoke(name);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var d = DialRect;
            var c = new Point(d.X + d.Width / 2, d.Y + d.Height / 2);
            if (Math.Pow(e.X - c.X, 2) + Math.Pow(e.Y - c.Y, 2) <= Math.Pow(d.Width / 2, 2)) { Select("Center"); return; }
            foreach (var k in KeyLayout) if (KeyRect(k).Contains(e.Location)) { Select(k.name); return; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int dpi = Theme.DpiOf(this);
            int P(int v) => Theme.Px(v, dpi);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var fKey = Theme.Regular(8f, FontStyle.Regular, dpi);                  // theme fonts are shared: no Dispose
            var fKeyI = Theme.Light(8f, FontStyle.Italic, dpi);
            var fSmall = Theme.Regular(7f, FontStyle.Regular, dpi);
            var fHint = Theme.Light(9f, FontStyle.Regular, dpi);
            using var keyBrush = new SolidBrush(Theme.Key);
            using var accent = new Pen(Theme.Accent, 3f * dpi / 96f);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;

            var dial = DialRect;
            g.FillEllipse(keyBrush, dial);
            using (var ring = new Pen(Color.FromArgb(90, 90, 95), 2f * dpi / 96f)) g.DrawEllipse(ring, dial);
            if (Selected == "Center") g.DrawEllipse(accent, dial);
            var (cl, ci) = Describe("Center");
            TextRenderer.DrawText(g, cl, ci ? fKeyI : fKey, Rectangle.Inflate(dial, -P(12), -P(12)), ci ? Color.Gray : Color.White, flags);
            TextRenderer.DrawText(g, L.T("Click a key (or press it on the Keydial) to change it.\nThe circle is the dial button.",
                                         "Clique une touche (ou appuie dessus sur le Keydial) pour la modifier.\nLe cercle = bouton central de la molette."),
                fHint, new Rectangle(dial.Right + P(14), dial.Top + P(6), Width - dial.Right - P(14 + Pad), P(86)), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.WordBreak);

            foreach (var k in KeyLayout)
            {
                var r = KeyRect(k);
                using (var path = Theme.Rounded(r, P(12)))
                {
                    g.FillPath(keyBrush, path);
                    if (k.name == Selected) g.DrawPath(accent, path);
                }
                TextRenderer.DrawText(g, k.name, fSmall, new Rectangle(r.Left + P(6), r.Top + P(4), P(40), P(16)), Color.Gray, TextFormatFlags.Left | TextFormatFlags.NoPadding);
                var (label, inherited) = Describe(k.name);
                TextRenderer.DrawText(g, label, inherited ? fKeyI : fKey, Rectangle.Inflate(r, -P(5), -P(8)),
                    label == "—" ? Color.DimGray : inherited ? Color.Gray : Color.White, flags);
            }
        }
    }

    sealed class ConfigForm : ThemedForm
    {
        readonly ListBox profiles = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        readonly KeypadControl keypad = new KeypadControl();
        readonly Label keyTitle = new Label { AutoSize = true, Font = Theme.Regular(10f), Margin = new Padding(3, 0, 3, 8) };
        readonly ActionEditor keyEditor = new ActionEditor();
        readonly ListBox modes = new ListBox { Height = 180, Width = 180, IntegralHeight = false, Margin = new Padding(3, 3, 10, 3) };
        readonly ActionEditor cwEditor = new ActionEditor(), ccwEditor = new ActionEditor();
        readonly DarkCheck optReverse = new DarkCheck { AutoSize = true };
        readonly DarkCheck optOsd = new DarkCheck { AutoSize = true };
        readonly DarkCheck optStartup = new DarkCheck { AutoSize = true };
        readonly DarkCheck optVerbose = new DarkCheck { AutoSize = true };
        readonly ComboBox optLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        readonly Button save = new Button { AutoSize = true, Tag = "primary" };
        readonly Label status = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 9, 12, 3) };
        readonly Action onSaved;
        IniDocument doc;
        string header = "";
        bool loading, dirty;

        public event Action LanguageChanged;

        public ConfigForm(Action onSaved, int dpi = 0)
        {
            this.onSaved = onSaved;
            Font = Theme.Light(9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;   // scaling is done by Theme.Dress
            KeyPreview = false;
            Icon = AppIcon.ForWindows();

            optReverse.Text = L.T("Reverse the dial direction", "Inverser le sens de la molette");
            optOsd.Text = L.T("Show the dial mode on screen", "Afficher le mode de la molette à l'écran");
            optStartup.Text = L.T("Start with Windows", "Lancer au démarrage de Windows");
            optVerbose.Text = L.T("Detailed log (troubleshooting)", "Journal détaillé (diagnostic)");
            save.Text = L.T("Save", "Enregistrer");
            optLanguage.Items.AddRange(L.Names);

            // --- left column: profiles
            var left = new Panel { Dock = DockStyle.Left, Width = 220, Padding = new Padding(16, 14, 6, 10) };
            var profTitle = new TitleLabel(L.T("Profiles", "Profils")) { Dock = DockStyle.Top };
            var profHint = new Label
            {
                Text = L.T("Global applies everywhere; an application profile only replaces the keys it defines.",
                           "Global s'applique partout ; un profil d'appli ne remplace que les touches qu'il définit."),
                Dock = DockStyle.Bottom, Height = 64, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 6, 0, 0)
            };
            var profButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
            var addProfile = new Button { Text = L.T("Add…", "Ajouter…"), AutoSize = true, Margin = new Padding(0, 3, 6, 3) };
            var delProfile = new Button { Text = L.T("Remove", "Supprimer"), AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
            profButtons.Controls.AddRange(new Control[] { addProfile, delProfile });
            left.Controls.Add(profiles);
            left.Controls.Add(profTitle);
            left.Controls.Add(profButtons);
            left.Controls.Add(profHint);

            // --- middle: the Keydial map, options below it
            var center = new Panel { Dock = DockStyle.Left, Width = keypad.Width + 20, Padding = new Padding(10) };
            keypad.Location = new Point(10, 10);
            center.Controls.Add(keypad);

            var optBox = new SectionPanel(L.T("Options", "Options")) { MinimumSize = new Size(keypad.Width - 2 * KeypadControl.Pad, 0), Location = new Point(10 + KeypadControl.Pad, keypad.Bottom + 4) };
            var optFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var langRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            langRow.Controls.AddRange(new Control[] { new Label { Text = L.T("Language", "Langue"), AutoSize = true, Margin = new Padding(3, 5, 10, 3) }, optLanguage });
            optFlow.Controls.AddRange(new Control[] { optReverse, optOsd, optStartup, optVerbose, langRow });
            optBox.Controls.Add(optFlow);
            center.Controls.Add(optBox);

            // --- right column: editing
            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(4, 14, 10, 10) };
            var sectionSize = new Size(408, 0);
            var keyBox = new SectionPanel(L.T("Selected key", "Touche sélectionnée")) { MinimumSize = sectionSize };
            var keyFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            keyFlow.Controls.AddRange(new Control[] { keyTitle, keyEditor });
            keyBox.Controls.Add(keyFlow);

            var dialBox = new SectionPanel(L.T("Dial — modes shared by all profiles", "Molette — modes communs à tous les profils")) { MinimumSize = sectionSize };
            var dialFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var modeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 10) };
            var modeButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var addMode = new Button { Text = L.T("Add…", "Ajouter…"), AutoSize = true };
            var renameMode = new Button { Text = L.T("Rename…", "Renommer…"), AutoSize = true };
            var delMode = new Button { Text = L.T("Remove", "Supprimer"), AutoSize = true };
            var upMode = new Button { Text = L.T("Move up", "Monter"), AutoSize = true };
            var downMode = new Button { Text = L.T("Move down", "Descendre"), AutoSize = true };
            foreach (var b in new[] { addMode, renameMode, delMode, upMode, downMode }) { b.MinimumSize = new Size(116, 30); b.Margin = new Padding(0, 3, 0, 3); }
            modeButtons.Controls.AddRange(new Control[] { addMode, renameMode, delMode, upMode, downMode });
            modeRow.Controls.AddRange(new Control[] { modes, modeButtons });
            dialFlow.Controls.AddRange(new Control[] {
                new Label { Text = L.T("The dial button switches to the next mode, in this order.", "Le bouton central passe au mode suivant, dans cet ordre."), AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(380, 0) },
                modeRow,
                new Label { Text = L.T("Clockwise", "Sens horaire"), AutoSize = true, Font = Theme.Regular(9.5f) }, cwEditor,
                new Label { Text = L.T("Counterclockwise", "Sens anti-horaire"), AutoSize = true, Font = Theme.Regular(9.5f), Margin = new Padding(3, 10, 3, 3) }, ccwEditor });
            dialBox.Controls.Add(dialFlow);
            right.Controls.AddRange(new Control[] { keyBox, dialBox });

            // --- bottom
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 10, 14, 10) };
            var close = new Button { Text = L.T("Close", "Fermer"), AutoSize = true, MinimumSize = new Size(100, 30) };
            save.MinimumSize = new Size(120, 30);
            var openIni = new Button { Text = L.T("Open settings file", "Ouvrir le fichier .ini"), AutoSize = true };
            bottom.Controls.AddRange(new Control[] { close, save, openIni, status });

            Controls.Add(right);
            Controls.Add(center);
            Controls.Add(left);
            Controls.Add(bottom);
            ClientSize = new Size(220 + keypad.Width + 20 + 440, 700 + 54);
            MinimumSize = new Size(ClientSize.Width / 2, 420);

            // --- behaviour
            keypad.Describe = Describe;
            keypad.SelectedChanged += n => ShowKey();
            profiles.SelectedIndexChanged += (s, e) => { ShowKey(); keypad.Invalidate(); };
            keyEditor.Changed += () =>
            {
                if (loading) return;
                string sec = CurrentSection, key = keypad.Selected, v = keyEditor.Value;
                if (v == null) doc.Remove(sec, key); else doc.Set(sec, key, v);
                MarkDirty();
                keypad.Invalidate();
            };
            modes.SelectedIndexChanged += (s, e) => ShowMode();
            cwEditor.Changed += () => { if (!loading && modes.SelectedItem is string m) { doc.Set(Config.DialPrefix + m, Config.Clockwise, cwEditor.Value ?? ""); MarkDirty(); } };
            ccwEditor.Changed += () => { if (!loading && modes.SelectedItem is string m) { doc.Set(Config.DialPrefix + m, Config.CounterClockwise, ccwEditor.Value ?? ""); MarkDirty(); } };
            addMode.Click += (s, e) => AddMode();
            renameMode.Click += (s, e) => RenameMode();
            modes.DoubleClick += (s, e) => RenameMode();
            delMode.Click += (s, e) => DeleteMode();
            upMode.Click += (s, e) => MoveMode(-1);
            downMode.Click += (s, e) => MoveMode(1);
            addProfile.Click += (s, e) => AddProfile();
            delProfile.Click += (s, e) => DeleteProfile();
            foreach (var c in new[] { optReverse, optOsd, optVerbose, optStartup }) c.CheckedChanged += (s, e) => { if (!loading) MarkDirty(); };
            optLanguage.SelectedIndexChanged += (s, e) => { if (!loading) MarkDirty(); };
            save.Click += (s, e) => Save();
            close.Click += (s, e) => Close();
            openIni.Click += (s, e) => Process.Start("notepad.exe", "\"" + AppPaths.ConfigFile + "\"");

            LoadDocument();
            Theme.Dress(this, dpi > 0 ? dpi : Theme.DpiAtCursor());
            // Height computed after scaling: the Options block holds text that is already at the right size.
            int d = Theme.DpiOf(this);
            ClientSize = new Size(ClientSize.Width, Math.Max(optBox.Top + optBox.PreferredSize.Height + Theme.Px(12, d), Theme.Px(700, d)) + bottom.Height);
        }

        string CurrentSection => profiles.SelectedItem is string p && p != "Global" ? Config.ProfilePrefix + p : Config.GlobalProfile;
        bool IsGlobal => CurrentSection == Config.GlobalProfile;

        void LoadDocument()
        {
            loading = true;
            doc = IniDocument.Load(AppPaths.ConfigFile);
            header = Config.DefaultHeader();
            profiles.Items.Clear();
            profiles.Items.Add("Global");
            string prefix = Str.Norm(Config.ProfilePrefix);
            foreach (var s in doc.Sections)
                if (Str.Norm(s.Name).StartsWith(prefix) && Str.Norm(s.Name) != Str.Norm(Config.GlobalProfile))
                    profiles.Items.Add(s.Name.Substring(Config.ProfilePrefix.Length).Trim());
            profiles.SelectedIndex = 0;
            ReloadModes(null);
            optReverse.Checked = Str.Norm(doc.Get(Config.General, "DialDirection") ?? "").StartsWith("rev");
            optOsd.Checked = doc.Get(Config.General, "ShowDialMode") is string osd ? Str.IsYes(osd) : true;
            optVerbose.Checked = Str.Norm(doc.Get(Config.General, "Log") ?? "").StartsWith("detail");
            optStartup.Checked = Setup.IsStartup();
            optLanguage.SelectedIndex = Str.Norm(doc.Get(Config.General, "Language") ?? "en").StartsWith("fr") ? 1 : 0;
            loading = false;
            ShowKey();
            dirty = false;
            UpdateTitle();
        }

        List<string> ModeList() => (doc.Get(Config.General, "DialModes") ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        void ReloadModes(string select)
        {
            var list = ModeList();
            bool was = loading;
            loading = true;
            modes.Items.Clear();
            foreach (var m in list) modes.Items.Add(m);
            if (modes.Items.Count > 0) modes.SelectedIndex = Math.Max(0, select == null ? 0 : list.IndexOf(select));
            foreach (var ed in new[] { keyEditor, cwEditor, ccwEditor }) ed.SetModes(list);
            loading = was;
            ShowMode();
        }

        (string, bool) Describe(string key)
        {
            string own = doc?.Get(CurrentSection, key);
            if (own != null) return (KeyNames.Pretty(own), false);
            if (!IsGlobal) return (KeyNames.Pretty(doc?.Get(Config.GlobalProfile, key) ?? ""), true);
            return ("—", false);
        }

        void ShowKey()
        {
            if (doc == null) return;
            bool was = loading;
            loading = true;
            string key = keypad.Selected;
            keyTitle.Text = (key == "Center" ? L.T("Dial button", "Bouton central de la molette") : L.T("Key ", "Touche ") + key)
                + (IsGlobal ? L.T(" — Global profile", " — profil Global") : L.T(" — profile ", " — profil ") + profiles.SelectedItem);
            keyEditor.AllowInherit = !IsGlobal;
            keyEditor.Value = doc.Get(CurrentSection, key);
            loading = was;
        }

        void ShowMode()
        {
            bool was = loading;
            loading = true;
            bool any = modes.SelectedItem is string;
            cwEditor.Enabled = ccwEditor.Enabled = any;
            if (modes.SelectedItem is string m)
            {
                cwEditor.Value = doc.Get(Config.DialPrefix + m, Config.Clockwise) ?? "";
                ccwEditor.Value = doc.Get(Config.DialPrefix + m, Config.CounterClockwise) ?? "";
            }
            loading = was;
        }

        void SaveModeList(List<string> list, string select)
        {
            doc.Set(Config.General, "DialModes", string.Join(", ", list));
            MarkDirty();
            ReloadModes(select);
        }

        void AddMode()
        {
            string name = Prompt(L.T("New dial mode", "Nouveau mode de molette"), L.T("Mode name (e.g. Rotation):", "Nom du mode (ex. Rotation) :"), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim().Replace(",", " ");
            var list = ModeList();
            if (list.Any(m => Str.Norm(m) == Str.Norm(name))) { MessageBox.Show(this, L.T("This mode already exists.", "Ce mode existe déjà."), AppInfo.Name); return; }
            list.Add(name);
            doc.Set(Config.DialPrefix + name, Config.Clockwise, "");
            doc.Set(Config.DialPrefix + name, Config.CounterClockwise, "");
            SaveModeList(list, name);
        }

        void RenameMode()
        {
            if (!(modes.SelectedItem is string old)) return;
            string name = Prompt(L.T("Rename the dial mode", "Renommer le mode de molette"), L.T("New name:", "Nouveau nom :"), old);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim().Replace(",", " ");
            if (name == old) return;
            var list = ModeList();
            if (Str.Norm(name) != Str.Norm(old) && list.Any(m => Str.Norm(m) == Str.Norm(name))) { MessageBox.Show(this, L.T("This mode already exists.", "Ce mode existe déjà."), AppInfo.Name); return; }
            int i = list.FindIndex(m => Str.Norm(m) == Str.Norm(old));
            if (i < 0) return;
            list[i] = name;
            doc.RenameDialMode(old, name);
            SaveModeList(list, name);
            ShowKey();
            keypad.Invalidate();
        }

        void DeleteMode()
        {
            if (!(modes.SelectedItem is string m)) return;
            var list = ModeList();
            if (list.Count <= 1) { MessageBox.Show(this, L.T("At least one mode is needed.", "Il faut garder au moins un mode."), AppInfo.Name); return; }
            if (MessageBox.Show(this, L.T($"Remove the mode \"{m}\"?", $"Supprimer le mode « {m} » ?"), AppInfo.Name, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            list.RemoveAll(x => x == m);
            doc.RemoveSection(Config.DialPrefix + m);
            SaveModeList(list, null);
        }

        void MoveMode(int delta)
        {
            if (!(modes.SelectedItem is string m)) return;
            var list = ModeList();
            int i = list.IndexOf(m), j = i + delta;
            if (i < 0 || j < 0 || j >= list.Count) return;
            (list[i], list[j]) = (list[j], list[i]);
            SaveModeList(list, m);
        }

        void AddProfile()
        {
            int self = Process.GetCurrentProcess().Id;
            var running = Process.GetProcesses()
                .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.Id != self; } catch { return false; } })
                .Select(p => { try { return Path.GetFileName(p.MainModule.FileName).ToLowerInvariant(); } catch { return p.ProcessName.ToLowerInvariant() + ".exe"; } })
                .Distinct().OrderBy(x => x).ToList();
            using var dlg = new ThemedForm
            {
                Text = L.T("Add an application profile", "Ajouter un profil d'application"),
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(420, 420), FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, ShowIcon = false, ShowInTaskbar = false, Font = Theme.Light(9.5f), Padding = new Padding(12, 6, 12, 0)
            };
            var lbl = new Label { Text = L.T("Pick a running application, or browse to its .exe:", "Choisis une application ouverte, ou parcours pour trouver son .exe :"), Dock = DockStyle.Top, Height = 44, Padding = new Padding(0, 8, 0, 8) };
            var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            foreach (var r in running) list.Items.Add(r);
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 8) };
            var ok = new Button { Text = L.T("Add", "Ajouter"), DialogResult = DialogResult.OK, AutoSize = true, Tag = "primary", MinimumSize = new Size(100, 30) };
            var cancel = new Button { Text = L.T("Cancel", "Annuler"), DialogResult = DialogResult.Cancel, AutoSize = true };
            var browseBtn = new Button { Text = L.T("Browse…", "Parcourir…"), AutoSize = true };
            string chosen = null;
            browseBtn.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog { Filter = L.T("Applications|*.exe", "Applications|*.exe") };
                if (ofd.ShowDialog(dlg) == DialogResult.OK) { chosen = Path.GetFileName(ofd.FileName).ToLowerInvariant(); dlg.DialogResult = DialogResult.OK; }
            };
            list.DoubleClick += (s, e) => { if (list.SelectedItem != null) dlg.DialogResult = DialogResult.OK; };
            row.Controls.AddRange(new Control[] { cancel, ok, browseBtn });
            dlg.Controls.Add(list);
            dlg.Controls.Add(lbl);
            dlg.Controls.Add(row);
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;
            Theme.Dress(dlg, Theme.DpiOf(this));
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            chosen ??= list.SelectedItem as string;
            if (string.IsNullOrEmpty(chosen)) return;
            var existing = profiles.Items.Cast<string>().FirstOrDefault(x => Str.Norm(x) == Str.Norm(chosen));
            if (existing != null) { profiles.SelectedItem = existing; return; }
            doc.Set(Config.ProfilePrefix + chosen, "Center", doc.Get(Config.GlobalProfile, "Center") ?? "DialMode");
            profiles.Items.Add(chosen);
            profiles.SelectedItem = chosen;
            MarkDirty();
        }

        void DeleteProfile()
        {
            if (IsGlobal) { MessageBox.Show(this, L.T("The Global profile cannot be removed.", "Le profil Global ne peut pas être supprimé."), AppInfo.Name); return; }
            string p = (string)profiles.SelectedItem;
            if (MessageBox.Show(this, L.T($"Remove the profile \"{p}\"?", $"Supprimer le profil « {p} » ?"), AppInfo.Name, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            doc.RemoveSection(Config.ProfilePrefix + p);
            profiles.Items.Remove(p);
            profiles.SelectedIndex = 0;
            MarkDirty();
        }

        string Prompt(string title, string text, string value)
        {
            using var f = new ThemedForm
            {
                Text = title, ClientSize = new Size(360, 136), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowIcon = false, ShowInTaskbar = false, Font = Theme.Light(9.5f)
            };
            var l = new Label { Text = text, Location = new Point(14, 14), AutoSize = true };
            var t = new TextBox { Text = value, Location = new Point(14, 42), Width = 332 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(156, 90), Size = new Size(90, 30), Tag = "primary" };
            var cancel = new Button { Text = L.T("Cancel", "Annuler"), DialogResult = DialogResult.Cancel, Location = new Point(256, 90), Size = new Size(90, 30) };
            f.Controls.AddRange(new Control[] { l, t, ok, cancel });
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            Theme.Dress(f, Theme.DpiOf(this));
            return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
        }

        void MarkDirty()
        {
            dirty = true;
            UpdateTitle();
        }

        void UpdateTitle()
        {
            Text = AppInfo.Name + L.T(" — keys and profiles", " — touches et profils") + (dirty ? " *" : "");
            save.Enabled = dirty;
            status.Text = dirty ? L.T("Unsaved changes", "Modifications non enregistrées") : "";
        }

        void Save()
        {
            string language = L.Codes[Math.Max(0, optLanguage.SelectedIndex)];
            doc.Set(Config.General, "Language", language);
            doc.Set(Config.General, "DialDirection", optReverse.Checked ? "reversed" : "normal");
            doc.Set(Config.General, "ShowDialMode", optOsd.Checked ? "yes" : "no");
            doc.Set(Config.General, "Log", optVerbose.Checked ? "detailed" : "normal");
            try
            {
                if (File.Exists(AppPaths.ConfigFile)) File.Copy(AppPaths.ConfigFile, AppPaths.ConfigFile + ".bak", true);
                onSaved?.Invoke();
                doc.Save(AppPaths.ConfigFile, header);
                if (optStartup.Checked != Setup.IsStartup()) Setup.SetStartup(optStartup.Checked);
                dirty = false;
                UpdateTitle();
                status.Text = L.T("Saved — active now.", "Enregistré — actif immédiatement.");
                if (language != L.Code)
                {
                    L.Set(language);
                    LanguageChanged?.Invoke();
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, L.T("Cannot save: ", "Enregistrement impossible : ") + e.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Called by the driver when a Keydial key is pressed while this window is active.
        public void SelectFromDevice(string name)
        {
            keypad.Select(name);
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (dirty && e.CloseReason == CloseReason.UserClosing)
            {
                var r = MessageBox.Show(this, L.T("Save your changes?", "Enregistrer les modifications ?"), AppInfo.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes) Save();
            }
            base.OnFormClosing(e);
        }
    }
}
