using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            canvas.Children.Add(new Path { Data = mesh, Stroke = Ink("#2E6171"), StrokeThickness = .75 });
            var arc = new Path { Data = Geometry.Parse("M 143,111 C 174,54 334,-7 419,6 C 466,17 354,76 249,103"), Stroke = Ink("#5CABB1"), StrokeThickness = 1 };
            canvas.Children.Add(arc);
            var rng = new Random(71);
            for (int i = 0; i < 65; i++)
            {
                double size = i % 6 == 0 ? 2.2 : 1;
                var dot = new Ellipse { Width = size, Height = size, Fill = Ink(i % 6 == 0 ? "#97EDE7" : "#48818E") };
                Canvas.SetLeft(dot, 132 + rng.NextDouble() * 324);
                Canvas.SetTop(dot, rng.NextDouble() * 101);
                canvas.Children.Add(dot);
            }
        }
    }
}
