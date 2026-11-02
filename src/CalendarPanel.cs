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
    internal class CalendarPanel : Window
    {
        readonly Grid root, dates;
        readonly AssemblyEffect effect;
        readonly Action collapse, toggleFocus;
        readonly CultureInfo ja = CultureInfo.GetCultureInfo("ja-JP");
        readonly Brush aqua = Art.Ink("#7BE5DA"), muted = Art.Ink("#96ADB8"), lines = Art.Ink("#203A46");
        DateTime selected = DateTime.Today, today = DateTime.Today;
        DateTime copiedUntil;

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
            effect = new AssemblyEffect(Get<Grid>("Face"));
            Get<Grid>("Effects").Children.Add(effect);
            Content = new Viewbox { Child = root, Stretch = Stretch.Uniform };
            Art.DrawSky(Get<Canvas>("SkyMap"));
            Get<Button>("Previous").Click += delegate { MoveMonth(-1); };
            Get<Button>("Next").Click += delegate { MoveMonth(1); };
            Get<Button>("Today").Click += delegate { SelectDate(DateTime.Today); };
            Get<Button>("CloseButton").Click += delegate { collapse(); };
            Get<Button>("FocusButton").Click += delegate { toggleFocus(); };
            Get<Button>("Copy").Click += delegate { CopyDate(); };
            root.MouseMove += delegate(object sender, MouseEventArgs e) { effect.Nudge(e.GetPosition(effect)); };
            PreviewKeyDown += OnKey;
            IsVisibleChanged += delegate { if (!IsVisible) effect.Stop(); };
            for (int c = 0; c < 8; c++) dates.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(37) : new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < 7; r++) dates.RowDefinitions.Add(new RowDefinition { Height = r == 0 ? new GridLength(35) : new GridLength(1, GridUnitType.Star) });
            DrawDates();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) collapse();
            else if (e.Key == Key.PageUp) MoveMonth(-1);
            else if (e.Key == Key.PageDown) MoveMonth(1);
            else if (e.Key == Key.Home) SelectDate(DateTime.Today);
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) CopyDate();
            else return;
            e.Handled = true;
        }

        void CopyDate()
        {
            string label;
            try { Clipboard.SetText(selected.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); label = "コピー済み ✓"; }
            catch (System.Runtime.InteropServices.ExternalException) { label = "再試行してください"; }
            Get<Button>("Copy").Content = label;
            copiedUntil = DateTime.UtcNow.AddSeconds(2);
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
            if (changeMonth) effect.Remember();
            selected = date;
            DrawDates();
            if (changeMonth) { UpdateLayout(); effect.Start(true); }
            else effect.Resample();
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
            Place(Label("週", muted, 11), 0, 0);
            string[] days = { "月", "火", "水", "木", "金", "土", "日" };
            var blue = Art.Ink("#8EC5FF");
            var red = Art.Ink("#FF9699");
            for (int col = 1; col <= 7; col++)
            {
                var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var dayColor = col == 6 ? blue : col == 7 ? red : muted;
                labels.Children.Add(Label(col.ToString("00"), dayColor, 9));
                labels.Children.Add(Label(days[col - 1], col > 5 ? dayColor : Brushes.WhiteSmoke, 12));
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
                        Padding = new Thickness(0), FontSize = 16, FontFamily = new FontFamily("Bahnschrift"),
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
            Get<TextBlock>("SelectedLabel").Text = "SELECTED    /    W" + IsoWeek(selected).ToString("00");
            Get<TextBlock>("SelectedDate").Text = selected.ToString("M月d日  dddd", ja);
            var selectedHoliday = Holidays.Name(selected);
            if (selectedHoliday != null) Get<TextBlock>("SelectedLabel").Text += "    /    " + selectedHoliday;
            Get<TextBlock>("DayCount").Text = "DAY  " + selected.DayOfYear.ToString("000") + "  /  " + (DateTime.IsLeapYear(selected.Year) ? "366" : "365");
            Get<Button>("Copy").Content = "日付をコピー";
        }

        public void Open()
        {
            Shell.Position(this, 458, 542);
            Show();
            Activate();
            UpdateLayout();
            effect.Start(false);
        }

        public void Tick(DateTime now, string focus)
        {
            if (today != now.Date)
            {
                if (selected == today) selected = now.Date;
                today = now.Date;
                DrawDates();
                effect.Resample();
            }
            if (copiedUntil != default(DateTime) && DateTime.UtcNow >= copiedUntil)
            {
                copiedUntil = default(DateTime);
                Get<Button>("Copy").Content = "日付をコピー";
            }
            Get<Button>("FocusButton").Content = focus.Length == 0 ? "▶   集中 25 分" : "■   集中  " + focus;
        }
    }
}
