using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Brighter
{
    /// <summary>
    /// Turns a display off and on from the Windows side: the output is detached from the desktop
    /// (the monitor loses its signal and goes to standby) and later attached again with the exact
    /// mode and position it had. Works for any monitor, including ones that ignore DDC/CI.
    /// </summary>
    internal static class DisplayPower
    {
        private const int DM_PELSWIDTH = 0x00080000;
        private const int DM_PELSHEIGHT = 0x00100000;

        /// <summary>Records the current mode and position of every active display.</summary>
        public static List<SavedDisplay> Capture(List<MonitorDevice> devices)
        {
            List<SavedDisplay> list = new List<SavedDisplay>();
            IntPtr buffer = Marshal.AllocHGlobal(NativeMethods.SizeOfDevMode);
            try
            {
                foreach (MonitorDevice device in devices)
                {
                    if (string.IsNullOrEmpty(device.GdiName) || Find(list, device.GdiName) != null) continue;
                    Clear(buffer);
                    if (!NativeMethods.EnumDisplaySettingsEx(device.GdiName, NativeMethods.ENUM_CURRENT_SETTINGS, buffer, 0)) continue;

                    SavedDisplay saved = new SavedDisplay();
                    saved.DeviceName = device.GdiName;
                    saved.Label = device.Name;
                    saved.Primary = device.IsPrimary;
                    saved.Mode = new byte[NativeMethods.SizeOfDevMode];
                    Marshal.Copy(buffer, saved.Mode, 0, NativeMethods.SizeOfDevMode);
                    list.Add(saved);
                }
            }
            catch (Exception error)
            {
                Log.Error("display capture", error);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return list;
        }

        /// <summary>Detaches <paramref name="target"/>. <paramref name="active"/> is a fresh capture of what is on now.</summary>
        public static bool Detach(List<SavedDisplay> active, string target)
        {
            SavedDisplay victim = Find(active, target);
            if (victim == null || active.Count < 2) return false;

            bool ok = true;
            IntPtr buffer = Marshal.AllocHGlobal(NativeMethods.SizeOfDevMode);
            try
            {
                // The primary display cannot be detached: hand that role to another one first.
                if (victim.Primary)
                {
                    SavedDisplay heir = null;
                    foreach (SavedDisplay display in active)
                    {
                        if (display != victim) { heir = display; break; }
                    }
                    int originX = BitConverter.ToInt32(heir.Mode, NativeMethods.DevModePositionXOffset);
                    int originY = BitConverter.ToInt32(heir.Mode, NativeMethods.DevModePositionYOffset);

                    List<SavedDisplay> ordered = new List<SavedDisplay>();
                    ordered.Add(heir);
                    foreach (SavedDisplay display in active)
                    {
                        if (display != heir && display != victim) ordered.Add(display);
                    }
                    foreach (SavedDisplay display in ordered)
                    {
                        Clear(buffer);
                        Marshal.WriteInt32(buffer, NativeMethods.DevModeFieldsOffset, NativeMethods.DM_POSITION);
                        Marshal.WriteInt32(buffer, NativeMethods.DevModePositionXOffset, BitConverter.ToInt32(display.Mode, NativeMethods.DevModePositionXOffset) - originX);
                        Marshal.WriteInt32(buffer, NativeMethods.DevModePositionYOffset, BitConverter.ToInt32(display.Mode, NativeMethods.DevModePositionYOffset) - originY);
                        uint flags = NativeMethods.CDS_UPDATEREGISTRY | NativeMethods.CDS_NORESET;
                        if (display == heir) flags |= NativeMethods.CDS_SET_PRIMARY;
                        ok &= Stage(display.DeviceName, buffer, flags, "move");
                    }
                }

                // Zero size at position 0,0 is how Windows is told to detach an output.
                Clear(buffer);
                Marshal.WriteInt32(buffer, NativeMethods.DevModeFieldsOffset, NativeMethods.DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT);
                ok &= Stage(victim.DeviceName, buffer, NativeMethods.CDS_UPDATEREGISTRY | NativeMethods.CDS_NORESET, "detach");
                ok &= Commit();
            }
            catch (Exception error)
            {
                Log.Error("display detach", error);
                ok = false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return ok;
        }

        /// <summary>Puts every display in the snapshot back exactly as it was captured.</summary>
        public static bool Restore(List<SavedDisplay> snapshot)
        {
            if (snapshot == null || snapshot.Count == 0) return false;

            bool ok = true;
            IntPtr buffer = Marshal.AllocHGlobal(NativeMethods.SizeOfDevMode);
            try
            {
                List<SavedDisplay> ordered = new List<SavedDisplay>();
                foreach (SavedDisplay display in snapshot)
                {
                    if (display.Primary) ordered.Add(display);
                }
                foreach (SavedDisplay display in snapshot)
                {
                    if (!display.Primary) ordered.Add(display);
                }

                foreach (SavedDisplay display in ordered)
                {
                    if (display.Mode == null || display.Mode.Length != NativeMethods.SizeOfDevMode) continue;
                    Marshal.Copy(display.Mode, 0, buffer, NativeMethods.SizeOfDevMode);
                    uint flags = NativeMethods.CDS_UPDATEREGISTRY | NativeMethods.CDS_NORESET;
                    if (display.Primary) flags |= NativeMethods.CDS_SET_PRIMARY;
                    ok &= Stage(display.DeviceName, buffer, flags, "attach");
                }
                ok &= Commit();
            }
            catch (Exception error)
            {
                Log.Error("display restore", error);
                ok = false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return ok;
        }

        /// <summary>Attaches a single display again without touching the others.</summary>
        public static bool AttachOne(SavedDisplay display)
        {
            if (display == null || display.Mode == null || display.Mode.Length != NativeMethods.SizeOfDevMode) return false;
            IntPtr buffer = Marshal.AllocHGlobal(NativeMethods.SizeOfDevMode);
            try
            {
                Marshal.Copy(display.Mode, 0, buffer, NativeMethods.SizeOfDevMode);
                bool ok = Stage(display.DeviceName, buffer, NativeMethods.CDS_UPDATEREGISTRY | NativeMethods.CDS_NORESET, "attach");
                return Commit() && ok;
            }
            catch (Exception error)
            {
                Log.Error("display attach", error);
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool Stage(string deviceName, IntPtr mode, uint flags, string what)
        {
            int result = NativeMethods.ChangeDisplaySettingsEx(deviceName, mode, IntPtr.Zero, flags, IntPtr.Zero);
            Log.Write("display: " + what + " " + deviceName + ", result " + result);
            return result == 0;
        }

        private static bool Commit()
        {
            int result = NativeMethods.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            Log.Write("display: commit result " + result);
            return result == 0;
        }

        private static SavedDisplay Find(List<SavedDisplay> list, string deviceName)
        {
            foreach (SavedDisplay display in list)
            {
                if (string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) return display;
            }
            return null;
        }

        private static void Clear(IntPtr buffer)
        {
            for (int i = 0; i < NativeMethods.SizeOfDevMode; i++)
                Marshal.WriteByte(buffer, i, 0);
            Marshal.WriteInt16(buffer, NativeMethods.DevModeSizeOffset, (short)NativeMethods.SizeOfDevMode);
        }
    }
}
