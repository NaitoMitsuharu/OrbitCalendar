using System;
using System.Device.Location;
using System.Globalization;
using System.IO;
using System.Text;

namespace OrbitCalendar
{
    internal static class SavedPlace
    {
        static readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrbitCalendar", "location.txt");
        public static GeoCoordinate Coordinate { get; private set; }
        public static string Name { get; private set; }
        public static string DisplayName { get { return string.IsNullOrEmpty(Name) ? "保存地点" : Name; } }
        public static int Revision { get; private set; }

        static SavedPlace() { Coordinate = Load(); }

        static GeoCoordinate Load()
        {
            Name = null;
            try
            {
                var rows = File.ReadAllLines(path);
                if (rows.Length == 0) return null;
                var parts = rows[0].Split(',');
                double latitude, longitude;
                if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) && Valid(latitude, longitude))
                {
                    Name = rows.Length > 1 ? CleanName(rows[1]) : null;
                    return new GeoCoordinate(latitude, longitude);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        static bool Valid(double latitude, double longitude)
        {
            return latitude >= 20 && latitude <= 48 && longitude >= 120 && longitude <= 155;
        }

        static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var text = new StringBuilder();
            foreach (char c in name.Trim()) if (!char.IsControl(c)) text.Append(c);
            return text.Length == 0 ? null : text.ToString();
        }

        public static void Save(GeoCoordinate coordinate, string name = null)
        {
            if (coordinate == null || !Valid(coordinate.Latitude, coordinate.Longitude)) throw new ArgumentException();
            string savedName = CleanName(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, coordinate.Latitude.ToString("F6", CultureInfo.InvariantCulture) + "," +
                    coordinate.Longitude.ToString("F6", CultureInfo.InvariantCulture) + "\n" + (savedName ?? ""), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Coordinate = coordinate;
            Name = savedName;
            Revision++;
        }

        public static void Clear()
        {
            if (File.Exists(path)) File.Delete(path);
            Coordinate = null;
            Name = null;
            Revision++;
        }
    }
}
