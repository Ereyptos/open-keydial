// Open Keydial — dark theme, embedded Montserrat font, and per-monitor DPI scaling.
//
// Windows are built in "100 %" pixels; Theme.Dress() styles a window and scales it to the DPI of
// its monitor. When the window moves to a monitor with another scale, WM_DPICHANGED rescales it
// (ThemedForm). Fonts are created in pixels for a given DPI, so text is sharp on every monitor.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenKeydial
{
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(30, 30, 32);      // window background
        public static readonly Color Panel = Color.FromArgb(40, 40, 43);     // blocks
        public static readonly Color Field = Color.FromArgb(52, 52, 56);     // input fields
        public static readonly Color Key = Color.FromArgb(58, 58, 62);       // buttons, keys
        public static readonly Color Border = Color.FromArgb(78, 78, 84);
        public static readonly Color Text = Color.FromArgb(236, 236, 239);
        public static readonly Color Dim = Color.FromArgb(150, 150, 156);
        public static readonly Color Accent = Color.FromArgb(0, 150, 255);
        public static readonly Color Error = Color.FromArgb(255, 125, 125);

        // ------------------------------------------------------------------ DPI

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        // Call first thing in Main, before any window exists.
        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext((IntPtr)(-4))) return; } catch { }   // per-monitor v2 (Windows 10 1703+)
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }                 // per-monitor (Windows 8.1+)
            try { SetProcessDPIAware(); } catch { }                                        // system DPI (older)
        }

        static int systemDpi;
        public static int SystemDpi
        {
            get
            {
                if (systemDpi == 0)
                    using (var g = Graphics.FromHwnd(IntPtr.Zero)) systemDpi = Math.Max(96, (int)Math.Round(g.DpiX));
                return systemDpi;
            }
        }

        public static int DpiAt(Point screenPoint)
        {
            try
            {
                IntPtr mon = MonitorFromPoint(new POINT { X = screenPoint.X, Y = screenPoint.Y }, 2); // MONITOR_DEFAULTTONEAREST
                if (GetDpiForMonitor(mon, 0, out uint x, out _) == 0 && x >= 72) return (int)x;   // MDT_EFFECTIVE_DPI
            }
            catch { }
            return SystemDpi;
        }

        public static int DpiAtCursor() => DpiAt(Cursor.Position);

        static readonly Dictionary<Form, int> formDpi = new Dictionary<Form, int>();

        // DPI a control is drawn at (the one of its window).
        public static int DpiOf(Control c)
        {
            var f = c as Form ?? c?.FindForm();
            return f != null && formDpi.TryGetValue(f, out int d) ? d : SystemDpi;
        }

        public static int Px(float v, int dpi) => (int)Math.Round(v * dpi / 96f);
        public static Rectangle Px(Rectangle r, int dpi) => new Rectangle(Px(r.X, dpi), Px(r.Y, dpi), Px(r.Width, dpi), Px(r.Height, dpi));

        // Styles a window built in 100 % pixels and scales it for the given DPI.
        public static void Dress(Form f, int dpi)
        {
            formDpi[f] = dpi;
            f.Disposed += (s, e) => formDpi.Remove(f);
            Apply(f, dpi);
            if (dpi != 96) f.Scale(new SizeF(dpi / 96f, dpi / 96f));
        }

        // The window moved to a monitor with another scale.
        public static bool Rescale(Form f, int dpi)
        {
            if (!formDpi.TryGetValue(f, out int old) || old == dpi) return false;
            formDpi[f] = dpi;
            Apply(f, dpi);
            f.Scale(new SizeF((float)dpi / old, (float)dpi / old));
            f.Invalidate(true);
            return true;
        }

        // ------------------------------------------------------------------ fonts

        [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

        static System.Drawing.Text.PrivateFontCollection fonts;
        static FontFamily light, regular;
        struct FontSpec { public bool Light; public float Pt; public FontStyle Style; }
        static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();
        static readonly ConditionalWeakTable<Font, object> fontSpecs = new ConditionalWeakTable<Font, object>();

        // The fonts are embedded in the exe: nothing to install in Windows.
        static void Load()
        {
            if (fonts != null) return;
            fonts = new System.Drawing.Text.PrivateFontCollection();
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf")))
            {
                using var st = asm.GetManifestResourceStream(name);
                var data = new byte[st.Length];
                int read = 0;
                while (read < data.Length) read += st.Read(data, read, data.Length - read);
                IntPtr mem = Marshal.AllocCoTaskMem(data.Length);   // never freed: the font lives there
                Marshal.Copy(data, 0, mem, data.Length);
                fonts.AddMemoryFont(mem, data.Length);              // for GDI+ (Font objects)
                uint n = 0;
                AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref n);   // for GDI (controls, TextRenderer)
            }
            light = fonts.Families.FirstOrDefault(f => f.Name == "Montserrat Light");
            regular = fonts.Families.FirstOrDefault(f => f.Name == "Montserrat");
        }

        static Font Get(bool lightFace, float pt, FontStyle style, int dpi)
        {
            Load();
            string key = (lightFace ? "L|" : "R|") + pt + "|" + style + "|" + dpi;
            if (!fontCache.TryGetValue(key, out var f))
            {
                float px = pt * dpi / 72f;
                var fam = lightFace ? light : regular;
                try { f = fam != null ? new Font(fam, px, style, GraphicsUnit.Pixel) : new Font(lightFace ? "Segoe UI Light" : "Segoe UI", px, style, GraphicsUnit.Pixel); }
                catch { f = new Font("Segoe UI", px, style, GraphicsUnit.Pixel); }
                fontCache[key] = f;
                fontSpecs.Add(f, new FontSpec { Light = lightFace, Pt = pt, Style = style });
            }
            return f;
        }

        public static Font Light(float pt, FontStyle style = FontStyle.Regular, int dpi = 96) => Get(true, pt, style, dpi);
        public static Font Regular(float pt, FontStyle style = FontStyle.Regular, int dpi = 96) => Get(false, pt, style, dpi);
        static bool IsThemeFont(Font f) => f != null && fontSpecs.TryGetValue(f, out _);

        static Font AtDpi(Font f, int dpi)
        {
            if (f == null || !fontSpecs.TryGetValue(f, out object o)) return f;
            var s = (FontSpec)o;
            return Get(s.Light, s.Pt, s.Style, dpi);
        }

        // ------------------------------------------------------------------ styling

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

        public static void DarkTitleBar(IntPtr hwnd)
        {
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
        }

        // Runs now if the handle exists, and again whenever it is recreated.
        static void OnHandle(Control c, Action<IntPtr> a)
        {
            c.HandleCreated += (s, e) => a(c.Handle);
            if (c.IsHandleCreated) a(c.Handle);
        }

        static readonly ConditionalWeakTable<Control, object> styled = new ConditionalWeakTable<Control, object>();

        // Applies the theme to a control and everything it contains; safe to call again for another DPI.
        static void Apply(Control c, int dpi)
        {
            bool first = !styled.TryGetValue(c, out _);
            if (first) styled.Add(c, null);

            var f = AtDpi(c.Font, dpi);
            if (!ReferenceEquals(f, c.Font)) c.Font = f;

            // Auto-sized blocks and buttons must be able to shrink: otherwise scaling would grow them
            // a second time (their text content is already at the right size).
            if (c.AutoSize)
            {
                if (c is Panel p) p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                else if (c is Button bt) bt.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                else if (c is UserControl uc) uc.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            }

            switch (c)
            {
                case KeypadControl _:
                    return;
                case Button b:
                    if (first) StyleButton(b, b.Tag as string == "primary", dpi);
                    break;
                case TextBox t:
                    if (first)
                    {
                        t.BackColor = Field;
                        t.ForeColor = Text;
                        t.BorderStyle = BorderStyle.FixedSingle;
                        OnHandle(t, h => SetMargins(t));
                        t.FontChanged += (s, e) => SetMargins(t);
                    }
                    SetMargins(t);
                    break;
                case ComboBox cb:
                    if (first) StyleCombo(cb);
                    cb.ItemHeight = Px(22, dpi);
                    break;
                case ListBox lb:
                    if (first) StyleList(lb);
                    lb.ItemHeight = Px(26, dpi);
                    break;
                case Label l:
                    // Font and light color come from the window; only the "special" colors change.
                    if (first && l.ForeColor == SystemColors.GrayText) l.ForeColor = Dim;
                    else if (first && l.ForeColor == Color.Firebrick) l.ForeColor = Error;
                    break;
                case Form form:
                    if (first)
                    {
                        form.BackColor = Back;
                        form.ForeColor = Text;
                        OnHandle(form, DarkTitleBar);
                    }
                    if (!IsThemeFont(form.Font)) form.Font = Light(9.5f, FontStyle.Regular, dpi);
                    break;
                case ScrollableControl sc when sc.AutoScroll:
                    if (first) OnHandle(sc, h => SetWindowTheme(h, "DarkMode_Explorer", null));
                    break;
            }
            foreach (Control child in c.Controls) Apply(child, dpi);
        }

        static void SetMargins(TextBox t)
        {
            if (!t.IsHandleCreated) return;
            int m = Px(8, DpiOf(t));
            SendMessage(t.Handle, 0xD3, (IntPtr)3, (IntPtr)(m | (m << 16)));   // EM_SETMARGINS: a little air left and right
        }

        public static void StyleButton(Button b, bool primary, int dpi)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.ForeColor = primary ? Color.White : Text;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(30, 165, 255) : Color.FromArgb(72, 72, 77);
            b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(0, 120, 210) : Color.FromArgb(88, 88, 94);
            b.Font = Regular(9f, FontStyle.Regular, dpi);
            b.Padding = new Padding(8, 3, 8, 3);
            b.MinimumSize = new Size(b.MinimumSize.Width, Math.Max(b.MinimumSize.Height, 30));
            b.Cursor = Cursors.Hand;
            // The main button turns grey while there is nothing to do.
            void Sync()
            {
                bool accent = primary && b.Enabled;
                b.BackColor = accent ? Accent : Key;
                b.FlatAppearance.BorderColor = accent ? Accent : Border;
            }
            b.EnabledChanged += (s, e) => Sync();
            Sync();
        }

        static void StyleCombo(ComboBox cb)
        {
            cb.BackColor = Field;
            cb.ForeColor = Text;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.DrawItem += (s, e) =>
            {
                int dpi = DpiOf(cb);
                bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
                bool sel = !edit && (e.State & DrawItemState.Selected) != 0;
                using (var bg = new SolidBrush(sel ? Accent : Field)) e.Graphics.FillRectangle(bg, e.Bounds);
                if (e.Index >= 0)
                    TextRenderer.DrawText(e.Graphics, cb.Items[e.Index].ToString(), cb.Font,
                        new Rectangle(e.Bounds.X + Px(6, dpi), e.Bounds.Y, e.Bounds.Width - Px(8, dpi), e.Bounds.Height), sel ? Color.White : Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            OnHandle(cb, h => SetWindowTheme(h, "DarkMode_CFD", null));
        }

        static void StyleList(ListBox lb)
        {
            lb.BackColor = Field;
            lb.ForeColor = Text;
            lb.BorderStyle = BorderStyle.None;
            lb.DrawMode = DrawMode.OwnerDrawFixed;
            lb.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                int dpi = DpiOf(lb);
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var bg = new SolidBrush(sel ? Accent : Field)) e.Graphics.FillRectangle(bg, e.Bounds);
                TextRenderer.DrawText(e.Graphics, lb.Items[e.Index].ToString(), sel ? Regular(9.5f, FontStyle.Regular, dpi) : lb.Font,
                    new Rectangle(e.Bounds.X + Px(8, dpi), e.Bounds.Y, e.Bounds.Width - Px(10, dpi), e.Bounds.Height), sel ? Color.White : Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            OnHandle(lb, h => SetWindowTheme(h, "DarkMode_Explorer", null));
        }

        public static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, rad, rad, 180, 90);
            p.AddArc(r.Right - rad, r.Top, rad, rad, 270, 90);
            p.AddArc(r.Right - rad, r.Bottom - rad, rad, rad, 0, 90);
            p.AddArc(r.Left, r.Bottom - rad, rad, rad, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // A window that follows the theme and rescales itself when it moves to a monitor with another scale.
    class ThemedForm : Form
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) // WM_DPICHANGED
            {
                int dpi = (int)((long)m.WParam & 0xFFFF);
                var r = Marshal.PtrToStructure<Theme.RECT>(m.LParam);
                if (Theme.Rescale(this, dpi)) Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }
    }

    // Rounded block with a title (replaces the Windows GroupBox).
    sealed class SectionPanel : Panel
    {
        string title;
        public SectionPanel(string title)
        {
            this.title = title;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14, 42, 14, 14);
            Margin = new Padding(0, 0, 0, 12);
            BackColor = Theme.Panel;
            DoubleBuffered = true;
        }
        public string Title { get => title; set { title = value; Invalidate(); } }
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            e.Control.Location = new Point(Padding.Left, Padding.Top);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            int dpi = Theme.DpiOf(this);
            g.Clear(Parent?.BackColor ?? Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Theme.Px(14, dpi)))
            using (var b = new SolidBrush(Theme.Panel))
                g.FillPath(b, path);
            using (var accent = new SolidBrush(Theme.Accent))
                g.FillRectangle(accent, Theme.Px(new Rectangle(14, 15, 3, 18), dpi));
            TextRenderer.DrawText(g, title, Theme.Regular(10.5f, FontStyle.Regular, dpi), new Point(Theme.Px(22, dpi), Theme.Px(13, dpi)), Theme.Text);
        }
    }

    // Column title with the blue bar of the blocks.
    sealed class TitleLabel : Control
    {
        public TitleLabel(string text)
        {
            Text = text;
            Height = 34;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            int dpi = Theme.DpiOf(this);
            e.Graphics.Clear(BackColor);
            using (var accent = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(accent, Theme.Px(new Rectangle(0, 2, 3, 18), dpi));
            TextRenderer.DrawText(e.Graphics, Text, Theme.Regular(10.5f, FontStyle.Regular, dpi), new Point(Theme.Px(8, dpi), 0), Theme.Text);
        }
    }

    // Hand-drawn check box (the Windows one stays white on a dark background).
    sealed class DarkCheck : CheckBox
    {
        public DarkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }
        public override Size GetPreferredSize(Size proposed)
        {
            int dpi = Theme.DpiOf(this);
            var t = TextRenderer.MeasureText(Text, Font);
            return new Size(t.Width + Theme.Px(32, dpi), Math.Max(t.Height, Theme.Px(18, dpi)) + Theme.Px(8, dpi));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int dpi = Theme.DpiOf(this);
            g.Clear(Parent?.BackColor ?? Theme.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int s = Theme.Px(18, dpi);
            var box = new Rectangle(1, (Height - s) / 2, s, s);
            using (var path = Theme.Rounded(box, Theme.Px(6, dpi)))
            {
                using (var fill = new SolidBrush(Checked ? Theme.Accent : Theme.Field)) g.FillPath(fill, path);
                using (var pen = new Pen(Checked ? Theme.Accent : Theme.Border)) g.DrawPath(pen, path);
            }
            if (Checked)
                using (var pen = new Pen(Color.White, 2f * dpi / 96f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, new[] {
                        new Point(box.X + Theme.Px(4, dpi), box.Y + Theme.Px(9, dpi)),
                        new Point(box.X + Theme.Px(8, dpi), box.Y + Theme.Px(13, dpi)),
                        new Point(box.X + Theme.Px(14, dpi), box.Y + Theme.Px(5, dpi)) });
            int tx = Theme.Px(28, dpi);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, Width - tx, Height), Enabled ? ForeColor : Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // Dark tray menu.
    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        sealed class Colors : ProfessionalColorTable
        {
            static readonly Color Hover = Color.FromArgb(0, 110, 190);
            public override Color ToolStripDropDownBackground => Theme.Panel;
            public override Color ImageMarginGradientBegin => Theme.Panel;
            public override Color ImageMarginGradientMiddle => Theme.Panel;
            public override Color ImageMarginGradientEnd => Theme.Panel;
            public override Color MenuBorder => Theme.Border;
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color MenuItemPressedGradientBegin => Theme.Field;
            public override Color MenuItemPressedGradientMiddle => Theme.Field;
            public override Color MenuItemPressedGradientEnd => Theme.Field;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Theme.Panel;
            public override Color CheckBackground => Theme.Accent;
            public override Color CheckSelectedBackground => Theme.Accent;
            public override Color CheckPressedBackground => Theme.Accent;
        }

        public DarkMenuRenderer() : base(new Colors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = Rectangle.Inflate(e.ImageRectangle, -1, -1);
            using (var path = Theme.Rounded(r, r.Height / 3f))
            using (var b = new SolidBrush(Theme.Accent))
                g.FillPath(b, path);
            float u = r.Width / 16f;
            using var pen = new Pen(Color.White, Math.Max(1.5f, 2f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, new[] {
                new PointF(r.X + 4 * u, r.Y + 8.5f * u),
                new PointF(r.X + 7 * u, r.Y + 11.5f * u),
                new PointF(r.X + 12 * u, r.Y + 5 * u) });
        }
    }
}
