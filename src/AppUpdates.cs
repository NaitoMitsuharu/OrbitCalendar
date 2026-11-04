using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OrbitCalendar
{
    internal static class AppUpdates
    {
        public const string Repository = "https://github.com/NaitoMitsuharu/OrbitCalendar";
        const long MaximumSize = 100 * 1024 * 1024;
        static readonly HttpClient http = CreateClient();
        public static Version CurrentVersion { get { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); } }
        public static string CurrentLabel { get { return CurrentVersion.ToString(3); } }
        public static Version Normalize(Version value) { return new Version(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision)); }
        static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitCalendar/" + CurrentLabel);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
        public sealed class Asset { public string name { get; set; } public string state { get; set; } public long size { get; set; } public string digest { get; set; } public string browser_download_url { get; set; } }
        public sealed class ReleaseData { public string tag_name { get; set; } public string name { get; set; } public string body { get; set; } public bool draft { get; set; } public bool prerelease { get; set; } public Asset[] assets { get; set; } }
        public sealed class Release
        {
            public string Tag, Title, Notes, DownloadUrl, Sha256;
            public long Size;
            public Version Version;
            public bool IsNew { get { return Version > CurrentVersion; } }
        }
        public static async Task<Release> Check(CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using (var response = await http.GetAsync("https://api.github.com/repos/NaitoMitsuharu/OrbitCalendar/releases/latest", timeout.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }
        internal static Release Parse(string json)
        {
            var data = new JavaScriptSerializer().Deserialize<ReleaseData>(json);
            if (data == null || data.draft || data.prerelease || data.tag_name == null || !Regex.IsMatch(data.tag_name, @"^v?\d+\.\d+\.\d+(\.\d+)?$")) throw new InvalidDataException("正式版の更新情報を確認できませんでした");
            Version version;
            if (!System.Version.TryParse(data.tag_name.TrimStart('v'), out version)) throw new InvalidDataException("バージョンを確認できませんでした");
            var asset = (data.assets ?? new Asset[0]).FirstOrDefault(a => a != null && a.name == "OrbitCalendar.exe" && a.state == "uploaded");
            string expectedUrl = Repository + "/releases/download/" + data.tag_name + "/OrbitCalendar.exe";
            if (asset == null || asset.size < 1024 || asset.size > MaximumSize || asset.browser_download_url != expectedUrl || asset.digest == null || !Regex.IsMatch(asset.digest, @"^sha256:[0-9a-fA-F]{64}$")) throw new InvalidDataException("更新ファイルの確認情報が不足しています");
            return new Release { Tag = data.tag_name, Title = data.name ?? data.tag_name, Notes = data.body ?? "", Version = Normalize(version), DownloadUrl = expectedUrl, Size = asset.size, Sha256 = asset.digest.Substring(7).ToLowerInvariant() };
        }
        internal static void ValidateRelease(Release release)
        {
            if (release == null || release.Tag == null || !Regex.IsMatch(release.Tag, @"^v?\d+\.\d+\.\d+(\.\d+)?$") || release.DownloadUrl != Repository + "/releases/download/" + release.Tag + "/OrbitCalendar.exe" || release.Size < 1024 || release.Size > MaximumSize || release.Sha256 == null || !Regex.IsMatch(release.Sha256, @"^[0-9a-fA-F]{64}$")) throw new InvalidDataException("更新ファイルの取得元を確認できませんでした");
            if (release.Version == null || Normalize(new Version(release.Tag.TrimStart('v'))) != Normalize(release.Version)) throw new InvalidDataException("更新バージョンの指定が一致しません");
        }
        public static async Task<string> Download(Release release, IProgress<double> progress, CancellationToken token)
        {
            ValidateRelease(release);
            string directory = Path.Combine(Path.GetTempPath(), "OrbitCalendar-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string staged = Path.Combine(directory, "OrbitCalendar.new.exe");
            try
            {
                using (var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var final = response.RequestMessage.RequestUri;
                    if (final.Scheme != "https" || !(final.Host == "github.com" || final.Host == "release-assets.githubusercontent.com" || final.Host == "objects.githubusercontent.com" || final.Host == "github-releases.githubusercontent.com")) throw new InvalidDataException("更新ファイルの配信先を確認できませんでした");
                    if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != release.Size) throw new InvalidDataException("更新ファイルのサイズが一致しません");
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        byte[] buffer = new byte[65536]; long total = 0; int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                        {
                            total += read; if (total > release.Size) throw new InvalidDataException("更新ファイルのサイズが一致しません");
                            await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                            if (progress != null) progress.Report((double)total / release.Size);
                        }
                        if (total != release.Size) throw new InvalidDataException("ダウンロードが完了しませんでした");
                    }
                }
                token.ThrowIfCancellationRequested();
                Verify(staged, release.Sha256, release.Version);
                return staged;
            }
            catch { Cleanup(directory); throw; }
        }
        public static string Hash(string path)
        {
            using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static void Verify(string path, string expectedHash, Version version)
        {
            if (!string.Equals(Hash(path), expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新ファイルの検証に失敗しました。再度ダウンロードしてください");
            var identity = AssemblyName.GetAssemblyName(path);
            if (identity.Name != "OrbitCalendar" || Normalize(identity.Version) != Normalize(version)) throw new InvalidDataException("更新ファイルのアプリ名またはバージョンが一致しません");
        }
        public static bool IsWorkDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return false;
            string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(Path.GetDirectoryName(full), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(Path.GetFileName(full), @"^OrbitCalendar-update-[0-9a-f]{32}$") && (!Directory.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0);
        }
        public static void Cleanup(string directory)
        {
            try
            {
                if (!IsWorkDirectory(directory)) return;
                foreach (string file in new[] { "OrbitCalendar.new.exe", "OrbitCalendar.Updater.exe", "plan.json", "ready.json", "result.json" })
                    try { File.Delete(Path.Combine(directory, file)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
