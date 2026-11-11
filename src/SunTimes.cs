using System;

namespace Brighter
{
    /// <summary>Sunrise / sunset for a location (the "Almanac for Computers" algorithm, good to a minute or two).</summary>
    internal static class SunTimes
    {
        private const double Zenith = 90.833; // official sunrise/sunset, includes refraction

        public static bool TryGet(DateTime date, double latitude, double longitude, out TimeSpan sunrise, out TimeSpan sunset)
        {
            sunrise = TimeSpan.Zero;
            sunset = TimeSpan.Zero;
            if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180) return false;

            double offsetHours = TimeZoneInfo.Local.GetUtcOffset(date).TotalHours;
            double rise;
            double set;
            if (!Calculate(date.DayOfYear, latitude, longitude, true, out rise)) return false;
            if (!Calculate(date.DayOfYear, latitude, longitude, false, out set)) return false;

            sunrise = TimeSpan.FromHours(Wrap(rise + offsetHours, 24));
            sunset = TimeSpan.FromHours(Wrap(set + offsetHours, 24));
            return true;
        }

        /// <summary>UTC hour of the event; false when the sun does not rise or set that day (polar regions).</summary>
        private static bool Calculate(int dayOfYear, double latitude, double longitude, bool rising, out double utcHour)
        {
            utcHour = 0;
            double lngHour = longitude / 15.0;
            double t = dayOfYear + ((rising ? 6.0 : 18.0) - lngHour) / 24.0;

            double meanAnomaly = 0.9856 * t - 3.289;
            double trueLongitude = Wrap(meanAnomaly + 1.916 * Sin(meanAnomaly) + 0.020 * Sin(2 * meanAnomaly) + 282.634, 360);

            double rightAscension = Wrap(Atan(0.91764 * Tan(trueLongitude)), 360);
            double longitudeQuadrant = Math.Floor(trueLongitude / 90.0) * 90.0;
            double ascensionQuadrant = Math.Floor(rightAscension / 90.0) * 90.0;
            rightAscension = (rightAscension + longitudeQuadrant - ascensionQuadrant) / 15.0;

            double sinDeclination = 0.39782 * Sin(trueLongitude);
            double cosDeclination = Math.Cos(Math.Asin(sinDeclination));

            double cosHour = (Cos(Zenith) - sinDeclination * Sin(latitude)) / (cosDeclination * Cos(latitude));
            if (cosHour > 1 || cosHour < -1) return false;

            double hour = rising ? 360.0 - Acos(cosHour) : Acos(cosHour);
            hour /= 15.0;

            double localMeanTime = hour + rightAscension - 0.06571 * t - 6.622;
            utcHour = Wrap(localMeanTime - lngHour, 24);
            return true;
        }

        private static double Wrap(double value, double range)
        {
            value %= range;
            if (value < 0) value += range;
            return value;
        }

        private static double Sin(double degrees) { return Math.Sin(degrees * Math.PI / 180.0); }
        private static double Cos(double degrees) { return Math.Cos(degrees * Math.PI / 180.0); }
        private static double Tan(double degrees) { return Math.Tan(degrees * Math.PI / 180.0); }
        private static double Atan(double value) { return Math.Atan(value) * 180.0 / Math.PI; }
        private static double Acos(double value) { return Math.Acos(value) * 180.0 / Math.PI; }
    }
}
