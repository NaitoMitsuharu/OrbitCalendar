using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace OrbitCalendar
{
    /// <summary>A station timetable the user typed in (one station, one line, one direction).</summary>
    internal sealed class TimetableEntry
    {
        public string Id { get; set; }
        public string Station { get; set; }
        public string Line { get; set; }
        public string Direction { get; set; }
        /// <summary>The text exactly as typed, so editing shows it again unchanged.</summary>
        public string Weekday { get; set; }
        public string Holiday { get; set; }
        /// <summary>ODPT source (null for timetables typed by hand). Weekday/Holiday then hold the converted data.</summary>
        public string OdptOperator { get; set; }
        public string OdptStation { get; set; }
        public string OdptRailway { get; set; }
        public string OdptDirection { get; set; }
        public string Fetched { get; set; }
        public bool FromOdpt { get { return !string.IsNullOrEmpty(OdptStation); } }
        public bool ChallengeData { get { var op = Odpt.Operator(OdptOperator); return op != null && op.Challenge; } }
        public DateTime? FetchedAt
        {
            get { DateTime value; return DateTime.TryParse(Fetched, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value) ? value : (DateTime?)null; }
        }

        public string Heading
        {
            get { return string.Join("  ", new[] { Station, Line, Direction }.Where(s => !string.IsNullOrWhiteSpace(s))); }
        }
    }

    internal static class Timetables
    {
        internal struct Departure
        {
            /// <summary>Minutes after midnight of the service day; after-midnight trains are 24:00 or later.</summary>
            public int Minutes;
            public string Label;
        }

        internal sealed class Parsed
        {
            public List<Departure> Departures = new List<Departure>();
            public List<int> Unreadable = new List<int>();
        }

        internal struct Upcoming
        {
            public DateTime Time;
            public string Label;
        }

        static string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitCalendar");
        static string path = Path.Combine(folder, "timetables.json");
        static List<TimetableEntry> items;
        sealed class State { public List<TimetableEntry> Items { get; set; } }

        public static TimetableEntry[] All { get { if (items == null) Reload(); return items.ToArray(); } }
        public static TimetableEntry Find(string id) { if (items == null) Reload(); return items.FirstOrDefault(i => i.Id == id); }

        internal static void Reload()
        {
            items = new List<TimetableEntry>();
            try
            {
                if (File.Exists(path))
                {
                    var state = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<State>(File.ReadAllText(path, Encoding.UTF8));
                    if (state != null && state.Items != null) items = state.Items.Where(i => i != null && !string.IsNullOrWhiteSpace(i.Id)).ToList();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }

        static void Save()
        {
            Directory.CreateDirectory(folder);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Serialize(new State { Items = items }), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }

        /// <summary>Adds or replaces an entry whose times come from ODPT.</summary>
        public static string PutOdpt(string id, string station, string line, string direction, string weekday, string holiday, string operatorId, string stationId, string railwayId, string directionId)
        {
            string saved = Put(id, station, line, direction, weekday, holiday);
            var entry = Find(saved);
            var previous = new[] { entry.OdptOperator, entry.OdptStation, entry.OdptRailway, entry.OdptDirection, entry.Fetched };
            entry.OdptOperator = operatorId; entry.OdptStation = stationId; entry.OdptRailway = railwayId; entry.OdptDirection = directionId;
            entry.Fetched = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            try { Save(); }
            catch { entry.OdptOperator = previous[0]; entry.OdptStation = previous[1]; entry.OdptRailway = previous[2]; entry.OdptDirection = previous[3]; entry.Fetched = previous[4]; throw; }
            return saved;
        }

        /// <summary>Stores a fresh download for an ODPT entry.</summary>
        public static void UpdateFetched(string id, string weekday, string holiday)
        {
            var entry = Find(id);
            if (entry == null) return;
            var previous = new[] { entry.Weekday, entry.Holiday, entry.Fetched };
            entry.Weekday = Clean(weekday); entry.Holiday = Clean(holiday); entry.Fetched = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            try { Save(); }
            catch { entry.Weekday = previous[0]; entry.Holiday = previous[1]; entry.Fetched = previous[2]; throw; }
        }

        /// <summary>
        /// Challenge-licence data must be deleted when the licence ends (2027-03-12). Removes the downloaded
        /// times of those entries and keeps only the station name so the user can see what was removed.
        /// </summary>
        public static int PurgeExpiredChallengeData(DateTime today)
        {
            if (!Odpt.ChallengeExpired(today)) return 0;
            if (items == null) Reload();
            int purged = 0;
            foreach (var entry in items.Where(e => e.FromOdpt && e.ChallengeData && (e.Weekday != null || e.Holiday != null)))
            {
                entry.Weekday = null; entry.Holiday = null; purged++;
            }
            if (purged > 0) { try { Save(); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            return purged;
        }

        /// <summary>Adds (id == null) or replaces an entry. Returns its id.</summary>
        public static string Put(string id, string station, string line, string direction, string weekday, string holiday)
        {
            if (items == null) Reload();
            var previous = items.ToList();
            var entry = id == null ? null : items.FirstOrDefault(i => i.Id == id);
            if (entry == null) { entry = new TimetableEntry { Id = Guid.NewGuid().ToString("N") }; items.Add(entry); }
            entry.Station = station.Trim(); entry.Line = (line ?? "").Trim(); entry.Direction = (direction ?? "").Trim();
            entry.Weekday = Clean(weekday); entry.Holiday = Clean(holiday);
            try { Save(); }
            catch { items = previous; Reload(); throw; }
            return entry.Id;
        }

        public static void Delete(string id)
        {
            if (items == null) Reload();
            var previous = items.ToList();
            items.RemoveAll(i => i.Id == id);
            try { Save(); }
            catch { items = previous; throw; }
        }

        public static void Move(string id, int offset)
        {
            if (items == null) Reload();
            int index = items.FindIndex(i => i.Id == id), target = index + offset;
            if (index < 0 || target < 0 || target >= items.Count) return;
            var entry = items[index]; items.RemoveAt(index); items.Insert(target, entry);
            try { Save(); }
            catch { items.RemoveAt(target); items.Insert(index, entry); throw; }
        }

        static string Clean(string text) { text = (text ?? "").Replace("\r\n", "\n").Trim(); return text.Length == 0 ? null : text; }

        static readonly Regex hourHead = new Regex(@"^\s*(\d{1,2})\s*(?:時台|時|:|：)?(?:\s+|(?<=[時:：]))(.*)$", RegexOptions.Compiled);
        static readonly Regex minuteToken = new Regex(@"^(\d{1,2})(.*)$", RegexOptions.Compiled);
        static readonly char[] separators = { ' ', '\t', ',', '、', '，', '　' };

        /// <summary>
        /// Reads one line per hour: the hour, then the minutes, each optionally followed by a short label
        /// (train type or destination), e.g. "7: 04 13急 21海老名" or "7時 04 13 21". Hours 0–3 that come after
        /// later hours are treated as after midnight (24–27) of the same service day.
        /// </summary>
        public static Parsed Parse(string text)
        {
            var result = new Parsed();
            if (string.IsNullOrWhiteSpace(text)) return result;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int lastHour = -1;
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n].Trim();
                if (line.Length == 0) continue;
                var head = hourHead.Match(line);
                int hour;
                if (!head.Success || !int.TryParse(head.Groups[1].Value, out hour) || hour > 29) { result.Unreadable.Add(n + 1); continue; }
                if (hour < 4 && lastHour >= 4) hour += 24;
                var tokens = head.Groups[2].Value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                var minutes = new List<Departure>();
                foreach (var token in tokens)
                {
                    var match = minuteToken.Match(token);
                    int minute;
                    if (match.Success && int.TryParse(match.Groups[1].Value, out minute) && minute < 60)
                        minutes.Add(new Departure { Minutes = hour * 60 + minute, Label = match.Groups[2].Value.Trim('(', ')', '（', '）', '/', '／').Trim() });
                    else if (minutes.Count > 0 && !token.Any(char.IsDigit))
                    {
                        // A label written with a space after its minute ("13 急") belongs to that minute.
                        var last = minutes[minutes.Count - 1];
                        last.Label = (last.Label + token.Trim('(', ')', '（', '）', '/', '／')).Trim();
                        minutes[minutes.Count - 1] = last;
                    }
                }
                if (minutes.Count == 0) { result.Unreadable.Add(n + 1); continue; }
                result.Departures.AddRange(minutes);
                lastHour = Math.Max(lastHour, hour);
            }
            result.Departures.Sort((a, b) => a.Minutes.CompareTo(b.Minutes));
            return result;
        }

        public static bool IsHolidaySchedule(DateTime day)
        {
            return day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday || Holidays.Name(day.Date) != null;
        }

        /// <summary>The text that applies on a service day: 土休日 falls back to 平日 when left empty.</summary>
        public static string ScheduleFor(TimetableEntry entry, DateTime serviceDay)
        {
            return IsHolidaySchedule(serviceDay) && !string.IsNullOrWhiteSpace(entry.Holiday) ? entry.Holiday : entry.Weekday;
        }

        /// <summary>The next departures at or after <paramref name="now"/>, including yesterday's after-midnight trains.</summary>
        public static List<Upcoming> Next(TimetableEntry entry, DateTime now, int count)
        {
            var list = new List<Upcoming>();
            foreach (var day in new[] { now.Date.AddDays(-1), now.Date, now.Date.AddDays(1) })
                foreach (var departure in Parse(ScheduleFor(entry, day)).Departures)
                {
                    var time = day.AddMinutes(departure.Minutes);
                    if (time >= now.AddSeconds(-30)) list.Add(new Upcoming { Time = time, Label = departure.Label });
                }
            return list.OrderBy(u => u.Time).Take(count).ToList();
        }

        public static string Clock(int minutes) { return (minutes / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture); }
    }
}
