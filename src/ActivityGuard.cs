using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Brighter
{
    /// <summary>What is playing right now, and on which monitors.</summary>
    internal sealed class MediaState
    {
        /// <summary>Something is playing.</summary>
        public bool Active;

        /// <summary>Playing, but it is not known where: every monitor should be treated as showing it.</summary>
        public bool Unlocated;

        /// <summary>HMONITORs of the monitors showing it.</summary>
        public readonly List<IntPtr> Monitors = new List<IntPtr>();

        public string Reason = "";

        public void Add(IntPtr monitor)
        {
            if (monitor != IntPtr.Zero && !Monitors.Contains(monitor)) Monitors.Add(monitor);
        }
    }

    /// <summary>
    /// Tells the idle dimmer whether the user is watching something even though keyboard and mouse
    /// are untouched, and on which monitor. Sources, most precise first:
    /// a full-screen app (the monitor of the foreground window), programs producing sound (the
    /// monitors of their windows), and a program asking Windows to keep the display on (location unknown).
    /// </summary>
    internal static class ActivityGuard
    {
        private const int SystemExecutionState = 16;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        private const int QUNS_BUSY = 2;
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        private const int QUNS_PRESENTATION_MODE = 4;

        private const int AudioSessionStateActive = 1;
        private const float AudioThreshold = 0.0005f;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int DWMWA_CLOAKED = 14;

        private static IAudioSessionManager2 _sessions;
        private static int _sessionsStamp;
        private static bool _audioBroken;

        [DllImport("powrprof.dll")]
        private static extern uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferLength, out uint outputBuffer, uint outputBufferLength);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeMethods.RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

        public static MediaState Check()
        {
            MediaState state = new MediaState();
            List<string> reasons = new List<string>();

            // 1. Full-screen video, game or presentation: it is on the foreground window's monitor.
            if (FullScreenApp())
            {
                state.Add(MonitorFromWindow(NativeMethods.GetForegroundWindow(), MONITOR_DEFAULTTONEAREST));
                reasons.Add("full-screen app");
            }

            // 2. Programs making sound: their visible windows are where the video is.
            //    Sound from a program without a visible window (music in the tray) concerns no monitor.
            bool unknownAudio;
            List<string> sounding = SoundingProcesses(out unknownAudio);
            foreach (string name in sounding)
            {
                if (AddWindowsOf(name, state)) reasons.Add("sound from " + name);
            }
            if (unknownAudio)
            {
                state.Unlocated = true;
                reasons.Add("sound from an unknown program");
            }

            // 3. A program asked Windows to keep the display on (video players do) but nothing above
            //    says where: keep every monitor as it is rather than guess.
            if (state.Monitors.Count == 0 && !state.Unlocated && DisplayRequested())
            {
                state.Unlocated = true;
                reasons.Add("a program keeps the display on (source unknown)");
            }

            state.Active = state.Unlocated || state.Monitors.Count > 0;
            state.Reason = string.Join(", ", reasons.ToArray());
            return state;
        }

        private static bool DisplayRequested()
        {
            try
            {
                uint value;
                if (CallNtPowerInformation(SystemExecutionState, IntPtr.Zero, 0, out value, 4) != 0) return false;
                return (value & ES_DISPLAY_REQUIRED) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool FullScreenApp()
        {
            try
            {
                int value;
                if (SHQueryUserNotificationState(out value) != 0) return false;
                return value == QUNS_BUSY || value == QUNS_RUNNING_D3D_FULL_SCREEN || value == QUNS_PRESENTATION_MODE;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ audio sessions

        /// <summary>Names of the processes producing sound right now on the default playback device.</summary>
        private static List<string> SoundingProcesses(out bool unknown)
        {
            unknown = false;
            List<string> names = new List<string>();
            if (_audioBroken) return names;

            IAudioSessionEnumerator list = null;
            try
            {
                // The default device can change (headphones plugged in): pick it up again now and then.
                if (_sessions == null || unchecked(Environment.TickCount - _sessionsStamp) > 30000)
                {
                    Release(_sessions);
                    _sessions = OpenSessionManager();
                    _sessionsStamp = Environment.TickCount;
                    if (_sessions == null) return names;
                }

                if (_sessions.GetSessionEnumerator(out list) != 0 || list == null)
                {
                    Release(_sessions);
                    _sessions = null;
                    return names;
                }

                int count;
                if (list.GetCount(out count) != 0) return names;
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl2 session;
                    if (list.GetSession(i, out session) != 0 || session == null) continue;
                    try
                    {
                        int sessionState;
                        if (session.GetState(out sessionState) != 0 || sessionState != AudioSessionStateActive) continue;

                        IAudioMeterInformation meter = session as IAudioMeterInformation;
                        float peak;
                        if (meter == null || meter.GetPeakValue(out peak) != 0 || peak <= AudioThreshold) continue;

                        uint processId;
                        if (session.GetProcessId(out processId) != 0 || processId == 0) continue; // 0: system sounds

                        string name = ProcessName(processId);
                        if (name == null) unknown = true;
                        else if (!names.Contains(name)) names.Add(name);
                    }
                    finally
                    {
                        Release(session);
                    }
                }
            }
            catch (Exception error)
            {
                Log.Error("audio sessions", error);
                _audioBroken = true; // never retry a COM setup that throws every second
                Release(_sessions);
                _sessions = null;
            }
            finally
            {
                Release(list);
            }
            return names;
        }

        private static IAudioSessionManager2 OpenSessionManager()
        {
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device) != 0 || device == null)
                    return null;
                try
                {
                    Guid iid = typeof(IAudioSessionManager2).GUID;
                    object instance;
                    if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out instance) != 0) return null;
                    return instance as IAudioSessionManager2;
                }
                finally
                {
                    Release(device);
                }
            }
            finally
            {
                Release(enumerator);
            }
        }

        private static string ProcessName(uint processId)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    return process.ProcessName;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ windows

        /// <summary>
        /// Adds the monitors of every visible, normal-sized window of the processes with this name.
        /// By name, not id, because browsers play sound from a helper process that owns no windows.
        /// </summary>
        private static bool AddWindowsOf(string processName, MediaState state)
        {
            HashSet<uint> ids = new HashSet<uint>();
            try
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    ids.Add((uint)process.Id);
                    process.Dispose();
                }
            }
            catch (Exception)
            {
                return false;
            }
            if (ids.Count == 0) return false;

            bool found = false;
            EnumWindowsProc callback = delegate(IntPtr window, IntPtr parameter)
            {
                uint owner;
                NativeMethods.GetWindowThreadProcessId(window, out owner);
                if (!ids.Contains(owner)) return true;
                if (!IsWindowVisible(window) || IsIconic(window)) return true;

                int cloaked;
                if (DwmGetWindowAttribute(window, DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true;

                NativeMethods.RECT rect;
                if (!GetWindowRect(window, out rect) || rect.Right - rect.Left < 200 || rect.Bottom - rect.Top < 150) return true;

                state.Add(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST));
                found = true;
                return true;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return found;
        }

        private static void Release(object comObject)
        {
            if (comObject == null) return;
            try { Marshal.ReleaseComObject(comObject); }
            catch (Exception) { }
        }
    }

    // ---- Core Audio COM interfaces. Every method up to the last one used is declared, in vtable
    //      order, because COM interop does not inherit vtables; unused ones are placeholders. ----

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(IntPtr sessionGuid, int streamFlags, out IntPtr sessionControl);

        [PreserveSig]
        int GetSimpleAudioVolume(IntPtr sessionGuid, int streamFlags, out IntPtr audioVolume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig]
        int GetState(out int state);

        [PreserveSig]
        int GetDisplayName(out IntPtr name);

        [PreserveSig]
        int SetDisplayName(IntPtr name, IntPtr eventContext);

        [PreserveSig]
        int GetIconPath(out IntPtr path);

        [PreserveSig]
        int SetIconPath(IntPtr path, IntPtr eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid grouping);

        [PreserveSig]
        int SetGroupingParam(IntPtr grouping, IntPtr eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr client);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr client);

        // IAudioSessionControl2
        [PreserveSig]
        int GetSessionIdentifier(out IntPtr identifier);

        [PreserveSig]
        int GetSessionInstanceIdentifier(out IntPtr identifier);

        [PreserveSig]
        int GetProcessId(out uint processId);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float peak);
    }
}
