using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        int infoPage;
        InformationFeeds.Feed information;
        int informationKey = -1;
        bool informationStale;
        static void OpenLink(string url)
        {
            if (InformationFeeds.SafeUrl(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        async Task LoadInformation(bool force)
        {
            if (loading != null) loading.Cancel();
            var request = new CancellationTokenSource(); loading = request; StopRadar();
            bool news = mode == 3; int requestedMode = mode, key = news ? choice[3] : 10;
            foreach (string name in new[] { "MarketValue", "MarketRange", "WeatherSwitch", "Plot", "TemperaturePlot", "DailyWeatherBody", "RadarControls", "RainLegend", "RadarRange", "MapTools", "MapHint", "PlotCaption", "MarketScroll", "WeatherScroll", "ManageMarkets" }) SetVisible(name, false);
            SetVisible("NewsCategories", news);
            SetVisible("InformationBody", true); SetVisible("RegionChoices", !news); SetVisible("DataChoices", !news); SetVisible("DataSource", false);
            string[] labels = news ? new[] { "総合", "経済", "IT" } : new[] { "近隣", "JR", "私鉄" };
            for (int i = 0; i < choices.Length; i++)
            {
                choices[i].Visibility = i < labels.Length ? Visibility.Visible : Visibility.Collapsed;
                if (i < labels.Length) { choices[i].Content = labels[i]; choices[i].Background = i == choice[mode] ? activeTab : Brushes.Transparent; }
            }
            for (int i = 0; i < 3; i++) Get<Button>("NewsCategory" + i).Background = i == choice[3] ? activeTab : Brushes.Transparent;
            Get<TextBlock>("SectionLabel").Text = news ? "NEWS" : "TRANSIT   /   YOKOHAMA";
            Get<TextBlock>("DataTitle").Text = news ? "ニュース" : "運行情報";
            Get<Button>("DataSource").Content = news ? "配信元のRSS ↗" : "各路線をクリックで公式情報 ↗";
            Get<TextBlock>("DataStamp").Text = "";
            Get<Button>("RefreshData").IsEnabled = false;
            if (informationKey != key) { information = null; Get<StackPanel>("InformationBody").Children.Clear(); infoPage = 0; }
            BeginDataLoading(request);
            UpdateLayout(); effect.Start(true);
            try
            {
                var feed = news ? await InformationFeeds.News(key, force, request.Token) : await InformationFeeds.Transit(force, request.Token);
                if (request.IsCancellationRequested || mode != requestedMode) return;
                EndDataLoading(request);
                information = feed; informationKey = key;
                RenderInformation(false);
                UpdateLayout(); effect.Start(true);
                nextUpdate = feed.Fetched.AddMinutes(news ? 10 : 5);
            }
            catch (Exception)
            {
                if (request.IsCancellationRequested) return;
                if (information != null) RenderInformation(true);
                else Get<TextBlock>("DataStamp").Text = "取得できません · 更新で再試行";
                nextUpdate = DateTime.UtcNow.AddMinutes(1);
            }
            finally
            {
                EndDataLoading(request);
                if (loading == request) { loading = null; Get<Button>("RefreshData").IsEnabled = true; UpdateLayout(); effect.Refresh(); RefreshViewAfterLayout(); }
                request.Dispose();
            }
        }
        void RenderInformation(bool stale)
        {
            informationStale = stale;
            bool news = mode == 3;
            if (news) { RenderNewsArticle(stale); return; }
            var routes = information.Items.Where(i => choice[4] == 0 || (choice[4] == 1 ? i.Source == "JR東日本" : i.Source != "JR東日本")).ToArray();
            var items = routes.Where(i => i.Normal == false).ToArray();
            var unconfirmed = (stale ? routes : routes.Where(i => !i.Normal.HasValue)).Select(i => i.Title).Distinct().ToArray();
            string missing = unconfirmed.Length == 0 ? "" : (stale ? "今回確認できなかった路線：" : "取得できなかった路線：") + string.Join("、", unconfirmed);
            Get<TextBlock>("DataStamp").Text = information.Fetched.AddHours(9).ToString("HH:mm") + " 確認" + (stale ? " · 更新失敗（前回の情報）" : unconfirmed.Length > 0 ? " · 一部の路線は確認できません" : "");
            var body = Get<StackPanel>("InformationBody"); body.Children.Clear();
            if (items.Length == 0)
            {
                bool clear = !stale && routes.Length > 0 && Array.TrueForAll(routes, r => r.Normal == true);
                string title = clear ? "現在、運行に大きな乱れはありません" : stale ? "前回の確認では、遅延等の掲載はありません" : "運行情報を確認できない路線があります";
                string detail = clear ? "対象路線に遅延・見合わせの発表はありません" : missing.Length > 0 ? missing : "対象路線の情報を確認できませんでした";
                var status = new StackPanel();
                status.Children.Add(new TextBlock { Text = clear ? "✓" : "—", FontSize = 25, Foreground = clear ? aqua : muted });
                status.Children.Add(new TextBlock { Text = title, FontSize = 16, TextWrapping = TextWrapping.Wrap, Foreground = clear ? aqua : muted, Margin = new Thickness(0, 9, 0, 7) });
                status.Children.Add(new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 54, ToolTip = detail, Foreground = muted, Tag = missing.Length > 0 ? "transit-missing-names" : null });
                body.Children.Add(new Border { Child = status, Padding = new Thickness(18), Margin = new Thickness(0, 16, 0, 0), CornerRadius = new CornerRadius(8), Background = Art.Ink("#152E39"), Tag = clear ? "transit-clear" : stale ? "transit-stale" : "transit-unavailable" });
                return;
            }
            int perPage = 3;
            int pages = Math.Max(1, (items.Length + perPage - 1) / perPage); infoPage = Math.Max(0, Math.Min(infoPage, pages - 1));
            if (missing.Length > 0)
                body.Children.Add(new Border { Child = new TextBlock { Text = missing, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 54, ToolTip = missing, Foreground = muted, Tag = "transit-missing-names" },
                    Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 7, 0, 5), CornerRadius = new CornerRadius(7), Background = Art.Ink("#152E39"), Tag = "transit-missing" });
            foreach (var item in items.Skip(infoPage * perPage).Take(perPage))
            {
                var captured = item;
                var content = new StackPanel { Width = 382 };
                var title = new TextBlock { Text = item.Title, FontSize = news ? 14 : 15, TextWrapping = TextWrapping.Wrap, MaxHeight = news ? 39 : 23, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Art.Ink("#E3F4F4"), ToolTip = item.Title };
                content.Children.Add(title);
                if (!news)
                {
                    content.Children.Add(new TextBlock { Text = (item.Normal == true ? "● " : item.Normal == false ? "▲ " : "？ ") + item.Detail, Foreground = Art.Ink(item.Normal == true ? "#86E8DE" : item.Normal == false ? "#FFB088" : "#AFC2CC"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Detail, Margin = new Thickness(0, 3, 0, 0) });
                }
                string date = item.Published.HasValue ? item.Published.Value.ToOffset(TimeSpan.FromHours(9)).ToString("M/d HH:mm") + (news ? " 配信" : " 発表") : news ? "配信時刻なし" : "発表時刻なし";
                content.Children.Add(new TextBlock { Text = item.Source + " · " + date + " ↗", FontSize = 10, Foreground = Art.Ink("#92B5BE"), Margin = new Thickness(0, 4, 0, 0) });
                var button = new Button { Content = content, Padding = new Thickness(9, 7, 9, 7), Background = Art.Ink("#152E39"), Margin = new Thickness(0, 4, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = news ? "元記事を開く" : "公式の運行情報を開く", Tag = "transit-disruption" };
                button.Click += delegate { OpenLink(captured.Url); }; body.Children.Add(button);
            }
            if (pages <= 1) return;
            var pager = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 0) };
            var previous = new Button { Content = "‹", Width = 35, FontSize = 18, IsEnabled = infoPage > 0 };
            var next = new Button { Content = "›", Width = 35, FontSize = 18, IsEnabled = infoPage < pages - 1 };
            previous.Click += delegate { infoPage--; RenderInformation(stale); UpdateLayout(); effect.StartArea(PanelBounds(body), true); };
            next.Click += delegate { infoPage++; RenderInformation(stale); UpdateLayout(); effect.StartArea(PanelBounds(body), true); };
            pager.Children.Add(previous); pager.Children.Add(new TextBlock { Text = (infoPage + 1) + " / " + pages, Width = 65, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = Art.Ink("#AFCAD2") }); pager.Children.Add(next); body.Children.Add(pager);

        }
    }
}
