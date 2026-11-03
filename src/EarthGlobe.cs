using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace OrbitCalendar
{
    internal sealed class EarthGlobe : FrameworkElement
    {
        struct Dot { public double Latitude, Longitude; public bool Land; }
        readonly List<Dot> dots = new List<Dot>();
        readonly Brush[] land = new Brush[12], sea = new Brush[12];
        public double Rotation;

        public EarthGlobe()
        {
            Width = 456; Height = 101;
            using (var reader = new BinaryReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("earth.bin")))
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                    dots.Add(new Dot { Latitude = reader.ReadSingle(), Longitude = reader.ReadSingle(), Land = reader.ReadBoolean() });
            for (int i = 0; i < 12; i++)
            {
                double light = .24 + .76 * i / 11;
                land[i] = new SolidColorBrush(Color.FromRgb((byte)(99 * light), (byte)(248 * light), (byte)(221 * light)));
                sea[i] = new SolidColorBrush(Color.FromRgb((byte)(29 * light), (byte)(113 * light), (byte)(155 * light)));
                land[i].Freeze(); sea[i].Freeze();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            const double radius = 43, cx = 244, cy = 50;
            foreach (var dot in dots)
            {
                double longitude = dot.Longitude - 2.15 + Rotation;
                double x = Math.Cos(dot.Latitude) * Math.Sin(longitude);
                double z = Math.Cos(dot.Latitude) * Math.Cos(longitude);
                double north = Math.Sin(dot.Latitude);
                double y = .92 * north - .39 * z;
                double front = .92 * z + .39 * north;
                if (front < 0) continue;
                int shade = Math.Min(11, (int)(front * 11));
                var p = new Point(cx + radius * x, cy - radius * y);
                double size = dot.Land ? .85 : .53;
                dc.DrawEllipse(dot.Land ? land[shade] : sea[shade], null, p, size, size);
            }
        }
    }
}
