using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

// A WPF-owned native parent reproduces the taskbar clock's lack of a managed
// WinForms parent. It is safe to click without touching Explorer or user settings.
internal static class ClockMenuFixture
{
    [STAThread]
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClockMenuFixture.error.txt"), ex.ToString()); Environment.ExitCode = 1; }
    }
    static void Run(string[] args)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        string folder = Path.Combine(Path.GetTempPath(), "orbit-clock-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var pair in new[] { new[] { "Preferences", "display.txt" }, new[] { "SavedPlace", "location.txt" }, new[] { "Watchlist", "watchlist.json" }, new[] { "MapScale", "map-zoom.txt" } })
            assembly.GetType("OrbitCalendar." + pair[0]).GetField("path", sf).SetValue(null, Path.Combine(folder, pair[1]));
        var prefs = assembly.GetType("OrbitCalendar.Preferences"); prefs.GetField("LastTab", sf).SetValue(null, 0);
        var place = assembly.GetType("OrbitCalendar.SavedPlace"); place.GetMethod("Load", sf).Invoke(null, null);
        place.GetMethod("Add", sf).Invoke(null, new object[] { new System.Device.Location.GeoCoordinate(35.4547, 139.6024), "天王町駅", 9 });
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var shellType = assembly.GetType("OrbitCalendar.Shell");
        using (var shell = (IDisposable)Activator.CreateInstance(shellType, instance, null, new object[] { false }, null))
        {
            var panel = (Window)shellType.GetField("panel", instance).GetValue(shell);
            panel.Title = "Orbit Calendar · 手動更新の動作確認";
            var menu = (Forms.ContextMenuStrip)shellType.GetField("menu", instance).GetValue(shell);
            string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClockMenuFixture.events.txt");
            menu.Opened += delegate { File.AppendAllText(log, "opened\r\n"); };
            menu.ItemClicked += delegate(object sender, Forms.ToolStripItemClickedEventArgs e) { File.AppendAllText(log, "clicked: " + e.ClickedItem.Text + "\r\n"); };
            menu.Closed += delegate(object sender, Forms.ToolStripDropDownClosedEventArgs e) { File.AppendAllText(log, "closed: " + e.CloseReason + "\r\n"); };
            panel.IsVisibleChanged += delegate { File.AppendAllText(log, "panel visible: " + panel.IsVisible + "\r\n"); };
            var host = new Window { Title = "Orbit Calendar · 時計右クリック検証", Width = 460, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(11, 25, 35)) };
            var body = new StackPanel { Margin = new Thickness(24) };
            body.Children.Add(new TextBlock { Text = "下の日時を右クリックしてください", FontSize = 19, Foreground = Brushes.White });
            body.Children.Add(new TextBlock { Text = "タスクバーと同じ時計コントロールです", Margin = new Thickness(0, 8, 0, 12), Foreground = Brushes.LightSeaGreen });
            body.Children.Add(new TextBlock { Text = "22:45\n2026/11/04", FontSize = 18, Foreground = Brushes.White }); host.Content = body;
            Forms.Control clock = null;
            host.SourceInitialized += delegate
            {
                Action open = delegate { shellType.GetMethod("Open").Invoke(shell, null); };
                clock = (Forms.Control)Activator.CreateInstance(assembly.GetType("OrbitCalendar.TaskbarClock"), instance, null, new object[] { new WindowInteropHelper(host).Handle, open, menu }, null);
                var handle = clock.Handle; clock.Bounds = new System.Drawing.Rectangle(20, 83, 200, 80);
            };
            host.Closed += delegate { if (clock != null) clock.Dispose(); };
            if (Array.IndexOf(args, "--verify") >= 0) host.Loaded += async delegate
            {
                try
                {
                    clock.Capture = true;
                    var message = Forms.Message.Create(clock.Handle, 0x205, IntPtr.Zero, IntPtr.Zero);
                    var parameters = new object[] { message };
                    clock.GetType().GetMethod("WndProc", instance).Invoke(clock, parameters);
                    await Task.Delay(100);
                    if (!menu.Visible || clock.Capture) throw new Exception("Clock popup missing or clock still holds mouse capture");
                    var check = menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == "アプリの更新を確認…");
                    check.PerformClick();
                    var root = (Grid)((System.Windows.Controls.Viewbox)panel.Content).Child;
                    var deadline = DateTime.UtcNow.AddSeconds(30);
                    while (panel.GetType().GetField("updateRequest", instance).GetValue(panel) != null)
                    {
                        if (DateTime.UtcNow > deadline) throw new Exception("Manual update check timeout");
                        await Task.Delay(50);
                    }
                    if (!panel.IsVisible || ((Border)root.FindName("UpdatePicker")).Visibility != Visibility.Visible) throw new Exception("Manual update page not shown");
                    string status = ((TextBlock)root.FindName("UpdateStatus")).Text;
                    if (status != "最新版を使用しています") throw new Exception("Unexpected update status: " + status);
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClockMenuFixture.pass.txt"), "PASS: embedded-clock right click, capture release, app update menu command, update screen and live version check");
                    app.Shutdown(0);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClockMenuFixture.error.txt"), ex.ToString()); app.Shutdown(1);
                }
            };
            Console.WriteLine(folder);
            app.Run(host);
        }
    }
}
