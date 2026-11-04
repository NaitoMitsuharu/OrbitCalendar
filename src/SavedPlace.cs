using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace OrbitCalendar
{
    internal static class SavedPlace
    {
        static readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitCalendar", "location.txt");
        public sealed class Entry
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public int Zoom { get; set; }
            [ScriptIgnore] public GeoCoordinate Coordinate { get { return new GeoCoordinate(Latitude, Longitude); } }
        }
        public sealed class State
        {
            public int Version { get; set; }
            public string ActiveId { get; set; }
            public Entry[] Places { get; set; }
            public bool[] Hidden { get; set; }
        }
        static List<Entry> entries = new List<Entry>();
        static bool[] hidden = new bool[4];
        public static IList<Entry> Entries { get { return entries.AsReadOnly(); } }
        public static string ActiveId { get; private set; }
        public static Entry Active { get { return entries.FirstOrDefault(p => p.Id == ActiveId); } }
        public static GeoCoordinate Coordinate { get; private set; }
        public static string Name { get; private set; }
        public static string DisplayName { get { return string.IsNullOrEmpty(Name) ? "保存地点" : Name; } }
        public static int Revision { get; private set; }
        public static bool IsHidden(int region) { return region >= 0 && region < 4 && hidden[region]; }
        static SavedPlace() { Coordinate = Load(); }

        static GeoCoordinate Load()
        {
            entries = new List<Entry>(); hidden = new bool[4]; ActiveId = Name = null;
            try
            {
                var rows = File.ReadAllLines(path);
                if (rows.Length > 2 && rows[2].StartsWith("{"))
                {
                    var state = new JavaScriptSerializer().Deserialize<State>(rows[2]);
                    if (state == null || state.Version != 3 || state.Places == null) return null;
                    foreach (var item in state.Places)
                    {
                        if (item == null || string.IsNullOrEmpty(item.Id) || entries.Any(e => e.Id == item.Id) || !Valid(item.Latitude, item.Longitude)) continue;
                        item.Name = CleanName(item.Name) ?? "保存地点";
                        if (item.Zoom < 4 || item.Zoom > 18) item.Zoom = 9;
                        entries.Add(item);
                    }
                    if (state.Hidden != null) Array.Copy(state.Hidden, hidden, Math.Min(4, state.Hidden.Length));
                    if (entries.Count == 0 && hidden.All(h => h)) hidden[0] = false;
                    ActiveId = entries.Any(p => p.Id == state.ActiveId) ? state.ActiveId :
                        ((state.ActiveId != null || hidden[0]) && entries.Count > 0 ? entries[0].Id : null);
                    var selected = Active;
                    Name = selected == null ? null : selected.Name;
                    return selected == null ? null : selected.Coordinate;
                }
                if (rows.Length == 0) return null;
                var parts = rows[0].Split(','); double lat, lon;
                if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon) && Valid(lat, lon))
                {
                    Name = rows.Length > 1 ? CleanName(rows[1]) : null;
                    entries.Add(new Entry { Id = "legacy", Name = Name ?? "保存地点", Latitude = lat, Longitude = lon, Zoom = MapScale.LegacyZoom });
                    ActiveId = "legacy";
                    return new GeoCoordinate(lat, lon);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return null;
        }
        static bool Valid(double lat, double lon) { return lat >= 20 && lat <= 48 && lon >= 120 && lon <= 155; }
        static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var text = new StringBuilder();
            foreach (char c in name.Trim()) if (!char.IsControl(c)) text.Append(c);
            return text.Length == 0 ? null : text.ToString();
        }
        static List<Entry> Copy() { return entries.Select(p => new Entry { Id = p.Id, Name = p.Name, Latitude = p.Latitude, Longitude = p.Longitude, Zoom = p.Zoom }).ToList(); }
        static void Commit(List<Entry> next, string activeId, bool[] nextHidden, bool revision = true)
        {
            var selected = next.FirstOrDefault(p => p.Id == activeId);
            var state = new State { Version = 3, ActiveId = activeId, Places = next.ToArray(), Hidden = nextHidden };
            // Retain the active coordinate/name on the first two lines; collections follow as JSON.
            string first = selected == null ? "" : selected.Latitude.ToString("F6", CultureInfo.InvariantCulture) + "," + selected.Longitude.ToString("F6", CultureInfo.InvariantCulture);
            string json = new JavaScriptSerializer().Serialize(state);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                File.WriteAllText(temporary, first + "\n" + (selected == null ? "" : selected.Name) + "\n" + json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            entries = next; hidden = nextHidden; ActiveId = activeId;
            Coordinate = selected == null ? null : selected.Coordinate; Name = selected == null ? null : selected.Name;
            if (revision) Revision++;
        }
        public static void Save(GeoCoordinate coordinate, string name = null)
        {
            if (coordinate == null || !Valid(coordinate.Latitude, coordinate.Longitude)) throw new ArgumentException();
            if (Active == null) { Add(coordinate, name, MapScale.LegacyZoom); return; }
            var next = Copy(); var item = next.First(p => p.Id == ActiveId);
            item.Latitude = coordinate.Latitude; item.Longitude = coordinate.Longitude; item.Name = CleanName(name) ?? "保存地点";
            Commit(next, ActiveId, (bool[])hidden.Clone());
        }
        public static void Add(GeoCoordinate coordinate, string name, int zoom)
        {
            if (coordinate == null || !Valid(coordinate.Latitude, coordinate.Longitude) || zoom < 4 || zoom > 18) throw new ArgumentException();
            var next = Copy(); string id = Guid.NewGuid().ToString("N");
            next.Add(new Entry { Id = id, Name = CleanName(name) ?? "地点" + (next.Count + 1), Latitude = coordinate.Latitude, Longitude = coordinate.Longitude, Zoom = zoom });
            Commit(next, id, (bool[])hidden.Clone());
        }
        public static void Select(string id)
        {
            if (id != null && !entries.Any(p => p.Id == id)) throw new ArgumentException();
            if (id == ActiveId && (id == null) == (Coordinate == null)) return;
            Commit(Copy(), id, (bool[])hidden.Clone());
        }
        public static void Rename(string id, string name)
        {
            var next = Copy(); var item = next.FirstOrDefault(p => p.Id == id); name = CleanName(name);
            if (item == null || name == null) throw new ArgumentException();
            item.Name = name; Commit(next, ActiveId, (bool[])hidden.Clone());
        }
        public static void UpdatePosition(string id, GeoCoordinate coordinate, int zoom)
        {
            if (coordinate == null || !Valid(coordinate.Latitude, coordinate.Longitude) || zoom < 4 || zoom > 18) throw new ArgumentException();
            var next = Copy(); var item = next.FirstOrDefault(p => p.Id == id);
            if (item == null) throw new ArgumentException();
            item.Latitude = coordinate.Latitude; item.Longitude = coordinate.Longitude; item.Zoom = zoom;
            Commit(next, ActiveId, (bool[])hidden.Clone());
        }
        public static bool Move(string id, int direction)
        {
            var next = Copy(); int index = next.FindIndex(p => p.Id == id), destination = index + direction;
            if ((direction != -1 && direction != 1) || index < 0 || destination < 0 || destination >= next.Count) return false;
            var item = next[index]; next.RemoveAt(index); next.Insert(destination, item);
            Commit(next, ActiveId, (bool[])hidden.Clone(), false); return true;
        }
        public static void Remove(string id)
        {
            var next = Copy(); if (next.RemoveAll(p => p.Id == id) == 0) return;
            string activeId = ActiveId == id ? (next.Count == 0 ? null : next[0].Id) : ActiveId;
            var visibility = (bool[])hidden.Clone(); if (next.Count == 0 && visibility.All(h => h)) visibility[0] = false;
            Commit(next, activeId, visibility);
        }
        public static bool SetHidden(int region, bool value)
        {
            if (region < 0 || region > 3) return false;
            var visibility = (bool[])hidden.Clone(); visibility[region] = value;
            if (entries.Count == 0 && visibility.All(h => h)) return false;
            Commit(Copy(), ActiveId, visibility, false); return true;
        }
        public static bool UpdateZoom(int zoom)
        {
            if (Active == null || zoom < 4 || zoom > 18) return false;
            if (Active.Zoom == zoom) return true;
            var next = Copy(); next.First(p => p.Id == ActiveId).Zoom = zoom;
            Commit(next, ActiveId, (bool[])hidden.Clone(), false); return true;
        }
        public static void Clear() { Select(null); }
    }
}
