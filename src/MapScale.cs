using System;
using System.Globalization;
using System.IO;

namespace OrbitCalendar
{
    internal static class MapScale
    {
        static readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitCalendar", "map-zoom.txt");
        static int[] zooms;
        static MapScale() { Load(); }
        internal static void Load()
        {
            zooms = new[] { 9, 4, 7, 7 };
            try
            {
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < Math.Min(lines.Length, zooms.Length); i++)
                {
                    int zoom;
                    if (int.TryParse(lines[i], out zoom) && zoom >= 4 && zoom <= 18) zooms[i] = zoom;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public static int LegacyZoom { get { return zooms[0]; } }
        public static int ForRegion(int region) { return region == 0 && SavedPlace.Active != null ? SavedPlace.Active.Zoom : zooms[Math.Max(0, Math.Min(3, region))]; }
        public static bool Remember(int region, int zoom)
        {
            if (region < 0 || region > 3 || zoom < 4 || zoom > 18) return false;
            if (region == 0 && SavedPlace.Active != null)
            {
                try { return SavedPlace.UpdateZoom(zoom); }
                catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
            }
            if (zooms[region] == zoom) return true;
            var updated = (int[])zooms.Clone(); updated[region] = zoom;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(temporary, Array.ConvertAll(updated, z => z.ToString(CultureInfo.InvariantCulture)));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                zooms = updated;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
