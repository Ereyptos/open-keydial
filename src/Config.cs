// Open Keydial — settings file (OpenKeydial.ini) reading.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace OpenKeydial
{
    static class Str
    {
        // Lower case without accents, so that "Général" and "general" compare equal.
        public static string Norm(string s)
        {
            if (s == null) return "";
            var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }

        public static bool IsYes(string v)
        {
            string n = Norm(v);
            return n == "yes" || n == "true" || n == "1" || n == "on" || n == "oui";
        }
    }

    sealed class Ini
    {
        public readonly Dictionary<string, Dictionary<string, string>> Sections = new Dictionary<string, Dictionary<string, string>>();
        public readonly List<string> Warnings = new List<string>();

        public static Ini Parse(string[] lines)
        {
            var ini = new Ini();
            Dictionary<string, string> cur = null;
            for (int n = 0; n < lines.Length; n++)
            {
                string line = StripComment(lines[n]).Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    string name = Str.Norm(line.Substring(1, line.Length - 2));
                    if (!ini.Sections.TryGetValue(name, out cur)) ini.Sections[name] = cur = new Dictionary<string, string>();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0 || cur == null) { ini.Warnings.Add($"line {n + 1} ignored: {lines[n].Trim()}"); continue; }
                cur[Str.Norm(line.Substring(0, eq))] = line.Substring(eq + 1).Trim();
            }
            return ini;
        }

        // A comment starts with ";" at the beginning of a line, or after a space.
        public static string StripComment(string line)
        {
            for (int i = 1; i < line.Length; i++)
                if (line[i] == ';' && char.IsWhiteSpace(line[i - 1])) return line.Substring(0, i);
            return line;
        }

        public string Get(string section, string key, string def = null)
        {
            if (Sections.TryGetValue(Str.Norm(section), out var s) && s.TryGetValue(Str.Norm(key), out var v)) return v;
            return def;
        }

        public bool Flag(string section, string key, bool def) => Str.IsYes(Get(section, key, def ? "yes" : "no"));
    }

    sealed class Profile
    {
        public string Name;
        public readonly Dictionary<string, string> Entries = new Dictionary<string, string>(); // normalized key -> raw action
    }

    sealed class Config
    {
        public const string General = "General";
        public const string GlobalProfile = "Profile Global";
        public const string ProfilePrefix = "Profile ";
        public const string DialPrefix = "Dial ";
        public const string Clockwise = "Clockwise", CounterClockwise = "CounterClockwise";

        public List<string> DialModes = new List<string>();
        public Dictionary<string, (string cw, string ccw)> DialDefaults = new Dictionary<string, (string, string)>();
        public Profile Global = new Profile { Name = "Global" };
        public Dictionary<string, Profile> ByExe = new Dictionary<string, Profile>();
        public bool ReverseDial, ShowOsd = true, PauseIfHuion = true, VerboseLog;
        public string Language = "en";
        public List<byte[]> ActivationCommands = new List<byte[]>();
        public List<string> Warnings = new List<string>();

        // FFE2 commands that switch the Keydial to "driver mode" (format "CD <cmd> 00 00 00 00 00 00"):
        // C9 asks for the model name, C8 enables driver mode (the Bluetooth twin of USB string 200).
        static readonly byte[][] DefaultActivation =
        {
            new byte[] { 0xCD, 0xC9, 0, 0, 0, 0, 0, 0 },
            new byte[] { 0xCD, 0xC8, 0, 0, 0, 0, 0, 0 },
        };

        public static string ReadLanguage(string path)
        {
            try { return Ini.Parse(File.ReadAllLines(path, Encoding.UTF8)).Get(General, "Language", "en"); }
            catch { return "en"; }
        }

        public static Config Load(string path)
        {
            var ini = Ini.Parse(File.ReadAllLines(path, Encoding.UTF8));
            var c = new Config();
            c.Warnings.AddRange(ini.Warnings);
            c.Language = ini.Get(General, "Language", "en");
            c.DialModes = (ini.Get(General, "DialModes", "Zoom") ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            c.ReverseDial = Str.Norm(ini.Get(General, "DialDirection", "normal")).StartsWith("rev");
            c.ShowOsd = ini.Flag(General, "ShowDialMode", true);
            c.PauseIfHuion = ini.Flag(General, "PauseIfHuionDriver", true);
            c.VerboseLog = Str.Norm(ini.Get(General, "Log", "normal")).StartsWith("detail");
            string activation = ini.Get(General, "ActivationCommand", "") ?? "";
            if (Str.Norm(activation).Length == 0 || Str.Norm(activation) == "auto")
                c.ActivationCommands.AddRange(DefaultActivation);
            else if (Str.Norm(activation) != "none")
                foreach (var cmd in activation.Split('|'))
                {
                    var hex = cmd.Replace(" ", "").Replace("-", "");
                    if (hex.Length == 0) continue;
                    try { c.ActivationCommands.Add(Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray()); }
                    catch { c.Warnings.Add("unreadable ActivationCommand: " + cmd); }
                }
            foreach (var m in c.DialModes)
                c.DialDefaults[Str.Norm(m)] = (ini.Get(DialPrefix + m, Clockwise, ""), ini.Get(DialPrefix + m, CounterClockwise, ""));

            string prefix = Str.Norm(ProfilePrefix);
            foreach (var kv in ini.Sections)
            {
                if (!kv.Key.StartsWith(prefix)) continue;
                string target = kv.Key.Substring(prefix.Length).Trim();
                if (target == "global") { Fill(c.Global, kv.Value); continue; }
                var p = new Profile { Name = target };
                Fill(p, kv.Value);
                foreach (var exe in target.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
                    c.ByExe[exe.EndsWith(".exe") ? exe : exe + ".exe"] = p;
            }
            // Check every action now, so that typos show up in the log.
            foreach (var p in new[] { c.Global }.Concat(c.ByExe.Values).Distinct())
                foreach (var e in p.Entries)
                    if (Actions.Parse(e.Value, out string err) == null && err != null) c.Warnings.Add($"[Profile {p.Name}] {e.Key} = {e.Value}: {err}");
            foreach (var d in c.DialDefaults)
                foreach (var a in new[] { d.Value.cw, d.Value.ccw })
                    if (Actions.Parse(a, out string err) == null && err != null) c.Warnings.Add($"[Dial {d.Key}] {a}: {err}");
            return c;
        }

        static void Fill(Profile p, Dictionary<string, string> s)
        {
            foreach (var kv in s) p.Entries[kv.Key] = kv.Value;
        }

        public string ButtonAction(string exe, string button)
        {
            string key = Str.Norm(button);
            if (exe != null && ByExe.TryGetValue(exe, out var p) && p.Entries.TryGetValue(key, out var v)) return v;
            return Global.Entries.TryGetValue(key, out v) ? v : null;
        }

        public string DialAction(string exe, string mode, bool clockwise)
        {
            string key = Str.Norm(mode + "." + (clockwise ? Clockwise : CounterClockwise));
            if (exe != null && ByExe.TryGetValue(exe, out var p) && p.Entries.TryGetValue(key, out var v)) return v;
            if (Global.Entries.TryGetValue(key, out v)) return v;
            return DialDefaults.TryGetValue(Str.Norm(mode), out var d) ? (clockwise ? d.cw : d.ccw) : null;
        }

        // The default settings file, with dial mode names in the chosen language.
        public static string DefaultText(string language)
        {
            using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenKeydial.default.ini");
            using var r = new StreamReader(st, Encoding.UTF8);
            bool fr = Str.Norm(language).StartsWith("fr");
            return r.ReadToEnd()
                .Replace("{Language}", fr ? "fr" : "en")
                .Replace("{ModeZoom}", "Zoom")
                .Replace("{ModeBrush}", fr ? "Pinceau" : "Brush size")
                .Replace("{ModeHistory}", fr ? "Historique" : "History")
                .Replace("{ModeScroll}", fr ? "Défilement" : "Scroll");
        }

        // Comment block at the top of the default file, reused when the settings window saves.
        public static string DefaultHeader() =>
            string.Join("\r\n", DefaultText("en").Split('\n').Select(l => l.TrimEnd('\r')).TakeWhile(l => !l.StartsWith("[")));

        public static void CreateDefault(string path, string language)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, DefaultText(language), new UTF8Encoding(true));
            Log.W("Default settings created: " + path);
        }
    }
}
