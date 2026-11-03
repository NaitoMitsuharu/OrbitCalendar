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
        static readonly Brush[] shades = MakeShades();
        static readonly Brush accent = Art.Ink("#F4DD8E");
        public double Phase;
        public ParticleLoading()
        {
            Width = 204; Height = 116; HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
            IsHitTestVisible = false; AutomationProperties.SetName(this, "読み込み中");
        }
        public void Restart() { Phase = 0; InvalidateVisual(); }
        static Brush[] MakeShades()
        {
            var brushes = new Brush[24];
            for (int i = 0; i < brushes.Length; i++)
            {
                double light = .28 + i * .72 / (brushes.Length - 1);
                var brush = new SolidColorBrush(Color.FromRgb((byte)(123 * light), (byte)(229 * light), (byte)(218 * light)));
                brush.Freeze(); brushes[i] = brush;
            }
            return brushes;
        }
        static double Wrap(double value) { return value - Math.Floor(value); }
        static double Pulse(double value) { return .5 + .5 * Math.Sin(value); }
        static void Dot(DrawingContext dc, double x, double y, double light, double radius = .8, bool gold = false)
        {
            var ink = gold ? accent : shades[(int)(Math.Max(0, Math.Min(1, light)) * (shades.Length - 1))];
            if (Preferences.Pattern == 3) radius *= 1 + .25 * light;
            if (Preferences.Pattern == 1) dc.DrawRectangle(ink, null, new Rect(x - radius, y - radius, radius * 1.8, radius * 1.8));
            else if (Preferences.Pattern == 2) dc.DrawRectangle(ink, null, new Rect(x - radius, y - .35, radius * 2.5, .7));
            else dc.DrawEllipse(ink, null, new Point(x, y), radius, radius);
        }
        static void Segment(DrawingContext dc, double x, double y, double a, double b, double light)
        {
            int n = Math.Max(1, (int)Math.Ceiling(new Vector(a - x, b - y).Length / 2.2));
            for (int i = 0; i <= n; i++) Dot(dc, x + (a - x) * i / n, y + (b - y) * i / n, light);
        }
        static void Block(DrawingContext dc, double x, double y, double width, double height, double light)
        {
            for (double py = y; py < y + height; py += 2.2)
                for (double px = x; px < x + width; px += 2.2) Dot(dc, px, py, light);
        }
        static void Corners(DrawingContext dc)
        {
            for (int side = -1; side <= 1; side += 2)
                for (int vertical = -1; vertical <= 1; vertical += 2)
                {
                    double x = 102 + side * 62, y = 55 + vertical * 30;
                    Segment(dc, x, y, x - side * 10, y, .42); Segment(dc, x, y, x, y - vertical * 8, .42);
                }
        }
        protected override void OnRender(DrawingContext dc)
        {
            double phase = SystemParameters.ClientAreaAnimation ? Phase : .65;
            switch (Preferences.LoadingStyle)
            {
                case 1: // A moving bright head and particle tail identify an indeterminate spinner.
                    for (int i = 0; i < 90; i++)
                    {
                        double angle = i * Math.PI * 2 / 90 - Math.PI / 2;
                        double light = Math.Pow(1 - Wrap(i / 90.0 - phase / 1.5), 2);
                        for (int band = 0; band < 3; band++) Dot(dc, 102 + Math.Cos(angle) * (27 + band * 2.2), 55 + Math.Sin(angle) * (27 + band * 2.2), light);
                    }
                    break;
                case 2:
                    for (int orbit = 0; orbit < 2; orbit++)
                    {
                        double tilt = orbit == 0 ? -.55 : .55;
                        for (int i = 0; i < 105; i++)
                        {
                            double angle = i * Math.PI * 2 / 105;
                            double x = Math.Cos(angle) * 47, y = Math.Sin(angle) * 18;
                            double light = Math.Pow(1 - Wrap(i / 105.0 - phase * (orbit == 0 ? .55 : -.55)), 2);
                            Dot(dc, 102 + x * Math.Cos(tilt) - y * Math.Sin(tilt), 55 + x * Math.Sin(tilt) + y * Math.Cos(tilt), light);
                        }
                    }
                    Block(dc, 99, 52, 6, 6, .8);
                    break;
                case 3:
                    Corners(dc);
                    for (int i = 0; i < 12; i++)
                    {
                        double angle = i * Math.PI / 6 - Math.PI / 2;
                        double light = 1 - Wrap(i / 12.0 - phase * .6);
                        for (int length = 0; length < 5; length++) for (int width = -1; width <= 1; width++)
                            Dot(dc, 102 + Math.Cos(angle) * (20 + length * 2.2) - Math.Sin(angle) * width * 2.2,
                                53 + Math.Sin(angle) * (20 + length * 2.2) + Math.Cos(angle) * width * 2.2, light);
                    }
                    for (int i = 0; i < 9; i++) Block(dc, 62 + i * 9, 94, 5, 4, 1 - Wrap(i / 9.0 - phase * .7));
                    break;
                case 4:
                    Corners(dc);
                    double scan = 102 + Math.Sin(phase * 2.5) * 49;
                    for (int row = 0; row < 5; row++) for (int col = 0; col < 11; col++)
                    {
                        double x = 52 + col * 10, light = Math.Exp(-Math.Abs(x - scan) / 17);
                        Block(dc, x, 32 + row * 10, 4, 4, light);
                    }
                    Segment(dc, scan, 30, scan, 80, 1);
                    break;
                case 5:
                    for (int i = 0; i < 6; i++)
                    {
                        double wave = Pulse(phase * 4 - i * .65), centerX = 57 + i * 18, centerY = 56 - wave * 12;
                        for (int y = -5; y <= 5; y += 2) for (int x = -5; x <= 5; x += 2)
                            if (x * x + y * y < 29) Dot(dc, centerX + x, centerY + y, .35 + .65 * wave);
                    }
                    break;
                default:
                    for (int i = 0; i < 6; i++)
                    {
                        double wave = Pulse(phase * 4.5 - i * .65), height = 14 + 36 * wave;
                        Block(dc, 62 + i * 15, 55 - height / 2, 5, height, .35 + .65 * wave);
                    }
                    break;
            }
        }
    }
    internal partial class CalendarPanel
    {
        readonly DispatcherTimer loadingAnimation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        readonly System.Diagnostics.Stopwatch loadingClock = System.Diagnostics.Stopwatch.StartNew();
        readonly System.Diagnostics.Stopwatch previewLoadingClock = new System.Diagnostics.Stopwatch();
        ParticleLoading mainLoadingParticles, newsLoadingParticles, settingsLoadingParticles;
        CancellationTokenSource dataLoadingOwner;
        void InitializeLoading()
        {
            mainLoadingParticles = new ParticleLoading(); Get<Grid>("LoadingArea").Children.Add(mainLoadingParticles);
            settingsLoadingParticles = new ParticleLoading(); Get<Grid>("LoadingPreview").Children.Add(settingsLoadingParticles);
            loadingAnimation.Tick += delegate
            {
                if (!IsVisible || IsClosing || !SystemParameters.ClientAreaAnimation) return;
                if (Get<Border>("SettingsPicker").Visibility == Visibility.Visible)
                {
                    if (showLoadingSettings)
                    {
                        settingsLoadingParticles.Phase = previewLoadingClock.Elapsed.TotalSeconds; settingsLoadingParticles.InvalidateVisual();
                        effect.RefreshArea(PanelBounds(Get<Border>("LoadingPreviewArea")));
                    }
                    return;
                }
                if (mode < 1) return;
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
            mainLoadingParticles.Restart(); loadingClock.Restart();
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
