using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Brighter
{
    internal enum ControlMethod
    {
        None,
        Ddc,      // external monitor, DDC/CI
        Wmi,      // built-in panel
        Software  // no hardware control: dimming overlay
    }

    /// <summary>A single physical monitor and the way it is controlled.</summary>
    internal sealed class MonitorDevice : IDisposable
    {
        private static readonly int[] DefaultInputs = new int[] { 15, 16, 17, 18, 3, 1 };

        private readonly object _sync = new object();
        private IntPtr _handle;
        private int _pendingBrightness = -1;
        private int _pendingContrast = -1;
        private int _pendingVolume = -1;
        private bool _working;
        private List<int> _inputs;

        public int Index { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Connection { get; set; }
        public string Detail { get; set; }
        public string GdiName { get; set; }
        public IntPtr HMonitor { get; set; }
        public NativeMethods.RECT Bounds { get; set; }
        public bool IsPrimary { get; set; }
        public ControlMethod Method { get; set; }
        public uint RawMax { get; set; }
        public int Percent { get; private set; }
        public bool HasContrast { get; set; }
        public uint ContrastRawMax { get; set; }
        public int Contrast { get; set; }
        public bool HasVolume { get; set; }
        public uint VolumeRawMax { get; set; }
        public int Volume { get; set; }
        public bool PoweredOff { get; private set; }

        /// <summary>Colour temperature level, 0 (warmest) .. 100 (neutral). Applied by <see cref="GammaRamp"/>.</summary>
        public int Temperature { get; set; }

        public bool IsDdc
        {
            get { return Method == ControlMethod.Ddc; }
        }

        public MonitorDevice(IntPtr physicalHandle, int percent)
        {
            _handle = physicalHandle;
            Percent = Clamp(percent);
            Id = "";
            Name = "";
            Connection = "";
            Detail = "";
            GdiName = "";
            RawMax = 100;
            ContrastRawMax = 100;
            VolumeRawMax = 100;
            Temperature = 100;
        }

        /// <summary>Sets the starting value without talking to the monitor (used for software dimming).</summary>
        public void SetInitialPercent(int percent)
        {
            Percent = Clamp(percent);
        }

        // ------------------------------------------------------------ brightness / contrast / volume

        /// <summary>
        /// Queues a brightness change. Monitor I/O is slow (tens of milliseconds per call), so the
        /// write runs on a worker thread and only the most recent value is sent.
        /// </summary>
        public void RequestPercent(int percent)
        {
            if (Method == ControlMethod.None) return;
            percent = Clamp(percent);
            Percent = percent;
            if (Method == ControlMethod.Software) return; // the overlay is driven by the UI
            Queue(percent, -1, -1);
        }

        public void RequestContrast(int percent)
        {
            if (!IsDdc || !HasContrast) return;
            percent = Clamp(percent);
            Contrast = percent;
            Queue(-1, percent, -1);
        }

        public void RequestVolume(int percent)
        {
            if (!IsDdc || !HasVolume) return;
            percent = Clamp(percent);
            Volume = percent;
            Queue(-1, -1, percent);
        }

        /// <summary>Writes immediately on the calling thread. Used by the command line, which exits right after.</summary>
        public void ApplyNow(int brightness, int contrast, int volume)
        {
            if (brightness >= 0) Percent = Clamp(brightness);
            if (contrast >= 0 && IsDdc && HasContrast) Contrast = Clamp(contrast); else contrast = -1;
            if (volume >= 0 && IsDdc && HasVolume) Volume = Clamp(volume); else volume = -1;
            try
            {
                Write(brightness >= 0 ? Percent : -1, contrast >= 0 ? Contrast : -1, volume >= 0 ? Volume : -1);
            }
            catch (Exception)
            {
            }
        }

        private void Queue(int brightness, int contrast, int volume)
        {
            lock (_sync)
            {
                if (brightness >= 0) _pendingBrightness = brightness;
                if (contrast >= 0) _pendingContrast = contrast;
                if (volume >= 0) _pendingVolume = volume;
                if (_working) return;
                _working = true;
            }
            ThreadPool.QueueUserWorkItem(Worker);
        }

        private void Worker(object state)
        {
            while (true)
            {
                int brightness;
                int contrast;
                int volume;
                lock (_sync)
                {
                    brightness = _pendingBrightness;
                    contrast = _pendingContrast;
                    volume = _pendingVolume;
                    _pendingBrightness = -1;
                    _pendingContrast = -1;
                    _pendingVolume = -1;
                    if (brightness < 0 && contrast < 0 && volume < 0)
                    {
                        _working = false;
                        return;
                    }
                }
                try
                {
                    Write(brightness, contrast, volume);
                }
                catch (Exception)
                {
                    // A monitor may be unplugged mid-write; nothing useful to do here.
                }
            }
        }

        private void Write(int brightness, int contrast, int volume)
        {
            if (brightness >= 0)
            {
                if (Method == ControlMethod.Ddc)
                    WriteVcp(MonitorService.VcpBrightness, Scale(brightness, RawMax));
                else if (Method == ControlMethod.Wmi)
                    WmiBrightness.Set(brightness);
            }
            if (contrast >= 0)
                WriteVcp(MonitorService.VcpContrast, Scale(contrast, ContrastRawMax));
            if (volume >= 0)
                WriteVcp(MonitorService.VcpVolume, Scale(volume, VolumeRawMax));
        }

        // ------------------------------------------------------------ power

        /// <summary>Puts the monitor into standby (VCP D6 = 4) or wakes it up again (D6 = 1).</summary>
        public void SetPower(bool on)
        {
            if (!IsDdc) return;
            PoweredOff = !on;
            uint value = on ? 1u : 4u;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try { WriteVcp(MonitorService.VcpPowerMode, value); }
                catch (Exception) { }
            });
        }

        // ------------------------------------------------------------ input source

        /// <summary>
        /// Returns the input codes this monitor advertises (falls back to the common ones when the
        /// monitor does not report them) and the currently selected one, or -1. Slow on first call.
        /// </summary>
        public List<int> ReadInputs(out int current)
        {
            current = -1;
            if (!IsDdc) return new List<int>();

            IntPtr handle = _handle;
            uint value;
            uint max;
            if (handle != IntPtr.Zero && MonitorService.TryReadVcp(handle, MonitorService.VcpInputSource, 2, out value, out max))
                current = (int)(value & 0xFF);

            if (_inputs == null)
            {
                List<int> parsed = ParseInputs(ReadCapabilities());
                if (parsed.Count == 0)
                    parsed.AddRange(DefaultInputs);
                if (current > 0 && !parsed.Contains(current))
                    parsed.Insert(0, current);
                _inputs = parsed;
            }
            return new List<int>(_inputs);
        }

        public void SetInput(int code)
        {
            if (!IsDdc) return;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try { WriteVcp(MonitorService.VcpInputSource, (uint)code); }
                catch (Exception) { }
            });
        }

        public static string InputName(int code)
        {
            switch (code)
            {
                case 1: return "VGA 1";
                case 2: return "VGA 2";
                case 3: return "DVI 1";
                case 4: return "DVI 2";
                case 5: return "Composite 1";
                case 6: return "Composite 2";
                case 7: return "S-Video 1";
                case 8: return "S-Video 2";
                case 12: return "Component 1";
                case 13: return "Component 2";
                case 14: return "Component 3";
                case 15: return "DisplayPort 1";
                case 16: return "DisplayPort 2";
                case 17: return "HDMI 1";
                case 18: return "HDMI 2";
                case 27: return "USB-C";
                default: return L.Input + " 0x" + code.ToString("X2");
            }
        }

        private string ReadCapabilities()
        {
            // One attempt only, on purpose: the capabilities request is the heaviest DDC/CI call, and
            // repeating it has been seen to leave a monitor's DDC/CI unresponsive until it is power-cycled.
            for (int attempt = 0; attempt < 1; attempt++)
            {
                try
                {
                    lock (MonitorService.DdcLock)
                    {
                        if (_handle == IntPtr.Zero) return "";
                        uint length;
                        if (NativeMethods.GetCapabilitiesStringLength(_handle, out length) && length > 0 && length <= 65536)
                        {
                            byte[] buffer = new byte[length];
                            if (NativeMethods.CapabilitiesRequestAndCapabilitiesReply(_handle, buffer, length))
                            {
                                string text = Encoding.ASCII.GetString(buffer).TrimEnd('\0');
                                Log.Write("ddc: capabilities of " + Name + ": " + (text.Length > 600 ? text.Substring(0, 600) : text));
                                return text;
                            }
                        }
                    }
                }
                catch (Exception error)
                {
                    Log.Error("capabilities", error);
                    return "";
                }
            }
            Log.Write("ddc: " + Name + " did not report its capabilities; using the common input list");
            return "";
        }

        /// <summary>Pulls the value list of VCP code 60 out of an MCCS capabilities string, e.g. "... 60(0F 11 12) ...".</summary>
        private static List<int> ParseInputs(string capabilities)
        {
            List<int> list = new List<int>();
            if (string.IsNullOrEmpty(capabilities)) return list;

            int vcp = capabilities.IndexOf("vcp(", StringComparison.OrdinalIgnoreCase);
            if (vcp < 0) return list;

            int position = vcp + 4;
            while (position < capabilities.Length)
            {
                int start = capabilities.IndexOf("60(", position, StringComparison.Ordinal);
                if (start < 0) return list;

                char before = capabilities[start - 1];
                if (before == ' ' || before == '(')
                {
                    int end = capabilities.IndexOf(')', start);
                    if (end < 0) return list;
                    string[] parts = capabilities.Substring(start + 3, end - start - 3)
                        .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string part in parts)
                    {
                        int parsed;
                        if (int.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed) && parsed > 0 && !list.Contains(parsed))
                            list.Add(parsed);
                    }
                    return list;
                }
                position = start + 3;
            }
            return list;
        }

        // ------------------------------------------------------------ plumbing

        private void WriteVcp(byte code, uint value)
        {
            lock (MonitorService.DdcLock)
            {
                if (_handle != IntPtr.Zero && !NativeMethods.SetVCPFeature(_handle, code, value))
                    Log.Write("ddc: write failed on " + Name + ", code 0x" + code.ToString("X2") + ", value " + value);
            }
        }

        private static uint Scale(int percent, uint max)
        {
            if (max == 0) max = 100;
            return (uint)Math.Round(max * percent / 100.0);
        }

        public void Dispose()
        {
            lock (MonitorService.DdcLock)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeMethods.DestroyPhysicalMonitor(_handle);
                    _handle = IntPtr.Zero;
                }
            }
        }

        private static int Clamp(int value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }
    }
}
