using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OrbitCalendar
{
    internal static class Preferences
    {
        static readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitCalendar", "display.txt");
        public static int Pattern, Motion, Minutes = 30;
        static Preferences()
        {
            try
            {
                var parts = File.ReadAllLines(path);
                int value;
                if (parts.Length > 0 && int.TryParse(parts[0], out value) && value >= 0 && value < 4) Pattern = value;
                if (parts.Length > 1 && int.TryParse(parts[1], out value) && value >= 0 && value < 6) Motion = value;
                if (parts.Length > 2 && int.TryParse(parts[2], out value) && value >= 1 && value <= 180) Minutes = value;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public static void Save()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllLines(path, new[] { Pattern.ToString(), Motion.ToString(), Minutes.ToString() }); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal partial class CalendarPanel
    {
        public int TimerMinutes { get { return Preferences.Minutes; } }
        OutsideClick outside;
        void InitializeControls()
        {
            outside = new OutsideClick(delegate { if (IsVisible && !IsClosing) collapse(); });
            IsVisibleChanged += delegate { outside.Enabled = IsVisible; };
            Closed += delegate { outside.Dispose(); };
            Get<Button>("TimerDuration").Click += delegate
            {
                var menu = new ContextMenu();
                foreach (int minutes in new[] { 1, 5, 10, 15, 20, 25, 30, 45, 60, 90, 120, 180 })
                {
                    int duration = minutes;
                    var item = new MenuItem { Header = minutes + " 分", IsCheckable = true, IsChecked = minutes == TimerMinutes };
                    item.Click += delegate { Preferences.Minutes = duration; Preferences.Save(); Get<Button>("TimerDuration").Content = duration + " 分"; effect.Refresh(); };
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = Get<Button>("TimerDuration");
                menu.IsOpen = true;
            };
            Get<Button>("SettingsButton").Click += delegate
            {
                var menu = new ContextMenu();
                var pattern = new MenuItem { Header = "粒子の見た目" };
                string[] looks = { "ドット", "グリッド", "ストライプ", "きらめき" };
                for (int i = 0; i < looks.Length; i++)
                {
                    int value = i;
                    var item = new MenuItem { Header = looks[i], IsCheckable = true, IsChecked = Preferences.Pattern == i };
                    item.Click += delegate { Preferences.Pattern = value; Preferences.Save(); effect.Refresh(); };
                    pattern.Items.Add(item);
                }
                var motion = new MenuItem { Header = "組み上がり方" };
                string[] moves = { "自動で切り替え", "球体", "渦巻き", "降り注ぐ", "爆発と収束", "ウェーブ" };
                for (int i = 0; i < moves.Length; i++)
                {
                    int value = i;
                    var item = new MenuItem { Header = moves[i], IsCheckable = true, IsChecked = Preferences.Motion == i };
                    item.Click += delegate { Preferences.Motion = value; Preferences.Save(); effect.Start(false); };
                    motion.Items.Add(item);
                }
                menu.Items.Add(pattern); menu.Items.Add(motion);
                menu.PlacementTarget = Get<Button>("SettingsButton");
                menu.IsOpen = true;
            };
        }
    }
}
