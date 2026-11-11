using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Brighter
{
    // These types are public only because XmlSerializer requires it.

    /// <summary>A global shortcut: Win32 MOD_* flags plus a virtual-key code. Key 0 means "not set".</summary>
    public class HotkeyBinding
    {
        public uint Modifiers;
        public uint Key;
    }

    public class ProfileEntry
    {
        public string MonitorId = "";
        public int Brightness;
        public int Contrast = -1;
        public int Temperature = -1;
    }

    public class ProfileData
    {
        public string Name = "";
        public HotkeyBinding Hotkey = new HotkeyBinding();
        public List<ProfileEntry> Entries = new List<ProfileEntry>();
    }

    public class MonitorMemory
    {
        public string MonitorId = "";
        public int Brightness;
        public int Contrast = -1;
        public int Temperature = -1;
    }

    /// <summary>"While this program is in the foreground, use this profile."</summary>
    public class AppRule
    {
        public string Process = "";
        public string Profile = "";
    }

    public class AppSettings
    {
        public bool HotkeysEnabled = true;
        public int HotkeyStep = 10;
        public HotkeyBinding KeyUp = new HotkeyBinding { Modifiers = 3, Key = 0x26 };      // Ctrl+Alt+Up
        public HotkeyBinding KeyDown = new HotkeyBinding { Modifiers = 3, Key = 0x28 };    // Ctrl+Alt+Down
        public HotkeyBinding KeyAllUp = new HotkeyBinding { Modifiers = 7, Key = 0x26 };   // Ctrl+Alt+Shift+Up
        public HotkeyBinding KeyAllDown = new HotkeyBinding { Modifiers = 7, Key = 0x28 }; // Ctrl+Alt+Shift+Down

        public bool RestoreOnConnect = true;
        public bool RelativeMaster;

        public bool ScheduleEnabled;
        public string DayTime = "07:00";
        public string NightTime = "20:00";
        public string DayProfile = "";
        public string NightProfile = "";
        public bool UseSun;
        public string Latitude = "";
        public string Longitude = "";

        public bool IdleEnabled;
        public int IdleMinutes = 5;
        public int IdlePercent = 10;

        public List<AppRule> AppRules = new List<AppRule>();
        public List<ProfileData> Profiles = new List<ProfileData>();
        public List<MonitorMemory> Monitors = new List<MonitorMemory>();
    }

    /// <summary>Loads and saves %APPDATA%\Brighter\settings.xml.</summary>
    internal static class SettingsStore
    {
        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Brighter"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(Folder, "settings.xml"); }
        }

        public static AppSettings Load()
        {
            AppSettings settings = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                    using (FileStream stream = File.OpenRead(FilePath))
                    {
                        settings = serializer.Deserialize(stream) as AppSettings;
                    }
                }
            }
            catch (Exception)
            {
                settings = null; // corrupt file: start fresh
            }

            if (settings == null) settings = new AppSettings();
            AppSettings defaults = new AppSettings();
            if (settings.KeyUp == null) settings.KeyUp = defaults.KeyUp;
            if (settings.KeyDown == null) settings.KeyDown = defaults.KeyDown;
            if (settings.KeyAllUp == null) settings.KeyAllUp = defaults.KeyAllUp;
            if (settings.KeyAllDown == null) settings.KeyAllDown = defaults.KeyAllDown;
            if (settings.HotkeyStep < 1 || settings.HotkeyStep > 50) settings.HotkeyStep = 10;
            if (settings.IdleMinutes < 1) settings.IdleMinutes = 5;
            if (settings.IdlePercent < 0 || settings.IdlePercent > 100) settings.IdlePercent = 10;
            if (settings.Profiles == null) settings.Profiles = new List<ProfileData>();
            if (settings.Monitors == null) settings.Monitors = new List<MonitorMemory>();
            if (settings.AppRules == null) settings.AppRules = new List<AppRule>();
            if (settings.DayTime == null) settings.DayTime = "07:00";
            if (settings.NightTime == null) settings.NightTime = "20:00";
            if (settings.DayProfile == null) settings.DayProfile = "";
            if (settings.NightProfile == null) settings.NightProfile = "";
            if (settings.Latitude == null) settings.Latitude = "";
            if (settings.Longitude == null) settings.Longitude = "";
            foreach (ProfileData profile in settings.Profiles)
            {
                if (profile.Name == null) profile.Name = "";
                if (profile.Hotkey == null) profile.Hotkey = new HotkeyBinding();
                if (profile.Entries == null) profile.Entries = new List<ProfileEntry>();
            }
            foreach (AppRule rule in settings.AppRules)
            {
                if (rule.Process == null) rule.Process = "";
                if (rule.Profile == null) rule.Profile = "";
            }
            return settings;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                using (FileStream stream = File.Create(FilePath))
                {
                    serializer.Serialize(stream, settings);
                }
            }
            catch (Exception)
            {
                // Settings are a convenience; never crash because the disk said no.
            }
        }
    }
}
