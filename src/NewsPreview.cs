using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace OrbitCalendar
{
    internal static partial class InformationFeeds
    {
        static string Short(string text) { return text.Length > 140 ? text.Substring(0, 140).TrimEnd() + "…" : text; }
        static string Meta(string html, string key)
        {
            foreach (Match tag in Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var attributes = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match attr in Regex.Matches(tag.Value, "([\\w:-]+)\\s*=\\s*([\"'])(.*?)\\2", RegexOptions.Singleline)) attributes[attr.Groups[1].Value] = WebUtility.HtmlDecode(attr.Groups[3].Value);
                string property, value;
                if ((attributes.TryGetValue("property", out property) || attributes.TryGetValue("name", out property)) && property.Equals(key, StringComparison.OrdinalIgnoreCase) && attributes.TryGetValue("content", out value)) return value;
            }
            return "";
        }
        internal static async Task PreviewNews(Item item, CancellationToken token)
        {
            if (item.DetailsLoaded) return;
            try
            {
                var bytes = await LiveData.Get(item.Url, token).ConfigureAwait(false);
                if (bytes.Length <= 3000000)
                {
                    string html = Encoding.UTF8.GetString(bytes);
                    string description = Meta(html, "og:description");
                    if (description.Length == 0) description = Meta(html, "description");
                    if (item.Description.Length == 0 && description.Length > 0) item.Description = Short(Plain(description));
                    if (!SafeUrl(item.ImageUrl))
                    {
                        string image = Meta(html, "og:image"); Uri absolute;
                        if (Uri.TryCreate(new Uri(item.Url), image, out absolute) && SafeUrl(absolute.AbsoluteUri) && image.Length > 0) item.ImageUrl = absolute.AbsoluteUri;
                    }
                }
            }
            catch (Exception) { token.ThrowIfCancellationRequested(); }
            if (SafeUrl(item.ImageUrl))
            {
                try
                {
                    var bytes = await LiveData.Get(item.ImageUrl, token).ConfigureAwait(false);
                    if (bytes.Length > 0 && bytes.Length < 6000000)
                        using (var stream = new MemoryStream(bytes))
                        {
                            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                            var frame = decoder.Frames[0];
                            if ((long)frame.PixelWidth * frame.PixelHeight <= 30000000)
                            {
                                stream.Position = 0; var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 790; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); item.Picture = bitmap;
                            }
                        }
                }
                catch (Exception) { token.ThrowIfCancellationRequested(); }
            }
            token.ThrowIfCancellationRequested(); item.DetailsLoaded = true;
        }
    }
}
