using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitCalendar
{
    internal sealed class Hourglass : FrameworkElement
    {
        public double Progress, Phase;
        public Hourglass() { Width = 28; Height = 34; }
        static StreamGeometry Shape(params Point[] points)
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(points[0], true, true); c.PolyLineTo(new System.Collections.Generic.List<Point>(points).GetRange(1, points.Length - 1), true, true); }
            g.Freeze(); return g;
        }
        protected override void OnRender(DrawingContext dc)
        {
            double p = Math.Max(0, Math.Min(1, Progress));
            var glass = new Pen(Art.Ink("#78A7B4"), 1.1);
            dc.DrawGeometry(null, glass, Shape(new Point(5, 5), new Point(23, 5), new Point(15, 16), new Point(23, 29), new Point(5, 29), new Point(13, 16)));
            dc.DrawLine(new Pen(Art.Ink("#7BE5DA"), 2), new Point(3, 2), new Point(25, 2));
            dc.DrawLine(new Pen(Art.Ink("#7BE5DA"), 2), new Point(3, 32), new Point(25, 32));
            double upper = Math.Sqrt(1 - p), lower = Math.Sqrt(p);
            var sand = Art.Ink("#F4DD8E");
            if (upper > .01) dc.DrawGeometry(sand, null, Shape(new Point(14 - 6 * upper, 15 - 8 * upper), new Point(14 + 6 * upper, 15 - 8 * upper), new Point(14, 15)));
            if (lower > .01) dc.DrawGeometry(sand, null, Shape(new Point(14, 28 - 10 * lower), new Point(14 + 7 * lower, 28), new Point(14 - 7 * lower, 28)));
            if (p < .999) for (int i = 0; i < 3; i++)
            {
                double y = 16 + ((Phase * 15 + i * 3.7) % 10);
                if (y < 28 - 10 * lower) dc.DrawEllipse(sand, null, new Point(14, y), .65, .9);
            }
        }
    }
    internal sealed class ParticleTimer : Window
    {
        readonly Grid root = new Grid { Width = 126, Height = 52 };
        readonly System.Collections.Generic.Dictionary<int, double> clockWidths = new System.Collections.Generic.Dictionary<int, double>();
        readonly TextBlock label = new TextBlock { FontSize = 23, FontFamily = new FontFamily("Bahnschrift"), Foreground = Art.Ink("#7BE5DA"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(52, 0, 18, 0) };
        readonly Hourglass glass = new Hourglass { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0) };
        readonly Border gauge = new Border { Width = 3, Height = 0, VerticalAlignment = VerticalAlignment.Bottom, Background = Art.Ink("#7BE5DA") };
        readonly AssemblyEffect effect;
        readonly System.Diagnostics.Stopwatch animation = System.Diagnostics.Stopwatch.StartNew();
        readonly DispatcherTimer motion = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
        public ParticleTimer(Action open)
        {
            Title = "Orbit Timer"; ShowActivated = false; Shell.StyleWindow(this);
            var surface = new Border { Background = Art.Ink("#102835"), BorderBrush = Art.Ink("#36545F"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9) }; root.Children.Add(surface);
            var face = new Grid { Margin = new Thickness(1) }; face.Children.Add(label); face.Children.Add(glass);
            var track = new Grid { Width = 3, Height = 30, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Background = Art.Ink("#294753") }; track.Children.Add(gauge); face.Children.Add(track);
            var source = new Grid { Opacity = 0 }; source.Children.Add(face); root.Children.Add(source);
            effect = new AssemblyEffect(face, surface) { IsHitTestVisible = false }; root.Children.Add(effect);
            Content = root; root.Cursor = Cursors.Hand; root.ToolTip = new ToolTip { Content = "クリックして前の画面を開く", Background = Art.Ink("#102835"), Foreground = Art.Ink("#DBEDF0"), BorderBrush = Art.Ink("#528793"), Padding = new Thickness(9, 6, 9, 6) }; root.MouseLeftButtonUp += delegate { open(); };
            motion.Tick += delegate { glass.Phase = animation.Elapsed.TotalSeconds; glass.InvalidateVisual(); effect.Refresh(); };
            IsVisibleChanged += delegate
            {
                if (IsVisible) Dispatcher.BeginInvoke(new Action(delegate { if (IsVisible) { UpdateLayout(); effect.Start(false); motion.Start(); } }), DispatcherPriority.Loaded);
                else { motion.Stop(); effect.Stop(); }
            };
            Closed += delegate { motion.Stop(); effect.Stop(); };
        }
        public double PreferredWidth { get { return root.Width; } }
        public void Update(string remaining, double progress)
        {
            label.Text = remaining;
            double width;
            if (!clockWidths.TryGetValue(remaining.Length, out width))
            {
                var measure = new TextBlock { Text = remaining.Length > 5 ? "000:00" : "00:00", FontFamily = label.FontFamily, FontSize = label.FontSize, Margin = label.Margin };
                measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                clockWidths[remaining.Length] = width = Math.Max(124, Math.Ceiling(measure.DesiredSize.Width + 2));
            }
            root.Width = width;
            glass.Progress = Math.Max(0, Math.Min(1, progress)); glass.InvalidateVisual(); gauge.Height = glass.Progress * 30;
            if (IsVisible) effect.Refresh();
        }
    }
    internal partial class CalendarPanel
    {
        double lastTimerProgress = -1;
        bool lastTimerGauge;
        public void UpdateTimerProgress(double progress, bool running)
        {
            if (!running && !lastTimerGauge && lastTimerProgress >= 0) return;
            bool appearing = running && !lastTimerGauge;
            var track = Get<Grid>("TimerProgressTrack");
            Rect bounds = track.Visibility == Visibility.Visible ? PanelBounds(track) : Rect.Empty;
            lastTimerGauge = running; lastTimerProgress = progress;
            track.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            Get<Border>("TimerProgress").Width = 104 * Math.Max(0, Math.Min(1, progress));
            if (mode != 0 || !IsVisible || IsClosing) return;
            if (running) bounds = PanelBounds(track);
            if (appearing) effect.StartArea(bounds, true);
            else if (!bounds.IsEmpty) effect.RefreshArea(bounds);

        }
    }
}
