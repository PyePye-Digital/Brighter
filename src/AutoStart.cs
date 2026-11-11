using System;
using System.Reflection;
using Microsoft.Win32;

namespace Brighter
{
    /// <summary>"Start with Windows" via the current user's Run key (no admin rights needed).</summary>
    internal static class AutoStart
    {
        private const string KeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName = "Brighter";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                    {
                        return key != null && key.GetValue(ValueName) != null;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (key == null) return;
                    if (enabled)
                        key.SetValue(ValueName, "\"" + Assembly.GetExecutingAssembly().Location + "\" --tray");
                    else
                        key.DeleteValue(ValueName, false);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
