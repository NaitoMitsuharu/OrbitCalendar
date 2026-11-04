using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
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
    internal static partial class LiveData
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
        public class Meta { public int gmtoffset { get; set; } }
        public class Series
        {
            public Meta meta { get; set; }
            public long[] timestamp { get; set; }
            public Indicators indicators { get; set; }
        }
        public class Chart { public Series[] result { get; set; } }
        public class Finance { public Chart chart { get; set; } }
        public class RadarTime
        {
            public string basetime { get; set; }
            public string validtime { get; set; }
            public string member { get; set; }
            public string[] elements { get; set; }
            public string Product { get; set; }
            public DateTime OriginUtc { get; set; }
        }

        public class DailyCodes { public int[] weather_code { get; set; } }
        public class DailyForecast { public DailyCodes daily { get; set; } }

        public static async Task<int?> TodayWeather()
        {
            var here = SavedPlace.Coordinate;
            if (here == null) here = DateTime.UtcNow - lastPositionTime < TimeSpan.FromMinutes(30) ? lastPosition : null;
            if (here == null) here = await CurrentPosition(CancellationToken.None);
            if (here == null) return null;
            if (SavedPlace.Coordinate == null) { lastPosition = here; lastPositionTime = DateTime.UtcNow; }
            int decimals = SavedPlace.Coordinate == null ? 2 : 6;
            string lat = Math.Round(here.Latitude, decimals).ToString(CultureInfo.InvariantCulture);
            string lon = Math.Round(here.Longitude, decimals).ToString(CultureInfo.InvariantCulture);
            var data = await Json<DailyForecast>("https://api.open-meteo.com/v1/forecast?latitude=" + lat + "&longitude=" + lon +
                "&daily=weather_code&forecast_days=1&timezone=Asia%2FTokyo", CancellationToken.None).ConfigureAwait(false);
            if (data.daily == null || data.daily.weather_code == null || data.daily.weather_code.Length == 0) throw new InvalidDataException();
            return data.daily.weather_code[0];
        }

        public class Candle { public DateTime Day; public double Open, High, Low, Close; }
        public class View
        {
            public string Heading, Stamp, Caption, MarketName, MarketValue;
            public List<Candle> Bars;
            public BitmapSource Map;
            public Point? LocationPoint;
            public View[] Frames;
            public bool IsFund, Forecast;
            public bool HourlyRain;
            public DateTime ValidUtc, OriginUtc;
            public Viewport Viewport;
            public double AccuracyPixels;
            public string LocationHint;
            public DateTime Fetched = DateTime.UtcNow;
        }

        public static string[] Regions
        {
            get { return new[] { SavedPlace.Coordinate == null ? "現在地" : "保存地点", "日本広域", "関東", "関西" }; }
        }
        public const int MapWidth = 356, MapHeight = 190;

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

        public static Task<View> Market(Instrument item, CancellationToken token) { return MarketPeriod(item, 3, token); }

        public static async Task<View> MarketPeriod(Instrument item, int months, CancellationToken token)
        {
            if (months != 1 && months != 3 && months != 6 && months != 12 && months != 60) throw new ArgumentOutOfRangeException("months");
            if (!item.Supported) throw new NotSupportedException();
            if (item.Provider == "mufg") return await FundPeriod(item, months, token).ConfigureAwait(false);
            if (item.Provider == "sbi") return await FundPrices.SbiPeriod(item, months, token).ConfigureAwait(false);
            string symbol = item.Key;
            string range = months < 12 ? months + "mo" : months / 12 + "y";
            string interval = months > 12 ? "1wk" : "1d";
            var data = await Json<Finance>("https://query1.finance.yahoo.com/v8/finance/chart/" +
                Uri.EscapeDataString(symbol) + "?interval=" + interval + "&range=" + range, token).ConfigureAwait(false);
            var s = data.chart.result[0];
            var q = s.indicators.quote[0];
            var bars = new List<Candle>();
            int count = new[] { s.timestamp.Length, q.open.Length, q.close.Length, q.low.Length, q.high.Length }.Min();
            for (int i = 0; i < count; i++)
            {
                if (!q.open[i].HasValue || !q.close[i].HasValue || !q.low[i].HasValue || !q.high[i].HasValue) continue;
                bars.Add(new Candle { Day = new DateTime(1970, 1, 1).AddSeconds(s.timestamp[i] + s.meta.gmtoffset),
                    Open = q.open[i].Value, Close = q.close[i].Value, High = q.high[i].Value, Low = q.low[i].Value });
            }
            if (bars.Count < 2) throw new InvalidDataException();
            var last = bars[bars.Count - 1];
            var change = last.Close - bars[bars.Count - 2].Close;
            return new View { Bars = bars, MarketName = item.Label, MarketValue = last.Close.ToString("#,##0.##") + " " + item.Unit, Heading = item.Label + "   " + last.Close.ToString("#,##0.##") + (" " + item.Unit),
                Stamp = last.Day.ToString("yyyy/MM/dd") + (months > 12 ? " 週足  ·  前週比 " : " 日足  ·  前日比 ") + change.ToString("+#,##0.##;-#,##0.##;0") + (" " + item.Unit),
                Caption = bars[0].Day.ToString(months > 6 ? "yyyy/MM/dd" : "MM/dd") + "  —  " + last.Day.ToString(months > 6 ? "yyyy/MM/dd" : "MM/dd") + "     緑：上昇   赤：下落" };
        }

        static async Task<View> FundPeriod(Instrument item, int months, CancellationToken token)
        {
            var bytes = await Get("https://www.am.mufg.jp/fund_file/setteirai/" + item.Key + ".csv", token).ConfigureAwait(false);
            var bars = new List<Candle>();
            using (var reader = new StringReader(System.Text.Encoding.GetEncoding(932).GetString(bytes)))
            {
                reader.ReadLine(); reader.ReadLine();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var row = line.Split(',');
                    DateTime day; double value;
                    if (row.Length < 2 || !DateTime.TryParseExact(row[0], "yyyy/MM/dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out day) || !double.TryParse(row[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value)) continue;
                    bars.Add(new Candle { Day = day, Open = value, Close = value, High = value, Low = value });
                }
            }
            return FundPrices.CreatePeriodView(item, bars, months);
        }

        public class Viewport
        {
            public double Latitude, Longitude;
            public int Zoom;
            public GeoCoordinate Marker;
            public string Name;
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

        static async Task<Tile> ReadTile(int zoom, int x, int y, RadarTime time, CancellationToken token, Dictionary<string, Task<byte[]>> requests)
        {
            string path = zoom + "/" + x + "/" + y + ".png";
            byte[] cached;
            lock (baseTiles) baseTiles.TryGetValue(path, out cached);
            var land = cached != null ? Task.FromResult(cached) : Get("https://cyberjapandata.gsi.go.jp/xyz/pale/" + path, token, true);
            var rain = RainPixels(zoom, x, y, time, token, requests);
            await Task.WhenAll(land, rain).ConfigureAwait(false);
            if (land.Result != null) lock (baseTiles)
            {
                if (baseTiles.Count > 100) baseTiles.Clear();
                baseTiles[path] = land.Result;
            }
            return new Tile { X = x, Y = y, Base = Pixels(land.Result), Rain = rain.Result };
        }

        static GeoCoordinate lastPosition;
        static DateTime lastPositionTime;

        public static async Task<View> Weather(int region, CancellationToken token, Viewport viewport = null)
        {
            var here = SavedPlace.Coordinate;
            if (here == null && region == 0 && viewport == null) here = await CurrentPosition(token);
            token.ThrowIfCancellationRequested();
            if (here != null && SavedPlace.Coordinate == null) { lastPosition = here; lastPositionTime = DateTime.UtcNow; }
            return await WeatherAt(region, here, token, viewport).ConfigureAwait(false);
        }

        static async Task<GeoCoordinate> CurrentPosition(CancellationToken token)
        {
            try
            {
                using (var watcher = new GeoCoordinateWatcher(GeoPositionAccuracy.Default))
                {
                    var result = new TaskCompletionSource<GeoCoordinate>();
                    GeoCoordinate best = null;
                    Action<GeoPosition<GeoCoordinate>> accept = delegate(GeoPosition<GeoCoordinate> fix)
                    {
                        if (!fix.Location.IsUnknown && fix.Timestamp >= DateTimeOffset.UtcNow.AddMinutes(-5))
                        {
                            if (best == null || double.IsNaN(best.HorizontalAccuracy) || fix.Location.HorizontalAccuracy < best.HorizontalAccuracy) best = fix.Location;
                            if (best.HorizontalAccuracy <= 100) result.TrySetResult(best);
                        }
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
                    return result.Task.IsCompleted ? result.Task.Result : best;
                }
            }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Runtime.InteropServices.COMException) { return null; }
            catch (System.Security.SecurityException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        static async Task<View> WeatherAt(int region, GeoCoordinate here, CancellationToken token, Viewport viewport = null)
        {
            var sequence = await FutureRainTimes(token).ConfigureAwait(false);
            if (viewport != null) return await RadarSequence(sequence, viewport.Name, viewport.Latitude, viewport.Longitude,
                viewport.Zoom, token, viewport.Marker).ConfigureAwait(false);
            if (region == 0 && here != null && here.Latitude >= 20 && here.Latitude <= 48 &&
                here.Longitude >= 120 && here.Longitude <= 155)
            {
                var local = await RadarSequence(sequence, SavedPlace.Coordinate == null ? "現在地周辺" : SavedPlace.DisplayName + "周辺", here.Latitude, here.Longitude, MapScale.ForRegion(0), token, here).ConfigureAwait(false);
                if (local != null) return local;
            }
            int preset = Math.Max(1, region) - 1;
            var map = await RadarSequence(sequence, Regions[preset + 1], new[] { 35.7, 35.7, 34.8 }[preset],
                new[] { 136.6, 139.7, 135.5 }[preset], MapScale.ForRegion(preset + 1), token, here ?? (DateTime.UtcNow - lastPositionTime < TimeSpan.FromMinutes(30) ? lastPosition : null)).ConfigureAwait(false);
            if (map == null) throw new InvalidDataException();
            return map;
        }

        static async Task<View> RadarSequence(RadarTime[] times, string name, double latitude, double lon, int zoom, CancellationToken token, GeoCoordinate marker = null)
        {
            var frames = new List<View>();
            foreach (var time in times)
            {
                token.ThrowIfCancellationRequested();
                View frame = null;
                try { frame = await RadarMap(time, name, latitude, lon, zoom, token, marker).ConfigureAwait(false); }
                catch (HttpRequestException) { if (time.basetime == time.validtime) throw; }
                catch (TaskCanceledException) { token.ThrowIfCancellationRequested(); if (time.basetime == time.validtime) throw; }
                if (frame != null) frame.Viewport = new Viewport { Name = name, Latitude = latitude, Longitude = lon, Zoom = zoom, Marker = marker };
                if (frame != null) frames.Add(frame);
            }
            if (!frames.Any(frame => !frame.Forecast)) return null;
            var latest = frames.LastOrDefault(frame => !frame.Forecast) ?? frames[0];
            var timeline = frames.ToArray();
            foreach (var frame in frames) frame.Frames = timeline;
            return latest;
        }

        static async Task<View> RadarMap(RadarTime time, string name, double latitude, double lon, int z, CancellationToken token, GeoCoordinate marker = null)
        {
            const int width = MapWidth, height = MapHeight;
            double scale = z == 4 ? .76 : .88;
            double lat = latitude * Math.PI / 180;
            int world = 256 << z;
            double left = world * (lon + 180) / 360 - width / (2 * scale);
            double top = world * (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 - height / (2 * scale);
            var rainRequests = new Dictionary<string, Task<byte[]>>();
            var requests = new List<Task<Tile>>();
            for (int y = (int)top / 256; y <= (int)(top + (height - 1) / scale) / 256; y++)
                for (int x = (int)left / 256; x <= (int)(left + (width - 1) / scale) / 256; x++)
                    requests.Add(ReadTile(z, x, y, time, token, rainRequests));
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
            Point? position = null;
            double accuracyPixels = 0;
            string locationHint = "測位なし";
            if (marker != null && !marker.IsUnknown)
            {
                double phi = marker.Latitude * Math.PI / 180;
                double mx = (world * (marker.Longitude + 180) / 360 - left) * scale;
                double my = (world * (1 - Math.Log(Math.Tan(phi) + 1 / Math.Cos(phi)) / Math.PI) / 2 - top) * scale;
                if (mx >= 0 && mx < width && my >= 0 && my < height)
                {
                    position = new Point(mx, my);
                    double accuracy = marker.HorizontalAccuracy;
                    if (SavedPlace.Coordinate != null) locationHint = SavedPlace.DisplayName + "（保存した基準地点）";
                    if (!double.IsNaN(accuracy) && !double.IsInfinity(accuracy))
                    {
                        accuracyPixels = accuracy / (Math.Cos(phi) * 40075016.686 / world) * scale;
                        locationHint = "現在地 推定±" + (accuracy < 1000 ? Math.Round(accuracy) + "m" : (accuracy / 1000).ToString("0.0") + "km");
                    }
                    else locationHint = SavedPlace.Coordinate == null ? "現在地 精度不明" : SavedPlace.DisplayName + "（保存した基準地点）";
                }
                else locationHint = SavedPlace.Coordinate == null ? "現在地は表示範囲外" : SavedPlace.DisplayName + "は表示範囲外";
            }
            var map = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            map.Freeze();
            DateTime valid = RainUtc(time.validtime), stamp = valid.AddHours(9);
            bool hourly = time.Product == "rasrf";
            return new View { Map = map, LocationPoint = position, AccuracyPixels = accuracyPixels, LocationHint = locationHint,
                Forecast = time.basetime != time.validtime, HourlyRain = hourly, ValidUtc = valid, OriginUtc = time.OriginUtc,
                Heading = name + "の雨雲", Stamp = stamp.ToString("yyyy/MM/dd HH:mm") +
                    (time.basetime == time.validtime ? " 観測（日本時間）" : hourly ? " 予測・1時間降水量" : " 予測・降水強度"),
                Caption = hourly ? stamp.AddHours(-1).ToString("HH:mm") + "〜" + stamp.ToString("HH:mm") + " の1時間降水量（mm）" :
                    "降水強度（mm/h） 青：弱い雨   黄〜赤：強い雨   紫：猛烈な雨" };

        }
    }
}
