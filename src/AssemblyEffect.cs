using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitCalendar
{
    internal sealed class AssemblyEffect : FrameworkElement
    {
        struct Grain
        {
            public Point Target, Origin, Current;
            public bool Edge, Moving;
            public double Latitude, Longitude, Radius, Speed, Lag;
            public Color Ink;
        }

        readonly UIElement face;
        readonly Stopwatch watch = new Stopwatch();
        readonly Random random = new Random();
        Grain[] grains;
        WriteableBitmap layer;
        int[] raster;
        readonly UIElement surface;
        Action onDismissed;
        public bool Busy { get { return assembling || Closing; } }
        public bool Closing { get { return onDismissed != null; } }
        double seconds;
        bool assembling, morph, rendering, dirty;
        int motionStyle, sequence;
        const double Gather = 1.05;

        public AssemblyEffect(UIElement face, UIElement surface)
        {
            this.face = face;
            this.surface = surface;
        }

        Grain[] Sample() { return Sample(new Rect(face.RenderSize)); }

        Grain[] Sample(Rect area)
        {
            int w = (int)face.RenderSize.Width, h = (int)face.RenderSize.Height;
            if (w <= 0 || h <= 0) return new Grain[0];
            var frame = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            frame.Render(face);
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            var dots = new List<Grain>();
            for (int y = (int)area.Top; y < Math.Min(h, area.Bottom); y++)
                for (int x = (int)area.Left; x < Math.Min(w, area.Right); x++)
                {
                    int p = (y * w + x) * 4;
                    int b = pixels[p], g = pixels[p + 1], r = pixels[p + 2];
                    if (Math.Max(r, Math.Max(g, b)) < 61) continue;
                    // A bright core and dimmer interstitial grains preserve the fine
                    // strokes of kana and small numbers on a 1x display.
                    double light = (x % 2 == 0 && y % 2 == 0) ? 1.12 :
                        (x % 2 == 1 && y % 2 == 1) ? .42 : .90;
                    if (Preferences.Pattern == 1) light = x % 3 == 0 && y % 3 == 0 ? 1.15 : .42;
                    else if (Preferences.Pattern == 2) light = y % 3 == 0 ? 1.12 : .45;
                    else if (Preferences.Pattern == 3) light = ((x * 17 + y * 31) % 11 < 3) ? 1.3 : .66;
                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) { edge = true; break; }
                            int at = (ny * w + nx) * 4;
                            if (Math.Abs(r - pixels[at + 2]) + Math.Abs(g - pixels[at + 1]) + Math.Abs(b - pixels[at]) > 105)
                            { edge = true; break; }
                        }
                    dots.Add(new Grain { Target = new Point(x, y), Edge = edge, Ink = Color.FromRgb(
                        (byte)Math.Min(255, r * light), (byte)Math.Min(255, g * light), (byte)Math.Min(255, b * light)) });
                }
            return dots.ToArray();
        }

        public void Start(bool fromPrevious) { Start(fromPrevious, new Rect(face.RenderSize)); }

        public void StartArea(Rect area, bool restart = false) { Start(!restart, area); }

        void Start(bool fromPrevious, Rect area)
        {
            motionStyle = Preferences.Motion == 0 ? (++sequence % 5) + 1 : Preferences.Motion;
            dirty = false;
            var old = grains;
            if (old != null && area.Width < face.RenderSize.Width)
            {
                var local = new List<Grain>();
                foreach (var grain in old) if (area.Contains(grain.Target)) local.Add(grain);
                old = local.ToArray();
            }
            Stop();
            face.UpdateLayout();
            grains = Sample();
            if (grains.Length == 0) return;
            morph = fromPrevious && old != null && old.Length > 0;
            for (int i = 0; i < grains.Length; i++)
            {
                // Roughly equal area per speck. Latitude bands on the inner shell
                // and a few eccentric outer orbits make the rotation legible.
                grains[i].Moving = area.Contains(grains[i].Target);
                double lat = Math.Asin(2 * random.NextDouble() - 1);
                grains[i].Latitude = i % 4 == 0 ? lat : Math.Round(lat * 16) / 16;
                grains[i].Longitude = random.NextDouble() * 2 * Math.PI;
                grains[i].Radius = i % 7 == 0 ? 102 + random.NextDouble() * 51 : 82 + random.NextDouble() * 3;
                grains[i].Speed = i % 7 == 0 ? -2 : 2.5;
                grains[i].Lag = random.NextDouble() * .25;
                if (morph) grains[i].Origin = old[(int)((long)i * old.Length / grains.Length)].Current;
                else
                {
                    double angle = grains[i].Longitude;
                    grains[i].Origin = new Point(grains[i].Target.X * .28 + 420 * .72 + Math.Cos(angle) * 65,
                        grains[i].Target.Y * .28 + 535 * .72 + Math.Sin(angle) * 65);
                }
                if (area.Width < face.RenderSize.Width && !morph)
                    grains[i].Origin = new Point(area.Right - 8 - random.NextDouble() * Math.Min(65, area.Width),
                        area.Bottom - 5 - random.NextDouble() * Math.Min(25, area.Height));
            }
            int w = (int)face.RenderSize.Width, h = (int)face.RenderSize.Height;
            if (layer == null || layer.PixelWidth != w || layer.PixelHeight != h)
            {
                layer = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                raster = new int[w * h];
            }
            assembling = SystemParameters.ClientAreaAnimation;
            seconds = 0;
            watch.Restart();
            Paint();
            if (assembling) Listen();
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
            int src = (int)((.08 + .92 * weight) * 255);
            if (src <= 0) return;
            int prev = raster[i];
            int a = Math.Min(255, (int)((uint)prev >> 24) + src);
            int r = Math.Min(255, ((prev >> 16) & 255) + red * src / 255);
            int g = Math.Min(255, ((prev >> 8) & 255) + green * src / 255);
            int b = Math.Min(255, (prev & 255) + blue * src / 255);
            raster[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }

        void Paint()
        {
            Array.Clear(raster, 0, raster.Length);
            int w = layer.PixelWidth, h = layer.PixelHeight;
            for (int index = 0; index < grains.Length; index++)
            {
                var g = grains[index];
                double px = g.Target.X, py = g.Target.Y;
                int red = g.Ink.R, green = g.Ink.G, blue = g.Ink.B;
                if (assembling && g.Moving)
                {
                    if (motionStyle == 6)
                    {
                        double delay = g.Edge ? .025 + g.Lag * .55 : .26 + g.Lag * 1.1;
                        double duration = g.Edge ? .57 : .91;
                        double progress = Math.Max(0, Math.Min(1, (seconds - delay) / duration));
                        double attraction = 1 - Math.Pow(1 - progress, 3);
                        attraction += Math.Sin(progress * Math.PI * 5) * .035 * Math.Pow(1 - progress, 2);
                        var displacement = g.Target - g.Origin;
                        double bend = Math.Sin(Math.PI * progress) * (g.Edge ? 9 : 23);
                        px = g.Origin.X + displacement.X * attraction + Math.Cos(g.Longitude) * bend;
                        py = g.Origin.Y + displacement.Y * attraction + Math.Sin(g.Longitude) * bend;
                    }
                    else
                    {
                        double angle = g.Longitude + seconds * g.Speed;
                        double ring = Math.Cos(g.Latitude) * g.Radius;
                        double x = Math.Sin(angle) * ring, y = Math.Sin(g.Latitude) * g.Radius;
                        double t = Ease((seconds - .38 - g.Lag) / Gather);
                        double ox = .5 * w + .94 * x + .34 * y;
                        double oy = .50 * h + .94 * y - .34 * x;
                        if (motionStyle == 2)
                        {
                            double spiral = g.Longitude + seconds * (3 + g.Lag * 8);
                            double radius = 35 + Math.Abs(g.Latitude) * 175;
                            ox = w * .5 + Math.Cos(spiral) * radius;
                            oy = h * .5 + Math.Sin(spiral) * radius;
                        }
                        else if (motionStyle == 3)
                        {
                            ox = (g.Longitude / (2 * Math.PI)) * w;
                            oy = ((seconds * 280 + g.Latitude * h * 2) % (h + 180)) - 120;
                        }
                        else if (motionStyle == 4)
                        {
                            double radius = 50 + seconds * 240;
                            ox = w * .5 + Math.Cos(g.Longitude) * radius;
                            oy = h * .5 + Math.Sin(g.Longitude) * radius;
                        }
                        else if (motionStyle == 5)
                        {
                            ox = g.Longitude / (2 * Math.PI) * w;
                            oy = h * .5 + Math.Sin(ox / 47 + seconds * 5) * (55 + Math.Abs(g.Latitude) * 100);
                        }
                        if (morph)
                        {
                            double scatter = Ease(seconds / .35);
                            ox = g.Origin.X + (ox - g.Origin.X) * scatter;
                            oy = g.Origin.Y + (oy - g.Origin.Y) * scatter;
                        }
                        double curl = Math.Sin(t * Math.PI) * 24;
                        px = ox + (px - ox) * t + Math.Sin(angle) * curl;
                        py = oy + (py - oy) * t + Math.Cos(angle) * curl;
                        double light = .63 + .32 * Math.Cos(angle) * ring / g.Radius;
                        red = (int)(29 * light * (1 - t) + red * t);
                        green = (int)(235 * light * (1 - t) + green * t);
                        blue = (int)(248 * light * (1 - t) + blue * t);
                    }
                }
                else if (Closing)
                {
                    double t = Ease(seconds / .62);
                    double sweep = Math.Sin(Math.PI * t);
                    px += (w - 18 - px) * t + sweep * g.Speed * (1 - t);
                    py += (h - py) * t - sweep * g.Radius * (1 - t);
                }
                grains[index].Current = new Point(px, py);
                if (px < 0 || py < 0 || px >= w - 1 || py >= h - 1) continue;
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

        public void RefreshArea(Rect area)
        {
            if (layer == null || !IsVisible || Busy || grains == null) return;
            face.UpdateLayout();
            var updated = new List<Grain>();
            foreach (var grain in grains) if (!area.Contains(grain.Target)) updated.Add(grain);
            updated.AddRange(Sample(area));
            grains = updated.ToArray();
            Paint();
        }

        public void Refresh()
        {
            if (layer == null || !IsVisible || Closing) return;
            face.UpdateLayout();
            if (assembling) { dirty = true; return; }
            grains = Sample();
            Paint();
        }

        public void Dismiss(Action done)
        {
            if (Closing) return;
            Stop();
            if (layer == null || !SystemParameters.ClientAreaAnimation) { done(); return; }
            // Use the most recently rendered positions even if opening was interrupted.
            var departing = new List<Grain>();
            for (int i = 0; i < raster.Length; i++)
            {
                int pixel = raster[i];
                if ((uint)pixel >> 24 < 40) continue;
                departing.Add(new Grain { Target = new Point(i % layer.PixelWidth, i / layer.PixelWidth),
                    Ink = Color.FromRgb((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel),
                    Radius = 15 + random.NextDouble() * 55, Speed = random.NextDouble() * 38 - 19,
                    Lag = random.NextDouble() * .12 });
            }
            grains = departing.ToArray();
            onDismissed = done;
            watch.Restart();
            Listen();
        }

        void Listen()
        {
            if (!rendering) { CompositionTarget.Rendering += Frame; rendering = true; }
        }

        void Frame(object sender, EventArgs e)
        {
            seconds = watch.Elapsed.TotalSeconds;
            if (Closing)
            {
                double scale = 1 - Ease(seconds / .62);
                surface.RenderTransformOrigin = new Point((face.RenderSize.Width - 18) / face.RenderSize.Width, 1);
                surface.RenderTransform = new ScaleTransform(scale, scale);
                if (seconds >= .65)
                {
                    var done = onDismissed;
                    done();
                    Stop();
                    return;
                }
            }
            else if (seconds >= 1.75) Stop();
            if (dirty && !assembling && !Closing) { dirty = false; face.UpdateLayout(); grains = Sample(); }
            Paint();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (layer != null) dc.DrawImage(layer, new Rect(face.RenderSize));
        }

        public void Stop()
        {
            CompositionTarget.Rendering -= Frame;
            rendering = assembling = false;
            onDismissed = null;
            surface.Opacity = 1;
            surface.RenderTransform = Transform.Identity;
            watch.Stop();
            InvalidateVisual();
        }
    }
}
