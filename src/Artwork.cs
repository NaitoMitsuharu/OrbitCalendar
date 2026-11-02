using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace OrbitCalendar
{
    internal static class Art
    {
        public static SolidColorBrush Ink(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }

        // Orthographic projection of a tilted globe, with back-facing lines culled.
        static Point Project(double lat, double lon, out bool visible)
        {
            double x = Math.Cos(lat) * Math.Sin(lon);
            double y = Math.Sin(lat);
            double z = Math.Cos(lat) * Math.Cos(lon);
            visible = .88 * z + .47 * y >= 0;
            double north = .88 * y - .47 * z;
            return new Point(293 + 77 * (.93 * x + .37 * north), 57 + 77 * (.37 * x - .93 * north));
        }

        public static void DrawSky(Canvas canvas)
        {
            var mesh = new StreamGeometry();
            using (var pen = mesh.Open())
            {
                for (int curve = 0; curve < 28; curve++)
                {
                    bool drawing = false;
                    for (int i = 0; i <= 120; i++)
                    {
                        bool latitude = curve < 11;
                        double lat = latitude ? (curve - 5) * Math.PI / 12 : (i / 120.0 - .5) * Math.PI;
                        double lon = latitude ? i * Math.PI / 60 : (curve - 11) * Math.PI * 2 / 17;
                        bool visible;
                        var p = Project(lat, lon, out visible);
                        if (visible && drawing) pen.LineTo(p, true, false);
                        else if (visible) pen.BeginFigure(p, false, false);
                        drawing = visible;
                    }
                }
            }
            mesh.Freeze();
            canvas.Children.Add(new Path { Data = mesh, Stroke = Ink("#51674B"), StrokeThickness = .75 });
            var arc = new Path { Data = Geometry.Parse("M 143,111 C 174,54 334,-7 419,6 C 466,17 354,76 249,103"), Stroke = Ink("#9CAD76"), StrokeThickness = 1 };
            canvas.Children.Add(arc);
            var rng = new Random(71);
            for (int i = 0; i < 65; i++)
            {
                double size = i % 6 == 0 ? 2.2 : 1;
                var dot = new Ellipse { Width = size, Height = size, Fill = Ink(i % 6 == 0 ? "#C9D6A5" : "#778678") };
                Canvas.SetLeft(dot, 132 + rng.NextDouble() * 324);
                Canvas.SetTop(dot, rng.NextDouble() * 101);
                canvas.Children.Add(dot);
            }
        }
    }

    internal sealed class AssemblyEffect : FrameworkElement
    {
        struct Grain { public double X, Y, Dx, Dy, Lag, Radius; public int Hue; }
        Grain[] grains;
        readonly Brush[] palette = { Art.Ink("#D4F399"), Art.Ink("#AEBDA0"), Art.Ink("#EDF2DF"), Art.Ink("#759B7B") };
        readonly Stopwatch watch = new Stopwatch();
        readonly UIElement face;
        readonly GradientStop edge = new GradientStop(Colors.White, 0);
        readonly GradientStop trail = new GradientStop(Colors.Transparent, .01);
        readonly LinearGradientBrush reveal;
        double phase;

        public AssemblyEffect(UIElement face)
        {
            this.face = face;
            reveal = new LinearGradientBrush();
            reveal.StartPoint = new Point(0, 0);
            reveal.EndPoint = new Point(1, .29);
            reveal.GradientStops.Add(edge);
            reveal.GradientStops.Add(trail);
        }

        public void Start()
        {
            Stop();
            if (!SystemParameters.ClientAreaAnimation) return;
            int w = (int)face.RenderSize.Width, h = (int)face.RenderSize.Height;
            if (w <= 0 || h <= 0) return;
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(face);
            var pixels = new byte[w * h * 4];
            bitmap.CopyPixels(pixels, w * 4, 0);
            var dots = new System.Collections.Generic.List<Grain>();
            var random = new Random(61);
            for (int y = 2; y < h; y += 3)
                for (int x = 2; x < w; x += 3)
                {
                    int p = (y * w + x) * 4;
                    if (pixels[p + 1] < 90) continue;
                    dots.Add(new Grain { X = x, Y = y, Dx = 30 + random.NextDouble() * 142,
                        Dy = (random.NextDouble() - .5) * 130, Radius = .7 + random.NextDouble() * .9,
                        Lag = .40 * (x / (double)w) + random.NextDouble() * .12, Hue = random.Next(4) });
                }
            grains = dots.ToArray();
            face.Opacity = 0;
            face.OpacityMask = reveal;
            edge.Offset = trail.Offset = 0;
            phase = 0;
            watch.Restart();
            CompositionTarget.Rendering += Frame;
        }

        void Frame(object sender, EventArgs e)
        {
            phase = watch.Elapsed.TotalSeconds / .92;
            if (phase >= 1) { Stop(); return; }
            face.Opacity = Math.Min(1, phase * 5);
            edge.Offset = Math.Max(0, (phase - .30) / .62);
            trail.Offset = Math.Max(.001, (phase - .16) / .62);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (grains == null) return;
            foreach (var g in grains)
            {
                double t = Math.Max(0, Math.Min(1, (phase - g.Lag) / .48));
                double remaining = (1 - t) * (1 - t);
                double radius = g.Radius * Math.Min(1, (1 - t) * 5);
                if (radius < .1 || phase + .09 < g.Lag) continue;
                var pos = new Point(g.X + remaining * g.Dx, g.Y + remaining * g.Dy + Math.Sin(t * Math.PI) * 12);
                dc.DrawEllipse(palette[g.Hue], null, pos, radius, radius);
            }
        }

        public void Stop()
        {
            CompositionTarget.Rendering -= Frame;
            watch.Stop();
            face.OpacityMask = null;
            face.Opacity = 1;
            grains = null;
            InvalidateVisual();
        }
    }
}
