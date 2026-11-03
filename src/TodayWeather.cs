using System;
using System.Device.Location;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace OrbitCalendar
{
    internal static class WeatherIcons
    {
        public static string Glyph(int code)
        {
            if (code == 0 || code == 1) return "☀";
            if (code == 2 || code == 3 || code == 45 || code == 48) return "☁";
            if (code >= 95) return "ϟ";
            if (code >= 71 && code <= 77 || code == 85 || code == 86) return "❄";
            return "☂";
        }
    }

    internal partial class CalendarPanel
    {
        DateTime nextTodayWeather;
        bool updatingToday;

        static string ComparedTemperature(double? value, double? previous)
        {
            string difference = "—";
            if (value.HasValue && previous.HasValue)
            {
                double delta = Math.Round(value.Value - previous.Value, 1, MidpointRounding.AwayFromZero);
                difference = (delta > 0 ? "+" : delta < 0 ? "−" : "") + Math.Abs(delta).ToString("0.#", CultureInfo.InvariantCulture) + "°";
            }
            return Value(value, "°") + " (" + difference + ")";
        }

        void RenderTodayWeather(LiveData.WeatherForecast forecast)
        {
            Get<TextBlock>("TodayHigh").Text = "↑ " + ComparedTemperature(forecast.daily.temperature_2m_max[0], forecast.PreviousHigh);
            Get<TextBlock>("TodayLow").Text = "↓ " + ComparedTemperature(forecast.daily.temperature_2m_min[0], forecast.PreviousLow);
            Get<StackPanel>("TodayTemperatures").ToolTip = "今日の最高 / 最低気温（括弧内は前日の最高 / 最低との差） · " + forecast.Place + " · Open-Meteoのモデル値";
            var icon = Get<TextBlock>("TodayWeather");
            int? code = forecast.daily.weather_code[0];
            icon.Text = code.HasValue ? WeatherIcons.Glyph(code.Value) : "◌";
            icon.ToolTip = DateTime.Now.ToString("M/d") + " 今日の代表的な天気（時間の変化は表しません） · " + forecast.Place + " · Open-Meteo";
        }

        async void UpdateTodayWeather()
        {
            if (updatingToday || DateTime.UtcNow < nextTodayWeather) return;
            updatingToday = true;
            nextTodayWeather = DateTime.UtcNow.AddMinutes(10);
            var icon = Get<TextBlock>("TodayWeather");
            try
            {
                int revision = SavedPlace.Revision;
                var forecast = await LiveData.ForecastWeather(CancellationToken.None, false);
                int? code = forecast.daily.weather_code[0];
                if (revision != SavedPlace.Revision) { nextTodayWeather = DateTime.MinValue; return; }
                RenderTodayWeather(forecast);
                if (code.HasValue) nextTodayWeather = DateTime.UtcNow.AddHours(1);
            }
            catch (Exception)
            {
                icon.Text = "◌";
                Get<TextBlock>("TodayHigh").Text = "↑ — (—)"; Get<TextBlock>("TodayLow").Text = "↓ — (—)";
                icon.ToolTip = "今日の天気を取得できませんでした";
            }
            finally { updatingToday = false; if (IsVisible && mode == 0) effect.Refresh(); }
        }
    }
}
