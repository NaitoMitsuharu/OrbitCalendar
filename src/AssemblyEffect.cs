using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitCalendar
{
    internal sealed class AssemblyEffect : FrameworkElement
    {
        struct Grain
        {
            public Point Target, Origin;
            public double Latitude, Longitude, Radius, Speed, Lag;
            public Color Ink;
        }

        readonly UIElement face;
        readonly Stopwatch watch = new Stopwatch();
        readonly Random random = new Random();
        readonly RadialGradientBrush shadow;
        readonly Dictionary<Color, Brush> inks = new Dictionary<Color, Brush>();
        Grain[] grains;
        WriteableBitmap layer;
        int[] raster;
        Point pointer;
        double seconds, pointerTime;
        bool assembling, morph, rendering;
        const double Gather = .85, Rest = 1.49, Finish = 1.85;

        public AssemblyEffect(UIElement face)
        {
            this.face = face;
            shadow = new RadialGradientBrush();
            shadow.GradientStops.Add(new GradientStop(Color.FromRgb(10, 24, 33), 0));
            shadow.GradientStops.Add(new GradientStop(Color.FromArgb(228, 10, 24, 33), .55));
            shadow.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            shadow.Freeze();
        }

        Grain[] Sample()
        {
            int w = (int)face.RenderSize.Width, h = (int)face.RenderSize.Height;
            if (w <= 0 || h <= 0) return new Grain[0];
            var frame = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            frame.Render(face);
            var pixels = new byte[w * h * 4];
            frame.CopyPixels(pixels, w * 4, 0);
            var dots = new List<Grain>();
            for (int y = 1; y < h; y += 2)
                for (int x = 1; x < w; x += 2)
                {
                    int p = (y * w + x) * 4;
                    int b = pixels[p], g = pixels[p + 1], r = pixels[p + 2];
                    if (Math.Max(r, Math.Max(g, b)) < 72) continue;
                    dots.Add(new Grain { Target = new Point(x, y), Ink = Color.FromRgb((byte)r, (byte)g, (byte)b) });
                }
            return dots.ToArray();
        }

        public void Start(bool fromPrevious)
        {
            var old = grains;
            Stop();
            if (!SystemParameters.ClientAreaAnimation) return;
            grains = Sample();
            if (grains.Length == 0) return;
            morph = fromPrevious && old != null && old.Length > 0;
            for (int i = 0; i < grains.Length; i++)
            {
                // Roughly equal area per speck. Latitude bands on the inner shell
                // and a few eccentric outer orbits make the rotation legible.
                double lat = Math.Asin(2 * random.NextDouble() - 1);
                grains[i].Latitude = i % 4 == 0 ? lat : Math.Round(lat * 16) / 16;
                grains[i].Longitude = random.NextDouble() * 2 * Math.PI;
                grains[i].Radius = i % 7 == 0 ? 102 + random.NextDouble() * 51 : 82 + random.NextDouble() * 3;
                grains[i].Speed = i % 7 == 0 ? -2 : 2.5;
                grains[i].Lag = random.NextDouble() * .24;
                if (morph) grains[i].Origin = old[(int)((long)i * old.Length / grains.Length)].Target;
            }
            int w = (int)face.RenderSize.Width, h = (int)face.RenderSize.Height;
            if (layer == null || layer.PixelWidth != w || layer.PixelHeight != h)
            {
                layer = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                raster = new int[w * h];
            }
            assembling = true;
            face.Opacity = 0;
            seconds = 0;
            watch.Restart();
            PaintFlight();
            Listen();
        }

        static double Ease(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t * t * (3 - 2 * t);
        }

        // Bilinear splats keep subpixel flight smooth and avoid thousands of WPF
        // drawing objects per frame. Accumulate premultiplied light at intersections.
        void Splat(int i, int red, int green, int blue, double weight)
        {
            int src = (int)((.15 + .85 * weight) * 255);
            if (src <= 0) return;
            int prev = raster[i];
            int a = Math.Min(255, (int)((uint)prev >> 24) + src);
            int r = Math.Min(255, ((prev >> 16) & 255) + red * src / 255);
            int g = Math.Min(255, ((prev >> 8) & 255) + green * src / 255);
            int b = Math.Min(255, (prev & 255) + blue * src / 255);
            raster[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }

        void PaintFlight()
        {
            Array.Clear(raster, 0, raster.Length);
            int w = layer.PixelWidth, h = layer.PixelHeight;
            foreach (var g in grains)
            {
                double angle = g.Longitude + seconds * g.Speed;
                double longitudeRadius = Math.Cos(g.Latitude) * g.Radius;
                double x = Math.Sin(angle) * longitudeRadius, y = Math.Sin(g.Latitude) * g.Radius;
                double depth = Math.Cos(angle) * longitudeRadius / g.Radius;
                double t = morph ? Ease((seconds - g.Lag * .30) / .46) : Ease((seconds - .31 - g.Lag) / Gather);
                double ox = morph ? g.Origin.X : .5 * w + .94 * x + .34 * y;
                double oy = morph ? g.Origin.Y : .50 * h + .94 * y - .34 * x;
                double curl = Math.Sin(t * Math.PI) * (morph ? 11 : 39);
                double px = ox + (g.Target.X - ox) * t + Math.Sin(angle) * curl;
                double py = oy + (g.Target.Y - oy) * t + Math.Cos(angle) * curl;
                if (px < 0 || py < 0 || px >= w - 1 || py >= h - 1) continue;
                double light = .63 + .32 * depth;
                int red = (int)(29 * light * (1 - t) + g.Ink.R * t);
                int green = (int)(235 * light * (1 - t) + g.Ink.G * t);
                int blue = (int)(248 * light * (1 - t) + g.Ink.B * t);
                int ix = (int)px, iy = (int)py, i = iy * w + ix;
                double fx = px - ix, fy = py - iy;
                Splat(i, red, green, blue, (1 - fx) * (1 - fy));
                Splat(i + 1, red, green, blue, fx * (1 - fy));
                Splat(i + w, red, green, blue, (1 - fx) * fy);
                Splat(i + w + 1, red, green, blue, fx * fy);
            }
            layer.WritePixels(new Int32Rect(0, 0, w, h), raster, w * 4, 0);
            InvalidateVisual();
        }

        public void Remember()
        {
            if (grains == null && SystemParameters.ClientAreaAnimation) grains = Sample();
        }

        public void Resample() { Stop(); grains = null; }

        public void Nudge(Point p)
        {
            if (assembling || !SystemParameters.ClientAreaAnimation) return;
            if (grains == null) grains = Sample();
            pointer = p;
            pointerTime = 1;
            watch.Restart();
            Listen();
            InvalidateVisual();
        }

        void Listen()
        {
            if (!rendering) { CompositionTarget.Rendering += Frame; rendering = true; }
        }

        void Frame(object sender, EventArgs e)
        {
            seconds = watch.Elapsed.TotalSeconds;
            if (assembling)
            {
                double rest = morph ? .55 : Rest, finish = morph ? .76 : Finish;
                if (seconds >= finish) { Stop(); return; }
                face.Opacity = Ease((seconds - rest) / (finish - rest));
                Opacity = 1 - face.Opacity;
                PaintFlight();
            }
            else
            {
                pointerTime = 1 - Ease(seconds / .55);
                if (pointerTime <= 0) Stop();
                else InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (assembling) { dc.DrawImage(layer, new Rect(0, 0, layer.PixelWidth, layer.PixelHeight)); return; }
            if (pointerTime <= 0 || grains == null) return;
            const double radius = 32;
            dc.PushOpacity(pointerTime);
            dc.DrawEllipse(shadow, null, pointer, radius, radius);
            foreach (var g in grains)
            {
                var delta = g.Target - pointer;
                double d2 = delta.LengthSquared;
                if (d2 > radius * radius || d2 < .01) continue;
                double distance = Math.Sqrt(d2);
                double push = (1 - distance / radius) * 13 * pointerTime;
                var pos = g.Target + delta * (push / distance);
                Brush ink;
                if (!inks.TryGetValue(g.Ink, out ink))
                {
                    ink = new SolidColorBrush(g.Ink);
                    ink.Freeze();
                    inks.Add(g.Ink, ink);
                }
                dc.DrawEllipse(ink, null, pos, .8, .8);
            }
            dc.Pop();
        }

        public void Stop()
        {
            CompositionTarget.Rendering -= Frame;
            rendering = assembling = false;
            pointerTime = 0;
            watch.Stop();
            face.Opacity = Opacity = 1;
            InvalidateVisual();
        }
    }
}
