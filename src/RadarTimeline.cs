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
        void AdvanceRadarPlayback()
        {
            if (radarFrames == null || radarFrames.Length < 2) { StopRadar(); return; }
            var slider = Get<Slider>("RadarTime");
            int index = RadarIndex(slider.Value);
            if (index < radarFrames.Length - 1) slider.Value = RadarMinutes(radarFrames[index + 1]);
            if (index >= radarFrames.Length - 2) { StopRadar(); effect.Refresh(); }
        }
        void UpdateRadarCaption(LiveData.View frame)
        {
            Get<TextBlock>("DataStamp").Text = frame.Stamp + (frame.HourlyRain ? "（" + frame.ValidUtc.AddHours(8).ToString("HH:mm") + "–" + frame.ValidUtc.AddHours(9).ToString("HH:mm") + "）" : "");
            Get<TextBlock>("PlotCaption").Text = frame.Caption;
            Get<TextBlock>("RadarClock").Text = frame.ValidUtc.AddHours(9).ToString("HH:mm");
            Get<TextBlock>("RainLegendTitle").Text = frame.HourlyRain ? "1時間雨量" : "降水強度";
            Get<TextBlock>("RainLegendUnit").Text = frame.HourlyRain ? "(mm)" : "(mm/h)";
            Get<Grid>("RainLegend").ToolTip = frame.Caption;
            Get<Button>("DataSource").Content = (frame.HourlyRain ? "気象庁 今後の雨" : "気象庁 雨雲の動き") + " ↗ ／ 地図：国土地理院";
        }
        void SetRadarTimeline(LiveData.View view)
        {
            Get<Grid>("RainLegend").Visibility = Visibility.Visible;
            Get<TextBlock>("PlotCaption").Visibility = Visibility.Collapsed;
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
            string range = radarFrames[0].ValidUtc.AddHours(9).ToString("HH:mm") + " → " + radarFrames.Last().ValidUtc.AddHours(9).ToString("HH:mm");
            Get<TextBlock>("RadarRange").Text = range + (age > 15 ? " · 観測" + (int)age + "分前" : "") + (radarFrames.Any(f => f.HourlyRain) ? " · 予測間隔：5分 / 約1時間" :
                radarFrames.Length > 1 ? " · 取得できた予測範囲" : " · 予測を取得できませんでした");
            DrawRadar(view);
            UpdateRadarCaption(view);
        }
    }
}
