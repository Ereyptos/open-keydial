// Open Keydial — the icon: a Keydial seen from above (dial over a grid of keys), drawn in code
// so that it is sharp at every size. Blue dial = active, grey dial = asleep / not found.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenKeydial
{
    static class AppIcon
    {
        public static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

        static readonly Color BodyFill = Color.FromArgb(45, 45, 50);
        static readonly Color BodyEdge = Color.FromArgb(210, 210, 218);
        static readonly Color KeyFill = Color.FromArgb(188, 188, 196);
        static readonly Color DialFill = Color.FromArgb(69, 69, 75);
        static readonly Color DialIdle = Color.FromArgb(140, 140, 148);

        public static Bitmap Draw(int s, bool active)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float u = s / 32f; // drawn on a 32 x 32 grid
                RectangleF R(float x, float y, float w, float h) => new RectangleF(x * u, y * u, w * u, h * u);

                // Body, with a light edge so that it shows on dark and light taskbars.
                float edge = Math.Max(1f, 1.3f * u);
                var body = R(6, 1, 20, 30);
                body.Inflate(-edge / 2, -edge / 2);
                using (var path = Theme.Rounded(body, 6.5f * u))
                {
                    using (var b = new SolidBrush(BodyFill)) g.FillPath(b, path);
                    using (var p = new Pen(BodyEdge, edge)) g.DrawPath(p, path);
                }

                // Dial.
                bool detailed = s >= 40;
                float dr = detailed ? 4.4f : 5f;
                var dial = R(16 - dr, 4f, 2 * dr, 2 * dr);
                float ring = Math.Max(1.2f, 1.8f * u);
                using (var b = new SolidBrush(DialFill)) g.FillEllipse(b, dial);
                using (var p = new Pen(active ? Theme.Accent : DialIdle, ring)) g.DrawEllipse(p, RectangleF.Inflate(dial, -ring / 2, -ring / 2));

                // Keys: the more pixels, the closer to the real 4-column layout.
                int cols = detailed ? 4 : 3, rows = detailed ? 4 : s >= 32 ? 3 : 2;
                float left = 9.3f, right = 22.7f, top = 4f + 2 * dr + 2.4f, bottom = 27.6f;
                float gap = detailed ? 1.1f : s >= 32 ? 1.4f : 1.9f;
                float kw = (right - left - (cols - 1) * gap) / cols, kh = (bottom - top - (rows - 1) * gap) / rows;
                using (var kb = new SolidBrush(KeyFill))
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            var k = R(left + c * (kw + gap), top + r * (kh + gap), kw, kh);
                            float rad = Math.Min(k.Width, k.Height) * 0.5f;
                            if (rad < 1.2f) g.FillRectangle(kb, k);
                            else using (var path = Theme.Rounded(k, rad)) g.FillPath(kb, path);
                        }
            }
            return bmp;
        }

        // .ico file: classic 32-bit DIB frames, PNG for 256 px.
        public static byte[] IcoBytes(int[] sizes, bool active)
        {
            var frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (var bmp = Draw(sizes[i], active))
                    frames[i] = sizes[i] >= 256 ? Png(bmp) : Dib(bmp);
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write(frames[i].Length); w.Write(offset);
                offset += frames[i].Length;
            }
            foreach (var f in frames) w.Write(f);
            w.Flush();
            return ms.ToArray();
        }

        static byte[] Png(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        static byte[] Dib(Bitmap bmp)
        {
            int s = bmp.Width;
            var data = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new byte[s * s * 4];
            try { for (int y = 0; y < s; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * s * 4, s * 4); }
            finally { bmp.UnlockBits(data); }
            int maskStride = ((s + 31) / 32) * 4;
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(40); w.Write(s); w.Write(s * 2); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(0); w.Write(s * s * 4 + maskStride * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = s - 1; y >= 0; y--) w.Write(px, y * s * 4, s * 4);     // bottom-up BGRA
            for (int y = s - 1; y >= 0; y--)                                    // AND mask: 1 = transparent
            {
                var row = new byte[maskStride];
                for (int x = 0; x < s; x++)
                    if (px[(y * s + x) * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                w.Write(row);
            }
            w.Flush();
            return ms.ToArray();
        }

        public static void SaveIco(string path) => File.WriteAllBytes(path, IcoBytes(Sizes, true));

        // Window icon (all sizes) and tray icon (the exact small-icon size of the display).
        public static Icon ForWindows() => new Icon(new MemoryStream(IcoBytes(Sizes, true)));

        public static Icon ForTray(bool active)
        {
            int s = SystemInformation.SmallIconSize.Width;
            return new Icon(new MemoryStream(IcoBytes(new[] { s }, active)), s, s);
        }

        // Preview sheet: every size, active and idle, on a dark and a light background.
        public static void SavePreviewSheet(string png)
        {
            int pad = 12, w = pad, h = 256 + 2 * pad;
            foreach (var s in Sizes) w += s + pad;
            using var sheet = new Bitmap(w, h * 4);
            using var g = Graphics.FromImage(sheet);
            var backs = new[] { Color.FromArgb(32, 32, 32), Color.FromArgb(32, 32, 32), Color.FromArgb(243, 243, 243), Color.FromArgb(243, 243, 243) };
            for (int band = 0; band < 4; band++)
            {
                using (var b = new SolidBrush(backs[band])) g.FillRectangle(b, 0, band * h, w, h);
                int x = pad;
                foreach (var s in Sizes)
                {
                    using (var ic = Draw(s, band % 2 == 0)) g.DrawImageUnscaled(ic, x, band * h + h - pad - s);
                    x += s + pad;
                }
            }
            sheet.Save(png, ImageFormat.Png);
        }
    }
}
