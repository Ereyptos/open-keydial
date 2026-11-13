// Open Keydial — an open-source Windows driver for the Huion Keydial Mini (K20) over Bluetooth LE.
// Copyright (c) 2026 Ereyptos. MIT License (see LICENSE). Not affiliated with or endorsed by Huion.
//
// The Keydial exposes a proprietary GATT service (0xFFE0):
//   FFE1 (notify)          data frames "55 54 <type> ... <checksum>"
//                          type E0 = state of the 19 buttons (bytes 5..7, one bit per button)
//                          type F1 = one dial step (byte 6: 1 = one way, 2 = the other)
//   FFE2 (write / notify)  command channel ("CD <cmd> 00 00 00 00 00 00")
// Open Keydial subscribes to these frames and turns them into keyboard / mouse input,
// depending on the application in the foreground. No system driver: only the Windows
// Bluetooth LE and SendInput APIs. See docs/protocol.md.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Open Keydial")]
[assembly: AssemblyProduct("Open Keydial")]
[assembly: AssemblyDescription("Open-source Windows driver for the Huion Keydial Mini (K20)")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Ereyptos. MIT License.")]
[assembly: AssemblyCompany("Open Keydial contributors")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace OpenKeydial
{
    static class AppInfo
    {
        public const string Name = "Open Keydial";
        public const string Id = "OpenKeydial";                       // folders, files, mutex, events
        public const string Url = "https://github.com/Ereyptos/open-keydial";
        public static Version Version => Assembly.GetExecutingAssembly().GetName().Version;
        public static string VersionText => Version.ToString(3);
    }

    static class AppPaths
    {
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Id);
        public static string ConfigOverride;   // previews only
        public static string ConfigFile => ConfigOverride ?? Path.Combine(DataDir, AppInfo.Id + ".ini");
        public static string LogFile => Path.Combine(DataDir, AppInfo.Id + ".log");
        public static string StartupLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppInfo.Name + ".lnk");
        public static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");
        public static string CurrentExe => Application.ExecutablePath;

        // Short form for display: %LOCALAPPDATA%\Programs\OpenKeydial rather than C:\Users\<name>\AppData\Local\...
        public static string Friendly(string path)
        {
            foreach (var (folder, name) in new[] {
                (Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%"),
                (Environment.SpecialFolder.ApplicationData, "%APPDATA%"),
                (Environment.SpecialFolder.UserProfile, "%USERPROFILE%") })
            {
                string f = Environment.GetFolderPath(folder).TrimEnd('\\');
                if (f.Length > 0 && path.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase)) return name + path.Substring(f.Length);
            }
            return path;
        }

        public static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
    }

    static class Log
    {
        static readonly object Lock = new object();
        public static string FilePath;
        public static void W(string s)
        {
            lock (Lock)
            {
                try
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 2_000_000)
                    {
                        string old = FilePath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ", CultureInfo.InvariantCulture) + s + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }
        public static string Hex(byte[] b) => b == null ? "" : BitConverter.ToString(b).Replace("-", " ");
    }

    // Named events through which a second launch (or the uninstaller) talks to the running instance.
    static class Signals
    {
        public const string Show = "Show", Quit = "Quit";
        static string MutexName => @"Local\" + AppInfo.Id;

        public static Mutex TakeInstanceLock(out bool first) => new Mutex(true, MutexName, out first);
        public static EventWaitHandle Open(string name) => new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + AppInfo.Id + "." + name);
        public static void Send(string name) { using (var e = Open(name)) e.Set(); }

        public static bool IsRunning()
        {
            try { using (Mutex.OpenExisting(MutexName)) return true; }
            catch { return false; }
        }

        public static bool WaitForExit(int ms)
        {
            var until = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < until)
            {
                if (!IsRunning()) return true;
                Thread.Sleep(100);
            }
            return !IsRunning();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Theme.EnableDpiAwareness();   // before any window: crisp text at every display scale
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            // Developer helpers (see build.cmd): write the icon file or render previews, without starting the driver.
            if (cmd == "--make-icon" && args.Length > 1) { AppIcon.SaveIco(args[1]); return; }
            if (cmd.StartsWith("--preview") && args.Length > 1) { Previews.Run(cmd, args); return; }

            Directory.CreateDirectory(AppPaths.DataDir);
            Log.FilePath = AppPaths.LogFile;
            if (File.Exists(AppPaths.ConfigFile)) L.Set(Config.ReadLanguage(AppPaths.ConfigFile));

            using var instance = Signals.TakeInstanceLock(out bool first);
            if (!first) { Signals.Send(Signals.Show); return; }   // already running: open its settings window

            // First launch: welcome window (language, start with Windows, Start menu). Closed = nothing changed.
            if (!File.Exists(AppPaths.ConfigFile) && !Setup.FirstRun()) return;

            Log.W("===== " + AppInfo.Name + " " + AppInfo.VersionText + " started (" + AppPaths.CurrentExe + ")");
            Setup.RepairShortcuts();
            Application.ThreadException += (s, e) => Log.W("ERROR " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.W("FATAL ERROR " + e.ExceptionObject);
            using var ctx = new TrayApp();
            Application.Run(ctx);
        }
    }
}
