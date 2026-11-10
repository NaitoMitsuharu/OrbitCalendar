using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OrbitCalendar
{
    internal sealed class OdptException : Exception { public OdptException(string message) : base(message) { } }

    /// <summary>
    /// Station timetables from the Public Transportation Open Data Center (ODPT, developer.odpt.org).
    /// Sotetsu, Tokyu, JR East and Keikyu are offered only under the Open Data Challenge limited license
    /// (api-challenge.odpt.org, challenge token, licence ends 2027-03-12); the others use the basic license.
    /// Fetched data stays on this PC (timetables.json) and is never redistributed.
    /// </summary>
    internal static class Odpt
    {
        internal static string StandardBase = "https://api.odpt.org/api/v4/";
        internal static string ChallengeBase = "https://api-challenge.odpt.org/api/v4/";
        /// <summary>第13条: the challenge licence ends on this day; the data must then be deleted.</summary>
        internal static DateTime ChallengeEnds = new DateTime(2027, 3, 12);
        static string tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitCalendar", "odpt-tokens.bin");
        static readonly byte[] entropy = Encoding.UTF8.GetBytes("OrbitCalendar.ODPT");

        internal sealed class OperatorInfo
        {
            public string Id, Title; public bool Challenge;
            public string Key { get { return Id.Substring(Id.IndexOf(':') + 1); } }
        }

        public static readonly OperatorInfo[] Operators =
        {
            new OperatorInfo { Id = "odpt.Operator:Sotetsu", Title = "相鉄", Challenge = true },
            new OperatorInfo { Id = "odpt.Operator:Tokyu", Title = "東急", Challenge = true },
            new OperatorInfo { Id = "odpt.Operator:JR-East", Title = "JR東日本", Challenge = true },
            new OperatorInfo { Id = "odpt.Operator:Keikyu", Title = "京急", Challenge = true },
            new OperatorInfo { Id = "odpt.Operator:YokohamaMunicipal", Title = "横浜市営地下鉄", Challenge = false },
            new OperatorInfo { Id = "odpt.Operator:TokyoMetro", Title = "東京メトロ", Challenge = false },
            new OperatorInfo { Id = "odpt.Operator:Toei", Title = "都営", Challenge = false },
            new OperatorInfo { Id = "odpt.Operator:TWR", Title = "りんかい線", Challenge = false },
        };

        public static OperatorInfo Operator(string id) { return Operators.FirstOrDefault(o => o.Id == id); }

        /// <summary>Whether a station/railway/train id belongs to a challenge-only operator.</summary>
        static bool ChallengeId(string id)
        {
            return Operators.Any(o => o.Challenge && (id ?? "").Contains(":" + o.Key + "."));
        }

        public static bool ChallengeExpired(DateTime today) { return today.Date > ChallengeEnds; }

        // ---------- Access tokens (stored with Windows DPAPI for the current user only) ----------

        sealed class Tokens { public string Standard { get; set; } public string Challenge { get; set; } }
        static Tokens tokens;

        static Tokens LoadTokens()
        {
            if (tokens != null) return tokens;
            tokens = new Tokens();
            try
            {
                if (File.Exists(tokenPath))
                {
                    var plain = ProtectedData.Unprotect(File.ReadAllBytes(tokenPath), entropy, DataProtectionScope.CurrentUser);
                    tokens = new JavaScriptSerializer().Deserialize<Tokens>(Encoding.UTF8.GetString(plain)) ?? new Tokens();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (CryptographicException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return tokens;
        }

        public static string Token(bool challenge) { var t = LoadTokens(); return (challenge ? t.Challenge : t.Standard) ?? ""; }
        public static bool HasToken(bool challenge) { return Token(challenge).Length > 0; }

        public static void SaveTokens(string standard, string challenge)
        {
            var next = new Tokens { Standard = (standard ?? "").Trim(), Challenge = (challenge ?? "").Trim() };
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(next)), entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(tokenPath));
            File.WriteAllBytes(tokenPath, data);
            tokens = next;
        }

        // ---------- HTTP ----------

        static HttpClient client;
        static HttpClient Client
        {
            get
            {
                if (client == null)
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    client = new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 64 * 1024 * 1024 };
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitCalendar");
                }
                return client;
            }
        }

        static async Task<object[]> Get(bool challenge, string type, string query, CancellationToken cancel)
        {
            if (challenge && ChallengeExpired(DateTime.Today)) throw new OdptException("チャレンジ限定データの利用期間（2027年3月12日まで）が終了しました");
            string token = Token(challenge);
            if (token.Length == 0) throw new OdptException(challenge ? "チャレンジ用のアクセストークンが未設定です" : "ODPTのアクセストークンが未設定です");
            string url = (challenge ? ChallengeBase : StandardBase) + type + "?" + (string.IsNullOrEmpty(query) ? "" : query + "&") + "acl:consumerKey=" + Uri.EscapeDataString(token);
            HttpResponseMessage response;
            try { response = await Client.GetAsync(url, cancel); }
            catch (HttpRequestException) { throw new OdptException("ODPTに接続できませんでした。通信状態を確認してください"); }
            catch (TaskCanceledException) { if (cancel.IsCancellationRequested) throw; throw new OdptException("ODPTの応答がありませんでした。時間をおいてお試しください"); }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    throw new OdptException(challenge ? "チャレンジ用のアクセストークンが無効です（相鉄・東急・JR東日本・京急）" : "ODPTのアクセストークンが無効です");
                if (!response.IsSuccessStatusCode) throw new OdptException("ODPTからデータを取得できませんでした（" + (int)response.StatusCode + "）");
                string json = await response.Content.ReadAsStringAsync();
                try { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as object[] ?? new object[0]; }
                catch (ArgumentException) { throw new OdptException("ODPTの応答を読み取れませんでした"); }
                catch (InvalidOperationException) { throw new OdptException("ODPTの応答を読み取れませんでした"); }
            }
        }

        static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
        static string Text(Dictionary<string, object> item, string key) { object value; return item != null && item.TryGetValue(key, out value) ? value as string : null; }
        static IEnumerable<string> Strings(Dictionary<string, object> item, string key)
        {
            object value;
            if (item == null || !item.TryGetValue(key, out value) || value == null) return Enumerable.Empty<string>();
            var list = value as object[];
            return list != null ? list.OfType<string>() : value is string ? new[] { (string)value } : Enumerable.Empty<string>();
        }

        /// <summary>The Japanese title: dc:title, else the *Title.ja field, else the last part of the id.</summary>
        static string Title(Dictionary<string, object> item, string titleField)
        {
            string title = Text(item, "dc:title");
            if (string.IsNullOrWhiteSpace(title) && titleField != null) title = Text(Map(item != null && item.ContainsKey(titleField) ? item[titleField] : null), "ja");
            if (string.IsNullOrWhiteSpace(title)) title = Tail(Text(item, "owl:sameAs"));
            return (title ?? "").Trim();
        }

        static string Tail(string id) { if (string.IsNullOrEmpty(id)) return ""; int dot = id.LastIndexOf('.'), colon = id.LastIndexOf(':'); return id.Substring(Math.Max(dot, colon) + 1); }

        // ---------- Titles (cached per operator for the session) ----------

        static readonly Dictionary<string, Dictionary<string, string>> titles = new Dictionary<string, Dictionary<string, string>>();

        static async Task<Dictionary<string, string>> Titles(string type, string titleField, string operatorId, bool challenge, CancellationToken cancel)
        {
            string key = type + "|" + operatorId + "|" + challenge;
            Dictionary<string, string> map;
            if (titles.TryGetValue(key, out map)) return map;
            map = new Dictionary<string, string>();
            foreach (var item in (await Get(challenge, type, operatorId == null ? null : "odpt:operator=" + operatorId, cancel)).Select(Map).Where(i => i != null))
            {
                string id = Text(item, "owl:sameAs");
                if (id != null) map[id] = Title(item, titleField);
            }
            titles[key] = map;
            return map;
        }

        internal sealed class StationHit
        {
            public string Id, Title, RailwayId, RailwayTitle, OperatorId;
            public override string ToString() { return Title + "  " + RailwayTitle; }
        }

        public static async Task<List<StationHit>> FindStations(OperatorInfo op, string query, CancellationToken cancel)
        {
            query = (query ?? "").Trim().TrimEnd('駅');
            var railways = await Titles("odpt:Railway", "odpt:railwayTitle", op.Id, op.Challenge, cancel);
            var stations = await Get(op.Challenge, "odpt:Station", "odpt:operator=" + op.Id, cancel);
            var hits = new List<StationHit>();
            foreach (var item in stations.Select(Map).Where(i => i != null))
            {
                string title = Title(item, "odpt:stationTitle");
                if (query.Length > 0 && title.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string railway = Text(item, "odpt:railway");
                string railwayTitle;
                if (railway == null || !railways.TryGetValue(railway, out railwayTitle)) railwayTitle = Tail(railway);
                hits.Add(new StationHit { Id = Text(item, "owl:sameAs"), Title = title, RailwayId = railway, RailwayTitle = railwayTitle, OperatorId = op.Id });
            }
            return hits.Where(h => h.Id != null).OrderBy(h => h.Title == query ? 0 : h.Title.StartsWith(query) ? 1 : 2).ThenBy(h => h.Title.Length).ThenBy(h => h.RailwayTitle).ToList();
        }

        internal sealed class DirectionChoice
        {
            public string RailDirectionId, DirectionTitle, Destination, Weekday, Holiday;
            public int WeekdayCount, HolidayCount;
            public string Label { get { return (string.IsNullOrEmpty(Destination) ? DirectionTitle : Destination + "方面") + (string.IsNullOrEmpty(DirectionTitle) || string.IsNullOrEmpty(Destination) ? "" : "（" + DirectionTitle + "）"); } }
        }

        static bool IsWeekday(string calendar) { return calendar == "odpt.Calendar:Weekday"; }
        static int HolidayRank(string calendar)
        {
            switch (calendar)
            {
                case "odpt.Calendar:SaturdayHoliday": return 0;
                case "odpt.Calendar:Holiday": return 1;
                case "odpt.Calendar:Sunday": return 2;
                case "odpt.Calendar:Saturday": return 3;
                default: return -1;
            }
        }

        /// <summary>Every direction served at the station on that railway, converted to the timetable text format.</summary>
        public static async Task<List<DirectionChoice>> Directions(OperatorInfo op, StationHit station, CancellationToken cancel)
        {
            var tables = (await Get(op.Challenge, "odpt:StationTimetable", "odpt:station=" + station.Id, cancel)).Select(Map).Where(t => t != null)
                .Where(t => station.RailwayId == null || Text(t, "odpt:railway") == null || Text(t, "odpt:railway") == station.RailwayId).ToList();
            var directionTitles = await SafeTitles("odpt:RailDirection", "odpt:railDirectionTitle", null, op.Challenge, cancel);
            var trainTypes = await SafeTitles("odpt:TrainType", "odpt:trainTypeTitle", op.Id, op.Challenge, cancel);
            var stationTitles = await StationTitles(tables, op, cancel);
            var choices = new List<DirectionChoice>();
            foreach (var group in tables.GroupBy(t => Text(t, "odpt:railDirection") ?? ""))
            {
                var weekday = group.FirstOrDefault(t => IsWeekday(Text(t, "odpt:calendar")));
                var holiday = group.Where(t => HolidayRank(Text(t, "odpt:calendar")) >= 0).OrderBy(t => HolidayRank(Text(t, "odpt:calendar"))).FirstOrDefault();
                if (weekday == null && holiday == null) continue;
                string directionTitle;
                if (!directionTitles.TryGetValue(group.Key, out directionTitle)) directionTitle = DirectionFallback(group.Key);
                int weekdayCount, holidayCount;
                string commonDestination;
                var choice = new DirectionChoice
                {
                    RailDirectionId = group.Key, DirectionTitle = directionTitle,
                    Weekday = ToText(weekday, trainTypes, stationTitles, out weekdayCount, out commonDestination),
                };
                string holidayDestination;
                choice.Holiday = ToText(holiday, trainTypes, stationTitles, out holidayCount, out holidayDestination);
                choice.WeekdayCount = weekdayCount; choice.HolidayCount = holidayCount;
                choice.Destination = commonDestination ?? holidayDestination;
                choices.Add(choice);
            }
            return choices.OrderBy(c => c.DirectionTitle, StringComparer.Ordinal).ToList();
        }

        static string DirectionFallback(string id)
        {
            string tail = Tail(id);
            switch (tail) { case "Inbound": return "上り"; case "Outbound": return "下り"; case "Northbound": return "北行"; case "Southbound": return "南行"; case "Eastbound": return "東行"; case "Westbound": return "西行"; default: return tail; }
        }

        static async Task<Dictionary<string, string>> SafeTitles(string type, string field, string operatorId, bool challenge, CancellationToken cancel)
        {
            try { return await Titles(type, field, operatorId, challenge, cancel); }
            catch (OdptException) { return new Dictionary<string, string>(); }
        }

        /// <summary>Titles for every destination station, including through-service stations of other operators.</summary>
        static async Task<Dictionary<string, string>> StationTitles(List<Dictionary<string, object>> tables, OperatorInfo op, CancellationToken cancel)
        {
            var ids = tables.SelectMany(t => Rows(t)).SelectMany(r => Strings(r, "odpt:destinationStation")).Distinct().ToList();
            var map = new Dictionary<string, string>(await SafeTitles("odpt:Station", "odpt:stationTitle", op.Id, op.Challenge, cancel));
            foreach (var group in ids.Where(id => !map.ContainsKey(id)).GroupBy(ChallengeId))
            {
                foreach (var chunk in group.Select((id, i) => new { id, i }).GroupBy(x => x.i / 40, x => x.id))
                {
                    try
                    {
                        foreach (var item in (await Get(group.Key, "odpt:Station", "owl:sameAs=" + string.Join(",", chunk), cancel)).Select(Map).Where(i => i != null))
                        {
                            string id = Text(item, "owl:sameAs");
                            if (id != null) map[id] = Title(item, "odpt:stationTitle");
                        }
                    }
                    catch (OdptException) { }
                }
            }
            return map;
        }

        static IEnumerable<Dictionary<string, object>> Rows(Dictionary<string, object> table)
        {
            object rows;
            if (table == null || !table.TryGetValue("odpt:stationTimetableObject", out rows)) return Enumerable.Empty<Dictionary<string, object>>();
            return (rows as object[] ?? new object[0]).Select(Map).Where(r => r != null);
        }

        /// <summary>
        /// Converts one ODPT station timetable to the "H: mm種別・行き先 …" text the timetable view reads,
        /// keeping the departure order so after-midnight trains stay at the end of the service day.
        /// </summary>
        static string ToText(Dictionary<string, object> table, Dictionary<string, string> trainTypes, Dictionary<string, string> stations, out int count, out string commonDestination)
        {
            count = 0; commonDestination = null;
            if (table == null) return null;
            var lines = new List<KeyValuePair<int, List<string>>>();
            var destinations = new Dictionary<string, int>();
            foreach (var row in Rows(table))
            {
                string time = Text(row, "odpt:departureTime") ?? Text(row, "odpt:arrivalTime");
                TimeSpan parsed;
                if (time == null || !TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out parsed)) continue;
                string type, destination = null;
                string typeId = Text(row, "odpt:trainType");
                if (typeId == null || !trainTypes.TryGetValue(typeId, out type)) type = typeId == null ? null : Tail(typeId);
                var destIds = Strings(row, "odpt:destinationStation").ToList();
                if (destIds.Count > 0)
                {
                    destination = string.Join("/", destIds.Select(id => { string t; return stations.TryGetValue(id, out t) ? t : Tail(id); }));
                    int n; destinations.TryGetValue(destination, out n); destinations[destination] = n + 1;
                }
                string label = string.Join("・", new[] { type, destination }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Replace(" ", "").Replace("　", "")));
                int hour = parsed.Hours;
                if (lines.Count == 0 || lines[lines.Count - 1].Key != hour) lines.Add(new KeyValuePair<int, List<string>>(hour, new List<string>()));
                lines[lines.Count - 1].Value.Add(parsed.Minutes.ToString("00", CultureInfo.InvariantCulture) + label);
                count++;
            }
            if (destinations.Count > 0) commonDestination = destinations.OrderByDescending(d => d.Value).First().Key;
            return count == 0 ? null : string.Join("\n", lines.Select(l => l.Key + ": " + string.Join(" ", l.Value)));
        }

        /// <summary>Fetches the latest timetable for a saved ODPT entry and stores it. Returns false when the direction is gone.</summary>
        public static async Task<bool> Refresh(TimetableEntry entry, CancellationToken cancel)
        {
            var op = Operator(entry.OdptOperator);
            if (op == null) throw new OdptException("この事業者には対応していません");
            var station = new StationHit { Id = entry.OdptStation, RailwayId = entry.OdptRailway, Title = entry.Station, OperatorId = op.Id };
            var choice = (await Directions(op, station, cancel)).FirstOrDefault(c => c.RailDirectionId == entry.OdptDirection);
            if (choice == null) return false;
            Timetables.UpdateFetched(entry.Id, choice.Weekday, choice.Holiday);
            return true;
        }

        /// <summary>For tests: forget cached titles and tokens.</summary>
        internal static void Reset() { titles.Clear(); tokens = null; client = null; }
    }
}
