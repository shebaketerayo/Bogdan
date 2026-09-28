using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace DesktopCat
{
    public static class SettingsStore
    {
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string StartupApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        public const string RunValueName = "DesktopCat";

        public static string DefaultName
        {
            get
            {
                string n = new CatSettings().CatName;
                return string.IsNullOrEmpty(n) ? "Котик" : n;
            }
        }

        public static string FilePath
        {
            get { return AppPaths.SettingsFile; }
        }

        public static CatSettings Load()
        {
            CatSettings s = new CatSettings();
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return s;
                string text = File.ReadAllText(path, Encoding.UTF8);
                s = Parse(text);
            }
            catch (Exception ex)
            {
                CatLog.Error("SettingsStore.Load", ex);
                s = new CatSettings();
            }
            return s;
        }

        public static CatSettings Parse(string text)
        {
            CatSettings s = new CatSettings();
            if (string.IsNullOrEmpty(text)) return s;
            Color?[] custom = new Color?[CatPalette.SlotCount];
            bool anyCustom = false;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                try
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                    if (line[0] == '﻿') line = line.Substring(1);
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "name": if (val.Length > 0) s.CatName = val; break;
                        case "palette": s.PaletteName = val; break;
                        case "size": { int v; if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) s.Size = v; } break;
                        case "play": { PlayMode pm; if (TryParsePlay(val, out pm)) s.Play = pm; } break;
                        case "alwaysontop": ParseBool(val, ref s.AlwaysOnTop); break;
                        case "walkonwindows": ParseBool(val, ref s.WalkOnWindows); break;
                        case "sleeponfolders": ParseBool(val, ref s.SleepOnFolders); break;
                        case "scratchicons": ParseBool(val, ref s.ScratchIcons); break;
                        case "hideinfullscreen": ParseBool(val, ref s.HideInFullscreen); break;
                        case "showeffects": ParseBool(val, ref s.ShowEffects); break;
                        case "autostart": ParseBool(val, ref s.Autostart); break;
                        default:
                            if (key.StartsWith("custom.", StringComparison.Ordinal))
                            {
                                string slotName = key.Substring(7);
                                PaletteSlot slot;
                                Color c;
                                if (TryParseSlot(slotName, out slot) && TryParseColor(val, out c))
                                {
                                    custom[(int)slot] = c;
                                    anyCustom = true;
                                }
                            }
                            break;
                    }
                }
                catch { }
            }
            if (anyCustom)
            {
                CatPalette baseP = AppPalette.DefaultPalette();
                CatPalette cp = new CatPalette();
                cp.Name = CatSettings.CustomPaletteName;
                for (int i = 0; i < CatPalette.SlotCount; i++)
                    cp.Colors[i] = custom[i].HasValue ? custom[i].Value : baseP.Colors[i];
                s.CustomPalette = cp;
            }
            Sanitize(s);
            return s;
        }

        public static CatSettings Sanitize(CatSettings s)
        {
            if (s == null) return new CatSettings();
            string n = (s.CatName ?? "").Trim();
            n = n.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            if (n.Length > 40) n = n.Substring(0, 40).Trim();
            if (n.Length == 0) n = DefaultName;
            s.CatName = n;
            if (s.PaletteName == null) s.PaletteName = "";
            s.PaletteName = s.PaletteName.Trim();
            if (s.Size < 1) s.Size = 1;
            if (s.Size > 6) s.Size = 6;
            if (s.Play != PlayMode.Never && s.Play != PlayMode.Sometimes && s.Play != PlayMode.Often) s.Play = PlayMode.Sometimes;
            if (s.CustomPalette != null) s.CustomPalette = AppPalette.Sanitize(s.CustomPalette, CatSettings.CustomPaletteName);
            if (s.PaletteName == CatSettings.CustomPaletteName && s.CustomPalette == null) s.PaletteName = "";
            return s;
        }

        static void ParseBool(string v, ref bool field)
        {
            string t = v.Trim().ToLowerInvariant();
            if (t == "1" || t == "true" || t == "yes" || t == "on" || t == "да") field = true;
            else if (t == "0" || t == "false" || t == "no" || t == "off" || t == "нет") field = false;
        }

        static bool TryParsePlay(string v, out PlayMode pm)
        {
            pm = PlayMode.Sometimes;
            string t = v.Trim().ToLowerInvariant();
            if (t == "never" || t == "0" || t == "никогда") { pm = PlayMode.Never; return true; }
            if (t == "sometimes" || t == "1" || t == "иногда") { pm = PlayMode.Sometimes; return true; }
            if (t == "often" || t == "2" || t == "часто") { pm = PlayMode.Often; return true; }
            return false;
        }

        static bool TryParseSlot(string name, out PaletteSlot slot)
        {
            slot = PaletteSlot.Outline;
            foreach (PaletteSlot ps in Enum.GetValues(typeof(PaletteSlot)))
            {
                if (string.Equals(ps.ToString(), name, StringComparison.OrdinalIgnoreCase)) { slot = ps; return true; }
            }
            return false;
        }

        public static bool TryParseColor(string v, out Color c)
        {
            c = Color.Black;
            if (v == null) return false;
            string t = v.Trim();
            if (t.StartsWith("#")) t = t.Substring(1);
            if (t.Length != 6) return false;
            int rgb;
            if (!int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return false;
            c = Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        public static string ColorToHex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        public static string Serialize(CatSettings s)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# DesktopCat (Котик) settings. Delete this file to restore the defaults.\r\n");
            sb.Append("name=").Append(s.CatName).Append("\r\n");
            sb.Append("palette=").Append(s.PaletteName ?? "").Append("\r\n");
            sb.Append("size=").Append(s.Size.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("play=").Append(s.Play.ToString()).Append("\r\n");
            sb.Append("alwaysOnTop=").Append(s.AlwaysOnTop ? "1" : "0").Append("\r\n");
            sb.Append("walkOnWindows=").Append(s.WalkOnWindows ? "1" : "0").Append("\r\n");
            sb.Append("sleepOnFolders=").Append(s.SleepOnFolders ? "1" : "0").Append("\r\n");
            sb.Append("scratchIcons=").Append(s.ScratchIcons ? "1" : "0").Append("\r\n");
            sb.Append("hideInFullscreen=").Append(s.HideInFullscreen ? "1" : "0").Append("\r\n");
            sb.Append("showEffects=").Append(s.ShowEffects ? "1" : "0").Append("\r\n");
            sb.Append("autostart=").Append(s.Autostart ? "1" : "0").Append("\r\n");
            if (s.CustomPalette != null)
            {
                for (int i = 0; i < CatPalette.SlotCount && i < s.CustomPalette.Colors.Length; i++)
                    sb.Append("custom.").Append(((PaletteSlot)i).ToString()).Append('=')
                      .Append(ColorToHex(s.CustomPalette.Colors[i])).Append("\r\n");
            }
            return sb.ToString();
        }

        public static bool Save(CatSettings s)
        {
            try
            {
                CatSettings c = Sanitize(s.Clone());
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Serialize(c), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, null); }
                    catch { File.Copy(tmp, path, true); File.Delete(tmp); }
                }
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                CatLog.Error("SettingsStore.Save", ex);
                return false;
            }
        }

        public static string ExePath
        {
            get
            {
                try { return System.Windows.Forms.Application.ExecutablePath; }
                catch { return System.Reflection.Assembly.GetEntryAssembly().Location; }
            }
        }

        public static string RunCommand { get { return "\"" + ExePath + "\""; } }

        public static void SyncAutostart(bool enabled)
        {
            SyncAutostart(enabled, false);
        }

        public static void SyncAutostart(bool enabled, bool userJustEnabled)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (k == null) return;
                    object cur = k.GetValue(RunValueName);
                    if (enabled)
                    {
                        string want = RunCommand;
                        string curS = cur as string;
                        if (curS == null || !string.Equals(curS, want, StringComparison.OrdinalIgnoreCase))
                        {
                            k.SetValue(RunValueName, want, RegistryValueKind.String);
                            CatLog.Write("autostart: Run value set to " + want);
                        }
                    }
                    else if (cur != null)
                    {
                        k.DeleteValue(RunValueName, false);
                        CatLog.Write("autostart: Run value removed");
                    }
                }
                // explorer keeps a "disabled in task manager" marker that overrides the run value; ours is cleared
                // when autostart is off or the user has just turned it on, a normal start respects it
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, true))
                {
                    if (a != null)
                    {
                        byte[] b = a.GetValue(RunValueName) as byte[];
                        if (b != null && (!enabled || (userJustEnabled && b.Length > 0 && (b[0] & 1) != 0)))
                            a.DeleteValue(RunValueName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                CatLog.Error("SettingsStore.SyncAutostart", ex);
            }
        }
    }
}
