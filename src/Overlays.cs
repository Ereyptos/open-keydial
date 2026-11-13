// Open Keydial — on-screen displays: the dial mode bubble and the key map.
// Both are click-through windows that never take the focus, drawn at the DPI of the monitor they appear on.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenKeydial
{
    abstract class OverlayForm : Form
    {
        protected int Dpi = 96;
        protected OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Back;
            DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; return cp; } // NOACTIVATE | TOOLWINDOW | TRANSPARENT
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) { m.Result = IntPtr.Zero; return; } // WM_DPICHANGED: size is set by the code that shows it
            base.WndProc(ref m);
        }
    }

    // Small bubble showing the dial mode.
    sealed class Osd : OverlayForm
    {
        readonly Timer hide = new Timer { Interval = 1200 };
        string text = "";
        public Osd()
        {
            Opacity = 0.92;
            hide.Tick += (s, e) => { hide.Stop(); Hide(); };
        }
        public void Flash(string t)
        {
            text = t;
            var at = Cursor.Position;
            Dpi = Theme.DpiAt(at);
            var scr = Screen.FromPoint(at).WorkingArea;
            Size = new Size(Theme.Px(260, Dpi), Theme.Px(56, Dpi));
            Location = new Point(scr.Left + (scr.Width - Width) / 2, scr.Bottom - Height - Theme.Px(80, Dpi));
            if (!Visible) Show();
            Invalidate();
            hide.Stop(); hide.Start();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var accent = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(accent, 0, 0, Theme.Px(4, Dpi), Height);
            TextRenderer.DrawText(e.Graphics, text, Theme.Regular(14f, FontStyle.Regular, Dpi), ClientRectangle, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Key map, shown by the ShowKeys action (like "Show / hide functions" in Huion's driver).
    sealed class KeyOverlay : OverlayForm
    {
        const int K = 84, Gap = 8, Pad = 16, HeaderH = 132;
        const int LogicalWidth = Pad * 2 + 4 * K + 3 * Gap, LogicalHeight = HeaderH + 5 * K + 4 * Gap + Pad;
        readonly Timer hide = new Timer { Interval = 8000 };
        string title = "", mode = "", cw = "", ccw = "";
        Dictionary<string, string> labels = new Dictionary<string, string>();

        public KeyOverlay()
        {
            Opacity = 0.95;
            SetDpi(96);
            hide.Tick += (s, e) => { hide.Stop(); Hide(); };
        }

        void SetDpi(int dpi)
        {
            Dpi = dpi;
            Size = new Size(Theme.Px(LogicalWidth, dpi), Theme.Px(LogicalHeight, dpi));
        }

        // Builds the map for an application (null = Global profile).
        public static void Fill(KeyOverlay ov, Config cfg, string exe, string mode)
        {
            string profile = exe != null && cfg.ByExe.ContainsKey(exe) ? exe : "Global";
            var labels = new Dictionary<string, string>();
            for (int i = 1; i <= 18; i++) labels["T" + i] = cfg.ButtonAction(exe, "T" + i) ?? "";
            labels["Center"] = cfg.ButtonAction(exe, "Center") ?? "";
            ov.title = L.T("Profile: ", "Profil : ") + profile;
            ov.mode = mode;
            ov.cw = cfg.DialAction(exe, mode, true) ?? "";
            ov.ccw = cfg.DialAction(exe, mode, false) ?? "";
            ov.labels = labels;
        }

        public void SavePreview(string png, int dpi)
        {
            SetDpi(dpi);
            using var bmp = new Bitmap(Width, Height);
            using (var g = Graphics.FromImage(bmp)) { g.Clear(BackColor); Render(g); }
            bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        }

        public void ShowNearCursor()
        {
            var at = Cursor.Position;
            SetDpi(Theme.DpiAt(at));
            var scr = Screen.FromPoint(at).WorkingArea;
            Location = new Point(scr.Left + (scr.Width - Width) / 2, scr.Top + (scr.Height - Height) / 2);
            if (!Visible) Show();
            Invalidate();
            hide.Stop(); hide.Start();
        }

        protected override void OnPaint(PaintEventArgs e) => Render(e.Graphics);

        void Render(Graphics g)
        {
            int dpi = Dpi;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var fTitle = Theme.Regular(11f, FontStyle.Regular, dpi);   // theme fonts are shared: no Dispose
            var fKey = Theme.Regular(8.5f, FontStyle.Regular, dpi);
            var fInfo = Theme.Light(9.5f, FontStyle.Regular, dpi);
            var fSmall = Theme.Regular(7f, FontStyle.Regular, dpi);
            using var keyBrush = new SolidBrush(Theme.Key);
            using var dialPen = new Pen(Theme.Accent, 3f * dpi / 96f);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;
            var line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            // Geometry in 100 % pixels, converted to real pixels by Px().
            Rectangle Px(int x, int y, int w, int h) => Theme.Px(new Rectangle(x, y, w, h), dpi);
            const int W = LogicalWidth;

            using (var accent = new SolidBrush(Theme.Accent)) g.FillRectangle(accent, Px(Pad, 11, 3, 18));
            TextRenderer.DrawText(g, title, fTitle, Px(Pad + 9, 8, W - 2 * Pad - 9, 24), Theme.Text, line);

            var dial = Px(Pad + 4, 38, 84, 84);
            g.FillEllipse(keyBrush, dial);
            g.DrawEllipse(dialPen, dial);
            labels.TryGetValue("Center", out var center);
            TextRenderer.DrawText(g, KeyNames.Pretty(center ?? ""), fKey, Rectangle.Inflate(dial, -Theme.Px(10, dpi), -Theme.Px(10, dpi)), Color.White, flags);

            // Montserrat has no ↻ ↺ arrows: they are drawn with Segoe UI Symbol.
            int infoX = Pad + 4 + 84 + 16, infoW = W - infoX - Pad;
            const int lh = 24;
            int y0 = 40 + (84 - 3 * lh) / 2;
            TextRenderer.DrawText(g, L.T("Dial: ", "Molette : ") + mode, Theme.Regular(9.5f, FontStyle.Regular, dpi), Px(infoX, y0, infoW, lh), Theme.Text, line);
            using (var fSym = new Font("Segoe UI Symbol", 10f * dpi / 72f, GraphicsUnit.Pixel))
                for (int i = 0; i < 2; i++)
                {
                    int y = y0 + (i + 1) * lh;
                    TextRenderer.DrawText(g, i == 0 ? "↻" : "↺", fSym, Px(infoX, y, 22, lh), Theme.Accent, line);
                    TextRenderer.DrawText(g, KeyNames.Pretty(i == 0 ? cw : ccw), fInfo, Px(infoX + 22, y, infoW - 22, lh), Theme.Dim, line);
                }

            foreach (var (name, col, row, w, h) in KeypadControl.KeyLayout)
            {
                int x = Pad + col * (K + Gap), y = HeaderH + row * (K + Gap);
                var r = Px(x, y, w * K + (w - 1) * Gap, h * K + (h - 1) * Gap);
                using (var path = Theme.Rounded(r, Theme.Px(14, dpi))) g.FillPath(keyBrush, path);
                TextRenderer.DrawText(g, name, fSmall, Px(x + 7, y + 5, 44, 18), Color.Gray, TextFormatFlags.Left | TextFormatFlags.NoPadding);
                labels.TryGetValue(name, out var lab);
                string txt = KeyNames.Pretty(lab ?? "");
                TextRenderer.DrawText(g, txt, fKey, Rectangle.Inflate(r, -Theme.Px(6, dpi), -Theme.Px(8, dpi)), txt == "—" ? Color.DimGray : Color.White, flags);
            }
        }
    }
}
