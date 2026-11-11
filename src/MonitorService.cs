using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Brighter
{
    /// <summary>Finds every connected monitor and works out how each one can be dimmed.</summary>
    internal static class MonitorService
    {
        /// <summary>Serialises all DDC/CI traffic; some GPU drivers dislike parallel I2C access.</summary>
        public static readonly object DdcLock = new object();

        /// <summary>VESA MCCS VCP code for luminance.</summary>
        public const byte VcpBrightness = 0x10;
        public const byte VcpContrast = 0x12;
        public const byte VcpInputSource = 0x60;
        public const byte VcpVolume = 0x62;
        public const byte VcpPowerMode = 0xD6;

        public static List<MonitorDevice> Enumerate()
        {
            List<DisplayTarget> targets = DisplayConfig.Query();

            List<IntPtr> monitorHandles = new List<IntPtr>();
            NativeMethods.MonitorEnumProc callback = delegate(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data)
            {
                monitorHandles.Add(hMonitor);
                return true;
            };
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);

            int wmiPercent;
            bool wmiAvailable = WmiBrightness.TryGet(out wmiPercent);
            bool wmiUsed = false;

            List<MonitorDevice> result = new List<MonitorDevice>();

            foreach (IntPtr hMonitor in monitorHandles)
            {
                string gdiName = "";
                bool primary = false;
                NativeMethods.MONITORINFOEX info = new NativeMethods.MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    gdiName = info.szDevice ?? "";
                    primary = (info.dwFlags & 1) != 0;
                }

                List<DisplayTarget> mine = new List<DisplayTarget>();
                foreach (DisplayTarget t in targets)
                {
                    if (string.Equals(t.GdiName, gdiName, StringComparison.OrdinalIgnoreCase))
                        mine.Add(t);
                }

                NativeMethods.PHYSICAL_MONITOR[] physical = GetPhysicalMonitors(hMonitor);
                int count = Math.Max(1, Math.Max(physical.Length, mine.Count));

                for (int i = 0; i < count; i++)
                {
                    DisplayTarget target = i < mine.Count ? mine[i] : (mine.Count > 0 ? mine[mine.Count - 1] : null);
                    IntPtr handle = i < physical.Length ? physical[i].hPhysicalMonitor : IntPtr.Zero;
                    string description = i < physical.Length ? (physical[i].szPhysicalMonitorDescription ?? "") : "";

                    ControlMethod method = ControlMethod.None;
                    int percent = 0;
                    uint rawMax = 100;
                    bool hasContrast = false;
                    int contrast = 0;
                    uint contrastMax = 100;
                    bool hasVolume = false;
                    int volume = 0;
                    uint volumeMax = 100;

                    bool isInternal = target != null && target.IsInternal;

                    // Built-in panels: WMI first.
                    if (isInternal && wmiAvailable && !wmiUsed)
                    {
                        method = ControlMethod.Wmi;
                        percent = wmiPercent;
                        wmiUsed = true;
                    }

                    // External monitors: DDC/CI.
                    if (method == ControlMethod.None && handle != IntPtr.Zero)
                    {
                        uint current;
                        uint max;
                        if (TryReadVcp(handle, VcpBrightness, 6, out current, out max))
                        {
                            method = ControlMethod.Ddc;
                            rawMax = max;
                            percent = (int)Math.Round(current * 100.0 / max);

                            uint contrastCurrent;
                            if (TryReadVcp(handle, VcpContrast, 2, out contrastCurrent, out contrastMax))
                            {
                                hasContrast = true;
                                contrast = (int)Math.Round(contrastCurrent * 100.0 / contrastMax);
                            }
                            else
                            {
                                contrastMax = 100;
                            }

                            uint volumeCurrent;
                            if (TryReadVcp(handle, VcpVolume, 1, out volumeCurrent, out volumeMax))
                            {
                                hasVolume = true;
                                volume = (int)Math.Round(volumeCurrent * 100.0 / volumeMax);
                            }
                            else
                            {
                                volumeMax = 100;
                            }
                        }
                    }

                    // Unknown output type that ignores DDC but WMI is there: assume it is the built-in panel.
                    if (method == ControlMethod.None && target == null && wmiAvailable && !wmiUsed)
                    {
                        method = ControlMethod.Wmi;
                        percent = wmiPercent;
                        wmiUsed = true;
                    }

                    // Nothing answered: fall back to a dimming overlay drawn by this app.
                    if (method == ControlMethod.None)
                    {
                        method = ControlMethod.Software;
                        percent = 100;
                    }

                    if (method != ControlMethod.Ddc && handle != IntPtr.Zero)
                    {
                        lock (DdcLock)
                        {
                            NativeMethods.DestroyPhysicalMonitor(handle);
                        }
                        handle = IntPtr.Zero;
                    }

                    MonitorDevice device = new MonitorDevice(handle, percent);
                    device.Method = method;
                    device.RawMax = rawMax;
                    device.HasContrast = hasContrast;
                    device.Contrast = contrast;
                    device.ContrastRawMax = contrastMax;
                    device.HasVolume = hasVolume;
                    device.Volume = volume;
                    device.VolumeRawMax = volumeMax;
                    device.Bounds = info.rcMonitor;
                    device.HMonitor = hMonitor;
                    device.GdiName = gdiName;
                    device.IsPrimary = primary;
                    device.Name = PickName(target, description, isInternal, gdiName);
                    device.Connection = target != null ? target.ConnectionName : "";
                    device.Detail = BuildDetail(target, primary, method);
                    device.Id = target != null && !string.IsNullOrEmpty(target.DevicePath)
                        ? target.DevicePath
                        : device.Name + "|" + gdiName + "|" + i;
                    result.Add(device);
                }
            }

            result.Sort(delegate(MonitorDevice a, MonitorDevice b)
            {
                int byNumber = DisplayNumber(a.GdiName).CompareTo(DisplayNumber(b.GdiName));
                if (byNumber != 0) return byNumber;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < result.Count; i++)
                result[i].Index = i + 1;

            return result;
        }

        private static NativeMethods.PHYSICAL_MONITOR[] GetPhysicalMonitors(IntPtr hMonitor)
        {
            try
            {
                uint count;
                if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count) || count == 0)
                    return new NativeMethods.PHYSICAL_MONITOR[0];

                NativeMethods.PHYSICAL_MONITOR[] array = new NativeMethods.PHYSICAL_MONITOR[count];
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, array))
                    return new NativeMethods.PHYSICAL_MONITOR[0];
                return array;
            }
            catch (Exception)
            {
                return new NativeMethods.PHYSICAL_MONITOR[0];
            }
        }

        public static bool TryReadVcp(IntPtr handle, byte code, int attempts, out uint current, out uint max)
        {
            current = 0;
            max = 0;
            // DDC/CI replies are flaky on some monitors; a couple of retries makes detection reliable.
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                uint codeType;
                bool ok;
                lock (DdcLock)
                {
                    ok = NativeMethods.GetVCPFeatureAndVCPFeatureReply(handle, code, out codeType, out current, out max);
                }
                if (ok && max > 0)
                {
                    if (current > max) current = max;
                    return true;
                }
                Thread.Sleep(80);
            }
            return false;
        }

        private static string PickName(DisplayTarget target, string description, bool isInternal, string gdiName)
        {
            if (target != null && !string.IsNullOrEmpty(target.FriendlyName))
                return target.FriendlyName;
            if (isInternal)
                return L.BuiltInDisplay;
            if (!string.IsNullOrEmpty(description))
                return description;
            int number = DisplayNumber(gdiName);
            return number == int.MaxValue ? L.Monitor : L.Monitor + " " + number;
        }

        private static string BuildDetail(DisplayTarget target, bool primary, ControlMethod method)
        {
            List<string> parts = new List<string>();
            if (target != null && target.Width > 0 && target.Height > 0)
                parts.Add(target.Width + " \u00D7 " + target.Height);
            if (target != null && target.RefreshHz > 1)
                parts.Add(Math.Round(target.RefreshHz) + " Hz");
            if (primary)
                parts.Add(L.Primary);
            if (method == ControlMethod.Ddc)
                parts.Add("DDC/CI");
            else if (method == ControlMethod.Wmi)
                parts.Add("WMI");
            else if (method == ControlMethod.Software)
                parts.Add(L.Software);
            return string.Join("  \u00B7  ", parts.ToArray());
        }

        /// <summary>Extracts 2 from "\\.\DISPLAY2"; int.MaxValue when there is no number.</summary>
        private static int DisplayNumber(string gdiName)
        {
            if (string.IsNullOrEmpty(gdiName)) return int.MaxValue;
            int end = gdiName.Length;
            int start = end;
            while (start > 0 && char.IsDigit(gdiName[start - 1])) start--;
            if (start == end) return int.MaxValue;
            int value;
            return int.TryParse(gdiName.Substring(start, end - start), out value) ? value : int.MaxValue;
        }
    }
}
