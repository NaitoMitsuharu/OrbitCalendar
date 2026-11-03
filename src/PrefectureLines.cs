using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        static readonly Point[][] prefectures = ReadPrefectures();
        static Point[][] ReadPrefectures()
        {
            var result = new List<Point[]>();
            using (var r = new BinaryReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("prefectures.bin")))
            {
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var points = new Point[r.ReadInt32()];
                    for (int p = 0; p < points.Length; p++) points[p] = new Point(r.ReadSingle(), r.ReadSingle());
                    result.Add(points);
                }
            }
            return result.ToArray();
        }
        void DrawPrefectureLines(LiveData.View view)
        {
            if (view.Viewport == null) return;
            var v = view.Viewport;
            double world = 256 << v.Zoom, scale = v.Zoom == 4 ? .76 : .88;
            Func<double, double> y = latitude => (1 - Math.Log(Math.Tan(latitude * Math.PI / 180) + 1 / Math.Cos(latitude * Math.PI / 180)) / Math.PI) * world / 2;
            double cy = y(v.Latitude);
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
                foreach (var line in prefectures)
                {
                    var projected = new List<Point>(); var bounds = Rect.Empty;
                    foreach (var p in line)
                    {
                        var point = new Point((p.X - v.Longitude) / 360 * world * scale + LiveData.MapWidth / 2.0, (y(p.Y) - cy) * scale + LiveData.MapHeight / 2.0);
                        projected.Add(point); bounds.Union(point);
                    }
                    if (projected.Count < 2 || !bounds.IntersectsWith(new Rect(0, 0, LiveData.MapWidth, LiveData.MapHeight))) continue;
                    drawing.BeginFigure(projected[0], false, false); projected.RemoveAt(0); drawing.PolyLineTo(projected, true, false);
                }
            geometry.Freeze();
            Get<Canvas>("Plot").Children.Add(new System.Windows.Shapes.Path { Data = geometry, Stroke = Art.Ink("#B1C9D0"), StrokeThickness = .7, Opacity = .42, IsHitTestVisible = false, Tag = "prefectures" });
        }
    }
}
