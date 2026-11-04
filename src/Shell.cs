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
        readonly ParticleTimer countdown;

        public Shell() : this(true) { }

        internal Shell(bool integrateDesktop)
        {
            panel = new CalendarPanel(Collapse, ToggleFocus);
            countdown = new ParticleTimer(Open);
            tray = new Forms.NotifyIcon { Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                Text = "Orbit Calendar", ContextMenuStrip = menu, Visible = integrateDesktop };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
            menu.Items.Add("ウィンドウを開く／閉じる", null, delegate { Toggle(); });
            var startup = new Forms.ToolStripMenuItem("Windowsへのサインイン時に起動");
            startup.Click += delegate
            {
                try { StartupRegistration.SetEnabled(!StartupRegistration.Enabled); startup.Checked = StartupRegistration.Enabled; }
                catch (Exception ex) { if (!(ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)) throw; tray.ShowBalloonTip(4000, "Orbit Calendar", "自動起動を保存できませんでした。もう一度お試しください", Forms.ToolTipIcon.Warning); }
            };
            menu.Items.Add(startup); menu.Opening += delegate { startup.Checked = StartupRegistration.Enabled; };
            menu.Items.Add("更新を確認…", null, delegate { Open(); panel.OpenUpdates(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Orbit Calendar を終了", null, delegate { Application.Current.Shutdown(); });
            panel.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            { if (!exiting) { e.Cancel = true; Collapse(); } };
            tick.Interval = TimeSpan.FromSeconds(1);
            tick.Tick += delegate { if (integrateDesktop) Holidays.Check(DateTime.UtcNow); Update(); };
            tick.Start();
            Update();
            if (integrateDesktop) clock = new ClockHost(Toggle, menu);
            panel.IsVisibleChanged += delegate { if (!exiting && !panel.IsVisible) Update(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += PreferencesChanged;
            if (integrateDesktop) panel.StartUpdateChecks();
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

        public void Open() { countdown.Hide(); panel.Open(); Update(); }
        internal void ShowUpdateResult(UpdateInstaller.Result result) { Open(); panel.ShowUpdateResult(result); }
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
            double progress = focusTimer.IsRunning && focusLength.TotalSeconds > 0 ? focusTimer.Elapsed.TotalSeconds / focusLength.TotalSeconds : 0;
            panel.UpdateTimerProgress(progress, focusTimer.IsRunning);
            if (focus.Length > 0 && !panel.IsVisible)
            {
                countdown.Update(focus, progress);
                Position(countdown, countdown.PreferredWidth, 52);
                if (!countdown.IsVisible) countdown.Show();
            }
            else countdown.Hide();
        }

        public void Dispose()
        {
            exiting = true;
            panel.StopUpdateChecks();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= PreferencesChanged;
            tick.Stop();
            countdown.Close();
            if (clock != null) clock.Dispose();
            tray.Visible = false;
            tray.Icon.Dispose();
            tray.Dispose();
            menu.Dispose();
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string plan = UpdateInstaller.Argument(args, "--apply-update");
            if (plan != null) { Environment.ExitCode = UpdateInstaller.Run(plan); return; }
            bool primary;
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\OrbitCalendar.Open.4cf34b7f", out primary))
            {
                if (!primary) { if (Array.IndexOf(args, "--autostart") < 0) signal.Set(); return; }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                using (var shell = new Shell())
                {
                    app.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        string resultPath = UpdateInstaller.Argument(args, "--after-update");
                        if (resultPath != null)
                        {
                            var result = UpdateInstaller.ReadResult(resultPath);
                            if (result != null) shell.ShowUpdateResult(result);
                        }
                        UpdateInstaller.Acknowledge(UpdateInstaller.Argument(args, "--update-health"));
                    }), DispatcherPriority.ApplicationIdle);
                    var waiter = ThreadPool.RegisterWaitForSingleObject(signal, delegate
                    { app.Dispatcher.BeginInvoke(new Action(shell.Open)); }, null, Timeout.Infinite, false);
                    try { app.Run(); }
                    finally { waiter.Unregister(null); }
                }
            }
        }
    }
}
