using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OrbitCalendar
{
    internal static class PlaceSearch
    {
        internal sealed class Result
        {
            public string Name, Area, Source;
            public int Category;
            public GeoCoordinate Coordinate;
            public string Kind { get { return Category == 1 ? "駅" : Category == 2 ? "建物・施設" : "地名・住所"; } }
            public string Label { get { string label = Category == 3 ? Regex.Replace(Name, @"^(北海道|東京都|京都府|大阪府|.{2,3}県)", "") : Name; return label.Length == 0 ? Name : label; } }
            public string Position { get { return Coordinate.Latitude.ToString("F4", CultureInfo.InvariantCulture) + "°, " + Coordinate.Longitude.ToString("F4", CultureInfo.InvariantCulture) + "°"; } }
        }
        public sealed class Geometry { public string type { get; set; } public double[] coordinates { get; set; } }
        public sealed class Properties
        {
            public string title { get; set; } public string addressCode { get; set; } public string dataSource { get; set; }
            public string name { get; set; } public string state { get; set; } public string city { get; set; } public string district { get; set; }
            public string locality { get; set; } public string street { get; set; } public string countrycode { get; set; }
            public string osm_key { get; set; } public string osm_value { get; set; }
        }
        public sealed class Feature { public Geometry geometry { get; set; } public Properties properties { get; set; } }
        public sealed class Collection { public Feature[] features { get; set; } }
        static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        static readonly Dictionary<string, Result[]> cache = new Dictionary<string, Result[]>();
        static readonly Dictionary<string, string> municipalities = ReadMunicipalities();
        static DateTime nextRequest;
        public static bool PartialFailure { get; private set; }
        static Dictionary<string, string> ReadMunicipalities()
        {
            var names = new Dictionary<string, string>();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("municipalities.tsv"))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line; while ((line = reader.ReadLine()) != null)
                { var parts = line.Split('\t'); if (parts.Length == 2) names[parts[0].TrimStart('0')] = parts[1]; }
            }
            return names;
        }
        public static async Task<Result[]> Search(string query, CancellationToken token)
        {
            query = (query ?? "").Trim().Normalize(NormalizationForm.FormKC);
            if (query.Length < 2 || query.Length > 120) throw new ArgumentException("2～120文字で入力してください");
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Result[] found;
                if (cache.TryGetValue(query, out found)) { PartialFailure = false; return found; }
                double delay = (nextRequest - DateTime.UtcNow).TotalMilliseconds;
                if (delay > 0) await Task.Delay((int)Math.Ceiling(delay), token).ConfigureAwait(false);
                nextRequest = DateTime.UtcNow.AddSeconds(1);
                var rows = new List<Result>(); int succeeded = 0;
                try
                {
                    var bytes = await LiveData.Get("https://msearch.gsi.go.jp/address-search/AddressSearch?q=" + Uri.EscapeDataString(query), token).ConfigureAwait(false);
                    rows.AddRange(Parse(Encoding.UTF8.GetString(bytes), query)); succeeded++;
                }
                catch (Exception) { token.ThrowIfCancellationRequested(); }
                try
                {
                    var bytes = await LiveData.Get("https://photon.komoot.io/api/?q=" + Uri.EscapeDataString(query) + "&limit=20&bbox=122,20,154,48", token).ConfigureAwait(false);
                    rows.AddRange(ParsePhoton(Encoding.UTF8.GetString(bytes))); succeeded++;
                }
                catch (Exception) { token.ThrowIfCancellationRequested(); }
                if (succeeded == 0) throw new IOException("地点検索に接続できません");
                PartialFailure = succeeded != 2;
                found = Rank(Dedupe(rows).ToArray(), query, null, 0);
                // A failed provider is retried next time instead of caching an incomplete set.
                if (succeeded == 2) { if (cache.Count >= 30) cache.Clear(); cache[query] = found; }
                return found;
            }
            finally { gate.Release(); }
        }
        static GeoCoordinate Coordinate(Feature f)
        {
            if (f == null || f.geometry == null || f.geometry.type != "Point" || f.properties == null) return null;
            var xy = f.geometry.coordinates;
            return xy != null && xy.Length >= 2 && xy[1] >= 20 && xy[1] <= 48 && xy[0] >= 120 && xy[0] <= 155 ? new GeoCoordinate(xy[1], xy[0]) : null;
        }
        internal static Result[] Parse(string json, string query)
        {
            var features = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Feature[]>(json);
            if (features == null) throw new InvalidDataException();
            var rows = new List<Result>();
            foreach (var feature in features)
            {
                var coordinate = Coordinate(feature); if (coordinate == null || string.IsNullOrWhiteSpace(feature.properties.title)) continue;
                var p = feature.properties; string area;
                municipalities.TryGetValue((p.addressCode ?? "").TrimStart('0'), out area);
                int kind = p.title.EndsWith("駅", StringComparison.Ordinal) ? 1 : p.dataSource == "3" ? 2 : 3;
                rows.Add(new Result { Name = p.title.Trim(), Area = area ?? (kind == 3 ? p.title.Trim() : "所在地の情報なし"), Category = kind, Coordinate = coordinate, Source = "国土地理院" });
            }
            return Rank(Dedupe(rows).ToArray(), query, null, 0);
        }
        internal static Result[] ParsePhoton(string json)
        {
            var data = new JavaScriptSerializer().Deserialize<Collection>(json); if (data == null || data.features == null) throw new InvalidDataException();
            var rows = new List<Result>();
            foreach (var f in data.features)
            {
                var coordinate = Coordinate(f); if (coordinate == null) continue;
                var p = f.properties; if (p.countrycode != "JP" || string.IsNullOrWhiteSpace(p.name)) continue;
                bool station = p.osm_key == "railway" && (p.osm_value == "station" || p.osm_value == "halt" || p.osm_value == "stop");
                int category = station ? 1 : p.osm_key == "place" || p.osm_key == "boundary" || p.osm_key == "highway" ? 3 : 2;
                string name = p.name.Trim(); if (station && !name.EndsWith("駅", StringComparison.Ordinal)) name += "駅";
                string area = string.Join(" ", new[] { p.state, p.city, p.district, p.locality, p.street }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
                rows.Add(new Result { Name = name, Area = area.Length == 0 ? "所在地の情報なし" : area, Category = category, Coordinate = coordinate, Source = "Photon / OpenStreetMap" });
            }
            return rows.ToArray();
        }
        static IEnumerable<Result> Dedupe(IEnumerable<Result> values)
        {
            var rows = new List<Result>();
            foreach (var item in values)
            {
                var alias = rows.FirstOrDefault(r => r.Category == 3 && item.Category == 3 && r.Source == "国土地理院" && item.Source == r.Source && r.Coordinate.GetDistanceTo(item.Coordinate) < 2);
                if (alias != null)
                {
                    if (item.Name.Length > alias.Name.Length) rows[rows.IndexOf(alias)] = item;
                    continue;
                }
                if (rows.Any(r => r.Name == item.Name && r.Category == item.Category && r.Coordinate.GetDistanceTo(item.Coordinate) < (item.Category == 1 ? 300 : 30))) continue;
                rows.Add(item);
            }
            return rows;
        }
        static int Match(Result item, string query)
        {
            var firstNumber = Regex.Match(query, @"[0-9０-９]");
            if (item.Source == "国土地理院" && item.Category == 3 && firstNumber.Success && firstNumber.Index >= 4 && item.Name.StartsWith(query.Substring(0, firstNumber.Index), StringComparison.Ordinal)) return 0;
            if (item.Name == query || item.Label == query || item.Label == query + "市" || item.Name == query + "駅") return 0;
            if (item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            var tokens = query.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.All(t => (item.Name + item.Area).IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)) return 2;
            return 3;
        }
        internal static Result[] Rank(Result[] values, string query, GeoCoordinate reference, int category)
        {
            return values.Where(r => category == 0 || r.Category == category).OrderBy(r => Match(r, query))
                .ThenBy(r => reference == null ? 0 : reference.GetDistanceTo(r.Coordinate)).ToArray();
        }
    }
}
