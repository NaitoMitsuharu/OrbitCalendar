using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace OrbitCalendar
{
    internal sealed class Instrument
    {
        public string Id, Name, Label, Kind, Code, Aliases, Provider, Key, Unit;
        internal string SearchText;
        public bool Supported { get { return Provider == "yahoo" || Provider == "mufg"; } }
        public string SourceUrl { get { return Provider == "mufg" ? "https://www.am.mufg.jp/fund/" + Key + ".html" :
            Provider == "yahoo" ? "https://finance.yahoo.com/quote/" + Uri.EscapeDataString(Key) + "/" :
            "https://www.imaj.or.jp/find/nisa_growth_productslist/index.html"; } }
    }
    internal static class Instruments
    {
        public static string Snapshot = "不明";
        public static readonly List<Instrument> Catalog = new List<Instrument>();
        public static readonly Dictionary<string, Instrument> ById = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);
        static Instruments()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("instruments.tsv"))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("#")) { var stamp = Regex.Match(line, "[0-9]{4}-[0-9]{2}-[0-9]{2}"); if (stamp.Success) Snapshot = stamp.Value; continue; }
                    var r = line.Split('\t'); if (r.Length != 9) continue;
                    var item = new Instrument { Id = r[0], Name = r[1], Label = r[2], Kind = r[3], Code = r[4], Aliases = r[5], Provider = r[6], Key = r[7], Unit = r[8] };
                    item.SearchText = Normalize(item.Name + " " + item.Label + " " + item.Code + " " + item.Aliases);
                    Catalog.Add(item); ById[item.Id] = item;
                }
            }
        }
        public static string Normalize(string text)
        {
            var b = new StringBuilder();
            foreach (char c in (text ?? "").Normalize(NormalizationForm.FormKC).ToUpperInvariant())
            {
                char k = c >= '\u3041' && c <= '\u3096' ? (char)(c + 0x60) : c;
                if (char.IsLetterOrDigit(k)) b.Append(k);
            }
            return b.ToString();
        }
        public static List<Instrument> Search(string query, int filter)
        {
            var terms = (query ?? "").Normalize(NormalizationForm.FormKC).Split(new[] { ' ', '\u3000' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize).Where(s => s.Length > 0).ToArray();
            string exact = Normalize(query);
            var found = Catalog.Where(i => (filter == 0 || (filter == 1 ? i.Kind == "投資信託" : filter == 2 ?
                i.Kind == "国内株" || i.Kind == "ETF" || i.Kind == "REIT" : i.Kind != "投資信託" && i.Kind != "国内株" && i.Kind != "ETF" && i.Kind != "REIT")) &&
                terms.All(t => i.SearchText.Contains(t)))
                .OrderBy(i => exact.Length > 0 && Normalize(i.Code) == exact ? 0 : exact.Length > 0 && Normalize(i.Label) == exact ? 1 :
                    i.Id == "f:0331418A" ? 2 : i.Id == "f:03311187" ? 3 : i.Id == "y:^N225" ? 4 : i.Id == "y:JPY=X" ? 5 : 6)
                .ThenBy(i => i.Name, StringComparer.CurrentCulture).ToList();
            string code = (query ?? "").Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
            if (found.Count == 0 && (filter == 0 || filter == 3) && Regex.IsMatch(code, "^[A-Z][A-Z0-9.-]{0,5}$")) found.Add(Custom(code));
            return found;
        }
        internal static Instrument Custom(string code)
        {
            return new Instrument { Id = "custom:" + code, Code = code, Name = code, Label = code, Kind = "米国コード（未照合）", Aliases = "", Provider = "yahoo", Key = code, Unit = "USD" };
        }
    }
    internal static class Watchlist
    {
        static string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitCalendar", "watchlist.json");
        public sealed class State
        {
            public int Version { get; set; }
            public string[] Items { get; set; }
            public Dictionary<string, string> Confirmed { get; set; }
        }
        public static readonly List<Instrument> Items = new List<Instrument>();
        static Dictionary<string, string> confirmed = new Dictionary<string, string>();
        public static string SaveError;
        static Watchlist() { Reload(); }
        internal static void Reload()
        {
            Items.Clear(); confirmed.Clear(); bool loaded = false;
            try
            {
                var state = new JavaScriptSerializer().Deserialize<State>(File.ReadAllText(path, Encoding.UTF8));
                if (state != null && state.Version == 1 && state.Items != null)
                {
                    foreach (string id in state.Items.Distinct())
                    {
                        Instrument item;
                        if (id != null && Instruments.ById.TryGetValue(id, out item)) Items.Add(item);
                        else if (id != null && Regex.IsMatch(id, "^custom:[A-Z][A-Z0-9.-]{0,5}$")) Items.Add(Instruments.Custom(id.Substring(7)));
                    }
                    if (state.Confirmed != null) confirmed = state.Confirmed;
                    loaded = true;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            if (!loaded) { Items.Add(Instruments.ById["y:^N225"]); Items.Add(Instruments.ById["y:JPY=X"]); }
        }
        public static string Status(Instrument item)
        {
            string day;
            return !item.Supported ? "価格取得未対応" : confirmed.TryGetValue(item.Id, out day) ? "取得確認済 " + day : "価格取得対応・未確認";
        }
        public static void Confirm(Instrument item) { confirmed[item.Id] = DateTime.Now.ToString("MM/dd HH:mm"); Save(); }
        public static bool Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var state = new State { Version = 1, Items = Items.Select(i => i.Id).ToArray(), Confirmed = confirmed };
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(state), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                SaveError = null; return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            SaveError = "保存できません。次回起動時に設定が戻る可能性があります"; return false;
        }
    }
}
