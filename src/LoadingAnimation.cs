using System;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitCalendar
{
    internal sealed class ParticleLoading : FrameworkElement
    {
        struct Seed { public Point Target, Origin; public double Delay, Size; public int Ink; }
        Seed[] seeds;
        static readonly Brush[] inks = { Art.Ink("#7BE5DA"), Art.Ink("#81BFCE"), Art.Ink("#C9EDEA"), Art.Ink("#E6CD87") };
        public double Phase;
        public ParticleLoading(int mode = 2)
        {
            Width = 204; Height = 116; HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
            IsHitTestVisible = false; AutomationProperties.SetName(this, "粒子で読み込み中"); SetMode(mode);
        }
        public void SetMode(int mode)
        {
            var targets = new System.Collections.Generic.List<Point>();
            Action<double, double, double, double> line = (x, y, a, b) =>
            {
                int n = Math.Max(1, (int)Math.Ceiling(new Vector(a - x, b - y).Length / 1.8));
                for (int i = 0; i <= n; i++) targets.Add(new Point(x + (a - x) * i / n, y + (b - y) * i / n));
            };
            Action<double, double, double, double, double, double> arc = (x, y, rx, ry, from, to) =>
            {
                int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(to - from) * Math.Max(rx, ry) / 1.8));
                for (int i = 0; i <= n; i++) { double t = from + (to - from) * i / n; targets.Add(new Point(x + rx * Math.Cos(t), y + ry * Math.Sin(t))); }
            };
            if (mode == 1)
            {
                line(46, 82, 157, 82); line(46, 30, 46, 82);
                double[] top = { 66, 49, 57, 39, 26 };
                for (int i = 0; i < top.Length; i++) { double x = 61 + i * 19; line(x, top[i], x, 76); line(x + 5, top[i] + 4, x + 5, 76); line(x, top[i], x + 5, top[i] + 4); }
                line(58, 58, 81, 39); line(81, 39, 99, 46); line(99, 46, 122, 28); line(122, 28, 149, 20);
            }
            else if (mode == 3)
            {
                line(57, 27, 147, 27); line(147, 27, 147, 88); line(147, 88, 57, 88); line(57, 88, 57, 27);
                line(67, 38, 137, 38); line(67, 43, 137, 43);
                line(67, 53, 91, 53); line(91, 53, 91, 72); line(91, 72, 67, 72); line(67, 72, 67, 53);
                for (int y = 54; y <= 77; y += 8) { line(102, y, 137, y); }
                line(67, 79, 91, 79);
            }
            else if (mode == 4)
            {
                line(78, 25, 126, 25); line(126, 25, 134, 35); line(134, 35, 134, 76); line(134, 76, 70, 76); line(70, 76, 70, 35); line(70, 35, 78, 25);
                line(79, 37, 125, 37); line(125, 37, 125, 55); line(125, 55, 79, 55); line(79, 55, 79, 37); line(102, 37, 102, 55);
                arc(81, 65, 3, 3, 0, Math.PI * 2); arc(123, 65, 3, 3, 0, Math.PI * 2);
                line(83, 79, 70, 96); line(121, 79, 134, 96); line(78, 87, 126, 87); line(74, 93, 130, 93);
            }
            else
            {
                arc(72, 55, 17, 17, Math.PI * .5, Math.PI * 1.55);
                arc(99, 43, 26, 24, Math.PI, Math.PI * 1.96);
                arc(130, 57, 20, 17, -Math.PI * .6, Math.PI * .5); line(72, 74, 130, 74);
                for (int i = 0; i < 4; i++) line(77 + i * 15, 83, 73 + i * 15, 94);
            }
            var random = new Random(741 + mode); seeds = new Seed[targets.Count];
            for (int i = 0; i < seeds.Length; i++) seeds[i] = new Seed { Target = targets[i], Origin = new Point(14 + random.NextDouble() * 176, 10 + random.NextDouble() * 96),
                Delay = random.NextDouble() * .12, Size = .6 + random.NextDouble() * .42, Ink = i % 23 == 0 ? 3 : i % 3 };
            Phase = 0; InvalidateVisual();
        }
        static double Ease(double value) { value = Math.Max(0, Math.Min(1, value)); return value * value * (3 - 2 * value); }
        protected override void OnRender(DrawingContext dc)
        {
            for (int i = 0; i < seeds.Length; i++)
            {
                var seed = seeds[i]; double t = (Phase / 3.8 + seed.Delay) % 1;
                double gather = t < .48 ? Ease(t / .48) : t < .68 ? 1 : 1 - Ease((t - .68) / .32);
                if (!SystemParameters.ClientAreaAnimation) gather = 1;
                double x = seed.Origin.X + Math.Sin(Phase * 1.1 + i * .73) * 8;
                double y = seed.Origin.Y + Math.Cos(Phase * .9 + i * 1.13) * 6;
                var point = new Point(x + (seed.Target.X - x) * gather, y + (seed.Target.Y - y) * gather);
                double r = Preferences.Pattern == 3 && i % 7 == 0 ? seed.Size * 1.3 : seed.Size;
                if (Preferences.Pattern == 1) dc.DrawRectangle(inks[seed.Ink], null, new Rect(point.X - r, point.Y - r, r * 1.7, r * 1.7));
                else if (Preferences.Pattern == 2) dc.DrawRectangle(inks[seed.Ink], null, new Rect(point.X - r, point.Y, r * 2.5, .7));
                else dc.DrawEllipse(inks[seed.Ink], null, point, r, r);
            }
        }
    }
    internal partial class CalendarPanel
    {
        readonly DispatcherTimer loadingAnimation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        readonly System.Diagnostics.Stopwatch loadingClock = System.Diagnostics.Stopwatch.StartNew();
        ParticleLoading mainLoadingParticles, newsLoadingParticles;
        CancellationTokenSource dataLoadingOwner;
        void InitializeLoading()
        {
            mainLoadingParticles = new ParticleLoading(); Get<Grid>("LoadingArea").Children.Add(mainLoadingParticles);
            loadingAnimation.Tick += delegate
            {
                if (!IsVisible || IsClosing || mode < 1) return;
                var dots = dataLoadingOwner != null ? mainLoadingParticles : mode == 3 && newsPreview != null ? newsLoadingParticles : null;
                if (dots == null || !dots.IsVisible || !SystemParameters.ClientAreaAnimation) return;
                dots.Phase = loadingClock.Elapsed.TotalSeconds; dots.InvalidateVisual();
                effect.RefreshArea(PanelBounds(dots));
            };
            IsVisibleChanged += delegate { if (IsVisible) loadingAnimation.Start(); else loadingAnimation.Stop(); };
            Closed += delegate { loadingAnimation.Stop(); };
            for (int i = 0; i < 3; i++)
            {
                int category = i;
                Get<Button>("NewsCategory" + i).Click += delegate { if (mode == 3) { choice[3] = category; infoPage = 0; LoadView(false); } };
            }
        }
        void BeginDataLoading(CancellationTokenSource owner)
        {
            dataLoadingOwner = owner;
            mainLoadingParticles.SetMode(mode); loadingClock.Restart();
            var host = Get<Grid>("LoadingArea");
            var toolbar = Get<Grid>("DataChoices");
            var choices = PanelBounds(toolbar.Visibility == Visibility.Visible ? toolbar : Get<Grid>("DataStampRow"));
            double rowTop = Get<Grid>("Face").RowDefinitions[0].ActualHeight;
            host.Margin = new Thickness(14, Math.Max(0, choices.Bottom - rowTop + 2), 14, 14);
            host.Visibility = Visibility.Visible;
        }
        void EndDataLoading(CancellationTokenSource owner)
        {
            if (dataLoadingOwner != owner) return;
            ClearDataLoading();
        }
        void ClearDataLoading()
        {
            dataLoadingOwner = null; Get<Grid>("LoadingArea").Visibility = Visibility.Collapsed;
        }
    }
}
