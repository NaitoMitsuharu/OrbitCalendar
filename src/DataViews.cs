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
        readonly int[] choice = new int[5];
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
            tabs = new[] { Get<Button>("CalendarTab"), Get<Button>("MarketTab"), Get<Button>("WeatherTab"), Get<Button>("NewsTab"), Get<Button>("TransitTab") };
            choices = new[] { Get<Button>("Choice0"), Get<Button>("Choice1"), Get<Button>("Choice2"), Get<Button>("Choice3"), Get<Button>("Choice4") };
            tabs[0].Background = activeTab;
            InitializeMapInteraction();
            InitializePlacePicker();
            InitializeMarketSettings();
            InitializeRangeAndCredits();
            InitializeLoading();
            radarPlayback.Interval = TimeSpan.FromMilliseconds(33);
            radarPlayback.Tick += delegate { AdvanceRadarPlayback(); };
            Get<Button>("RadarPlay").Click += delegate
            {
                if (radarFrames == null || radarFrames.Length < 2) return;
                if (radarPlayback.IsEnabled) StopRadar();
                else
                {
                    var slider = Get<Slider>("RadarTime");
                    if (RadarIndex(slider.Value) == radarFrames.Length - 1) slider.Value = RadarMinutes(radarFrames[0]);
                    StartRadarPlayback(); Get<Button>("RadarPlay").Content = "■"; effect.Refresh();
                }
            };
            Get<Slider>("RadarTime").PreviewMouseLeftButtonDown += delegate { StopRadar(); effect.Refresh(); };
            InitializeRainLegend();
            InitializeWeatherModes();
            Get<Slider>("RadarTime").ValueChanged += delegate
            {
                if (updatingTimeline || radarFrames == null || mode != 2) return;
                SeekRadar(Get<Slider>("RadarTime").Value);
            };
            IsVisibleChanged += delegate { if (!IsVisible) StopRadar(); };

            for (int i = 0; i < choices.Length; i++)
            {
                int value = i;
                if (i < tabs.Length) tabs[i].Click += delegate { if (mode != value) { mapViewport = null; SwitchView(value); } };
                choices[i].Click += delegate { if (mode >= 3) { choice[mode] = value; infoPage = 0; LoadView(false); return; } if (choice[mode] != value || mode == 2) { choice[mode] = value; mapViewport = null; if (mode == 2) saved.Remove(mode * 10 + value); SwitchView(mode); } };
            }
            Get<Button>("RefreshData").Click += delegate { LoadView(true); };
            Get<Button>("DataSource").Click += delegate
            {
                if (mode >= 3) { OpenLink(mode == 3 ? InformationFeeds.NewsUrl(choice[3]) : "https://www.sotetsu.co.jp/train/status/"); return; }
                if (mode == 2 && showDailyWeather) { OpenLink("https://open-meteo.com/"); return; }
                string url = mode == 1 && SelectedMarket != null ? SelectedMarket.SourceUrl : (activeWeather != null && activeWeather.HourlyRain ? "https://www.jma.go.jp/bosai/kaikotan/" : "https://www.jma.go.jp/bosai/nowc/");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            };
            IsVisibleChanged += delegate { if (!IsVisible && loading != null) loading.Cancel(); };
            root.AddHandler(System.Windows.Input.Keyboard.GotKeyboardFocusEvent,
                new System.Windows.Input.KeyboardFocusChangedEventHandler(delegate { effect.Refresh(); }));
        }

        void SwitchView(int value)
        {
            CancelNewsPreview();
            ClearDataLoading();
            CloseMarketSettings();
            Get<Border>("RangePicker").Visibility = Visibility.Collapsed;
            Get<Border>("CreditsPicker").Visibility = Visibility.Collapsed;
            Get<Border>("TimerPicker").Visibility = Visibility.Collapsed;
            Get<Border>("SettingsPicker").Visibility = Visibility.Collapsed; Get<Border>("SpeedPicker").Visibility = Visibility.Collapsed;
            CancelPlaceSelection();
            zoomWait.Stop();
            StopRadar();
            if (loading != null) loading.Cancel();
            mode = value;
            if (mode == 2 && !showDailyWeather && choice[2] == 0 && SavedPlace.Coordinate == null && SavedPlace.IsHidden(0))
                choice[2] = Enumerable.Range(1, 3).FirstOrDefault(r => !SavedPlace.IsHidden(r));
            Preferences.LastTab = value; Preferences.Save();
            root.Height = PreferredHeight;
            if (IsVisible) Shell.Position(this, 458, PreferredHeight);
            for (int i = 0; i < tabs.Length; i++) tabs[i].Background = i == mode ? activeTab : Brushes.Transparent;
            Get<Grid>("CalendarBody").Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("DataBody").Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (mode == 0) { if (IsVisible) { UpdateLayout(); effect.Start(true); } }
            else LoadView(false);
        }

        public double PreferredHeight { get { return mode == 2 && !showDailyWeather ? 574 : 542; } }

        async void LoadView(bool force)
        {
            if (mode == 0 || !IsVisible || IsClosing) return;
            if (mode >= 3) { await LoadInformation(force); return; }
            if (mode == 2 && showDailyWeather) { await LoadDailyWeather(force); return; }
            if (loading != null) loading.Cancel();
            PrepareStandardView();
            Get<TextBlock>("MarketValue").Text = "";
            SetVisible("MarketValue", false);
            var request = new CancellationTokenSource();
            loading = request;
            int index = choice[mode], key = mode * 10 + index;
            StopRadar();
            var requestedViewport = mapViewport;
            var previousWeather = activeWeather;
            bool market = mode == 1;
            var instrument = market ? SelectedMarket : null;
            Get<ScrollViewer>("MarketScroll").Visibility = market ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("ManageMarkets").Visibility = market ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("RegionChoices").Visibility = Visibility.Collapsed;
            SetVisible("WeatherScroll", !market);
            if (!market) RebuildWeatherChoices();
            Get<Button>("PickPlace").IsEnabled = false;
            Get<TextBlock>("MapHint").Visibility = Visibility.Collapsed;
            Get<StackPanel>("MapTools").Visibility = market ? Visibility.Collapsed : Visibility.Visible;
            Get<Button>("AutoPlace").Visibility = Visibility.Collapsed;
            Get<Canvas>("Plot").Height = market ? 220 : LiveData.MapHeight;
            Get<Canvas>("Plot").Width = market ? 414 : LiveData.MapWidth;
            SetVisible("MarketRange", market);
            SetVisible("DataSource", false);
            Get<Grid>("RadarControls").Visibility = market ? Visibility.Collapsed : Visibility.Visible;
            Get<TextBlock>("RadarRange").Visibility = Visibility.Visible;
            if (market) Get<TextBlock>("RadarRange").Visibility = Visibility.Collapsed;
            Get<Grid>("RainLegend").Visibility = market ? Visibility.Collapsed : Visibility.Visible;
            Get<TextBlock>("PlotCaption").Visibility = market ? Visibility.Visible : Visibility.Collapsed;
            var names = LiveData.Regions;
            var stamp = Get<TextBlock>("DataStamp");
            var refresh = Get<Button>("RefreshData");
            try
            {
                for (int i = 0; !market && i < choices.Length; i++)
                {
                    choices[i].Visibility = i < names.Length ? Visibility.Visible : Visibility.Collapsed;
                    if (i >= names.Length) continue;
                    choices[i].Content = names[i];
                    choices[i].ToolTip = i == 0 && SavedPlace.Coordinate != null ? SavedPlace.DisplayName + "周辺へ戻る" : null;
                    choices[i].Background = i == index ? activeTab : Brushes.Transparent;
                }
                Get<TextBlock>("SectionLabel").Text = market ? "MARKET   /" : "WEATHER";
                Get<Button>("DataSource").IsEnabled = !market || instrument != null;
                Get<Button>("DataSource").Content = market ? (instrument != null && instrument.Supported ? instrument.SourceName + "  ↗" : "投資信託の名簿（価格取得未対応） ↗") : "気象庁 雨雲の動き  ↗   ／   地図：国土地理院（加工）";
                LiveData.View view;
                if (market && (instrument == null || !instrument.Supported))
                {
                    Get<Canvas>("Plot").Children.Clear();
                    Get<TextBlock>("DataTitle").Text = instrument == null ? "銘柄を追加してください" : instrument.Label;
                    stamp.Text = instrument == null ? "＋ または設定 → 銘柄の管理" : "価格取得未対応 · 登録のみ";
                    Get<TextBlock>("PlotCaption").Text = instrument == null ? "初期表示は日経平均とドル円です" : "この運用会社の基準価額には未対応です";
                    refresh.IsEnabled = false; nextUpdate = DateTime.UtcNow.AddHours(1);
                    effect.Start(true); return;
                }
                if (market) marketSaved.TryGetValue(MarketCacheKey(instrument), out view); else saved.TryGetValue(key, out view);
                bool fetch = force || requestedViewport != null || view == null || DateTime.UtcNow - view.Fetched >= TimeSpan.FromMinutes(5);
                if (!market && weatherForecast == null) weatherForecast = LiveData.CachedForecast;
                if (view != null && requestedViewport == null) ShowData(view);
                else if (requestedViewport != null && previousWeather != null) ShowData(previousWeather);
                else
                {
                    radarFrames = null;
                    activeWeather = null;
                    Get<Button>("RadarPlay").IsEnabled = false;
                    Get<Slider>("RadarTime").IsEnabled = false;
                    Get<Canvas>("Plot").Children.Clear();
                    Get<TextBlock>("DataTitle").Text = market ? instrument.Label : names[index];
                    Get<TextBlock>("PlotCaption").Text = "";
                }
                if (fetch)
                {
                    stamp.Text = view == null ? "" : view.Stamp;
                    BeginDataLoading(request);
                    refresh.IsEnabled = false;
                }
                UpdateLayout();
                effect.Start(true);
                if (fetch)
                {
                    view = market ? await LiveData.MarketPeriod(instrument, marketMonths, request.Token) : await LiveData.Weather(index, request.Token, requestedViewport);
                    if (request.IsCancellationRequested) return;
                    if (market) { marketSaved[MarketCacheKey(instrument)] = view; Watchlist.Confirm(instrument); }
                    else if (requestedViewport == null) saved[key] = view;
                    if (!market) { weatherForecast = await TryForecast(request.Token, force); if (request.IsCancellationRequested) return; }
                    EndDataLoading(request);
                    ShowData(view);
                    UpdateLayout();
                    effect.Start(true);
                }
                nextUpdate = view.Fetched.AddMinutes(5);
            }
            catch (FundDataUnavailableException ex)
            {
                if (request.IsCancellationRequested) return;
                Watchlist.NoData(instrument);
                LiveData.View previous;
                stamp.Text = marketSaved.TryGetValue(MarketCacheKey(instrument), out previous) ? previous.Stamp + " · " + ex.Message : ex.Message;
                nextUpdate = DateTime.UtcNow.AddMinutes(5);
            }
            catch (Exception)
            {
                if (request.IsCancellationRequested) return;
                LiveData.View previous;
                bool hasPrevious = market ? marketSaved.TryGetValue(MarketCacheKey(instrument), out previous) : saved.TryGetValue(key, out previous);
                stamp.Text = hasPrevious ? previous.Stamp + "  ·  更新失敗" : "取得失敗 · 配信対象外または通信エラー。更新で再試行";
                if (!market && requestedViewport != null && previousWeather != null)
                {
                    mapViewport = previousWeather.Viewport;
                    ShowData(previousWeather);
                    stamp.Text = previousWeather.Stamp + " · 範囲更新失敗";
                }
                nextUpdate = DateTime.UtcNow.AddMinutes(1);
            }
            finally
            {
                EndDataLoading(request);
                if (loading == request)
                {
                    loading = null;
                    refresh.IsEnabled = !market || instrument != null && instrument.Supported;
                    Get<Button>("PickPlace").IsEnabled = mode == 2 && activeWeather != null;
                    if (mode == 2 && selectingPlace && candidatePlace != null && activeWeather != null)
                    {
                        var point = PlacePixel(activeWeather.Viewport, candidatePlace);
                        bool visible = point.X >= 0 && point.X <= LiveData.MapWidth && point.Y >= 0 && point.Y <= LiveData.MapHeight;
                        Get<Button>("SavePlace").IsEnabled = visible;
                        Get<TextBlock>("MapHint").Visibility = Visibility.Visible;
                        Get<TextBlock>("MapHint").Text = visible ? "橙色の候補を確認して保存 · 地図クリックで調整できます" : "候補の地図を取得できませんでした。更新で再試行してください";
                    }
                    if (mode != 0 && IsVisible)
                    {
                        effect.Refresh();
                        RefreshViewAfterLayout();
                    }
                }
                request.Dispose();
            }
        }

        void RefreshViewAfterLayout()
        {
            Dispatcher.BeginInvoke(new Action(delegate { if (IsVisible && !IsClosing) effect.Refresh(); }),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        void StopRadar()
        {
            radarPlayback.Stop();
            radarWatch.Stop();
            Get<Button>("RadarPlay").Content = "▶";
        }

        void DrawRadar(LiveData.View view)
        {
            var plot = Get<Canvas>("Plot");
            activeWeather = view;
            plot.Children.Add(new Image { Source = view.Map });
            DrawPrefectureLines(view);
            DrawCityLabels(view);
            Get<Canvas>("Plot").ToolTip = view.LocationHint + " · ホイール拡縮 / ドラッグ移動";
            if (!selectingPlace) Get<TextBlock>("MapHint").Visibility = Visibility.Collapsed;
            DrawCandidatePlace(view);
            if (!view.LocationPoint.HasValue) return;
            var point = view.LocationPoint.Value;
            if (view.AccuracyPixels > 4)
            {
                double radius = Math.Min(230, view.AccuracyPixels);
                var uncertainty = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = Art.Ink("#F5A2A2"),
                    StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, Opacity = .65 };
                Canvas.SetLeft(uncertainty, point.X - radius); Canvas.SetTop(uncertainty, point.Y - radius); plot.Children.Add(uncertainty);
            }
            var halo = new Ellipse { Width = 14, Height = 14, Fill = Art.Ink("#492D3C"), Stroke = Art.Ink("#FFE6E6"), StrokeThickness = 1 };
            Canvas.SetLeft(halo, point.X - 7); Canvas.SetTop(halo, point.Y - 7); plot.Children.Add(halo);
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Art.Ink("#FF4949"), ToolTip = SavedPlace.Coordinate == null ? "現在地（位置情報による推定）" : SavedPlace.DisplayName + "（保存した基準地点）" };
            Canvas.SetLeft(dot, point.X - 4); Canvas.SetTop(dot, point.Y - 4); plot.Children.Add(dot);
        }

        void ShowRadarFrame(int index)
        {
            var frame = radarFrames[index];
            var plot = Get<Canvas>("Plot");
            plot.Children.Clear();
            DrawRadar(frame);
            UpdateRadarCaption(frame);
            effect.Refresh();
        }

        void ShowData(LiveData.View view)
        {
            Get<TextBlock>("DataTitle").Text = view.Map == null && !string.IsNullOrEmpty(view.MarketName) ? view.MarketName : view.Heading;
            Get<TextBlock>("MarketValue").Text = view.MarketValue ?? "";
            SetVisible("MarketValue", view.Map == null && !string.IsNullOrEmpty(view.MarketValue));
            Get<TextBlock>("DataStamp").Text = view.Stamp;
            Get<TextBlock>("PlotCaption").Text = view.Caption;
            var plot = Get<Canvas>("Plot");
            plot.Children.Clear();
            if (view.Map != null)
            {
                SetRadarTimeline(view);
                return;
            }
            var bars = view.Bars;
            double low = bars.Min(b => b.Low), high = bars.Max(b => b.High);
            double span = Math.Max(high - low, .01);
            Func<double, double> y = v => 207 - 185 * (v - low) / span;
            var faint = Art.Ink("#4A717E");
            for (int i = 0; i <= 4; i++)
            {
                double price = low + span * i / 4;
                double lineY = y(price);
                plot.Children.Add(new Line { X1 = 0, X2 = 348, Y1 = lineY, Y2 = lineY, Stroke = faint });
                var label = new TextBlock { Text = price.ToString(high < 1000 ? "#,##0.00" : "#,##0"), Foreground = Art.Ink("#A9CAD6"), FontSize = 12 };
                Canvas.SetLeft(label, 354); Canvas.SetTop(label, lineY - 8); plot.Children.Add(label);
            }
            if (view.IsFund)
            {
                var line = new Polyline { Stroke = Art.Ink("#7BE5DA"), StrokeThickness = 1.7 };
                for (int i = 0; i < bars.Count; i++) line.Points.Add(new Point(344.0 * i / (bars.Count - 1), y(bars[i].Close)));
                plot.Children.Add(line);
                return;
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
                var body = new Rectangle { Width = Math.Max(.6, Math.Min(5, step * .72)), Height = Math.Max(1.5, Math.Abs(y(bar.Open) - y(bar.Close))), Fill = brush };
                Canvas.SetLeft(body, x - body.Width / 2); Canvas.SetTop(body, y(Math.Max(bar.Open, bar.Close))); plot.Children.Add(body);
            }
        }
    }
}
