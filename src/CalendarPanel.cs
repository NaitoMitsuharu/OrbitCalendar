using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace OrbitCalendar
{
    internal partial class CalendarPanel : Window
    {
        readonly Grid root, dates;
        readonly AssemblyEffect effect;
        readonly Action collapse, toggleFocus;
        readonly CultureInfo ja = CultureInfo.GetCultureInfo("ja-JP");
        readonly Brush aqua = Art.Ink("#7BE5DA"), muted = Art.Ink("#96ADB8"), lines = Art.Ink("#203A46");
        DateTime selected = DateTime.Today, today = DateTime.Today;
        int wheelDelta;
        bool timerRunning;
        int holidayRevision;

        T Get<T>(string name) where T : FrameworkElement { return (T)root.FindName(name); }

        public CalendarPanel(Action collapse, Action toggleFocus)
        {
            this.collapse = collapse;
            this.toggleFocus = toggleFocus;
            Title = "Orbit Calendar";
            Shell.StyleWindow(this);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Calendar.xaml"))
                root = (Grid)XamlReader.Load(stream);
            dates = Get<Grid>("Dates");
            effect = new AssemblyEffect(Get<Grid>("Face"), Get<Border>("ShellSurface"));
            Get<Grid>("Effects").Children.Add(effect);
            Content = new Viewbox { Child = root, Stretch = Stretch.Uniform };
            var earth = new EarthGlobe();
            Get<Canvas>("SkyMap").Children.Add(earth);
            var rotation = new System.Diagnostics.Stopwatch();
            var earthTick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
            earthTick.Tick += delegate
            {
                var globeArea = new Rect(198, 53, 95, 94);
                if (!SystemParameters.ClientAreaAnimation || mode != 0 || !effect.CanRefreshArea(globeArea)) return;
                earth.Rotation = rotation.Elapsed.TotalSeconds * .14;
                earth.InvalidateVisual();
                effect.RefreshArea(globeArea);
            };
            IsVisibleChanged += delegate
            {
                if (IsVisible) { rotation.Start(); earthTick.Start(); }
                else { rotation.Stop(); earthTick.Stop(); }
            };
            Get<Button>("Previous").Click += delegate { MoveMonth(-1); };
            Get<Button>("Next").Click += delegate { MoveMonth(1); };
            Get<Button>("Today").Click += delegate { SelectDate(DateTime.Today); };
            Get<Button>("FocusButton").Click += delegate { toggleFocus(); };
            InitializeControls();
            PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (mode != 0 || IsClosing || Get<Border>("MarketPicker").Visibility == Visibility.Visible || Get<Border>("SettingsPicker").Visibility == Visibility.Visible) return;
                wheelDelta += e.Delta;
                int months = wheelDelta / 120;
                wheelDelta %= 120;
                if (months != 0) MoveMonth(-months);
                e.Handled = true;
            };
            PreviewKeyDown += OnKey;
            IsVisibleChanged += delegate { if (!IsVisible) effect.Stop(); };
            for (int c = 0; c < 8; c++) dates.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(37) : new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < 7; r++) dates.RowDefinitions.Add(new RowDefinition { Height = r == 0 ? new GridLength(35) : new GridLength(1, GridUnitType.Star) });
            DrawDates();
            InitializeViews();
            if (Preferences.LastTab != 0) SwitchView(Preferences.LastTab);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (IsClosing) { e.Handled = true; return; }
            if (e.Key == Key.Escape && Get<Border>("MarketPicker").Visibility == Visibility.Visible) CloseMarketSettings();
            else if (Get<Border>("MarketPicker").Visibility == Visibility.Visible) return;
            else if (e.Key == Key.Escape && selectingPlace) { CancelPlaceSelection(); effect.Refresh(); }
            else if (e.Key == Key.Escape && Get<Border>("SpeedPicker").Visibility == Visibility.Visible) ClosePanel("SpeedPicker");
            else if (e.Key == Key.Escape && Get<Border>("SettingsPicker").Visibility == Visibility.Visible)
            {
                ClosePanel("SettingsPicker");
            }
            else if (e.Key == Key.Escape && Get<Border>("TimerPicker").Visibility == Visibility.Visible)
            {
                ClosePanel("TimerPicker");
            }
            else if (e.Key == Key.Escape && Get<Border>("RangePicker").Visibility == Visibility.Visible) ClosePanel("RangePicker");
            else if (e.Key == Key.Escape && Get<Border>("CreditsPicker").Visibility == Visibility.Visible) ClosePanel("CreditsPicker");
            else if (e.Key == Key.Escape) collapse();
            else if (mode != 0) return;
            else if (e.Key == Key.PageUp) MoveMonth(-1);
            else if (e.Key == Key.PageDown) MoveMonth(1);
            else if (e.Key == Key.Home) SelectDate(DateTime.Today);
            else return;
            e.Handled = true;
        }

        internal static int IsoDay(DateTime date) { return ((int)date.DayOfWeek + 6) % 7 + 1; }
        internal static int IsoWeek(DateTime date)
        {
            var thursday = date.AddDays(4 - IsoDay(date));
            return 1 + (thursday.DayOfYear - 1) / 7;
        }

        void MoveMonth(int direction)
        {
            var candidate = selected.AddMonths(direction);
            SelectDate(candidate);
        }

        void SelectDate(DateTime date)
        {
            if (date.Year < 2 || date.Year > 9998) return;
            bool changeMonth = date.Month != selected.Month || date.Year != selected.Year;
            effect.Stop();
            selected = date;
            DrawDates();
            UpdateLayout();
            var gauge = PanelBounds(Get<Border>("YearGauge")); gauge.Width = 422;
            var regions = new System.Collections.Generic.List<Rect> { PanelBounds(dates), PanelBounds(Get<TextBlock>("SelectedDate")), PanelBounds(Get<TextBlock>("HolidayName")), gauge };
            if (changeMonth) { regions.Add(PanelBounds(Get<TextBlock>("Year"))); regions.Add(PanelBounds(Get<TextBlock>("Month"))); }
            effect.StartCalendarChange(regions.ToArray());
        }

        void Place(UIElement item, int col, int row)
        {
            Grid.SetColumn(item, col);
            Grid.SetRow(item, row);
            dates.Children.Add(item);
        }

        TextBlock Label(string value, Brush brush, double size)
        {
            return new TextBlock { Text = value, Foreground = brush, FontSize = size,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        void DrawDates()
        {
            dates.Children.Clear();
            Get<TextBlock>("Year").Text = selected.ToString("yyyy", CultureInfo.InvariantCulture) + "   /   " + selected.ToString("MMMM", CultureInfo.InvariantCulture).ToUpperInvariant();
            Get<TextBlock>("Month").Text = selected.Month.ToString("00");
            var blue = Art.Ink("#8EC5FF");
            var red = Art.Ink("#FF9699");
            for (int col = 1; col <= 7; col++)
            {
                var labels = new Border { CornerRadius = new CornerRadius(4) };
                var dayColor = col == 6 ? blue : col == 7 ? red : muted;
                if (col == IsoDay(selected)) labels.Background = Art.Ink("#23505E");
                var number = Label(col.ToString(CultureInfo.InvariantCulture), col == IsoDay(selected) ? aqua : dayColor, 12);
                number.FontFamily = new FontFamily("Consolas");
                labels.Child = number;
                AutomationProperties.SetName(labels, "曜日番号 " + col);
                Place(labels, col, 0);
            }
            var first = new DateTime(selected.Year, selected.Month, 1);
            var start = first.AddDays(1 - IsoDay(first));
            for (int row = 1; row <= 6; row++)
            {
                var monday = start.AddDays(7 * (row - 1));
                var rule = new Border { BorderBrush = lines, BorderThickness = new Thickness(0, 1, 0, 0), IsHitTestVisible = false };
                Grid.SetColumnSpan(rule, 8);
                Place(rule, 0, row);
                bool currentWeek = selected >= monday && selected < monday.AddDays(7);
                var week = Label(IsoWeek(monday).ToString("00"), currentWeek ? aqua : muted, 12);
                week.FontFamily = new FontFamily("Consolas");
                week.ToolTip = "ISO 第" + IsoWeek(monday) + "週";
                Place(week, 0, row);
                for (int col = 1; col <= 7; col++)
                {
                    var day = monday.AddDays(col - 1);
                    bool active = day == selected;
                    string holiday = Holidays.Name(day);
                    bool redDay = holiday != null || col == 7;
                    Brush ink = redDay ? red : col == 6 ? blue : Art.Ink("#D9E9EB");
                    if (active) ink = Art.Ink(redDay ? "#822736" : col == 6 ? "#174C81" : "#0B2830");
                    var button = new Button { Content = day.Day.ToString(), Margin = new Thickness(3, 3, 3, 2),
                        Padding = new Thickness(0), FontSize = 18, FontFamily = new FontFamily("Bahnschrift"),
                        FontWeight = active ? FontWeights.Bold : FontWeights.Normal,
                        Background = active ? aqua : Brushes.Transparent,
                        Foreground = ink, Opacity = day.Month == selected.Month ? 1 : .62, ToolTip = holiday };
                    AutomationProperties.SetName(button, (day.ToString("yyyy年M月d日", ja) + " " + holiday).TrimEnd());
                    button.Click += delegate { SelectDate(day); };
                    Place(button, col, row);
                    if (day == today)
                    {
                        var dot = new Border { Width = 4, Height = 3, CornerRadius = new CornerRadius(2), Background = active ? Art.Ink("#16454C") : aqua,
                            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5), IsHitTestVisible = false };
                        Place(dot, col, row);
                    }
                }
            }
            Get<TextBlock>("SelectedDate").Text = "W" + IsoWeek(selected).ToString("00") + "." + IsoDay(selected);
            Get<TextBlock>("HolidayName").Text = Holidays.Name(selected) ?? "";
            Get<Border>("YearGauge").Width = 422.0 * selected.DayOfYear / (DateTime.IsLeapYear(selected.Year) ? 366 : 365);

        }

        public bool IsClosing { get { return effect.Closing; } }

        public void Collapse()
        {
            if (!IsVisible || IsClosing) return;
            CloseMarketSettings();
            Get<Border>("RangePicker").Visibility = Visibility.Collapsed;
            Get<Border>("CreditsPicker").Visibility = Visibility.Collapsed;
            StopRadar();
            CancelNewsPreview();
            ClearDataLoading();
            if (loading != null) { loading.Cancel(); nextUpdate = DateTime.MinValue; }
            CancelPlaceSelection();
            Get<Border>("TimerPicker").Visibility = Visibility.Collapsed;
            Get<Border>("SettingsPicker").Visibility = Visibility.Collapsed; Get<Border>("SpeedPicker").Visibility = Visibility.Collapsed;
            root.IsHitTestVisible = false;
            effect.Dismiss(Hide);
        }

        public void Open()
        {
            effect.Stop();
            root.IsHitTestVisible = true;
            root.Height = PreferredHeight;
            Shell.Position(this, 458, PreferredHeight);
            Show();
            Activate();
            if (mode == 3 && information != null) RenderInformation(informationStale);
            UpdateLayout();
            effect.Start(false);
            if (mode == 0) UpdateTodayWeather();
            else if (nextUpdate <= DateTime.UtcNow || loading == null && Get<TextBlock>("DataStamp").Text.Contains("取得中")) LoadView(false);
        }

        public void Tick(DateTime now, string focus)
        {
            if (IsClosing) return;
            if (IsVisible && mode == 0) UpdateTodayWeather();
            if (IsVisible && mode != 0 && loading == null && DateTime.UtcNow >= nextUpdate) LoadView(false);
            if (today != now.Date || holidayRevision != Holidays.Revision)
            {
                if (today != now.Date) nextTodayWeather = DateTime.MinValue;
                if (selected == today) selected = now.Date;
                holidayRevision = Holidays.Revision;
                today = now.Date;
                DrawDates();
                effect.Refresh();
            }
            timerRunning = focus.Length > 0;
            if (Get<Border>("TimerPicker").Visibility == Visibility.Visible)
                Get<TextBlock>("TimePickerHint").Text = timerRunning ? "選んだ時間は次回のタイマーに使います" : "時間を選び、▶ でスタート";
            var button = Get<Button>("TimerDuration");
            string label = focus.Length == 0 ? TimerMinutes + " 分" : focus;
            if (!Equals(label, button.Content))
            {
                button.Content = label;
                Get<Button>("FocusButton").Content = focus.Length == 0 ? "▶" : "■";
                if (mode == 0) effect.Refresh();
            }
        }
    }
}
