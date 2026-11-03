using System;
using System.Globalization;

namespace RowVR.Stroke
{
    /// <summary>How the panel writes numbers, the way rowing monitors do (#29).</summary>
    public static class MetricsFormat
    {
        /// <summary>Elapsed time: m:ss under an hour, h:mm:ss after.</summary>
        public static string Time(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0d) seconds = 0d;
            var total = (long)Math.Floor(seconds);
            var h = total / 3600;
            var m = total / 60 % 60;
            var s = total % 60;
            return h > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, s)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", m, s);
        }

        /// <summary>Pace per 500 m as m:ss.t; a dash when there is none (resting, or slower than 9:59.9).</summary>
        public static string Split(float seconds)
        {
            if (float.IsInfinity(seconds) || float.IsNaN(seconds) || seconds <= 0f || seconds >= 600f) return "-:--";
            var tenths = (int)Math.Round(seconds * 10f);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2}", tenths / 600, tenths / 10 % 60, tenths % 10);
        }

        /// <summary>Distance in whole metres, with a thin thousands separator.</summary>
        public static string Distance(double metres) =>
            Math.Max(0d, Math.Floor(metres)).ToString("#,0", CultureInfo.InvariantCulture);

        /// <summary>Stroke rate in whole strokes per minute; a dash at rest.</summary>
        public static string Rate(float spm) => spm >= 1f ? Math.Round(spm).ToString(CultureInfo.InvariantCulture) : "-";
    }
}
