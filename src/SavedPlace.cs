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
        public static int Revision { get; private set; }

        static SavedPlace() { Coordinate = Load(); }

        static GeoCoordinate Load()
        {
            try
            {
                var parts = File.ReadAllText(path).Split(',');
                double latitude, longitude;
                if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) && Valid(latitude, longitude))
                    return new GeoCoordinate(latitude, longitude);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        static bool Valid(double latitude, double longitude)
        {
            return latitude >= 20 && latitude <= 48 && longitude >= 120 && longitude <= 155;
        }

        public static void Save(GeoCoordinate coordinate)
        {
            if (coordinate == null || !Valid(coordinate.Latitude, coordinate.Longitude)) throw new ArgumentException();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, coordinate.Latitude.ToString("F6", CultureInfo.InvariantCulture) + "," +
                    coordinate.Longitude.ToString("F6", CultureInfo.InvariantCulture), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Coordinate = coordinate;
            Revision++;
        }

        public static void Clear()
        {
            if (File.Exists(path)) File.Delete(path);
            Coordinate = null;
            Revision++;
        }
    }
}
