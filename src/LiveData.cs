using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Device.Location;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitCalendar
{
    internal static class LiveData
    {
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        static LiveData()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 OrbitCalendar/1.3");
        }

        public class Prices
        {
            public double?[] open { get; set; }
            public double?[] high { get; set; }
            public double?[] low { get; set; }
            public double?[] close { get; set; }
        }
        public class Indicators { public Prices[] quote { get; set; } }
        public class Series
        {
            public long[] timestamp { get; set; }
            public Indicators indicators { get; set; }
        }
        public class Chart { public Series[] result { get; set; } }
        public class Finance { public Chart chart { get; set; } }
        public class RadarTime
        {
            public string basetime { get; set; }
            public string validtime { get; set; }
            public string[] elements { get; set; }
        }

        public class Candle { public DateTime Day; public double Open, High, Low, Close; }
        public class View
        {
            public string Heading, Stamp, Caption;
            public List<Candle> Bars;
            public BitmapSource Map;
            public View[] Frames;
            public DateTime Fetched = DateTime.UtcNow;
        }

        public static readonly string[] MarketNames = { "日経平均", "トヨタ", "ソニー" };
        public static readonly string[] Symbols = { "^N225", "7203.T", "6758.T" };
        public static readonly string[] Regions = { "現在地", "日本広域", "関東", "関西" };

        internal static async Task<byte[]> Get(string url, CancellationToken token, bool missingAllowed = false)
        {
            using (var response = await http.GetAsync(url, token).ConfigureAwait(false))
            {
                if (missingAllowed && response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        static async Task<T> Json<T>(string url, CancellationToken token)
        {
            var bytes = await Get(url, token).ConfigureAwait(false);
            return new JavaScriptSerializer().Deserialize<T>(System.Text.Encoding.UTF8.GetString(bytes));
        }

        public static async Task<View> Market(int index, CancellationToken token)
        {
            string symbol = Symbols[index];
            var data = await Json<Finance>("https://query1.finance.yahoo.com/v8/finance/chart/" +
                Uri.EscapeDataString(symbol) + "?interval=1d&range=3mo", token).ConfigureAwait(false);
            var s = data.chart.result[0];
            var q = s.indicators.quote[0];
            var bars = new List<Candle>();
            int count = new[] { s.timestamp.Length, q.open.Length, q.close.Length, q.low.Length, q.high.Length }.Min();
            for (int i = 0; i < count; i++)
            {
                if (!q.open[i].HasValue || !q.close[i].HasValue || !q.low[i].HasValue || !q.high[i].HasValue) continue;
                bars.Add(new Candle { Day = new DateTime(1970, 1, 1).AddSeconds(s.timestamp[i]).AddHours(9),
                    Open = q.open[i].Value, Close = q.close[i].Value, High = q.high[i].Value, Low = q.low[i].Value });
            }
            if (bars.Count < 2) throw new InvalidDataException();
            var last = bars[bars.Count - 1];
            var change = last.Close - bars[bars.Count - 2].Close;
            return new View { Bars = bars, Heading = MarketNames[index] + "   " + last.Close.ToString("#,##0.##") + " 円",
                Stamp = last.Day.ToString("yyyy/MM/dd") + " 日足  ·  前日比 " + change.ToString("+#,##0.##;-#,##0.##;0") + " 円",
                Caption = bars[0].Day.ToString("MM/dd") + "  —  " + last.Day.ToString("MM/dd") + "     緑：上昇   赤：下落" };
        }

        static readonly Dictionary<string, byte[]> baseTiles = new Dictionary<string, byte[]>();
        class Tile { public int X, Y; public byte[] Base, Rain; }
        static byte[] Pixels(byte[] png)
        {
            if (png == null) return null;
            using (var input = new MemoryStream(png))
            {
                var src = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var bmp = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                if (bmp.PixelWidth != 256 || bmp.PixelHeight != 256) throw new InvalidDataException();
                var pixels = new byte[256 * 256 * 4];
                bmp.CopyPixels(pixels, 256 * 4, 0);
                return pixels;
            }
        }

        static async Task<Tile> ReadTile(int zoom, int x, int y, RadarTime time, CancellationToken token)
        {
            string path = zoom + "/" + x + "/" + y + ".png";
            byte[] cached;
            lock (baseTiles) baseTiles.TryGetValue(path, out cached);
            var land = cached != null ? Task.FromResult(cached) : Get("https://cyberjapandata.gsi.go.jp/xyz/pale/" + path, token, true);
            var rain = Get("https://www.jma.go.jp/bosai/jmatile/data/nowc/" + time.basetime + "/none/" +
                time.validtime + "/surf/hrpns/" + path, token, true);
            await Task.WhenAll(land, rain).ConfigureAwait(false);
            if (land.Result != null) lock (baseTiles)
            {
                if (baseTiles.Count > 100) baseTiles.Clear();
                baseTiles[path] = land.Result;
            }
            return new Tile { X = x, Y = y, Base = Pixels(land.Result), Rain = Pixels(rain.Result) };
        }

        public static async Task<View> Weather(int region, CancellationToken token)
        {
            var here = region == 0 ? await CurrentPosition(token) : null;
            token.ThrowIfCancellationRequested();
            return await WeatherAt(region, here, token).ConfigureAwait(false);
        }

        static async Task<GeoCoordinate> CurrentPosition(CancellationToken token)
        {
            try
            {
                using (var watcher = new GeoCoordinateWatcher(GeoPositionAccuracy.Default))
                {
                    var result = new TaskCompletionSource<GeoCoordinate>();
                    Action<GeoPosition<GeoCoordinate>> accept = delegate(GeoPosition<GeoCoordinate> fix)
                    {
                        if (!fix.Location.IsUnknown && fix.Timestamp >= DateTimeOffset.UtcNow.AddMinutes(-30))
                            result.TrySetResult(fix.Location);
                    };
                    watcher.PositionChanged += delegate(object sender, GeoPositionChangedEventArgs<GeoCoordinate> e) { accept(e.Position); };
                    watcher.StatusChanged += delegate(object sender, GeoPositionStatusChangedEventArgs e)
                    {
                        if (e.Status == GeoPositionStatus.Disabled) result.TrySetResult(null);
                    };
                    watcher.Start(false);
                    accept(watcher.Position);
                    if (watcher.Permission == GeoPositionPermission.Denied) return null;
                    var timeout = Task.Delay(TimeSpan.FromSeconds(6), token);
                    await Task.WhenAny(result.Task, timeout);
                    token.ThrowIfCancellationRequested();
                    return result.Task.IsCompleted ? result.Task.Result : null;
                }
            }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Runtime.InteropServices.COMException) { return null; }
            catch (System.Security.SecurityException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        static async Task<View> WeatherAt(int region, GeoCoordinate here, CancellationToken token)
        {
            var times = await Json<RadarTime[]>("https://www.jma.go.jp/bosai/jmatile/data/nowc/targetTimes_N1.json", token).ConfigureAwait(false);
            var sequence = times.Where(v => v.elements != null && v.elements.Contains("hrpns") && v.validtime == v.basetime)
                            .OrderByDescending(v => v.validtime, StringComparer.Ordinal).Where((v, i) => i % 2 == 0).Take(7).Reverse().ToArray();
            if (region == 0 && here != null && here.Latitude >= 20 && here.Latitude <= 48 &&
                here.Longitude >= 120 && here.Longitude <= 155)
            {
                var local = await RadarSequence(sequence, "現在地周辺", here.Latitude, here.Longitude, 9, token).ConfigureAwait(false);
                if (local != null) return local;
            }
            int preset = Math.Max(1, region) - 1;
            var map = await RadarSequence(sequence, Regions[preset + 1], new[] { 35.7, 35.7, 34.8 }[preset],
                new[] { 136.6, 139.7, 135.5 }[preset], preset == 0 ? 4 : 7, token).ConfigureAwait(false);
            if (map == null) throw new InvalidDataException();
            return map;
        }

        static async Task<View> RadarSequence(RadarTime[] times, string name, double latitude, double lon, int zoom, CancellationToken token)
        {
            var frames = new List<View>();
            foreach (var time in times)
            {
                token.ThrowIfCancellationRequested();
                var frame = await RadarMap(time, name, latitude, lon, zoom, token).ConfigureAwait(false);
                if (frame != null) frames.Add(frame);
            }
            if (frames.Count == 0) return null;
            var latest = frames[frames.Count - 1];
            latest.Frames = frames.ToArray();
            return latest;
        }

        static async Task<View> RadarMap(RadarTime time, string name, double latitude, double lon, int z, CancellationToken token)
        {
            const int width = 414, height = 230;
            double scale = z == 4 ? .76 : .88;
            double lat = latitude * Math.PI / 180;
            int world = 256 << z;
            double left = world * (lon + 180) / 360 - width / (2 * scale);
            double top = world * (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 - height / (2 * scale);
            var requests = new List<Task<Tile>>();
            for (int y = (int)top / 256; y <= (int)(top + (height - 1) / scale) / 256; y++)
                for (int x = (int)left / 256; x <= (int)(left + (width - 1) / scale) / 256; x++)
                    requests.Add(ReadTile(z, x, y, time, token));
            var tiles = (await Task.WhenAll(requests).ConfigureAwait(false)).ToDictionary(t => t.Y * 1024 + t.X);
            if (!tiles.Values.Any(t => t.Rain != null) || !tiles.Values.Any(t => t.Base != null)) return null;
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int wx = (int)(left + x / scale), wy = (int)(top + y / scale);
                    var tile = tiles[(wy / 256) * 1024 + wx / 256];
                    int j = ((wy % 256) * 256 + wx % 256) * 4, p = (y * width + x) * 4;
                    int r = 9, g = 29, b = 40;
                    if (tile.Base != null && tile.Base[j + 3] > 0 && tile.Base[j] - tile.Base[j + 2] < 12)
                    {
                        double shade = tile.Base[j + 1] / 255.0;
                        r = (int)(36 + 28 * shade); g = (int)(76 + 42 * shade); b = (int)(82 + 44 * shade);
                    }
                    if (tile.Rain == null && (x + y) % 10 < 2) { r = 54; g = 66; b = 73; }
                    if (tile.Rain != null && tile.Rain[j + 3] > 48)
                    { b = tile.Rain[j]; g = tile.Rain[j + 1]; r = tile.Rain[j + 2]; }
                    pixels[p] = (byte)b; pixels[p + 1] = (byte)g; pixels[p + 2] = (byte)r; pixels[p + 3] = 255;
                }
            var map = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            map.Freeze();
            var stamp = DateTime.ParseExact(time.validtime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture).AddHours(9);
            return new View { Map = map, Heading = name + "の雨雲", Stamp = stamp.ToString("yyyy/MM/dd HH:mm") + " 観測（日本時間）",
                Caption = "降水強度    青：弱い雨   黄～赤：強い雨   紫：猛烈な雨" };
        }
    }
}
