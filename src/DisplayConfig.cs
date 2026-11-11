using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Brighter
{
    /// <summary>One active display output as reported by the Windows display configuration API.</summary>
    internal sealed class DisplayTarget
    {
        public string GdiName = "";
        public string FriendlyName = "";
        public string DevicePath = "";
        public uint Technology = 0xFFFFFFFF;
        public int Width;
        public int Height;
        public double RefreshHz;

        /// <summary>True for laptop / all-in-one panels (LVDS, embedded DisplayPort, ...).</summary>
        public bool IsInternal
        {
            get
            {
                return Technology == 6 || Technology == 11 || Technology == 13 || Technology == 0x80000000;
            }
        }

        /// <summary>Human readable connector name, empty when unknown.</summary>
        public string ConnectionName
        {
            get
            {
                switch (Technology)
                {
                    case 0: return "VGA";
                    case 1: return "S-Video";
                    case 2: return "Composite";
                    case 3: return "Component";
                    case 4: return "DVI";
                    case 5: return "HDMI";
                    case 6: return L.Internal;
                    case 9: return "SDI";
                    case 10: return "DisplayPort";
                    case 11: return L.Internal;
                    case 12: return "UDI";
                    case 13: return L.Internal;
                    case 15: return "Miracast";
                    case 16: return "USB";
                    case 17: return L.Virtual;
                    case 18: return "USB-C / DP";
                    case 0x80000000: return L.Internal;
                    default: return "";
                }
            }
        }
    }

    internal static class DisplayConfig
    {
        /// <summary>Returns every active display path. Never throws; returns an empty list on failure.</summary>
        public static List<DisplayTarget> Query()
        {
            List<DisplayTarget> list = new List<DisplayTarget>();
            IntPtr paths = IntPtr.Zero;
            IntPtr modes = IntPtr.Zero;
            try
            {
                uint pathCount;
                uint modeCount;
                if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != 0)
                    return list;
                if (pathCount == 0)
                    return list;

                paths = Marshal.AllocHGlobal((int)pathCount * NativeMethods.SizeOfPathInfo);
                modes = Marshal.AllocHGlobal((int)Math.Max(1u, modeCount) * NativeMethods.SizeOfModeInfo);

                if (NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                    return list;

                for (int i = 0; i < (int)pathCount; i++)
                {
                    IntPtr p = IntPtr.Add(paths, i * NativeMethods.SizeOfPathInfo);

                    // DISPLAYCONFIG_PATH_SOURCE_INFO starts at offset 0
                    NativeMethods.LUID sourceAdapter;
                    sourceAdapter.LowPart = (uint)Marshal.ReadInt32(p, 0);
                    sourceAdapter.HighPart = Marshal.ReadInt32(p, 4);
                    uint sourceId = (uint)Marshal.ReadInt32(p, 8);
                    uint sourceModeIndex = (uint)Marshal.ReadInt32(p, 12);

                    // DISPLAYCONFIG_PATH_TARGET_INFO starts at offset 20
                    NativeMethods.LUID targetAdapter;
                    targetAdapter.LowPart = (uint)Marshal.ReadInt32(p, 20);
                    targetAdapter.HighPart = Marshal.ReadInt32(p, 24);
                    uint targetId = (uint)Marshal.ReadInt32(p, 28);
                    uint technology = (uint)Marshal.ReadInt32(p, 36);
                    uint refreshNumerator = (uint)Marshal.ReadInt32(p, 48);
                    uint refreshDenominator = (uint)Marshal.ReadInt32(p, 52);

                    DisplayTarget target = new DisplayTarget();
                    target.Technology = technology;
                    if (refreshDenominator != 0)
                        target.RefreshHz = (double)refreshNumerator / refreshDenominator;

                    NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME source = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    source.header.type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    source.header.size = (uint)Marshal.SizeOf(typeof(NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                    source.header.adapterId = sourceAdapter;
                    source.header.id = sourceId;
                    if (NativeMethods.DisplayConfigGetDeviceInfo(ref source) == 0)
                        target.GdiName = source.viewGdiDeviceName ?? "";

                    NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME name = new NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    name.header.type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    name.header.size = (uint)Marshal.SizeOf(typeof(NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME));
                    name.header.adapterId = targetAdapter;
                    name.header.id = targetId;
                    if (NativeMethods.DisplayConfigGetDeviceInfo(ref name) == 0)
                    {
                        target.FriendlyName = (name.monitorFriendlyDeviceName ?? "").Trim();
                        target.DevicePath = (name.monitorDevicePath ?? "").Trim();
                    }

                    // Source mode (infoType == 1) carries the desktop resolution of this output.
                    if (sourceModeIndex < modeCount)
                    {
                        IntPtr m = IntPtr.Add(modes, (int)sourceModeIndex * NativeMethods.SizeOfModeInfo);
                        if (Marshal.ReadInt32(m, 0) == 1)
                        {
                            target.Width = Marshal.ReadInt32(m, 16);
                            target.Height = Marshal.ReadInt32(m, 20);
                        }
                    }

                    list.Add(target);
                }
            }
            catch (Exception)
            {
                // Display config is only cosmetic (names / connector badges); ignore failures.
            }
            finally
            {
                if (paths != IntPtr.Zero) Marshal.FreeHGlobal(paths);
                if (modes != IntPtr.Zero) Marshal.FreeHGlobal(modes);
            }
            return list;
        }
    }
}
