using System;
using System.Device.Location;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OrbitCalendar
{
    internal static partial class LiveData
    {
        public class HourlyWeather
        {
            public long[] time { get; set; }
            public double?[] temperature_2m { get; set; }
            public double?[] precipitation_probability { get; set; }
            public int?[] weather_code { get; set; }
            public double?[] wind_speed_10m { get; set; }
        }
        public class DaysWeather
        {
            public long[] time { get; set; }
            public int?[] weather_code { get; set; }
            public double?[] temperature_2m_max { get; set; }
            public double?[] temperature_2m_min { get; set; }
            public double?[] precipitation_probability_max { get; set; }
        }
        public class WeatherForecast
        {
            public HourlyWeather hourly { get; set; }
            public DaysWeather daily { get; set; }
            public string Place;
            public DateTime Fetched;
            public string CoordinateKey;
            public double? PreviousHigh, PreviousLow;
            public static DateTime Utc(long seconds) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds); }
            internal void SelectForecastDays(DateTime today)
            {
                if (daily == null || daily.time == null) throw new InvalidDataException("日別の予報がありません");
                int first = Array.FindIndex(daily.time, t => Utc(t).AddHours(9).Date == today.Date);
                int end = first + 3;
                if (first < 0 || daily.time.Length < end || daily.weather_code == null || daily.weather_code.Length < end ||
                    daily.temperature_2m_max == null || daily.temperature_2m_max.Length < end || daily.temperature_2m_min == null || daily.temperature_2m_min.Length < end ||
                    daily.precipitation_probability_max == null || daily.precipitation_probability_max.Length < end)
                    throw new InvalidDataException("3日間の予報がありません");
                PreviousHigh = PreviousLow = null;
                if (first > 0 && Utc(daily.time[first - 1]).AddHours(9).Date == today.Date.AddDays(-1))
                {
                    PreviousHigh = daily.temperature_2m_max[first - 1]; PreviousLow = daily.temperature_2m_min[first - 1];
                }
                daily.time = daily.time.Skip(first).Take(3).ToArray();
                daily.weather_code = daily.weather_code.Skip(first).Take(3).ToArray();
                daily.temperature_2m_max = daily.temperature_2m_max.Skip(first).Take(3).ToArray();
                daily.temperature_2m_min = daily.temperature_2m_min.Skip(first).Take(3).ToArray();
                daily.precipitation_probability_max = daily.precipitation_probability_max.Skip(first).Take(3).ToArray();
            }
            public double? Temperature(DateTime utc)
            {
                if (hourly == null || hourly.time == null || hourly.temperature_2m == null) return null;
                int n = Math.Min(hourly.time.Length, hourly.temperature_2m.Length);
                for (int i = 1; i < n; i++)
                {
                    DateTime a = Utc(hourly.time[i - 1]), b = Utc(hourly.time[i]);
                    if (utc < a || utc > b || !hourly.temperature_2m[i - 1].HasValue || !hourly.temperature_2m[i].HasValue) continue;
                    double t = (utc - a).TotalSeconds / (b - a).TotalSeconds;
                    return hourly.temperature_2m[i - 1].Value * (1 - t) + hourly.temperature_2m[i].Value * t;
                }
                return null;
            }
        }
        internal static WeatherForecast CachedForecast;
        public static async Task<WeatherForecast> ForecastWeather(CancellationToken token, bool force)
        {
            var here = SavedPlace.Coordinate;
            string place = "保存地点周辺";
            if (here == null)
            {
                place = "現在地周辺";
                here = DateTime.UtcNow - lastPositionTime < TimeSpan.FromMinutes(30) ? lastPosition : null;
                if (here == null) here = await CurrentPosition(token).ConfigureAwait(false);
                if (here != null) { lastPosition = here; lastPositionTime = DateTime.UtcNow; }
            }
            if (here == null) { here = new GeoCoordinate(35.681236, 139.767125); place = "東京（位置情報なし）"; }
            string key = Math.Round(here.Latitude, 6).ToString(CultureInfo.InvariantCulture) + "," + Math.Round(here.Longitude, 6).ToString(CultureInfo.InvariantCulture);
            var previous = CachedForecast;
            if (!force && previous != null && previous.CoordinateKey == key && previous.Place == place && DateTime.UtcNow - previous.Fetched < TimeSpan.FromMinutes(15) && WeatherForecast.Utc(previous.daily.time[0]).AddHours(9).Date == DateTime.UtcNow.AddHours(9).Date) return previous;
            var parts = key.Split(',');
            var data = await Json<WeatherForecast>("https://api.open-meteo.com/v1/forecast?latitude=" + parts[0] + "&longitude=" + parts[1] +
                "&hourly=temperature_2m,precipitation_probability,weather_code,wind_speed_10m&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&past_days=1&forecast_days=3&timeformat=unixtime&timezone=Asia%2FTokyo", token).ConfigureAwait(false);
            data.SelectForecastDays(DateTime.UtcNow.AddHours(9).Date);
            data.Fetched = DateTime.UtcNow; data.Place = place; data.CoordinateKey = key;
            token.ThrowIfCancellationRequested();
            CachedForecast = data;
            return data;
        }
    }
}
