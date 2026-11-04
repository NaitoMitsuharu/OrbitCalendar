using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OrbitCalendar
{
    // The helper is a copy of this executable. Only the executable is replaced;
    // user data and the existing startup registration are never moved or rewritten.
    internal static class UpdateInstaller
    {
        public sealed class Plan
        {
            public string Target, Staged, Sha256, Version, OriginalSha256, ReadyEvent, HealthEvent;
            public int ParentPid, HealthSeconds = 20;
            public long ParentStart;
        }
        public sealed class Result
        {
            public bool Success;
            public string Message;
            public int HelperPid;
            public long HelperStart;
        }
        static readonly JavaScriptSerializer json = new JavaScriptSerializer();
        static string Item(string directory, string name) { return Path.Combine(directory, name); }
        static bool Same(string a, string b) { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        static void Write(string path, object data) { File.WriteAllText(path, json.Serialize(data)); }
        static string Quote(string value) { if (value.IndexOf('"') >= 0) throw new InvalidDataException("Invalid path"); return "\"" + value + "\""; }
        static Process Start(string exe, string arguments)
        {
            return Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(exe) });
        }
        internal static bool EventName(string name, string kind)
        {
            return name != null && Regex.IsMatch(name, @"^Local\\OrbitCalendar\.Update" + kind + @"\.[0-9a-f]{32}$");
        }
        public static Task Launch(string staged, AppUpdates.Release release)
        {
            return LaunchForProcess(staged, release, Assembly.GetExecutingAssembly().Location, Process.GetCurrentProcess());
        }
        internal static async Task LaunchForProcess(string staged, AppUpdates.Release release, string target, Process parent)
        {
            AppUpdates.ValidateRelease(release);
            string dir = Path.GetDirectoryName(staged);
            if (!AppUpdates.IsWorkDirectory(dir) || !Same(staged, Item(dir, "OrbitCalendar.new.exe"))) throw new InvalidDataException("更新ファイルの保存先を確認できませんでした");
            AppUpdates.Verify(staged, release.Sha256, release.Version);
            string id = Guid.NewGuid().ToString("N");
            var plan = new Plan { Target = Path.GetFullPath(target), Staged = staged, Sha256 = release.Sha256, Version = release.Version.ToString(), OriginalSha256 = AppUpdates.Hash(target), ParentPid = parent.Id, ParentStart = parent.StartTime.ToUniversalTime().Ticks, ReadyEvent = @"Local\OrbitCalendar.UpdateReady." + id, HealthEvent = @"Local\OrbitCalendar.UpdateHealthy." + id };
            string helper = Item(dir, "OrbitCalendar.Updater.exe"), path = Item(dir, "plan.json");
            File.Copy(Assembly.GetExecutingAssembly().Location, helper, true); Write(path, plan);
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, plan.ReadyEvent))
            using (var process = Start(helper, "--apply-update " + Quote(path)))
            {
                bool signaled = await Task.Run(delegate { return ready.WaitOne(TimeSpan.FromSeconds(20)); });
                if (!signaled)
                {
                    // The parent is still alive, so this helper cannot have begun replacement.
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                    throw new IOException("更新の準備が完了しませんでした。もう一度お試しください");
                }
                var result = json.Deserialize<Result>(File.ReadAllText(Item(dir, "ready.json")));
                if (!result.Success)
                {
                    await Task.Run(delegate { process.WaitForExit(5000); });
                    throw new IOException(result.Message);
                }
            }
        }
        static Process Validate(Plan plan, string dir)
        {
            if (plan == null || !EventName(plan.ReadyEvent, "Ready") || !EventName(plan.HealthEvent, "Healthy") || plan.HealthSeconds < 2 || plan.HealthSeconds > 30 || !Same(plan.Staged, Item(dir, "OrbitCalendar.new.exe")) || !Path.IsPathRooted(plan.Target) || Path.GetExtension(plan.Target).ToLowerInvariant() != ".exe" || Same(plan.Target, Assembly.GetExecutingAssembly().Location) || Same(plan.Target, plan.Staged)) throw new InvalidDataException("更新の指定を確認できませんでした");
            var parent = Process.GetProcessById(plan.ParentPid);
            try
            {
                if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStart || !Same(parent.MainModule.FileName, plan.Target) || AppUpdates.Hash(plan.Target) != plan.OriginalSha256) throw new InvalidDataException("起動中のアプリを確認できませんでした");
                AppUpdates.Verify(plan.Staged, plan.Sha256, new Version(plan.Version));
                return parent;
            }
            catch { parent.Dispose(); throw; }
        }
        static void Retry(Action action)
        {
            for (int i = 0; ; i++)
            {
                try { action(); return; }
                catch (IOException) { if (i >= 19) throw; Thread.Sleep(250); }
            }
        }
        public static int Run(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!AppUpdates.IsWorkDirectory(dir) || !Same(path, Item(dir, "plan.json"))) return 2;
            Plan plan = null; Process parent = null, child = null;
            bool ready = false, replaced = false;
            string next = null, backup = null;
            try
            {
                plan = json.Deserialize<Plan>(File.ReadAllText(path)); parent = Validate(plan, dir);
                string stem = Item(Path.GetDirectoryName(plan.Target), ".OrbitCalendar.update-" + Guid.NewGuid().ToString("N"));
                next = stem + ".new"; backup = stem + ".previous";
                File.Copy(plan.Staged, next, false); AppUpdates.Verify(next, plan.Sha256, new Version(plan.Version));
                Write(Item(dir, "ready.json"), new Result { Success = true });
                using (var signal = EventWaitHandle.OpenExisting(plan.ReadyEvent)) signal.Set();
                ready = true;
                if (!parent.WaitForExit(60000)) throw new IOException("アプリの終了を待てなかったため、更新を中止しました");
                if (AppUpdates.Hash(plan.Target) != plan.OriginalSha256) throw new IOException("更新中にアプリのファイルが変わりました");
                Retry(delegate { File.Replace(next, plan.Target, backup); }); replaced = true;
                WriteResult(dir, true, "v" + new Version(plan.Version).ToString(3) + " に更新しました");
                using (var healthy = new EventWaitHandle(false, EventResetMode.ManualReset, plan.HealthEvent))
                {
                    child = Start(plan.Target, "--after-update " + Quote(Item(dir, "result.json")) + " --update-health " + Quote(plan.HealthEvent));
                    var until = DateTime.UtcNow.AddSeconds(plan.HealthSeconds);
                    while (!healthy.WaitOne(100))
                        if (child.HasExited || DateTime.UtcNow >= until) throw new IOException("新しいアプリの起動を確認できませんでした");
                }
                // A cleanup failure must not roll back an already healthy application.
                try { File.Delete(backup); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return 0;
            }
            catch (Exception ex)
            {
                string message = "更新できませんでした。";
                if (replaced)
                {
                    try
                    {
                        if (child != null && !child.HasExited) { child.Kill(); child.WaitForExit(5000); }
                        Retry(delegate { File.Replace(backup, plan.Target, next); }); replaced = false;
                        message += "元のバージョンに戻しました。";
                    }
                    catch { message += "復元用ファイル: " + backup; }
                }
                message += "\n" + ex.Message;
                try
                {
                    if (!ready)
                    {
                        Write(Item(dir, "ready.json"), new Result { Success = false, Message = message });
                        if (plan != null && EventName(plan.ReadyEvent, "Ready")) using (var signal = EventWaitHandle.OpenExisting(plan.ReadyEvent)) signal.Set();
                    }
                    else
                    {
                        WriteResult(dir, false, message);
                        if (!replaced && parent != null && parent.HasExited) Start(plan.Target, "--after-update " + Quote(Item(dir, "result.json")));
                    }
                }
                catch { }
                return 1;
            }
            finally
            {
                if (parent != null) parent.Dispose(); if (child != null) child.Dispose();
                try { if (next != null) File.Delete(next); } catch { }
                // A backup is retained if restoration itself failed.
            }
        }
        static void WriteResult(string dir, bool success, string message)
        {
            using (var self = Process.GetCurrentProcess()) Write(Item(dir, "result.json"), new Result { Success = success, Message = message, HelperPid = self.Id, HelperStart = self.StartTime.ToUniversalTime().Ticks });
        }
        public static string Argument(string[] args, string key)
        {
            int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        public static void Acknowledge(string name)
        {
            if (!EventName(name, "Healthy")) return;
            try { using (var signal = EventWaitHandle.OpenExisting(name)) signal.Set(); } catch (WaitHandleCannotBeOpenedException) { }
        }
        public static Result ReadResult(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!AppUpdates.IsWorkDirectory(dir) || !Same(path, Item(dir, "result.json"))) return null;
                var result = json.Deserialize<Result>(File.ReadAllText(path));
                Task.Run(delegate
                {
                    try { using (var helper = Process.GetProcessById(result.HelperPid)) if (helper.StartTime.ToUniversalTime().Ticks == result.HelperStart) helper.WaitForExit(40000); } catch { }
                    AppUpdates.Cleanup(dir);
                });
                return result;
            }
            catch { return null; }
        }
    }
}
