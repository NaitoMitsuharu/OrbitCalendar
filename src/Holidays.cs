using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OrbitCalendar
{
    internal static class Holidays
    {
        const string Feed = "https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv";
        static readonly string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrbitCalendar", "holidays.csv");
        static volatile Dictionary<DateTime, string> dates;
        static DateTime nextCheck;
        static bool fetching;
        public static int Revision { get; private set; }

        static Holidays()
        {
            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("holidays.csv")))
                dates = Parse(reader.ReadToEnd());
            try
            {
                if (File.Exists(cache))
                {
                    dates = Validate(File.ReadAllText(cache));
                    var saved = File.GetLastWriteTimeUtc(cache);
                    if (saved <= DateTime.UtcNow) nextCheck = saved.AddDays(1);
                }
            }
            catch (InvalidDataException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static Dictionary<DateTime, string> Parse(string csv)
        {
            var parsed = new Dictionary<DateTime, string>();
            using (var reader = new StringReader(csv))
            {
                string header = reader.ReadLine();
                if (header == null || !header.Contains("祝日")) throw new InvalidDataException();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (String.IsNullOrWhiteSpace(line)) continue;
                    var fields = line.Split(',');
                    DateTime day;
                    if (fields.Length != 2 || !DateTime.TryParseExact(fields[0], "yyyy/M/d", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out day) || String.IsNullOrWhiteSpace(fields[1]) || parsed.ContainsKey(day))
                        throw new InvalidDataException();
                    parsed.Add(day, fields[1]);
                }
            }
            return parsed;
        }

        static Dictionary<DateTime, string> Validate(string csv)
        {
            var parsed = Parse(csv);
            if (parsed.Count == 0 || parsed.Keys.Max() < dates.Keys.Max() || parsed.Keys.Min() > dates.Keys.Min())
                throw new InvalidDataException();
            int first = dates.Keys.Min().Year, last = parsed.Keys.Max().Year;
            var years = parsed.Keys.GroupBy(day => day.Year).ToArray();
            if (years.Length != last - first + 1 || years.Any(year => year.Count() < 8 ||
                year.Min().Month != 1 || year.Max().Month < 11)) throw new InvalidDataException();
            return parsed;
        }

        // Called on the UI thread. A completed file replaces the last good snapshot in one operation.
        public static async void Check(DateTime utc)
        {
            if (fetching || utc < nextCheck) return;
            fetching = true;
            nextCheck = utc.AddHours(1);
            try
            {
                var bytes = await LiveData.Get(Feed, CancellationToken.None);
                string csv = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
                if (!csv.StartsWith("国民の祝日", StringComparison.Ordinal)) csv = Encoding.GetEncoding(932).GetString(bytes);
                var updated = Validate(csv);
                Directory.CreateDirectory(Path.GetDirectoryName(cache));
                string temporary = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, csv, new UTF8Encoding(false));
                    if (File.Exists(cache)) File.Replace(temporary, cache, null);
                    else File.Move(temporary, cache);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                dates = updated;
                Revision++;
                nextCheck = DateTime.UtcNow.AddDays(1);
            }
            catch (System.Net.Http.HttpRequestException) { }
            catch (TaskCanceledException) { }
            catch (InvalidDataException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { fetching = false; }
        }

        public static string Name(DateTime day)
        {
            string name;
            return dates.TryGetValue(day.Date, out name) ? name : null;
        }
    }
}
