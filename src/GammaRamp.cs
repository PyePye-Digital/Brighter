using System;
using System.Collections.Generic;

namespace Brighter
{
    /// <summary>
    /// Colour temperature per display, done with the graphics card's gamma ramp (the same technique
    /// night-light tools use). It works on every monitor, including ones without DDC/CI, and does
    /// not touch the monitor's own settings. The ramp is put back to neutral when the app exits.
    /// </summary>
    internal static class GammaRamp
    {
        // Windows rejects ramps that stray too far from neutral, so the warm end stops at 3400 K.
        public const int MinKelvin = 3400;
        public const int MaxKelvin = 6500;

        private static readonly HashSet<string> Touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Slider level 0..100 to Kelvin.</summary>
        public static int KelvinFromLevel(int level)
        {
            if (level < 0) level = 0;
            if (level > 100) level = 100;
            return MinKelvin + (MaxKelvin - MinKelvin) * level / 100;
        }

        public static int LevelFromKelvin(int kelvin)
        {
            if (kelvin < MinKelvin) kelvin = MinKelvin;
            if (kelvin > MaxKelvin) kelvin = MaxKelvin;
            return (int)Math.Round((kelvin - MinKelvin) * 100.0 / (MaxKelvin - MinKelvin));
        }

        /// <summary>Applies a temperature level (100 = neutral) to one display, e.g. "\\.\DISPLAY1".</summary>
        public static bool Apply(string gdiName, int level)
        {
            if (string.IsNullOrEmpty(gdiName)) return false;
            if (level < 0) level = 0;
            if (level > 100) level = 100;

            double red = 1;
            double green = 1;
            double blue = 1;
            if (level < 100)
                Multipliers(KelvinFromLevel(level), out red, out green, out blue);

            NativeMethods.GAMMA_RAMP ramp = new NativeMethods.GAMMA_RAMP();
            ramp.Red = new ushort[256];
            ramp.Green = new ushort[256];
            ramp.Blue = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                double value = i * 257.0;
                ramp.Red[i] = (ushort)Math.Min(65535.0, value * red);
                ramp.Green[i] = (ushort)Math.Min(65535.0, value * green);
                ramp.Blue[i] = (ushort)Math.Min(65535.0, value * blue);
            }

            bool ok = false;
            try
            {
                IntPtr dc = NativeMethods.CreateDC(null, gdiName, null, IntPtr.Zero);
                if (dc == IntPtr.Zero)
                {
                    Log.Write("gamma: no device context for " + gdiName);
                    return false;
                }
                try
                {
                    ok = NativeMethods.SetDeviceGammaRamp(dc, ref ramp);
                    if (!ok) Log.Write("gamma: ramp rejected for " + gdiName + " at level " + level);
                }
                finally
                {
                    NativeMethods.DeleteDC(dc);
                }
            }
            catch (Exception)
            {
                return false;
            }

            if (level < 100) Touched.Add(gdiName);
            else Touched.Remove(gdiName);
            return ok;
        }

        /// <summary>Puts every display this app changed back to neutral.</summary>
        public static void ResetAll()
        {
            foreach (string name in new List<string>(Touched))
                Apply(name, 100);
        }

        private static void Multipliers(int kelvin, out double red, out double green, out double blue)
        {
            double r, g, b, r0, g0, b0;
            Raw(kelvin, out r, out g, out b);
            Raw(MaxKelvin, out r0, out g0, out b0);
            red = Math.Min(1.0, r / r0);
            green = Math.Min(1.0, g / g0);
            blue = Math.Min(1.0, b / b0);
        }

        /// <summary>Black-body colour approximation (Tanner Helland's curve fit), components 0..255.</summary>
        private static void Raw(double kelvin, out double red, out double green, out double blue)
        {
            double t = kelvin / 100.0;
            if (t <= 66)
            {
                red = 255;
                green = 99.4708025861 * Math.Log(t) - 161.1195681661;
                blue = t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            }
            else
            {
                red = 329.698727446 * Math.Pow(t - 60, -0.1332047592);
                green = 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
                blue = 255;
            }
            red = Math.Max(1, Math.Min(255, red));
            green = Math.Max(1, Math.Min(255, green));
            blue = Math.Max(1, Math.Min(255, blue));
        }
    }
}
