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
        static void OpenLink(string url)
        {
            if (InformationFeeds.SafeUrl(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        async Task LoadInformation(bool force)
        {
            if (loading != null) loading.Cancel();
            var request = new CancellationTokenSource(); loading = request; StopRadar();
            bool news = mode == 3; int requestedMode = mode, key = news ? choice[3] : 10;
            foreach (string name in new[] { "MarketRange", "WeatherSwitch", "Plot", "TemperaturePlot", "DailyWeatherBody", "RadarControls", "RainLegend", "RadarRange", "MapTools", "MapHint", "PlotCaption", "MarketScroll", "ManageMarkets" }) SetVisible(name, false);
            SetVisible("InformationBody", true); SetVisible("RegionChoices", true); SetVisible("DataChoices", true); SetVisible("DataSource", false);
            string[] labels = news ? new[] { "総合", "経済", "IT" } : new[] { "近隣", "JR", "私鉄" };
            for (int i = 0; i < choices.Length; i++)
            {
                choices[i].Visibility = i < labels.Length ? Visibility.Visible : Visibility.Collapsed;
                if (i < labels.Length) { choices[i].Content = labels[i]; choices[i].Background = i == choice[mode] ? activeTab : Brushes.Transparent; }
            }
            Get<TextBlock>("SectionLabel").Text = news ? "NEWS" : "TRANSIT   /   YOKOHAMA";
            Get<TextBlock>("DataTitle").Text = news ? "ニュース" : "運行情報";
            Get<Button>("DataSource").Content = news ? "配信元のRSS ↗" : "各路線をクリックで公式情報 ↗";
            Get<TextBlock>("DataStamp").Text = "情報を取得中…";
            Get<Button>("RefreshData").IsEnabled = false;
            if (informationKey != key) { information = null; Get<StackPanel>("InformationBody").Children.Clear(); infoPage = 0; }
            UpdateLayout(); effect.Start(true);
            try
            {
                var feed = news ? await InformationFeeds.News(key, force, request.Token) : await InformationFeeds.Transit(force, request.Token);
                if (request.IsCancellationRequested || mode != requestedMode) return;
                information = feed; informationKey = key;
                RenderInformation(false); nextUpdate = feed.Fetched.AddMinutes(news ? 10 : 5);
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
                if (loading == request) { loading = null; Get<Button>("RefreshData").IsEnabled = true; UpdateLayout(); effect.Refresh(); RefreshViewAfterLayout(); }
                request.Dispose();
            }
        }
        void RenderInformation(bool stale)
        {
            bool news = mode == 3;
            if (news) { RenderNewsArticle(stale); return; }
            var items = information.Items.Where(i => i.Normal == false && (choice[4] == 0 || (choice[4] == 1 ? i.Source == "JR東日本" : i.Source != "JR東日本"))).ToArray();
            Get<TextBlock>("DataStamp").Text = information.Fetched.AddHours(9).ToString("HH:mm") + " 確認" + (stale ? " · 更新失敗（前回の情報）" : information.PartialFailure ? " · 一部の路線は確認できません" : "");
            var body = Get<StackPanel>("InformationBody"); body.Children.Clear();
            int perPage = 3;
            int pages = Math.Max(1, (items.Length + perPage - 1) / perPage); infoPage = Math.Max(0, Math.Min(infoPage, pages - 1));
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
                var button = new Button { Content = content, Padding = new Thickness(9, 7, 9, 7), Background = Art.Ink("#152E39"), Margin = new Thickness(0, 4, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = news ? "元記事を開く" : "公式の運行情報を開く" };
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
