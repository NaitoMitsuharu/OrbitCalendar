using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OrbitCalendar
{
    internal sealed class FundDataUnavailableException : Exception
    {
        public FundDataUnavailableException(string message) : base(message) { }
    }

    internal static class FundPrices
    {
        static readonly HttpClient http = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
        static readonly SemaphoreSlim requests = new SemaphoreSlim(1, 1);
        static long lastRequest;
        static FundPrices() { http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 OrbitCalendar/1.11"); }

        // Public, read-only history export linked by SBI Securities' fund history page.
        public static async Task<LiveData.View> Sbi(Instrument item, CancellationToken token)
        {
            DateTime end = DateTime.UtcNow.AddHours(9).Date, start = end.AddMonths(-3).AddDays(-7);
            string url = "https://site0.sbisec.co.jp/marble/fund/history/standardprice/standardPriceHistoryCsvAction.do?fund_sec_code=" +
                Uri.EscapeDataString(item.Code) + DateQuery("from", start) + DateQuery("to", end);
            await requests.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Anonymous exports must not inherit another fund's broker session.
                // Space requests to avoid overloading the public history service.
                double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - lastRequest) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (elapsed < 750) await Task.Delay((int)(750 - elapsed), token).ConfigureAwait(false);
                lastRequest = System.Diagnostics.Stopwatch.GetTimestamp();
                using (var response = await http.GetAsync(url, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    string csv = Encoding.GetEncoding(932).GetString(bytes);
                    if (csv.TrimStart().StartsWith("<", StringComparison.Ordinal)) throw new HttpRequestException("取得元が一時的に利用できません");
                    return ParseSbi(item, csv);
                }
            }
            finally { requests.Release(); }
        }

        static string DateQuery(string side, DateTime day)
        {
            return "&in_term_" + side + "_yyyy=" + day.ToString("yyyy", CultureInfo.InvariantCulture) +
                "&in_term_" + side + "_mm=" + day.ToString("MM", CultureInfo.InvariantCulture) +
                "&in_term_" + side + "_dd=" + day.ToString("dd", CultureInfo.InvariantCulture);
        }

        internal static LiveData.View ParseSbi(Instrument item, string csv)
        {
            string name = null;
            bool priceHeader = false;
            var bars = new List<LiveData.Candle>();
            using (var reader = new StringReader(csv))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var row = CsvRow(line);
                    if (row.Length > 1 && row[0] == "ファンド名") name = row[1];
                    if (row.Length > 1 && row[0] == "年月日" && row[1] == "基準価額（円）") { priceHeader = true; continue; }
                    DateTime day; double value;
                    if (!priceHeader || row.Length < 2 || !DateTime.TryParseExact(row[0], "yyyy/MM/dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out day) || !double.TryParse(row[1], NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
                        value <= 0 || double.IsNaN(value) || double.IsInfinity(value)) continue;
                    bars.Add(new LiveData.Candle { Day = day, Open = value, High = value, Low = value, Close = value });
                }
            }
            if (!priceHeader || string.IsNullOrWhiteSpace(name) || bars.Count < 2)
                throw new FundDataUnavailableException("この銘柄の基準価額は配信を確認できませんでした");
            // Never display a similarly named fund, an ETF proxy, or a default response.
            if (Instruments.Normalize(name) != Instruments.Normalize(item.Name))
                throw new FundDataUnavailableException("取得元のファンド名を照合できませんでした");
            if (bars.GroupBy(b => b.Day).Any(g => g.Select(b => b.Close).Distinct().Count() > 1)) throw new InvalidDataException("同じ日付の基準価額が重複しています");
            bars = bars.GroupBy(b => b.Day).Select(g => g.First()).OrderBy(b => b.Day).ToList();
            return CreateView(item, bars);
        }

        internal static LiveData.View CreateView(Instrument item, List<LiveData.Candle> bars)
        {
            if (bars.Count < 2) throw new InvalidDataException();
            var last = bars[bars.Count - 1];
            double change = last.Close - bars[bars.Count - 2].Close;
            bars = bars.Where(b => b.Day >= last.Day.AddMonths(-3)).ToList();
            if (bars.Count < 2) throw new InvalidDataException();
            return new LiveData.View { IsFund = true, Bars = bars, Heading = item.Label + "   " + last.Close.ToString("#,##0.##") + " 円",
                Stamp = last.Day.ToString("yyyy/MM/dd") + "  基準価額 " + last.Close.ToString("#,##0.##") + "円 / 1万口",
                Caption = "前日比 " + change.ToString("+#,##0.##;-#,##0.##;0") + " 円   " + bars[0].Day.ToString("MM/dd") + " — " + last.Day.ToString("MM/dd") };
        }

        static string[] CsvRow(string line)
        {
            var values = new List<string>(); var text = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { text.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { values.Add(text.ToString()); text.Clear(); }
                else text.Append(c);
            }
            if (quoted) throw new InvalidDataException("CSVが途中で切れています");
            values.Add(text.ToString()); return values.ToArray();
        }
    }
}
