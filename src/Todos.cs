using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;

namespace OrbitCalendar
{
    internal sealed class TodoItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Notes { get; set; }
        public string Due { get; set; }
        public string Created { get; set; }
        public string Completed { get; set; }
        public List<string> Images { get; set; }

        public DateTime? DueDate
        {
            get
            {
                DateTime value;
                return DateTime.TryParseExact(Due ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value) ? value : (DateTime?)null;
            }
        }
        public bool HasLinks { get { return Todos.Links(Notes).Any(); } }
        public int ImageCount { get { return Images == null ? 0 : Images.Count; } }
    }

    internal static class Todos
    {
        static string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitCalendar");
        static string path = Path.Combine(folder, "todos.json");
        static string imageFolder = Path.Combine(folder, "todo-images");
        static readonly string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
        static readonly Regex url = new Regex(@"https?://[^\s<>""'「」（）]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static List<TodoItem> items;
        public static int Revision { get; private set; }

        sealed class State { public List<TodoItem> Items { get; set; } }

        static List<TodoItem> All { get { if (items == null) Reload(); return items; } }

        internal static void Reload()
        {
            items = new List<TodoItem>();
            try
            {
                if (File.Exists(path))
                {
                    var state = new JavaScriptSerializer().Deserialize<State>(File.ReadAllText(path, Encoding.UTF8));
                    if (state != null && state.Items != null)
                        items = state.Items.Where(i => i != null && !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Title)).ToList();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            foreach (var item in items) if (item.Images == null) item.Images = new List<string>();
            Revision++;
        }

        static void Save()
        {
            Directory.CreateDirectory(folder);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(new State { Items = All }), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            Revision++;
        }

        /// <summary>Open tasks: the ones with a due date first (earliest first), then undated tasks by creation.</summary>
        public static TodoItem[] Open
        {
            get
            {
                return All.Where(i => i.Completed == null)
                    .OrderBy(i => i.DueDate.HasValue ? 0 : 1).ThenBy(i => i.DueDate ?? DateTime.MaxValue)
                    .ThenBy(i => i.Created, StringComparer.Ordinal).ToArray();
            }
        }

        /// <summary>Completed tasks, most recently completed first.</summary>
        public static TodoItem[] Done
        {
            get { return All.Where(i => i.Completed != null).OrderByDescending(i => i.Completed, StringComparer.Ordinal).ToArray(); }
        }

        public static DateTime? CompletedDate(TodoItem item)
        {
            DateTime value;
            return item != null && DateTime.TryParse(item.Completed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value) ? value : (DateTime?)null;
        }

        /// <summary>Removes picture files no task refers to (left behind by drafts that were never saved).</summary>
        public static void PruneImages(IEnumerable<string> keep)
        {
            try
            {
                if (!Directory.Exists(imageFolder)) return;
                var used = new HashSet<string>(All.SelectMany(i => i.Images ?? new List<string>()).Concat(keep ?? Enumerable.Empty<string>()), StringComparer.OrdinalIgnoreCase);
                DeleteImages(Directory.GetFiles(imageFolder).Select(Path.GetFileName).Where(n => !used.Contains(n)).ToArray());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static TodoItem[] DueOn(DateTime day) { return Open.Where(i => i.DueDate == day.Date).ToArray(); }
        public static TodoItem Find(string id) { return All.FirstOrDefault(i => i.Id == id); }

        public static TodoItem Add(string title, string notes, DateTime? due, IEnumerable<string> images)
        {
            var item = new TodoItem { Id = Guid.NewGuid().ToString("N"), Title = title.Trim(), Notes = Clean(notes), Due = Format(due),
                Created = DateTime.Now.ToString("o", CultureInfo.InvariantCulture), Images = images.ToList() };
            All.Add(item);
            try { Save(); }
            catch { All.Remove(item); throw; }
            return item;
        }

        public static void Update(string id, string title, string notes, DateTime? due, IEnumerable<string> images)
        {
            var item = Find(id);
            if (item == null) return;
            var previous = new TodoItem { Title = item.Title, Notes = item.Notes, Due = item.Due, Images = item.Images };
            var removed = item.Images.Except(images).ToArray();
            item.Title = title.Trim(); item.Notes = Clean(notes); item.Due = Format(due); item.Images = images.ToList();
            try { Save(); }
            catch { item.Title = previous.Title; item.Notes = previous.Notes; item.Due = previous.Due; item.Images = previous.Images; throw; }
            DeleteImages(removed);
        }

        public static void SetCompleted(string id, bool completed)
        {
            var item = Find(id);
            if (item == null) return;
            string previous = item.Completed;
            item.Completed = completed ? DateTime.Now.ToString("o", CultureInfo.InvariantCulture) : null;
            try { Save(); }
            catch { item.Completed = previous; throw; }
        }

        public static void Delete(string id)
        {
            var item = Find(id);
            if (item == null) return;
            int index = All.IndexOf(item);
            All.Remove(item);
            try { Save(); }
            catch { All.Insert(index, item); throw; }
            DeleteImages(item.Images);
        }

        static string Clean(string notes) { notes = (notes ?? "").Replace("\r\n", "\n").TrimEnd(); return notes.Length == 0 ? null : notes; }
        static string Format(DateTime? due) { return due.HasValue ? due.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null; }

        internal struct Link { public int Index; public string Url; }

        /// <summary>http(s) addresses in the notes, without trailing punctuation from the surrounding sentence.</summary>
        public static IEnumerable<Link> Links(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            foreach (Match match in url.Matches(text))
            {
                string value = match.Value.TrimEnd('.', ',', '。', '、', ')', ']', '!', '?');
                if (InformationFeeds.SafeUrl(value)) yield return new Link { Index = match.Index, Url = value };
            }
        }

        public static string ImagePath(string name) { return Path.Combine(imageFolder, Path.GetFileName(name ?? "")); }
        public static bool IsImageFile(string file) { return imageExtensions.Contains(Path.GetExtension(file ?? "").ToLowerInvariant()); }

        /// <summary>Copies a picture into the app data folder so the task keeps it after the original moves.</summary>
        public static string ImportFile(string file)
        {
            if (!IsImageFile(file) || !File.Exists(file)) return null;
            Directory.CreateDirectory(imageFolder);
            string name = Guid.NewGuid().ToString("N") + Path.GetExtension(file).ToLowerInvariant();
            File.Copy(file, ImagePath(name));
            return name;
        }

        public static string ImportBitmap(BitmapSource bitmap)
        {
            if (bitmap == null) return null;
            Directory.CreateDirectory(imageFolder);
            string name = Guid.NewGuid().ToString("N") + ".png";
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(ImagePath(name))) encoder.Save(stream);
            return name;
        }

        public static void DeleteImages(IEnumerable<string> names)
        {
            foreach (var name in names ?? Enumerable.Empty<string>())
            {
                try { File.Delete(ImagePath(name)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static BitmapImage LoadImage(string name, int decodeWidth)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
                image.UriSource = new Uri(ImagePath(name));
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is NotSupportedException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is UriFormatException || ex is System.Runtime.InteropServices.COMException) return null;
                throw;
            }
        }
    }
}
