// Open Keydial — developer previews: render the windows and the icon to PNG files
// without starting the driver (used to check layouts at several display scales).
//
//   OpenKeydial.exe --preview-config  out.png [key|-] [dpi] [en|fr] [settings.ini]
//   OpenKeydial.exe --preview-keys    out.png [app.exe|-] [dpi] [en|fr] [settings.ini]
//   OpenKeydial.exe --preview-welcome out.png [-] [dpi] [en|fr]
//   OpenKeydial.exe --preview-icon    out.png

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace OpenKeydial
{
    static class Previews
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        public static void Run(string cmd, string[] args)
        {
            string png = args[1];
            string arg = args.Length > 2 && args[2] != "-" ? args[2] : null;
            int dpi = args.Length > 3 && int.TryParse(args[3], out var d) ? d : Theme.DpiAtCursor();
            if (args.Length > 5) AppPaths.ConfigOverride = Path.GetFullPath(args[5]);
            else if (!File.Exists(AppPaths.ConfigFile))
            {
                AppPaths.ConfigOverride = Path.Combine(Path.GetTempPath(), "OpenKeydial-preview.ini");
                File.WriteAllText(AppPaths.ConfigOverride, Config.DefaultText(args.Length > 4 ? args[4] : "en"));
            }
            L.Set(Config.ReadLanguage(AppPaths.ConfigFile));
            if (args.Length > 4) L.Set(args[4]);

            switch (cmd)
            {
                case "--preview-icon":
                    AppIcon.SavePreviewSheet(png);
                    break;
                case "--preview-keys":
                    var cfg = Config.Load(AppPaths.ConfigFile);
                    using (var ov = new KeyOverlay())
                    {
                        KeyOverlay.Fill(ov, cfg, arg?.ToLowerInvariant(), cfg.DialModes.FirstOrDefault() ?? "");
                        ov.SavePreview(png, dpi);
                    }
                    break;
                case "--preview-config":
                    using (var f = new ConfigForm(null, dpi))
                    {
                        if (arg != null) f.SelectFromDevice(arg);
                        Capture(f, png);
                    }
                    break;
                case "--preview-welcome":
                    using (var w = new WelcomeForm(dpi)) Capture(w, png);
                    break;
            }
        }

        static void Capture(Form f, string png)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-30000, -30000);
            f.ShowInTaskbar = false;
            f.Show();
            for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(25); }
            using var bmp = new Bitmap(f.Width, f.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                // Faithful capture (themed title bar and controls included); DrawToBitmap as a fallback.
                IntPtr hdc = g.GetHdc();
                bool ok = PrintWindow(f.Handle, hdc, 2);
                g.ReleaseHdc(hdc);
                if (!ok) f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            }
            bmp.Save(png, ImageFormat.Png);
        }
    }
}
