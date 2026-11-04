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
        static void Block(DrawingContext dc, double x, double y, double width, double height, double light)
        {
            for (double py = y; py < y + height; py += 2.2)
                for (double px = x; px < x + width; px += 2.2) Dot(dc, px, py, light);
        }
        static void Cube(DrawingContext dc, double phase)
        {
            double yaw = phase * Math.PI / 2, pitch = -.48;
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            for (int face = 0; face < 6; face++)
            {
                int axis = face / 2; double side = face % 2 == 0 ? -1 : 1;
                double nx = axis == 0 ? side : 0, ny = axis == 1 ? side : 0, nz = axis == 2 ? side : 0;
                double normalDepth = ny * sp + (-nx * sy + nz * cy) * cp;
                if (normalDepth <= 0) continue;
                for (int a = 0; a <= 10; a++) for (int b = 0; b <= 10; b++)
                {
                    double u = -22 + a * 4.4, v = -22 + b * 4.4;
                    double x = axis == 0 ? side * 22 : u;
                    double y = axis == 1 ? side * 22 : axis == 0 ? u : v;
                    double z = axis == 2 ? side * 22 : v;
                    double rotatedX = x * cy + z * sy, rotatedZ = -x * sy + z * cy;
                    double rotatedY = y * cp - rotatedZ * sp, depth = y * sp + rotatedZ * cp;
                    double perspective = 150 / (150 - depth);
                    bool corner = (a == 0 || a == 10) && (b == 0 || b == 10);
                    Dot(dc, 102 + rotatedX * perspective, 56 + rotatedY * perspective,
                        .3 + normalDepth * .6, .7 + (depth + 35) * .002, corner);
                }
            }
        }
        static readonly int[] tilePath = { 8, 7, 4, 3, 0, 1, 2, 5, 4, 7, 6, 3, 4, 5, 8 };
        static int TileGap(int step) { return tilePath[step <= 14 ? step : 28 - step]; }
        static void Tiles(DrawingContext dc, double phase)
        {
            // Reverse the sliding sequence to return to the initial board without a jump.
            double clock = Wrap(phase / 14) * 28;
            int step = (int)clock;
            double t = Math.Min(1, (clock - step) / .82); t = t * t * (3 - 2 * t);
            int[] board = { 0, 1, 2, 3, 4, 5, 6, 7, -1 };
            for (int move = 0; move < step; move++)
            {
                int from = TileGap(move + 1), to = TileGap(move);
                board[to] = board[from]; board[from] = -1;
            }
            int moving = TileGap(step + 1), gap = TileGap(step);
            for (int cell = 0; cell < board.Length; cell++)
            {
                if (board[cell] < 0) continue;
                double col = cell % 3, row = cell / 3;
                if (cell == moving) { col += (gap % 3 - col) * t; row += (gap / 3 - row) * t; }
                Block(dc, 68 + col * 24, 22 + row * 24, 20, 20, cell == moving ? .98 : .48 + board[cell] % 4 * .1);
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
                    Cube(dc, phase);
                    break;
                case 4:
                    Tiles(dc, phase);
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
        bool mapLoading;
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
                dots.Phase = loadingClock.Elapsed.TotalSeconds * (mapLoading ? 2 : 1); dots.InvalidateVisual();
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
            mapLoading = false;
            var area = Get<Grid>("LoadingArea"); area.Width = area.Height = double.NaN; area.HorizontalAlignment = HorizontalAlignment.Stretch; area.VerticalAlignment = VerticalAlignment.Stretch; area.Background = Art.Ink("#0B1923");
            dataLoadingOwner = owner;
            mainLoadingParticles.Restart(); loadingClock.Restart();
            var host = Get<Grid>("LoadingArea");
            var toolbar = Get<Grid>("DataChoices");
            var choices = PanelBounds(toolbar.Visibility == Visibility.Visible ? toolbar : Get<Grid>("DataStampRow"));
            double rowTop = Get<Grid>("Face").RowDefinitions[0].ActualHeight;
            host.Margin = new Thickness(14, Math.Max(0, choices.Bottom - rowTop + 2), 14, 14);
            host.Visibility = Visibility.Visible;
        }
        void BeginMapLoading(CancellationTokenSource owner)
        {
            BeginDataLoading(owner); mapLoading = true;
            var host = Get<Grid>("LoadingArea"); var plot = PanelBounds(Get<Canvas>("Plot"));
            double rowTop = Get<Grid>("Face").RowDefinitions[0].ActualHeight;
            host.Width = plot.Width; host.Height = plot.Height; host.HorizontalAlignment = HorizontalAlignment.Left; host.VerticalAlignment = VerticalAlignment.Top;
            host.Margin = new Thickness(plot.Left, plot.Top - rowTop, 0, 0); host.Background = Art.Ink("#660B1923");
        }
        void EndDataLoading(CancellationTokenSource owner)
        {
            if (dataLoadingOwner != owner) return;
            ClearDataLoading();
        }
        void ClearDataLoading()
        {
            mapLoading = false;
            dataLoadingOwner = null; Get<Grid>("LoadingArea").Visibility = Visibility.Collapsed;
        }
    }
}
