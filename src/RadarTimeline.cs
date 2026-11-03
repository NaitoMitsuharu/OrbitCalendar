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
        readonly System.Diagnostics.Stopwatch radarWatch = new System.Diagnostics.Stopwatch();
        double radarStartMinute;
        int drawnRadarIndex = -1;
        bool radarPaintQueued;
        double RadarMinutes(LiveData.View frame) { return Math.Max(0, (frame.ValidUtc - radarOrigin).TotalMinutes); }
        int RadarIndex(double minutes)
        {
            if (radarFrames == null || radarFrames.Length == 0) return 0;
            int index = 0;
            for (int i = 1; i < radarFrames.Length; i++) if (RadarMinutes(radarFrames[i]) <= minutes + .00001) index = i;
            return index;
        }
        void StartRadarPlayback()
        {
            radarStartMinute = Get<Slider>("RadarTime").Value;
            radarWatch.Restart(); radarPlayback.Start();
        }
        void AdvanceRadarPlayback()
        {
            if (radarFrames == null || radarFrames.Length < 2) { StopRadar(); return; }
            var slider = Get<Slider>("RadarTime");
            slider.Value = Math.Min(slider.Maximum, radarStartMinute + radarWatch.Elapsed.TotalSeconds * slider.Maximum / 15);
            if (slider.Value >= slider.Maximum) { StopRadar(); effect.Refresh(); }
        }
        void SeekRadar(double minute)
        {
            int index = RadarIndex(minute);
            if (index != drawnRadarIndex) { ShowRadarFrame(index); drawnRadarIndex = index; }
            UpdateTemperature(minute);
            Get<TextBlock>("RadarClock").Text = radarOrigin.AddMinutes(minute).AddHours(9).ToString("HH:mm");
            // Track arranges its thumb after ValueChanged. Capture after that
            // layout pass, coalescing drag/playback updates into one refresh.
            if (radarPaintQueued) return;
            radarPaintQueued = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                radarPaintQueued = false;
                if (!IsVisible || mode != 2 || showDailyWeather || IsClosing) return;
                var area = PanelBounds(Get<Canvas>("TemperaturePlot"));
                area.Union(PanelBounds(Get<Grid>("RadarControls")));
                effect.RefreshArea(area);
            }), System.Windows.Threading.DispatcherPriority.Render);
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
            string range = radarOrigin.AddHours(9).ToString("HH:mm") + " → " + radarFrames.Last().ValidUtc.AddHours(9).ToString("HH:mm");
            Get<TextBlock>("RadarRange").Text = range + (age > 15 ? " · 観測" + (int)age + "分前" : "");
            DrawRadar(view);
            drawnRadarIndex = Array.IndexOf(radarFrames, view);
            UpdateRadarCaption(view);
            DrawTemperatureGraph();
            Get<TextBlock>("RadarClock").Text = radarOrigin.AddMinutes(slider.Value).AddHours(9).ToString("HH:mm");
        }
    }
}
