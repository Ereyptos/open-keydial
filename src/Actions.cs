// Open Keydial — actions ("Ctrl+Z", "WheelUp", "DialMode:Zoom"...) and keyboard / mouse injection.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace OpenKeydial
{
    abstract class Act
    {
        public virtual void Down(TrayApp app) { }
        public virtual void Up(TrayApp app) { }
    }

    sealed class KeyAct : Act
    {
        public ushort[] Mods = new ushort[0];
        public ushort Key; // 0 = modifiers only
        public override void Down(TrayApp app)
        {
            foreach (var m in Mods) Input.Press(m);
            if (Key != 0) Input.Press(Key);
        }
        public override void Up(TrayApp app)
        {
            if (Key != 0) Input.Release(Key);
            foreach (var m in Mods.Reverse()) Input.Release(m);
        }
    }

    sealed class MouseAct : Act
    {
        public ushort[] Mods = new ushort[0];
        public string What; // wheelup, wheeldown, wheelleft, wheelright, left, right, middle, x1, x2
        public override void Down(TrayApp app)
        {
            foreach (var m in Mods) Input.Press(m);
            switch (What)
            {
                case "wheelup": Input.Wheel(120, false); break;
                case "wheeldown": Input.Wheel(-120, false); break;
                case "wheelright": Input.Wheel(120, true); break;
                case "wheelleft": Input.Wheel(-120, true); break;
                default: Input.Button(What, true); return; // modifiers are released on Up
            }
            foreach (var m in Mods.Reverse()) Input.Release(m);
        }
        public override void Up(TrayApp app)
        {
            if (What.StartsWith("wheel")) return;
            Input.Button(What, false);
            foreach (var m in Mods.Reverse()) Input.Release(m);
        }
    }

    sealed class DialModeAct : Act
    {
        public string Target; // null = next mode
        public override void Down(TrayApp app) => app.SetDialMode(Target);
    }

    sealed class ShowKeysAct : Act
    {
        public override void Down(TrayApp app) => app.ToggleKeyOverlay();
    }

    sealed class RunAct : Act
    {
        public string Command;
        public override void Down(TrayApp app)
        {
            try { Process.Start(new ProcessStartInfo(Command) { UseShellExecute = true }); }
            catch (Exception e) { Log.W("Cannot run " + Command + " (" + e.Message + ")"); }
        }
    }

    static class Actions
    {
        static readonly Dictionary<string, ushort> Names = BuildNames();
        static readonly Dictionary<string, ushort> Modifiers = new Dictionary<string, ushort>
        {
            ["ctrl"] = 0x11, ["control"] = 0x11, ["shift"] = 0x10, ["maj"] = 0x10, ["alt"] = 0x12, ["win"] = 0x5B,
            ["lctrl"] = 0xA2, ["rctrl"] = 0xA3, ["lshift"] = 0xA0, ["rshift"] = 0xA1, ["lalt"] = 0xA4, ["ralt"] = 0xA5, ["altgr"] = 0xA5,
        };

        static Dictionary<string, ushort> BuildNames()
        {
            var d = new Dictionary<string, ushort>
            {
                ["esc"] = 0x1B, ["escape"] = 0x1B, ["echap"] = 0x1B, ["tab"] = 0x09, ["enter"] = 0x0D, ["entree"] = 0x0D,
                ["space"] = 0x20, ["espace"] = 0x20, ["backspace"] = 0x08, ["del"] = 0x2E, ["delete"] = 0x2E, ["suppr"] = 0x2E,
                ["ins"] = 0x2D, ["insert"] = 0x2D, ["home"] = 0x24, ["end"] = 0x23,
                ["pgup"] = 0x21, ["pageup"] = 0x21, ["pgdn"] = 0x22, ["pagedown"] = 0x22,
                ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
                ["numplus"] = 0x6B, ["numminus"] = 0x6D, ["nummul"] = 0x6A, ["numdiv"] = 0x6F, ["numdot"] = 0x6E,
                ["oem1"] = 0xBA, ["oem2"] = 0xBF, ["oem3"] = 0xC0, ["oem4"] = 0xDB, ["oem5"] = 0xDC, ["oem6"] = 0xDD, ["oem7"] = 0xDE, ["oem8"] = 0xDF,
                ["oem102"] = 0xE2, ["oemplus"] = 0xBB, ["oemcomma"] = 0xBC, ["oemminus"] = 0xBD, ["oemperiod"] = 0xBE,
                ["printscreen"] = 0x2C, ["pause"] = 0x13, ["capslock"] = 0x14, ["menu"] = 0x5D, ["apps"] = 0x5D,
                ["volumeup"] = 0xAF, ["volumedown"] = 0xAE, ["mute"] = 0xAD, ["playpause"] = 0xB3, ["nexttrack"] = 0xB0, ["prevtrack"] = 0xB1,
            };
            for (char c = 'a'; c <= 'z'; c++) d[c.ToString()] = (ushort)char.ToUpperInvariant(c);
            for (char c = '0'; c <= '9'; c++) { d[c.ToString()] = c; d["num" + c] = (ushort)(0x60 + (c - '0')); }
            for (int i = 1; i <= 24; i++) d["f" + i] = (ushort)(0x6F + i);
            return d;
        }

        static readonly Dictionary<string, Act> Cache = new Dictionary<string, Act>();

        // Returns null for an empty action (err = null) or an invalid one (err set, in the UI language).
        public static Act Parse(string raw, out string err)
        {
            err = null;
            if (raw == null) return null;
            string s = raw.Trim();
            string n = Str.Norm(s);
            if (n.Length == 0 || n == "none") return null;
            if (Cache.TryGetValue(s, out var cached)) return cached;
            Act result = null;
            if (n == "showkeys") result = new ShowKeysAct();
            else if (n == "dialmode") result = new DialModeAct();
            else if (n.StartsWith("dialmode:")) result = new DialModeAct { Target = s.Substring(s.IndexOf(':') + 1).Trim() };
            else if (n.StartsWith("run:")) result = new RunAct { Command = s.Substring(s.IndexOf(':') + 1).Trim() };
            else
            {
                var mods = new List<ushort>();
                ushort key = 0; string mouse = null;
                var parts = s.Split('+').Select(p => p.Trim()).ToList();
                for (int i = 0; i < parts.Count; i++)
                {
                    string p = parts[i], pn = Str.Norm(p);
                    bool last = i == parts.Count - 1;
                    if (pn.Length == 0) { err = L.T("extra \"+\" (write NumPlus or OemPlus for the + key)", "« + » en trop (écris NumPlus ou OemPlus pour la touche +)"); return null; }
                    if (Modifiers.TryGetValue(pn, out var mv)) { mods.Add(mv); continue; } // alone = held while the key is held
                    if (!last) { err = L.T($"\"{p}\" is not a modifier (Ctrl, Shift, Alt, Win)", $"« {p} » n'est pas un modificateur (Ctrl, Shift, Alt, Win)"); return null; }
                    switch (pn)
                    {
                        case "wheelup": case "wheeldown": case "wheelleft": case "wheelright": mouse = pn; break;
                        case "leftclick": mouse = "left"; break;
                        case "rightclick": mouse = "right"; break;
                        case "middleclick": mouse = "middle"; break;
                        case "back": mouse = "x1"; break;
                        case "forward": mouse = "x2"; break;
                        default:
                            if (Names.TryGetValue(pn, out var vk)) key = vk;
                            else if (pn.StartsWith("vk:"))
                            {
                                string num = pn.Substring(3);
                                try { key = num.StartsWith("0x") ? Convert.ToUInt16(num.Substring(2), 16) : ushort.Parse(num); }
                                catch { err = L.T("invalid vk code", "code vk invalide"); return null; }
                            }
                            else if (p.Length == 1)
                            {
                                short r = Native.VkKeyScan(p[0]);
                                if (r == -1) { err = L.T($"character \"{p}\" not found on the keyboard layout", $"caractère « {p} » introuvable sur la disposition clavier"); return null; }
                                key = (ushort)(r & 0xFF);
                                int shift = (r >> 8) & 0xFF;
                                if ((shift & 1) != 0) mods.Add(0x10);
                                if ((shift & 2) != 0) mods.Add(0x11);
                                if ((shift & 4) != 0) mods.Add(0x12);
                            }
                            else { err = L.T($"unknown key \"{p}\"", $"touche « {p} » inconnue"); return null; }
                            break;
                    }
                }
                result = mouse != null ? new MouseAct { Mods = mods.ToArray(), What = mouse } : (Act)new KeyAct { Mods = mods.ToArray(), Key = key };
            }
            Cache[s] = result;
            return result;
        }
    }

    // Keyboard / mouse injection, with a hold count per key (two Keydial keys can hold Ctrl at once).
    static class Input
    {
        const uint KEYUP = 0x2, EXTENDED = 0x1;
        public static readonly IntPtr Tag = new IntPtr(0x4F4B4459); // "OKDY", marks our own events
        static readonly Dictionary<ushort, int> Held = new Dictionary<ushort, int>();
        static readonly HashSet<ushort> Extended = new HashSet<ushort> { 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0x6F, 0xA3, 0xA5, 0x2C };

        public static void Press(ushort vk)
        {
            Held.TryGetValue(vk, out int n);
            Held[vk] = n + 1;
            if (n == 0) Key(vk, false);
        }
        public static void Release(ushort vk)
        {
            if (!Held.TryGetValue(vk, out int n) || n == 0) return;
            Held[vk] = n - 1;
            if (n == 1) Key(vk, true);
        }
        public static void ReleaseAll()
        {
            foreach (var kv in Held.Where(k => k.Value > 0).ToList()) Key(kv.Key, true);
            Held.Clear();
        }

        static void Key(ushort vk, bool up)
        {
            var i = new Native.INPUT { type = 1 };
            i.ki.wVk = vk;
            i.ki.wScan = (ushort)Native.MapVirtualKey(vk, 0);
            i.ki.dwFlags = (up ? KEYUP : 0) | (Extended.Contains(vk) ? EXTENDED : 0);
            i.ki.dwExtraInfo = Tag;
            Native.SendInput(1, new[] { i }, Marshal.SizeOf<Native.INPUT>());
        }

        public static void Wheel(int delta, bool horizontal)
        {
            var i = new Native.INPUT { type = 0 };
            i.mi.mouseData = delta;
            i.mi.dwFlags = horizontal ? 0x1000u : 0x0800u;
            i.mi.dwExtraInfo = Tag;
            Native.SendInput(1, new[] { i }, Marshal.SizeOf<Native.INPUT>());
        }

        public static void Button(string which, bool down)
        {
            var i = new Native.INPUT { type = 0 };
            switch (which)
            {
                case "left": i.mi.dwFlags = down ? 0x0002u : 0x0004u; break;
                case "right": i.mi.dwFlags = down ? 0x0008u : 0x0010u; break;
                case "middle": i.mi.dwFlags = down ? 0x0020u : 0x0040u; break;
                case "x1": i.mi.dwFlags = down ? 0x0080u : 0x0100u; i.mi.mouseData = 1; break;
                case "x2": i.mi.dwFlags = down ? 0x0080u : 0x0100u; i.mi.mouseData = 2; break;
                default: return;
            }
            i.mi.dwExtraInfo = Tag;
            Native.SendInput(1, new[] { i }, Marshal.SizeOf<Native.INPUT>());
        }
    }
}
