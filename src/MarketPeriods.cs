using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        int marketMonths = 3;
        string MarketCacheKey(Instrument item) { return item.Id + ":" + marketMonths; }
        void InitializeRangeAndCredits()
        {
            int[] months = { 1, 3, 6, 12, 60 };
            string[] labels = { "1か月", "3か月", "6か月", "1年", "5年" };
            for (int i = 0; i < months.Length; i++)
            {
                int period = months[i];
                var button = new Button { Content = labels[i], Padding = new Thickness(10, 7, 10, 7), HorizontalContentAlignment = HorizontalAlignment.Left, Tag = period };
                button.Click += delegate
                {
                    marketMonths = period;
                    Get<Button>("MarketRange").Content = (period < 12 ? period + (period == 1 ? " MONTH" : " MONTHS") : period / 12 + " YEAR" + (period == 12 ? "" : "S")) + " ▾";
                    Get<Border>("RangePicker").Visibility = Visibility.Collapsed;
                    LoadView(false);
                };
                Get<StackPanel>("RangeChoices").Children.Add(button);
            }
            Get<Button>("MarketRange").Click += delegate
            {
                var picker = Get<Border>("RangePicker");
                if (picker.Visibility == Visibility.Visible) { ClosePanel("RangePicker"); return; }
                foreach (Button b in Get<StackPanel>("RangeChoices").Children) b.Background = (int)b.Tag == marketMonths ? activeTab : Brushes.Transparent;
                picker.Visibility = Visibility.Visible; UpdateLayout(); effect.StartArea(PanelBounds(picker), true);
            };
            root.PreviewMouseDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var target = e.OriginalSource as DependencyObject; var picker = Get<Border>("RangePicker"); var trigger = Get<Button>("MarketRange");
                if (target != null && picker.Visibility == Visibility.Visible && !picker.IsAncestorOf(target) && !trigger.IsAncestorOf(target) && target != trigger) ClosePanel("RangePicker");
            };
            Get<Button>("OpenCredits").Click += delegate { Get<Border>("SettingsPicker").Visibility = Visibility.Collapsed; Get<Border>("SpeedPicker").Visibility = Visibility.Collapsed; Get<Border>("CreditsPicker").Visibility = Visibility.Visible; UpdateLayout(); effect.StartArea(PanelBounds(Get<Border>("CreditsPicker")), true); };
            Get<Button>("CloseCredits").Click += delegate { ClosePanel("CreditsPicker"); };
            string[,] links = {
                { "地図：国土地理院（色を加工）", "https://maps.gsi.go.jp/development/ichiran.html" },
                { "地点検索：国土地理院", "https://maps.gsi.go.jp/" },
                { "雨雲：気象庁", "https://www.jma.go.jp/bosai/nowc/" },
                { "天気・気温：Open-Meteo · CC BY 4.0", "https://open-meteo.com/" },
                { "地球・県境：Natural Earth", "https://www.naturalearthdata.com/about/terms-of-use/" },
                { "株式・指数・為替：Yahoo Finance", "https://finance.yahoo.com/" },
                { "投資信託：SBI証券 / ウエルスアドバイザー", "https://site0.sbisec.co.jp/marble/fund/history/standardprice.do?fund_sec_code=89311199" },
                { "投資信託：三菱UFJアセットマネジメント", "https://www.am.mufg.jp/" },
                { "News：NHK・Yahoo!ニュース・ITmedia", "https://news.web.nhk/" },
                { "運行情報：相鉄・JR東日本・東急", "https://www.sotetsu.co.jp/train/status/" }
            };
            for (int i = 0; i < links.GetLength(0); i++)
            {
                string url = links[i, 1]; var b = new Button { Content = links[i, 0] + " ↗", HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12, Margin = new Thickness(0, 3, 0, 3), Foreground = Art.Ink("#A6D3D6") };
                b.Click += delegate { OpenLink(url); }; Get<StackPanel>("CreditLinks").Children.Add(b);
            }
        }
    }
}
