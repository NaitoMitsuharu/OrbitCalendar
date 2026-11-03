using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        LiveData.Viewport mapViewport;
        LiveData.View activeWeather;
        readonly DispatcherTimer zoomWait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        Point? dragStart;
        DateTime lastMapDraw;

        static LiveData.Viewport MoveMap(LiveData.Viewport start, double dx, double dy, int zoom)
        {
            double oldWorld = 256 << start.Zoom, world = 256 << zoom;
            double latitude = start.Latitude * Math.PI / 180;
            double x = (start.Longitude + 180) / 360 * oldWorld;
            double y = (1 - Math.Log(Math.Tan(latitude) + 1 / Math.Cos(latitude)) / Math.PI) * oldWorld / 2;
            double scale = zoom == 4 ? .76 : .88;
            x = (x / oldWorld * world) - dx / scale;
            y = (y / oldWorld * world) - dy / scale;
            return new LiveData.Viewport { Latitude = Math.Max(20, Math.Min(48, Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / world))) * 180 / Math.PI)),
                Longitude = Math.Max(120, Math.Min(155, x / world * 360 - 180)), Zoom = zoom, Marker = start.Marker, Name = "表示範囲" };
        }

        void InitializeMapInteraction()
        {
            var plot = Get<Canvas>("Plot");
            zoomWait.Tick += delegate { zoomWait.Stop(); if (mode == 2) LoadView(true); };
            plot.MouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (mode != 2 || activeWeather == null) return;
                e.Handled = true;
                StopRadar();
                var start = mapViewport ?? activeWeather.Viewport;
                int zoom = Math.Max(4, Math.Min(10, start.Zoom + Math.Sign(e.Delta)));
                if (zoom == start.Zoom) return;
                Point at = e.GetPosition(plot);
                double oldScale = start.Zoom == 4 ? .76 : .88, newScale = zoom == 4 ? .76 : .88;
                double factor = Math.Pow(2, zoom - start.Zoom);
                mapViewport = MoveMap(start, (at.X - LiveData.MapWidth / 2.0) * (1 - factor * newScale / oldScale),
                    (at.Y - LiveData.MapHeight / 2.0) * (1 - factor * newScale / oldScale), zoom);
                if (loading != null) loading.Cancel();
                zoomWait.Stop(); zoomWait.Start();
            };
            plot.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (mode != 2 || activeWeather == null || loading != null || zoomWait.IsEnabled || effect.Busy) return;
                if (selectingPlace) { SelectPlaceAt(e.GetPosition(plot)); e.Handled = true; return; }
                StopRadar(); dragStart = e.GetPosition(plot); plot.CaptureMouse(); e.Handled = true;
            };
            plot.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!dragStart.HasValue || DateTime.UtcNow - lastMapDraw < TimeSpan.FromMilliseconds(30)) return;
                var delta = e.GetPosition(plot) - dragStart.Value;
                foreach (UIElement child in plot.Children) child.RenderTransform = new TranslateTransform(delta.X, delta.Y);
                lastMapDraw = DateTime.UtcNow;
                effect.RefreshArea(PanelBounds(plot));
            };
            plot.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!dragStart.HasValue) return;
                var delta = e.GetPosition(plot) - dragStart.Value;
                dragStart = null; plot.ReleaseMouseCapture(); e.Handled = true;
                foreach (UIElement child in plot.Children) child.RenderTransform = Transform.Identity;
                if (delta.Length > 3)
                {
                    mapViewport = MoveMap(activeWeather.Viewport, delta.X, delta.Y, activeWeather.Viewport.Zoom);
                    LoadView(true);
                }
                else effect.Refresh();
            };
            plot.LostMouseCapture += delegate
            {
                if (!dragStart.HasValue) return;
                dragStart = null;
                foreach (UIElement child in plot.Children) child.RenderTransform = Transform.Identity;
                effect.Refresh();
            };
            IsVisibleChanged += delegate { if (!IsVisible) { zoomWait.Stop(); plot.ReleaseMouseCapture(); } };
        }
    }
}
