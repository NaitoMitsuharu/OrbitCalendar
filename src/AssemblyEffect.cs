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
            public double Longitude, Radius, Speed, Lag;
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
        double timeScale = 1;
        bool assembling, morph, rendering, dirty;
        int motionStyle;

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
            // Capture in local coordinates, independent of the source panel's
            // cached parent layout and clipping (notably ScrollViewer updates).
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawRectangle(new VisualBrush(face) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h),
                    ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, w, h), Stretch = Stretch.Fill }, null, new Rect(0, 0, w, h));
            frame.Render(visual);
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

        public void StartQuick(Rect area) { Start(true, area); timeScale = Math.Min(Preferences.AnimationScale, .14); }

        public void StartCalendarChange(Rect[] regions) { Start(true, new Rect(face.RenderSize), regions); }

        static bool InRegions(Point point, Rect area, Rect[] regions)
        {
            if (!area.Contains(point)) return false;
            if (regions == null) return true;
            foreach (var region in regions) if (region.Contains(point)) return true;
            return false;
        }

        void Start(bool fromPrevious, Rect area, Rect[] regions = null)
        {
            timeScale = Preferences.AnimationScale;
            motionStyle = Preferences.Motion >= 6 && Preferences.Motion <= 8 ? Preferences.Motion : 6;
            dirty = false;
            var previous = grains;
            var oldByPixel = new Dictionary<int, Grain>();
            int width = (int)face.RenderSize.Width;
            if (previous != null)
                foreach (var grain in previous) oldByPixel[(int)grain.Target.Y * width + (int)grain.Target.X] = grain;
            Stop();
            face.UpdateLayout();
            grains = Sample();
            if (grains.Length == 0) return;
            var fixedPixels = new HashSet<int>();
            int movingCount = 0;
            for (int i = 0; i < grains.Length; i++)
            {
                int key = (int)grains[i].Target.Y * width + (int)grains[i].Target.X;
                Grain prior;
                bool unchanged = fromPrevious && oldByPixel.TryGetValue(key, out prior) &&
                    prior.Ink == grains[i].Ink && (prior.Current - prior.Target).LengthSquared < .25;
                grains[i].Moving = InRegions(grains[i].Target, area, regions) && !unchanged;
                if (grains[i].Moving) movingCount++;
                else fixedPixels.Add(key);
            }
            var origins = new List<Grain>();
            if (fromPrevious && previous != null)
                foreach (var grain in previous)
                {
                    int key = (int)grain.Target.Y * width + (int)grain.Target.X;
                    if (InRegions(grain.Target, area, regions) && !fixedPixels.Contains(key)) origins.Add(grain);
                }
            morph = fromPrevious && origins.Count > 0;
            int movingIndex = 0;
            for (int i = 0; i < grains.Length; i++)
            {
                if (!grains[i].Moving) { grains[i].Current = grains[i].Target; continue; }
                grains[i].Longitude = random.NextDouble() * 2 * Math.PI;
                grains[i].Radius = i % 7 == 0 ? 102 + random.NextDouble() * 51 : 82 + random.NextDouble() * 3;
                grains[i].Speed = i % 7 == 0 ? -2 : 2.5;
                grains[i].Lag = random.NextDouble() * .25;
                if (morph) grains[i].Origin = origins[(int)((long)movingIndex * origins.Count / movingCount)].Current;
                else
                {
                    double angle = grains[i].Longitude;
                    double spread = Math.Sqrt(-2 * Math.Log(Math.Max(1e-9, random.NextDouble()))) * 28;
                    grains[i].Origin = new Point(grains[i].Target.X * .28 + (face.RenderSize.Width - 38) * .72 + Math.Cos(angle) * spread,
                        grains[i].Target.Y * .28 + (face.RenderSize.Height - 7) * .72 + Math.Sin(angle) * spread);
                    if (motionStyle == 7)
                        grains[i].Origin = new Point(area.Left + random.NextDouble() * Math.Min(55, area.Width * .2),
                            area.Top + (grains[i].Target.Y - area.Top) * .65 + Math.Sin(angle) * 24);
                    else if (motionStyle == 8)
                        grains[i].Origin = new Point(area.Left + area.Width * .5 + (grains[i].Target.X - area.Left - area.Width * .5) * .22 + Math.Cos(angle) * spread * .2,
                            area.Top + area.Height * .5 + (grains[i].Target.Y - area.Top - area.Height * .5) * .22 + Math.Sin(angle) * spread * .2);
                }
                if (area.Width < face.RenderSize.Width && !morph && motionStyle == 6)
                    grains[i].Origin = new Point(area.Right - 8 - random.NextDouble() * Math.Min(65, area.Width),
                        area.Bottom - 5 - random.NextDouble() * Math.Min(25, area.Height));
                movingIndex++;
            }
            EnsureLayer();
            assembling = SystemParameters.ClientAreaAnimation && movingCount > 0;
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

        void EnsureLayer()
        {
            int w = Math.Max(1, (int)face.RenderSize.Width), h = Math.Max(1, (int)face.RenderSize.Height);
            if (layer != null && layer.PixelWidth == w && layer.PixelHeight == h) return;
            layer = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            raster = new int[w * h];
        }

        void Paint()
        {
            EnsureLayer();
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
                        // Every grain follows its own destination from the first
                        // frame, including transitions from the previous screen.
                        double delay = (g.Edge ? .015 : .10) + g.Lag * .45;
                        if (motionStyle == 7) delay += g.Target.X / Math.Max(1, w) * .16;
                        double duration = motionStyle == 7 ? .91 : 1.12;
                        double progress = Math.Max(0, Math.Min(1, (seconds - delay) / duration));
                        double t = motionStyle == 7 ? 1 - Math.Pow(1 - progress, 3) : Ease(progress);
                        var displacement = g.Target - g.Origin;
                        double length = Math.Max(1, displacement.Length);
                        double curve = Math.Sin(Math.PI * t) * Math.Min(48, length * .20);
                        if (motionStyle == 7)
                        {
                            px = g.Origin.X + displacement.X * t - displacement.Y / length * curve;
                            py = g.Origin.Y + displacement.Y * t + displacement.X / length * curve;
                        }
                        else
                        {
                            double drift = Math.Sin(Math.PI * progress) * (1 - progress) * 9;
                            px = g.Origin.X + displacement.X * t + Math.Cos(g.Longitude) * drift;
                            py = g.Origin.Y + displacement.Y * t + Math.Sin(g.Longitude) * drift;
                            double light = .28 + .72 * t;
                            red = (int)(red * light); green = (int)(green * light); blue = (int)(blue * light);
                        }
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

        public bool CanRefreshArea(Rect area)
        {
            if (layer == null || !IsVisible || Closing || grains == null) return false;
            if (assembling)
                foreach (var grain in grains) if (grain.Moving && area.Contains(grain.Target)) return false;
            return true;
        }

        public void RefreshArea(Rect area)
        {
            if (!CanRefreshArea(area)) return;
            face.UpdateLayout();
            var updated = new List<Grain>();
            foreach (var grain in grains) if (!area.Contains(grain.Target)) updated.Add(grain);
            updated.AddRange(Sample(area));
            grains = updated.ToArray();
            Paint();
        }

        public void ReplaceArea(Rect area)
        {
            if (layer == null || !IsVisible || Closing || grains == null) return;
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
            timeScale = Preferences.AnimationScale;
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
            seconds = watch.Elapsed.TotalSeconds / timeScale;
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
