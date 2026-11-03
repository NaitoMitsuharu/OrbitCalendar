using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace OrbitCalendar
{
    internal sealed class Shell : IDisposable
    {
        readonly Forms.NotifyIcon tray;
        readonly Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
        readonly CalendarPanel panel;
        readonly ClockHost clock;
        readonly Stopwatch focusTimer = new Stopwatch();
        readonly DispatcherTimer tick = new DispatcherTimer();
        bool exiting;
        TimeSpan focusLength;
        readonly Window countdown = new Window { Title = "Orbit Timer", ShowActivated = false };
        readonly System.Windows.Controls.Button remainingLabel = new System.Windows.Controls.Button();

        public Shell()
        {
            panel = new CalendarPanel(Collapse, ToggleFocus);
            StyleWindow(countdown);
            remainingLabel.Background = Art.Ink("#102835");
            remainingLabel.Foreground = Art.Ink("#7BE5DA");
            remainingLabel.FontSize = 21;
            remainingLabel.FontFamily = new System.Windows.Media.FontFamily("Bahnschrift");
            remainingLabel.BorderBrush = Art.Ink("#36545F");
            remainingLabel.ToolTip = "クリックしてカレンダーを開く";
            remainingLabel.Click += delegate { Open(); };
            countdown.Content = remainingLabel;
            tray = new Forms.NotifyIcon { Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                Text = "Orbit Calendar", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
            menu.Items.Add("カレンダーを開く／閉じる", null, delegate { Toggle(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Orbit Calendar を終了", null, delegate { Application.Current.Shutdown(); });
            panel.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            { if (!exiting) { e.Cancel = true; Collapse(); } };
            tick.Interval = TimeSpan.FromSeconds(1);
            tick.Tick += delegate { Holidays.Check(DateTime.UtcNow); Update(); };
            tick.Start();
            Update();
            clock = new ClockHost(Toggle, menu);
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

        void DisplayChanged(object sender, EventArgs e) { Reposition(); }
        void PreferencesChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) { Reposition(); }
        void Reposition()
        {
            panel.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (panel.IsVisible) Position(panel, 458, panel.PreferredHeight);
            }));
        }

        public void Open() { countdown.Hide(); panel.Open(); }
        void Collapse() { panel.Collapse(); }
        void Toggle() { if (panel.IsVisible && !panel.IsClosing) Collapse(); else Open(); }

        void ToggleFocus()
        {
            if (focusTimer.IsRunning) focusTimer.Reset();
            else { focusLength = TimeSpan.FromMinutes(panel.TimerMinutes); focusTimer.Restart(); }
            Update();
        }

        void Update()
        {
            var now = DateTime.Now;
            string focus = "";
            if (focusTimer.IsRunning)
            {
                var remaining = focusLength - focusTimer.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    focusTimer.Reset();
                    tray.ShowBalloonTip(6000, "Orbit Calendar", "集中タイムが終了しました。お疲れさまでした。", Forms.ToolTipIcon.Info);
                    System.Media.SystemSounds.Asterisk.Play();
                }
                else
                {
                    var rounded = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds));
                    focus = ((int)rounded.TotalMinutes).ToString("00") + ":" + rounded.Seconds.ToString("00");
                }
            }
            string newDate = now.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            string newTime = now.ToString("HH:mm", CultureInfo.InvariantCulture);
            string tooltip = "Orbit Calendar  " + newDate + "  " + newTime + (focus.Length == 0 ? "" : "\n集中  " + focus);
            if (tray.Text != tooltip) tray.Text = tooltip;
            panel.Tick(now, focus);
            if (focus.Length > 0 && !panel.IsVisible)
            {
                remainingLabel.Content = "◷  " + focus;
                Position(countdown, 140, 44);
                if (!countdown.IsVisible) countdown.Show();
            }
            else countdown.Hide();
        }

        public void Dispose()
        {
            exiting = true;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= PreferencesChanged;
            tick.Stop();
            countdown.Close();
            clock.Dispose();
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
