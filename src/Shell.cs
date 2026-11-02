using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Color = System.Windows.Media.Color;

namespace OrbitCalendar
{
    internal sealed class Shell : IDisposable
    {
        readonly Forms.NotifyIcon tray;
        readonly Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
        readonly CalendarPanel panel;
        readonly Window clock;
        readonly TextBlock time = new TextBlock(), date = new TextBlock();
        readonly Stopwatch focusTimer = new Stopwatch();
        readonly DispatcherTimer tick = new DispatcherTimer();
        readonly Forms.ToolStripMenuItem clockOption;
        bool exiting;
        string lastClockDate = "", lastClockTime = "";

        public Shell()
        {
            panel = new CalendarPanel(Collapse, ToggleFocus);
            clock = CreateClock();
            tray = new Forms.NotifyIcon { Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                Text = "Orbit Calendar", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
            menu.Items.Add("カレンダーを開く／閉じる", null, delegate { Toggle(); });
            clockOption = new Forms.ToolStripMenuItem("ミニ時計を表示") { CheckOnClick = true, Checked = true };
            clockOption.CheckedChanged += delegate { if (!panel.IsVisible) ShowClock(); };
            menu.Items.Add(clockOption);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Orbit Calendar を終了", null, delegate { Application.Current.Shutdown(); });
            panel.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            { if (!exiting) { e.Cancel = true; Collapse(); } };
            clock.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            { if (!exiting) { e.Cancel = true; clockOption.Checked = false; } };
            tick.Interval = TimeSpan.FromSeconds(1);
            tick.Tick += delegate { Update(); };
            tick.Start();
            Update();
            ShowClock();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += PreferencesChanged;
        }

        internal static void StyleWindow(Window w)
        {
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.AllowsTransparency = true;
            w.Background = System.Windows.Media.Brushes.Transparent;
            w.ShowInTaskbar = false;
            w.Topmost = true;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
        }

        internal static void Position(Window window, double width, double height)
        {
            var area = SystemParameters.WorkArea;
            double fit = Math.Min(1, Math.Min((area.Width - 20) / width, (area.Height - 20) / height));
            window.Width = width * fit;
            window.Height = height * fit;
            window.Left = area.Right - window.Width - 10;
            window.Top = area.Bottom - window.Height - 9;
        }

        Window CreateClock()
        {
            var window = new Window { Title = "Orbit Calendar ミニ時計", ShowActivated = false };
            StyleWindow(window);
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center };
            var indicator = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Art.Ink("#D3F394"), Margin = new Thickness(0, 0, 9, 0) };
            row.Children.Add(indicator);
            date.FontFamily = new System.Windows.Media.FontFamily("Consolas");
            date.FontSize = 11;
            date.Foreground = Art.Ink("#BECAB3");
            date.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(date);
            time.FontFamily = new System.Windows.Media.FontFamily("Bahnschrift");
            time.FontSize = 18;
            time.Margin = new Thickness(11, 0, 0, 0);
            time.VerticalAlignment = VerticalAlignment.Center;
            time.Foreground = Art.Ink("#F0F2DE");
            row.Children.Add(time);
            var casing = new Border { Background = Art.Ink("#1F2B24"), BorderBrush = Art.Ink("#596951"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = row,
                Cursor = Cursors.Hand, ToolTip = "カレンダーを開く · 右クリックでメニュー", Focusable = true };
            casing.Focusable = false;
            var open = new Button { Content = casing, Cursor = Cursors.Hand };
            open.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><ContentPresenter/></ControlTemplate>");
            System.Windows.Automation.AutomationProperties.SetName(open, "カレンダーを開く");
            open.Click += delegate { Open(); };
            open.MouseRightButtonUp += delegate { menu.Show(Forms.Cursor.Position); };
            window.Content = open;
            return window;
        }

        void DisplayChanged(object sender, EventArgs e) { Reposition(); }
        void PreferencesChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) { Reposition(); }
        void Reposition()
        {
            clock.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (panel.IsVisible) Position(panel, 458, 542);
                if (clock.IsVisible) Position(clock, 164, 34);
            }));
        }

        void ShowClock()
        {
            if (!clockOption.Checked) { clock.Hide(); return; }
            Position(clock, 164, 34);
            clock.Show();
        }

        public void Open() { clock.Hide(); panel.Open(); }
        void Collapse() { panel.Hide(); ShowClock(); }
        void Toggle() { if (panel.IsVisible) Collapse(); else Open(); }

        void ToggleFocus()
        {
            if (focusTimer.IsRunning) focusTimer.Reset();
            else focusTimer.Restart();
            Update();
        }

        void Update()
        {
            var now = DateTime.Now;
            string focus = "";
            if (focusTimer.IsRunning)
            {
                var remaining = TimeSpan.FromMinutes(25) - focusTimer.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    focusTimer.Reset();
                    tray.ShowBalloonTip(6000, "Orbit Calendar", "集中タイムが終了しました。お疲れさまでした。", Forms.ToolTipIcon.Info);
                    System.Media.SystemSounds.Asterisk.Play();
                }
                else focus = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)).ToString(@"mm\:ss");
            }
            string newDate = now.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            string newTime = now.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (lastClockDate != newDate || lastClockTime != newTime)
            {
                date.Text = newDate;
                time.Text = newTime;
                lastClockDate = newDate;
                lastClockTime = newTime;
            }
            string tooltip = "Orbit Calendar  " + newDate + "  " + newTime + (focus.Length == 0 ? "" : "\n集中  " + focus);
            if (tray.Text != tooltip) tray.Text = tooltip;
            panel.Tick(now, focus);
        }

        public void Dispose()
        {
            exiting = true;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= PreferencesChanged;
            tick.Stop();
            tray.Visible = false;
            tray.Icon.Dispose();
            tray.Dispose();
            menu.Dispose();
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool primary;
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\OrbitCalendar.Open.4cf34b7f", out primary))
            {
                if (!primary) { signal.Set(); return; }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                using (var shell = new Shell())
                {
                    var waiter = ThreadPool.RegisterWaitForSingleObject(signal, delegate
                    { app.Dispatcher.BeginInvoke(new Action(shell.Open)); }, null, Timeout.Infinite, false);
                    try { app.Run(); }
                    finally { waiter.Unregister(null); }
                }
            }
        }
    }
}
