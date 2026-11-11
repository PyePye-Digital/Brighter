using System;
using System.Management;

namespace Brighter
{
    /// <summary>
    /// Brightness for built-in panels (laptops, all-in-ones). These do not speak DDC/CI,
    /// Windows exposes them through WMI instead.
    /// </summary>
    internal static class WmiBrightness
    {
        public static bool TryGet(out int percent)
        {
            percent = 0;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE"))
                using (ManagementObjectCollection items = searcher.Get())
                {
                    foreach (ManagementBaseObject item in items)
                    {
                        percent = Convert.ToInt32(item["CurrentBrightness"]);
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // No WMI brightness support (typical on desktop PCs).
            }
            return false;
        }

        public static bool Set(int percent)
        {
            bool ok = false;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE"))
                using (ManagementObjectCollection items = searcher.Get())
                {
                    foreach (ManagementBaseObject item in items)
                    {
                        ManagementObject target = item as ManagementObject;
                        if (target == null) continue;
                        target.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                        ok = true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return ok;
        }
    }
}
