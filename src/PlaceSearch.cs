using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OrbitCalendar
{
    internal static class PlaceSearch
    {
        internal sealed class Result
        {
            public string Name;
            public GeoCoordinate Coordinate;
            public string Position { get { return Coordinate.Latitude.ToString("F4", CultureInfo.InvariantCulture) + "°, " + Coordinate.Longitude.ToString("F4", CultureInfo.InvariantCulture) + "°"; } }
        }
        public sealed class Geometry { public string type { get; set; } public double[] coordinates { get; set; } }
        public sealed class Properties { public string title { get; set; } }
        public sealed class Feature { public Geometry geometry { get; set; } public Properties properties { get; set; } }
        static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        static readonly Dictionary<string, Result[]> cache = new Dictionary<string, Result[]>();
        static DateTime nextRequest;

        public static async Task<Result[]> Search(string query, CancellationToken token)
        {
            query = (query ?? "").Trim().Normalize(NormalizationForm.FormKC);
            if (query.Length < 2 || query.Length > 120) throw new ArgumentException("駅名・地名・住所を2～120文字で入力してください");
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Result[] found;
                if (cache.TryGetValue(query, out found)) return found;
                double delay = (nextRequest - DateTime.UtcNow).TotalMilliseconds;
                if (delay > 0) await Task.Delay((int)Math.Ceiling(delay), token).ConfigureAwait(false);
                nextRequest = DateTime.UtcNow.AddSeconds(1);
                var bytes = await LiveData.Get("https://msearch.gsi.go.jp/address-search/AddressSearch?q=" + Uri.EscapeDataString(query), token).ConfigureAwait(false);
                found = Parse(Encoding.UTF8.GetString(bytes), query);
                if (cache.Count >= 30) cache.Clear();
                cache[query] = found;
                return found;
            }
            finally { gate.Release(); }
        }

        internal static Result[] Parse(string json, string query)
        {
            var features = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Feature[]>(json);
            if (features == null) throw new System.IO.InvalidDataException();
            var rows = new List<Result>();
            foreach (var feature in features)
            {
                if (feature == null || feature.geometry == null || feature.geometry.type != "Point" || feature.properties == null) continue;
                var xy = feature.geometry.coordinates;
                string title = feature.properties.title;
                if (xy == null || xy.Length < 2 || string.IsNullOrWhiteSpace(title) ||
                    !(xy[1] >= 20 && xy[1] <= 48 && xy[0] >= 120 && xy[0] <= 155)) continue;
                if (rows.Any(r => r.Name == title && Math.Abs(r.Coordinate.Latitude - xy[1]) < .00001 && Math.Abs(r.Coordinate.Longitude - xy[0]) < .00001)) continue;
                rows.Add(new Result { Name = title.Trim(), Coordinate = new GeoCoordinate(xy[1], xy[0]) });
            }
            // The service can return towns before a station with the exact requested name.
            return rows.OrderBy(r => r.Name == query ? 0 : r.Name.StartsWith(query, StringComparison.Ordinal) ? 1 : 2).ToArray();
        }
    }
}
