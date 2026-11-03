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

        async void UpdateTodayWeather()
        {
            if (updatingToday || DateTime.UtcNow < nextTodayWeather) return;
            updatingToday = true;
            nextTodayWeather = DateTime.UtcNow.AddMinutes(10);
            var icon = Get<TextBlock>("TodayWeather");
            try
            {
                int revision = SavedPlace.Revision;
                int? code = await LiveData.TodayWeather();
                if (revision != SavedPlace.Revision) { nextTodayWeather = DateTime.MinValue; return; }
                icon.Text = code.HasValue ? WeatherIcons.Glyph(code.Value) : "◌";
                icon.ToolTip = code.HasValue ? DateTime.Now.ToString("M/d") + " 今日の天気予報 · " + (SavedPlace.Coordinate == null ? "現在地周辺" : "保存地点周辺") + " · Open-Meteo" :
                    "今日の天気 · 現在地を取得できませんでした";
                if (code.HasValue) nextTodayWeather = DateTime.UtcNow.AddHours(1);
            }
            catch (Exception)
            {
                icon.Text = "◌";
                icon.ToolTip = "今日の天気を取得できませんでした";
            }
            finally { updatingToday = false; if (IsVisible && mode == 0) effect.Refresh(); }
        }
    }
}
