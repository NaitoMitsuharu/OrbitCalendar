using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        readonly int[] choice = new int[3];
        readonly Dictionary<int, LiveData.View> saved = new Dictionary<int, LiveData.View>();
        int mode;
        LiveData.View[] radarFrames;
        bool updatingTimeline;
        readonly System.Windows.Threading.DispatcherTimer radarPlayback = new System.Windows.Threading.DispatcherTimer();

        CancellationTokenSource loading;
        DateTime nextUpdate;
        Button[] tabs, choices;
        readonly Brush activeTab = Art.Ink("#296675");

        void InitializeViews()
        {
            tabs = new[] { Get<Button>("CalendarTab"), Get<Button>("MarketTab"), Get<Button>("WeatherTab") };
            choices = new[] { Get<Button>("Choice0"), Get<Button>("Choice1"), Get<Button>("Choice2"), Get<Button>("Choice3") };
            tabs[0].Background = activeTab;
            radarPlayback.Interval = TimeSpan.FromMilliseconds(650);
            radarPlayback.Tick += delegate
            {
                var slider = Get<Slider>("RadarTime");
                slider.Value = slider.Value >= slider.Maximum ? 0 : slider.Value + 1;
            };
            Get<Button>("RadarPlay").Click += delegate
            {
                if (radarFrames == null || radarFrames.Length < 2) return;
                if (radarPlayback.IsEnabled) StopRadar();
                else { radarPlayback.Start(); Get<Button>("RadarPlay").Content = "■"; effect.Refresh(); }
            };
            Get<Slider>("RadarTime").ValueChanged += delegate
            {
                if (updatingTimeline || radarFrames == null || mode != 2) return;
                ShowRadarFrame((int)Get<Slider>("RadarTime").Value);
            };
            IsVisibleChanged += delegate { if (!IsVisible) StopRadar(); };

            for (int i = 0; i < choices.Length; i++)
            {
                int value = i;
                if (i < tabs.Length) tabs[i].Click += delegate { if (mode != value) SwitchView(value); };
                choices[i].Click += delegate { if (choice[mode] != value) { choice[mode] = value; SwitchView(mode); } };
            }
            Get<Button>("RefreshData").Click += delegate { LoadView(true); };
            Get<Button>("DataSource").Click += delegate
            {
                string url = mode == 1 ? "https://finance.yahoo.com/quote/" + Uri.EscapeDataString(LiveData.Symbols[choice[1]]) + "/" :
                    "https://www.jma.go.jp/bosai/nowc/";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            };
            IsVisibleChanged += delegate { if (!IsVisible && loading != null) loading.Cancel(); };
            root.AddHandler(System.Windows.Input.Keyboard.GotKeyboardFocusEvent,
                new System.Windows.Input.KeyboardFocusChangedEventHandler(delegate { effect.Refresh(); }));
        }

        void SwitchView(int value)
        {
            Get<Border>("TimerPicker").Visibility = Visibility.Collapsed;
            Get<Border>("SettingsPicker").Visibility = Visibility.Collapsed;
            StopRadar();
            if (loading != null) loading.Cancel();
            mode = value;
            for (int i = 0; i < tabs.Length; i++) tabs[i].Background = i == mode ? activeTab : Brushes.Transparent;
            Get<Grid>("CalendarBody").Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("DataBody").Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (mode == 0) { if (IsVisible) { UpdateLayout(); effect.Start(true); } }
            else LoadView(false);
        }

        async void LoadView(bool force)
        {
            if (mode == 0 || !IsVisible) return;
            if (loading != null) loading.Cancel();
            var request = new CancellationTokenSource();
            loading = request;
            int index = choice[mode], key = mode * 10 + index;
            StopRadar();
            bool market = mode == 1;
            Get<StackPanel>("RadarControls").Visibility = market ? Visibility.Collapsed : Visibility.Visible;
            var names = market ? LiveData.MarketNames : LiveData.Regions;
            var stamp = Get<TextBlock>("DataStamp");
            var refresh = Get<Button>("RefreshData");
            try
            {
                for (int i = 0; i < choices.Length; i++)
                {
                    choices[i].Visibility = i < names.Length ? Visibility.Visible : Visibility.Collapsed;
                    if (i >= names.Length) continue;
                    choices[i].Content = names[i];
                    choices[i].Background = i == index ? activeTab : Brushes.Transparent;
                }
                Get<TextBlock>("SectionLabel").Text = market ? "MARKET   /   3 MONTHS" : "WEATHER   /   PRECIPITATION";
                Get<Button>("DataSource").Content = market ? "Yahoo Finance  ↗" : "気象庁 雨雲の動き  ↗   ／   地図：国土地理院（加工）";
                LiveData.View view;
                saved.TryGetValue(key, out view);
                bool fetch = force || view == null || DateTime.UtcNow - view.Fetched >= TimeSpan.FromMinutes(5);
                if (view != null) ShowData(view);
                else
                {
                    radarFrames = null;
                    Get<Button>("RadarPlay").IsEnabled = false;
                    Get<Slider>("RadarTime").IsEnabled = false;
                    Get<Canvas>("Plot").Children.Clear();
                    Get<TextBlock>("DataTitle").Text = names[index];
                    Get<TextBlock>("PlotCaption").Text = "";
                }
                if (fetch)
                {
                    stamp.Text = view == null ? (!market && index == 0 ? "現在地と雨雲を確認中…" : "データを取得中…") : view.Stamp + "  ·  更新中";
                    refresh.IsEnabled = false;
                }
                UpdateLayout();
                effect.Start(true);
                if (fetch)
                {
                    view = market ? await LiveData.Market(index, request.Token) : await LiveData.Weather(index, request.Token);
                    if (request.IsCancellationRequested) return;
                    saved[key] = view;
                    ShowData(view);
                    UpdateLayout();
                    effect.Start(true);
                }
                nextUpdate = view.Fetched.AddMinutes(5);
            }
            catch (Exception)
            {
                if (request.IsCancellationRequested) return;
                LiveData.View previous;
                stamp.Text = saved.TryGetValue(key, out previous) ? previous.Stamp + "  ·  更新失敗" : "取得できませんでした。「更新」で再試行";
                nextUpdate = DateTime.UtcNow.AddMinutes(1);
            }
            finally
            {
                if (loading == request)
                {
                    loading = null;
                    refresh.IsEnabled = true;
                    if (mode != 0 && IsVisible) effect.Refresh();
                }
                request.Dispose();
            }
        }

        void StopRadar()
        {
            radarPlayback.Stop();
            Get<Button>("RadarPlay").Content = "▶";
        }

        void DrawRadar(LiveData.View view)
        {
            var plot = Get<Canvas>("Plot");
            plot.Children.Add(new Image { Source = view.Map });
            if (!view.LocationPoint.HasValue) return;
            var point = view.LocationPoint.Value;
            var halo = new Ellipse { Width = 14, Height = 14, Fill = Art.Ink("#492D3C"), Stroke = Art.Ink("#FFE6E6"), StrokeThickness = 1 };
            Canvas.SetLeft(halo, point.X - 7); Canvas.SetTop(halo, point.Y - 7); plot.Children.Add(halo);
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Art.Ink("#FF4949"), ToolTip = "現在地（位置情報による推定）" };
            Canvas.SetLeft(dot, point.X - 4); Canvas.SetTop(dot, point.Y - 4); plot.Children.Add(dot);
        }

        void ShowRadarFrame(int index)
        {
            var frame = radarFrames[index];
            var plot = Get<Canvas>("Plot");
            plot.Children.Clear();
            DrawRadar(frame);
            Get<TextBlock>("DataStamp").Text = frame.Stamp;
            Get<TextBlock>("RadarClock").Text = frame.Stamp.Substring(11, 5);
            effect.Refresh();
        }

        void ShowData(LiveData.View view)
        {
            Get<TextBlock>("DataTitle").Text = view.Heading;
            Get<TextBlock>("DataStamp").Text = view.Stamp;
            Get<TextBlock>("PlotCaption").Text = view.Caption;
            var plot = Get<Canvas>("Plot");
            plot.Children.Clear();
            if (view.Map != null)
            {
                radarFrames = view.Frames ?? new[] { view };
                Get<Button>("RadarPlay").IsEnabled = radarFrames.Length > 1;
                Get<Slider>("RadarTime").IsEnabled = radarFrames.Length > 1;
                updatingTimeline = true;
                Get<Slider>("RadarTime").Maximum = radarFrames.Length - 1;
                Get<Slider>("RadarTime").Value = radarFrames.Length - 1;
                updatingTimeline = false;
                DrawRadar(view);
                Get<TextBlock>("RadarClock").Text = view.Stamp.Substring(11, 5);
                return;
            }
            var bars = view.Bars;
            double low = bars.Min(b => b.Low), high = bars.Max(b => b.High);
            double span = Math.Max(high - low, 1);
            Func<double, double> y = v => 207 - 185 * (v - low) / span;
            var faint = Art.Ink("#4A717E");
            for (int i = 0; i <= 4; i++)
            {
                double price = low + span * i / 4;
                double lineY = y(price);
                plot.Children.Add(new Line { X1 = 0, X2 = 348, Y1 = lineY, Y2 = lineY, Stroke = faint });
                var label = new TextBlock { Text = price.ToString("#,##0"), Foreground = Art.Ink("#A9CAD6"), FontSize = 12 };
                Canvas.SetLeft(label, 354); Canvas.SetTop(label, lineY - 8); plot.Children.Add(label);
            }
            double step = 344.0 / bars.Count;
            var up = Art.Ink("#5CE3AC");
            var down = Art.Ink("#FF8595");
            for (int i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                var brush = bar.Close >= bar.Open ? up : down;
                double x = step * (i + .5);
                plot.Children.Add(new Line { X1 = x, X2 = x, Y1 = y(bar.High), Y2 = y(bar.Low), Stroke = brush });
                var body = new Rectangle { Width = Math.Max(2, step * .72), Height = Math.Max(1.5, Math.Abs(y(bar.Open) - y(bar.Close))), Fill = brush };
                Canvas.SetLeft(body, x - body.Width / 2); Canvas.SetTop(body, y(Math.Max(bar.Open, bar.Close))); plot.Children.Add(body);
            }
        }
    }
}
