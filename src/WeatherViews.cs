using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        bool showDailyWeather;
        int forecastDay;
        LiveData.WeatherForecast weatherForecast;
        Line temperatureCursor;
        Ellipse temperatureDot;
        TextBlock temperatureValue;
        double temperatureLow, temperatureHigh;
        void InitializeWeatherModes()
        {
            Get<Button>("RainMode").Click += delegate { if (showDailyWeather) { showDailyWeather = false; SwitchView(2); } };
            Get<Button>("DailyMode").Click += delegate { if (!showDailyWeather) { showDailyWeather = true; SwitchView(2); } };
        }
        void SetVisible(string name, bool visible) { Get<FrameworkElement>(name).Visibility = visible ? Visibility.Visible : Visibility.Collapsed; }
        void PrepareStandardView()
        {
            SetVisible("InformationBody", false); SetVisible("DailyWeatherBody", false);
            SetVisible("WeatherSwitch", mode == 2); SetVisible("Plot", true); SetVisible("TemperaturePlot", mode == 2);
            SetVisible("DataChoices", true); SetVisible("MarketRange", mode == 1); SetVisible("DataSource", false);
            Get<Button>("RainMode").Background = activeTab; Get<Button>("DailyMode").Background = Brushes.Transparent;
        }
        async Task<LiveData.WeatherForecast> TryForecast(CancellationToken token, bool force)
        {
            try { return await LiveData.ForecastWeather(token, force); }
            catch (Exception) { token.ThrowIfCancellationRequested(); return null; }
        }
        async Task LoadDailyWeather(bool force)
        {
            if (loading != null) loading.Cancel();
            var request = new CancellationTokenSource(); loading = request;
            StopRadar();
            SetVisible("InformationBody", false); SetVisible("DailyWeatherBody", true); SetVisible("WeatherSwitch", true);
            foreach (string name in new[] { "Plot", "TemperaturePlot", "RadarControls", "RainLegend", "RadarRange", "MapTools", "MapHint", "PlotCaption", "MarketScroll", "ManageMarkets" }) SetVisible(name, false);
            SetVisible("DataChoices", true); SetVisible("RegionChoices", false); SetVisible("DataSource", false);
            Get<Button>("RainMode").Background = Brushes.Transparent; Get<Button>("DailyMode").Background = activeTab;
            Get<TextBlock>("SectionLabel").Text = "WEATHER";
            SetVisible("MarketRange", false);
            Get<TextBlock>("DataTitle").Text = "今日・明日・明後日";
            Get<TextBlock>("DataStamp").Text = "基準地点の予報を取得中…";
            Get<Button>("DataSource").Content = "Open-Meteo ↗ · 予報は随時変わります";
            Get<Button>("RefreshData").IsEnabled = false;
            Get<StackPanel>("DailyWeatherBody").Children.Clear();
            UpdateLayout(); effect.Start(true);
            try
            {
                var result = await LiveData.ForecastWeather(request.Token, force);
                if (request.IsCancellationRequested) return;
                weatherForecast = result; RenderDailyWeather(); nextUpdate = result.Fetched.AddMinutes(15);
                var midnight = DateTime.UtcNow.AddHours(9).Date.AddDays(1).AddHours(-9);
                if (nextUpdate > midnight) nextUpdate = midnight;
            }
            catch (Exception)
            {
                if (request.IsCancellationRequested) return;
                Get<TextBlock>("DataStamp").Text = "予報を取得できません · 更新で再試行"; nextUpdate = DateTime.UtcNow.AddMinutes(1);
            }
            finally
            {
                if (loading == request) { loading = null; Get<Button>("RefreshData").IsEnabled = true; UpdateLayout(); effect.Refresh(); RefreshViewAfterLayout(); }
                request.Dispose();
            }
        }
        static TextBlock WeatherText(string text, double size, string ink)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = Art.Ink(ink), VerticalAlignment = VerticalAlignment.Center };
        }
        static string Value(double? value, string suffix) { return value.HasValue ? value.Value.ToString("0.#") + suffix : "—"; }
        void RenderDailyWeather()
        {
            var f = weatherForecast; var d = f.daily;
            Get<TextBlock>("DataStamp").Text = f.Place + " · " + f.Fetched.AddHours(9).ToString("HH:mm") + " 取得";
            var body = Get<StackPanel>("DailyWeatherBody"); body.Children.Clear();
            var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 14) };
            string[] days = { "今日", "明日", "明後日" };
            for (int i = 0; i < 3; i++)
            {
                int index = i; var stack = new StackPanel { Margin = new Thickness(3, 8, 3, 8) };
                var date = LiveData.WeatherForecast.Utc(d.time[i]).AddHours(9);
                stack.Children.Add(WeatherText(days[i] + "  " + date.ToString("M/d"), 14, "#D0E5EB"));
                var glyph = WeatherText(d.weather_code[i].HasValue ? WeatherIcons.Glyph(d.weather_code[i].Value) : "—", 34, "#F4DD8E"); glyph.FontFamily = new FontFamily("Segoe UI Symbol"); glyph.Margin = new Thickness(0, 7, 0, 7); stack.Children.Add(glyph);
                stack.Children.Add(WeatherText(Value(d.temperature_2m_max[i], "°") + " / " + Value(d.temperature_2m_min[i], "°"), 19, "#E3F4F4"));
                stack.Children.Add(WeatherText("降水 " + Value(d.precipitation_probability_max[i], "%"), 12, "#91CCD1"));
                var button = new Button { Content = stack, Margin = new Thickness(2, 0, 2, 0), Background = i == forecastDay ? activeTab : Art.Ink("#152E39"), Padding = new Thickness(7) };
                button.Click += delegate { forecastDay = index; RenderDailyWeather(); UpdateLayout(); effect.Start(true); RefreshViewAfterLayout(); };
                cards.Children.Add(button);
            }
            body.Children.Add(cards);
            body.Children.Add(WeatherText(days[forecastDay] + "の気温と降水確率", 15, "#86E8DE"));
            var chart = new Canvas { Width = 414, Height = 146, Margin = new Thickness(0, 12, 0, 8) }; body.Children.Add(chart);
            DateTime dateStart = LiveData.WeatherForecast.Utc(d.time[forecastDay]);
            var h = f.hourly;
            if (h == null || h.time == null || h.temperature_2m == null) { chart.Children.Add(WeatherText("時間別の気温を取得できません", 13, "#B3CAD2")); return; }
            var hours = Enumerable.Range(0, Math.Min(h.time.Length, h.temperature_2m.Length)).Where(i => LiveData.WeatherForecast.Utc(h.time[i]) >= dateStart && LiveData.WeatherForecast.Utc(h.time[i]) < dateStart.AddDays(1)).ToArray();
            var values = hours.Where(i => h.temperature_2m[i].HasValue).Select(i => h.temperature_2m[i].Value).ToArray();
            if (values.Length == 0) { chart.Children.Add(WeatherText("時間別の気温を取得できません", 13, "#B3CAD2")); return; }
            double low = Math.Floor(values.Min()) - 1, high = Math.Ceiling(values.Max()) + 1;
            var line = new Polyline { Stroke = Art.Ink("#F4B09F"), StrokeThickness = 1.8 };
            foreach (var i in hours)
            {
                double x = 28 + (LiveData.WeatherForecast.Utc(h.time[i]) - dateStart).TotalHours / 23 * 366;
                if (h.precipitation_probability != null && i < h.precipitation_probability.Length && h.precipitation_probability[i].HasValue)
                {
                    double height = Math.Max(0, Math.Min(100, h.precipitation_probability[i].Value)) * .9;
                    var bar = new Rectangle { Width = 10, Height = height, Fill = Art.Ink("#24515F"), ToolTip = LiveData.WeatherForecast.Utc(h.time[i]).AddHours(9).ToString("HH:mm") + " 降水 " + Value(h.precipitation_probability[i], "%") };
                    Canvas.SetLeft(bar, x - 5); Canvas.SetTop(bar, 112 - height); chart.Children.Add(bar);
                }
                if (h.temperature_2m[i].HasValue) line.Points.Add(new Point(x, 100 - (h.temperature_2m[i].Value - low) / (high - low) * 85));
            }
            chart.Children.Add(line);
            foreach (int hour in new[] { 0, 6, 12, 18, 23 })
            {
                var text = WeatherText(hour.ToString("00") + ":00", 11, "#A6C8D0"); Canvas.SetLeft(text, Math.Min(377, 18 + hour / 23.0 * 366)); Canvas.SetTop(text, 122); chart.Children.Add(text);
            }
            var bounds = WeatherText(high.ToString("0") + "°", 11, "#F4B09F"); chart.Children.Add(bounds);
            var bottom = WeatherText(low.ToString("0") + "°", 11, "#F4B09F"); Canvas.SetTop(bottom, 94); chart.Children.Add(bottom);
            body.Children.Add(WeatherText("線：気温 (°C)     棒：降水確率 (0–100%)", 12, "#B3CAD2"));

        }
        void DrawTemperatureGraph()
        {
            var plot = Get<Canvas>("TemperaturePlot"); plot.Children.Clear(); temperatureCursor = null;
            var f = weatherForecast;
            if (f == null || !f.Temperature(radarOrigin).HasValue || !f.Temperature(radarOrigin.AddMinutes(Get<Slider>("RadarTime").Maximum)).HasValue)
            {
                plot.Children.Add(WeatherText("気温を取得できません", 12, "#92B5BE")); return;
            }
            double maximum = Get<Slider>("RadarTime").Maximum;
            var temperatures = Enumerable.Range(0, 61).Select(i => f.Temperature(radarOrigin.AddMinutes(maximum * i / 60)).Value).ToArray();
            temperatureLow = temperatures.Min() - .5; temperatureHigh = temperatures.Max() + .5;
            var line = new Polyline { Stroke = Art.Ink("#F4B09F"), StrokeThickness = 1.6 };
            for (int i = 0; i < temperatures.Length; i++) line.Points.Add(new Point(LiveData.MapWidth * i / 60.0, TemperatureY(temperatures[i])));
            plot.Children.Add(line);
            temperatureCursor = new Line { Y1 = 0, Y2 = 46, Stroke = Art.Ink("#7CB6BE"), StrokeThickness = 1 };
            temperatureDot = new Ellipse { Width = 6, Height = 6, Fill = Art.Ink("#FFE18B") };
            temperatureValue = WeatherText("", 14, "#F4DD8E"); Canvas.SetTop(temperatureValue, 11); Canvas.SetLeft(temperatureValue, LiveData.MapWidth + 7);
            plot.Children.Add(temperatureCursor); plot.Children.Add(temperatureDot); plot.Children.Add(temperatureValue);
            UpdateTemperature(Get<Slider>("RadarTime").Value);
        }
        double TemperatureY(double v) { return 41 - (v - temperatureLow) / (temperatureHigh - temperatureLow) * 35; }
        void UpdateTemperature(double minute)
        {
            if (temperatureCursor == null || weatherForecast == null) return;
            var v = weatherForecast.Temperature(radarOrigin.AddMinutes(minute));
            if (!v.HasValue) return;
            double x = LiveData.MapWidth * minute / Math.Max(1, Get<Slider>("RadarTime").Maximum);
            temperatureCursor.X1 = temperatureCursor.X2 = x;
            Canvas.SetLeft(temperatureDot, x - 3); Canvas.SetTop(temperatureDot, TemperatureY(v.Value) - 3);
            temperatureValue.Text = v.Value.ToString("0.0") + "°C";
        }
    }
}
