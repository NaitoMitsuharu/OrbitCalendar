using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        sealed class MapCity
        {
            public string Name;
            public GeoCoordinate Coordinate;
            public int MinZoom, Priority;
        }
        static readonly MapCity[] mapCities = ReadMapCities();

        static MapCity[] ReadMapCities()
        {
            var cities = new List<MapCity>();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("cities.csv"))
            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("#")) continue;
                    var row = line.Split(',');
                    cities.Add(new MapCity { Name = row[0], Coordinate = new GeoCoordinate(
                        double.Parse(row[1], CultureInfo.InvariantCulture), double.Parse(row[2], CultureInfo.InvariantCulture)),
                        MinZoom = int.Parse(row[3], CultureInfo.InvariantCulture), Priority = int.Parse(row[4], CultureInfo.InvariantCulture) });
                }
            }
            return cities.ToArray();
        }

        void DrawCityLabels(LiveData.View view)
        {
            if (view.Viewport == null) return;
            var plot = Get<Canvas>("Plot");
            var viewport = new Rect(3, 3, LiveData.MapWidth - 6, LiveData.MapHeight - 6);
            var occupied = new List<Rect>();
            // Keep the saved/current location and the candidate point unobscured.
            if (view.LocationPoint.HasValue)
            {
                Point p = view.LocationPoint.Value;
                occupied.Add(new Rect(p.X - 10, p.Y - 10, 20, 20));
            }
            if (selectingPlace && candidatePlace != null)
            {
                Point p = PlacePixel(view.Viewport, candidatePlace);
                occupied.Add(new Rect(p.X - 10, p.Y - 10, 20, 20));
            }
            var candidates = mapCities.Where(c => c.MinZoom <= view.Viewport.Zoom)
                .Select(c => new { City = c, Point = PlacePixel(view.Viewport, c.Coordinate) })
                .Where(c => viewport.Contains(c.Point))
                .OrderBy(c => view.Viewport.Zoom <= 5 && c.City.Name == "東京" ? -1 : c.City.Priority)
                .ThenBy(c => (c.Point - new Point(LiveData.MapWidth / 2.0, LiveData.MapHeight / 2.0)).LengthSquared);
            foreach (var city in candidates)
            {
                var text = new TextBlock { Text = city.City.Name, Foreground = Art.Ink("#E4F4F5"), FontSize = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Yu Gothic UI"), FontWeight = FontWeights.SemiBold };
                var label = new Border { Child = text, Background = Art.Ink("#E80B1923"), CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 2), IsHitTestVisible = false, Tag = "city:" + city.City.Name };
                label.Measure(new Size(100, 30));
                double w = label.DesiredSize.Width, h = label.DesiredSize.Height;
                var p = city.Point;
                // Alternative offsets also keep names legible along the map edges.
                var positions = new[] { new Point(p.X + 6, p.Y - h - 3), new Point(p.X + 6, p.Y + 3),
                    new Point(p.X - w - 6, p.Y - h - 3), new Point(p.X - w - 6, p.Y + 3),
                    new Point(p.X - w / 2, p.Y - h - 6), new Point(p.X - w / 2, p.Y + 6),
                    new Point(p.X - w / 2, p.Y - h - 14), new Point(p.X - w / 2, p.Y + 14),
                    new Point(p.X + 14, p.Y - h / 2), new Point(p.X - w - 14, p.Y - h / 2) };
                foreach (var position in positions)
                {
                    var bounds = new Rect(position, label.DesiredSize);
                    var padded = bounds; padded.Inflate(2, 2);
                    if (!viewport.Contains(bounds) || occupied.Any(r => r.IntersectsWith(padded))) continue;
                    var dot = new Ellipse { Width = 3, Height = 3, Fill = Art.Ink("#D6ECF0"), IsHitTestVisible = false };
                    Canvas.SetLeft(dot, p.X - 1.5); Canvas.SetTop(dot, p.Y - 1.5); plot.Children.Add(dot);
                    Canvas.SetLeft(label, position.X); Canvas.SetTop(label, position.Y); plot.Children.Add(label);
                    occupied.Add(bounds);
                    break;
                }
            }
        }
    }
}
