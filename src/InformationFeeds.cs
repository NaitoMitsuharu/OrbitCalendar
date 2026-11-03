using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;

namespace OrbitCalendar
{
    internal static class InformationFeeds
    {
        internal class Item
        {
            public string Title, Detail, Source, Url;
            public DateTimeOffset? Published;
            public bool? Normal;
        }
        internal class Feed
        {
            public Item[] Items;
            public DateTime Fetched;
            public bool PartialFailure;
        }
        static readonly string[] newsUrls = { "https://news.web.nhk/n-data/conf/na/rss/cat0.xml", "https://news.yahoo.co.jp/rss/topics/business.xml", "https://rss.itmedia.co.jp/rss/2.0/news_bursts.xml" };
        static readonly string[] newsNames = { "NHK", "Yahoo!ニュース · 経済", "ITmedia NEWS" };
        static readonly Dictionary<int, Feed> cache = new Dictionary<int, Feed>();
        public static string NewsUrl(int category) { return newsUrls[category]; }
        public static bool SafeUrl(string text)
        {
            Uri uri;
            return Uri.TryCreate(text, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        }
        public static async Task<Feed> News(int category, bool force, CancellationToken token)
        {
            Feed previous;
            lock (cache) if (!force && cache.TryGetValue(category, out previous) && DateTime.UtcNow - previous.Fetched < TimeSpan.FromMinutes(10)) return previous;
            var bytes = await LiveData.Get(newsUrls[category], token).ConfigureAwait(false);
            var items = ParseNews(bytes, newsNames[category]);
            if (items.Length == 0) throw new InvalidDataException("見出しを取得できませんでした");
            var result = new Feed { Items = items, Fetched = DateTime.UtcNow };
            token.ThrowIfCancellationRequested(); lock (cache) cache[category] = result; return result;
        }
        internal static Item[] ParseNews(byte[] xml, string source)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var stream = new MemoryStream(xml))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2000000 })) doc.Load(reader);
            var result = new List<Item>();
            foreach (XmlNode node in doc.SelectNodes("//*[local-name()='item']"))
            {
                Func<string, string> text = name => { var v = node.SelectSingleNode("*[local-name()='" + name + "']"); return v == null ? "" : v.InnerText.Trim(); };
                string title = text("title"), url = text("link"), stamp = text("pubDate");
                if (stamp.Length == 0) stamp = text("date");
                DateTimeOffset date;
                if (title.Length == 0 || !SafeUrl(url)) continue;
                result.Add(new Item { Title = title, Url = url, Source = source,
                    Published = DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date) ? date : (DateTimeOffset?)null });
            }
            return result.GroupBy(i => i.Url).Select(g => g.First()).Take(80).ToArray();
        }
        internal static string Plain(string html)
        {
            return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]+>", " ")), @"\s+", " ").Trim();
        }
        static Item Unknown(string name, string source, string url)
        {
            return new Item { Title = name, Source = source, Url = url, Detail = "確認できません · 公式サイトで確認", Normal = null };
        }
        public class SotetsuStatus { public string DATE { get; set; } public string MSG { get; set; } public bool? IRREGULAR { get; set; } }
        internal static Item ParseSotetsu(string json)
        {
            var items = new JavaScriptSerializer().Deserialize<SotetsuStatus[]>(json.TrimStart('\uFEFF'));
            var item = items == null ? null : items.FirstOrDefault();
            var result = Unknown("相鉄線", "相模鉄道", "https://www.sotetsu.co.jp/train/status/");
            if (item == null || !item.IRREGULAR.HasValue || string.IsNullOrWhiteSpace(item.MSG)) return result;
            result.Normal = !item.IRREGULAR.Value; result.Detail = Plain(item.MSG); result.Published = ParseJapanDate(item.DATE); return result;
        }
        static DateTimeOffset? ParseJapanDate(string text)
        {
            DateTime d;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return null;
            return new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Unspecified), TimeSpan.FromHours(9));
        }
        internal static Item[] ParseJr(string html)
        {
            string[] names = { "横須賀線", "東海道線", "京浜東北線", "湘南新宿ライン" };
            return names.Select(name =>
            {
                var result = Unknown(name, "JR東日本", "https://traininfo.jreast.co.jp/train_info/kanto.aspx?");
                foreach (Match m in Regex.Matches(html, @"<li\b[^>]*class=""traininfo-routes__table__item[^"" ]*""[^>]*>(.*?)</li>", RegexOptions.Singleline))
                {
                    var block = m.Groups[1].Value;
                    var route = Regex.Match(block, @"class=""traininfo-routes__name""[^>]*>(.*?)</span>", RegexOptions.Singleline);
                    if (Plain(route.Groups[1].Value) != name) continue;
                    var status = Regex.Match(block, @"class=""traininfo-routes__status\s+([^""]+)""[^>]*>(.*?)</p>", RegexOptions.Singleline);
                    string label = Plain(status.Groups[2].Value);
                    if (!status.Success || label.Length == 0) continue;
                    bool normal = status.Groups[1].Value.Split(' ').Contains("normal") && label == "平常運転";
                    if (result.Normal == false && normal) continue;
                    result.Normal = normal; result.Detail = label;
                    var link = Regex.Match(block, @"href=""([^""]*line\.aspx[^""]*)""");
                    if (link.Success) result.Url = new Uri(new Uri("https://traininfo.jreast.co.jp"), WebUtility.HtmlDecode(link.Groups[1].Value)).AbsoluteUri;
                }
                return result;
            }).ToArray();
        }
        internal static Item[] ParseTokyu(string json)
        {
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json.TrimStart('\uFEFF'));
            return new[] { "ty", "sh" }.Select(code =>
            {
                var result = Unknown(code == "ty" ? "東急東横線" : "東急新横浜線", "東急電鉄", "https://www.tokyu.co.jp/unten2/unten.html");
                object check, message;
                if (data == null || !data.TryGetValue("check_" + code, out check) || !data.TryGetValue("unten_" + code, out message) || message == null || string.IsNullOrWhiteSpace(message.ToString())) return result;
                var flags = Convert.ToString(check).Split(',');
                if (flags.Length != 2 || flags.Any(f => f != "0" && f != "1")) return result;
                result.Normal = flags.All(f => f == "0"); result.Detail = Plain(message.ToString()); return result;
            }).ToArray();
        }
        static async Task<Item[]> Sotetsu(CancellationToken token)
        {
            try { return new[] { ParseSotetsu(Encoding.UTF8.GetString(await LiveData.Get("https://cdn.sotetsu.co.jp/unkou/dat/train_status1_v2.json", token).ConfigureAwait(false))) }; }
            catch (Exception) { token.ThrowIfCancellationRequested(); return new[] { Unknown("相鉄線", "相模鉄道", "https://www.sotetsu.co.jp/train/status/") }; }
        }
        static async Task<Item[]> Jr(CancellationToken token)
        {
            try { return ParseJr(Encoding.UTF8.GetString(await LiveData.Get("https://traininfo.jreast.co.jp/train_info/kanto.aspx?", token).ConfigureAwait(false))); }
            catch (Exception) { token.ThrowIfCancellationRequested(); return ParseJr(""); }
        }
        static async Task<Item[]> Tokyu(CancellationToken token)
        {
            try { return ParseTokyu(Encoding.UTF8.GetString(await LiveData.Get("https://www.tokyu.co.jp/unten/unten2.json", token).ConfigureAwait(false))); }
            catch (Exception) { token.ThrowIfCancellationRequested(); return ParseTokyu("{}"); }
        }
        public static async Task<Feed> Transit(bool force, CancellationToken token)
        {
            Feed previous;
            lock (cache) if (!force && cache.TryGetValue(10, out previous) && DateTime.UtcNow - previous.Fetched < TimeSpan.FromMinutes(5)) return previous;
            var groups = await Task.WhenAll(Sotetsu(token), Jr(token), Tokyu(token)).ConfigureAwait(false);
            var items = groups.SelectMany(g => g).ToArray();
            var result = new Feed { Items = items, Fetched = DateTime.UtcNow, PartialFailure = items.Any(i => !i.Normal.HasValue) };
            token.ThrowIfCancellationRequested(); lock (cache) cache[10] = result; return result;
        }
    }
}
