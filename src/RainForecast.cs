using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OrbitCalendar
{
    internal static partial class LiveData
    {
        internal static DateTime RainUtc(string text)
        {
            return DateTime.ParseExact(text, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        static async Task<RadarTime[]> FutureRainTimes(CancellationToken token)
        {
            var times = await Json<RadarTime[]>("https://www.jma.go.jp/bosai/jmatile/data/nowc/targetTimes_N1.json", token).ConfigureAwait(false);
            var observed = times.Where(v => v.elements != null && v.elements.Contains("hrpns") && v.validtime == v.basetime)
                .OrderByDescending(v => v.validtime, StringComparer.Ordinal).FirstOrDefault();
            if (observed == null) throw new System.IO.InvalidDataException("最新の観測時刻がありません");
            var nowcast = new RadarTime[0]; var shortRange = new RadarTime[0];
            try { nowcast = await Json<RadarTime[]>("https://www.jma.go.jp/bosai/jmatile/data/nowc/targetTimes_N2.json", token).ConfigureAwait(false); }
            catch (System.Net.Http.HttpRequestException) { }
            catch (TaskCanceledException) { token.ThrowIfCancellationRequested(); }
            try { shortRange = await Json<RadarTime[]>("https://www.jma.go.jp/bosai/jmatile/data/rasrf/targetTimes.json", token).ConfigureAwait(false); }
            catch (System.Net.Http.HttpRequestException) { }
            catch (TaskCanceledException) { token.ThrowIfCancellationRequested(); }
            return SelectFutureRainTimes(observed, nowcast, shortRange);
        }

        internal static RadarTime[] SelectFutureRainTimes(RadarTime observed, RadarTime[] nowcast, RadarTime[] shortRange)
        {
            DateTime origin = DateTime.UtcNow;
            var result = new List<RadarTime> { observed };
            var near = nowcast.Where(t => t.elements != null && t.elements.Contains("hrpns") &&
                RainUtc(t.validtime) > origin && RainUtc(t.validtime) <= origin.AddHours(1)).ToArray();
            for (int minutes = 10; minutes <= 60; minutes += 10)
                AddNearestRain(result, near, origin.AddMinutes(minutes), 5, false);
            // Public short-range tiles advance in roughly one-hour steps. Use real
            // available times, ending closest to +3h (within about half an hour).
            var far = shortRange.Where(t => t.elements != null && t.elements.Contains("rasrf") && t.basetime != t.validtime &&
                RainUtc(t.validtime) > origin.AddHours(1) && RainUtc(t.validtime) <= origin.AddHours(3.5))
                .GroupBy(t => t.validtime).Select(g => g.OrderByDescending(t => t.basetime, StringComparer.Ordinal).First()).ToArray();
            // Keep one issuance across the long-range animation, rather than
            // alternating older and newer forecasts for neighbouring times.
            string latestBase = far.Select(t => t.basetime).OrderByDescending(t => t, StringComparer.Ordinal).FirstOrDefault();
            far = far.Where(t => t.basetime == latestBase).ToArray();
            var endpoint = far.OrderBy(t => Math.Abs((RainUtc(t.validtime) - origin.AddHours(3)).TotalMinutes))
                .ThenBy(t => t.validtime, StringComparer.Ordinal).FirstOrDefault();
            if (endpoint != null)
                foreach (var time in far.Where(t => String.CompareOrdinal(t.validtime, endpoint.validtime) <= 0))
                {
                    time.Product = "rasrf"; result.Add(time);
                }
            foreach (var time in result) time.OriginUtc = origin;
            return result.OrderBy(t => t.validtime, StringComparer.Ordinal).ToArray();
        }

        static void AddNearestRain(List<RadarTime> result, RadarTime[] candidates, DateTime target, int tolerance, bool hourly)
        {
            var time = candidates.OrderBy(t => Math.Abs((RainUtc(t.validtime) - target).TotalMinutes))
                .ThenByDescending(t => t.basetime, StringComparer.Ordinal).FirstOrDefault();
            if (time == null || Math.Abs((RainUtc(time.validtime) - target).TotalMinutes) > tolerance ||
                result.Any(t => t.validtime == time.validtime)) return;
            time.Product = hourly ? "rasrf" : "nowc";
            result.Add(time);
        }

        static async Task<byte[]> RainPixels(int zoom, int x, int y, RadarTime time, CancellationToken token,
            Dictionary<string, Task<byte[]>> requests)
        {
            bool hourly = time.Product == "rasrf";
            // Short-range forecast tiles use even zoom levels. Crop and scale the
            // parent tile at odd levels, preserving alignment with the base map.
            int nativeZoom = hourly ? Math.Min(10, zoom - zoom % 2) : zoom;
            int factor = 1 << (zoom - nativeZoom);
            string product = hourly ? "rasrf" : "nowc", element = hourly ? "rasrf" : "hrpns";
            string member = string.IsNullOrEmpty(time.member) ? "none" : Uri.EscapeDataString(time.member);
            string url = "https://www.jma.go.jp/bosai/jmatile/data/" + product + "/" + time.basetime + "/" + member + "/" +
                time.validtime + "/surf/" + element + "/" + nativeZoom + "/" + (x / factor) + "/" + (y / factor) + ".png";
            Task<byte[]> request;
            // All entries are created synchronously by the caller's tile loop.
            if (!requests.TryGetValue(url, out request)) requests[url] = request = Get(url, token, true);
            var original = Pixels(await request.ConfigureAwait(false));
            if (original == null || factor == 1) return original;
            var scaled = new byte[256 * 256 * 4];
            int left = x % factor * 256 / factor, top = y % factor * 256 / factor;
            for (int py = 0; py < 256; py++)
                for (int px = 0; px < 256; px++)
                    Buffer.BlockCopy(original, ((top + py / factor) * 256 + left + px / factor) * 4,
                        scaled, (py * 256 + px) * 4, 4);
            return scaled;
        }
    }
}
