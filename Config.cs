using System;
using Microsoft.Win32;

namespace PowerTray
{
    // Settings live in the registry rather than a file next to the exe. People download
    // a single binary to Downloads and move it later; a config file would be orphaned by
    // that, and a folder in %APPDATA% would break the "one file, no footprint" promise
    // that is the whole reason to prefer this over Control Panel.
    static class Config
    {
        const string Root = @"Software\PowerTray";

        static RegistryKey Open(bool write)
        {
            return write ? Registry.CurrentUser.CreateSubKey(Root)
                         : Registry.CurrentUser.OpenSubKey(Root);
        }

        static string GetString(string name, string fallback)
        {
            try
            {
                using (var key = Open(false))
                    return key == null ? fallback : (key.GetValue(name) as string) ?? fallback;
            }
            catch { return fallback; }
        }

        static void SetString(string name, string value)
        {
            try
            {
                using (var key = Open(true))
                {
                    if (key == null) return;
                    if (value == null) key.DeleteValue(name, false);
                    else key.SetValue(name, value);
                }
            }
            catch { }
        }

        static bool GetBool(string name, bool fallback)
        {
            string raw = GetString(name, null);
            if (raw == null) return fallback;
            return raw == "1";
        }

        static void SetBool(string name, bool value) { SetString(name, value ? "1" : "0"); }

        // ---- preferences ----

        public static bool CycleHotkeyEnabled
        {
            get { return GetBool("CycleHotkey", true); }
            set { SetBool("CycleHotkey", value); }
        }

        public static bool CheckForUpdates
        {
            get { return GetBool("CheckForUpdates", true); }
            set { SetBool("CheckForUpdates", value); }
        }

        public static DateTime LastUpdateCheck
        {
            get
            {
                long ticks;
                if (long.TryParse(GetString("LastUpdateCheck", null), out ticks))
                {
                    try { return new DateTime(ticks, DateTimeKind.Utc); }
                    catch { return DateTime.MinValue; }
                }
                return DateTime.MinValue;
            }
            set { SetString("LastUpdateCheck", value.ToUniversalTime().Ticks.ToString()); }
        }

        // Remembers which version was already announced by balloon, so the popup fires
        // once per release rather than at every login until the user updates.
        public static string AnnouncedVersion
        {
            get { return GetString("AnnouncedVersion", ""); }
            set { SetString("AnnouncedVersion", value); }
        }

        // ---- per-target hotkeys ----
        // Stored as "modifiers:virtualkey", e.g. "3:49" for Ctrl+Alt+1.

        public static string GetHotkey(string targetKey)
        {
            return GetString("Hotkey_" + targetKey, null);
        }

        public static void SetHotkey(string targetKey, string encoded)
        {
            SetString("Hotkey_" + targetKey, encoded);
        }

        public static void ClearHotkey(string targetKey)
        {
            SetString("Hotkey_" + targetKey, null);
        }
    }
}
