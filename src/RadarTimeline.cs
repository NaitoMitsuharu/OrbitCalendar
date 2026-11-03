using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        DateTime radarOrigin;
        double RadarMinutes(LiveData.View frame) { return Math.Max(0, (frame.ValidUtc - radarOrigin).TotalMinutes); }
        int RadarIndex(double minutes)
        {
            if (radarFrames == null || radarFrames.Length == 0) return 0;
            return Enumerable.Range(0, radarFrames.Length).OrderBy(i => Math.Abs(RadarMinutes(radarFrames[i]) - minutes)).First();
        }
        static string RainOffset(double minutes)
        {
            int total = (int)Math.Round(minutes);
            if (total <= 0) return "今";
            return "+" + (total >= 60 ? total / 60 + "時間" : "") + (total % 60 > 0 ? total % 60 + "分" : "");
        }
        void UpdateRadarCaption(LiveData.View frame)
        {
            Get<TextBlock>("DataStamp").Text = frame.Stamp;
            Get<TextBlock>("PlotCaption").Text = frame.Caption;
            Get<TextBlock>("RadarClock").Text = (!frame.Forecast && (radarOrigin - frame.ValidUtc).TotalMinutes > 15 ? "観測" : RainOffset(RadarMinutes(frame))) + " " + frame.ValidUtc.AddHours(9).ToString("HH:mm");
            Get<Button>("DataSource").Content = (frame.HourlyRain ? "気象庁 今後の雨" : "気象庁 雨雲の動き") + " ↗ ／ 地図：国土地理院";
        }
        void SetRadarTimeline(LiveData.View view)
        {
            radarFrames = view.Frames ?? new[] { view };
            radarOrigin = view.OriginUtc == default(DateTime) ? radarFrames[0].ValidUtc : view.OriginUtc;
            Get<Button>("RadarPlay").IsEnabled = radarFrames.Length > 1;
            var slider = Get<Slider>("RadarTime");
            slider.IsEnabled = radarFrames.Length > 1;
            updatingTimeline = true;
            slider.Minimum = 0;
            slider.Maximum = RadarMinutes(radarFrames.Last());
            slider.Ticks = new DoubleCollection(radarFrames.Select(RadarMinutes));
            slider.Value = RadarMinutes(view);
            updatingTimeline = false;
            double age = Math.Max(0, (radarOrigin - radarFrames[0].ValidUtc).TotalMinutes);
            string range = (age > 15 ? "観測" + (int)age + "分前" : "今（最新観測）") + " → " + RainOffset(slider.Maximum);
            Get<TextBlock>("RadarRange").Text = range + (radarFrames.Any(f => f.HourlyRain) ? " · 1時間先以降は1時間降水量" :
                radarFrames.Length > 1 ? " · 取得できた予測範囲" : " · 予測を取得できませんでした");
            DrawRadar(view);
            UpdateRadarCaption(view);
        }
    }
}
