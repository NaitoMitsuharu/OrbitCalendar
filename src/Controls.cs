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
        public static int Pattern, Motion = 6, Minutes = 30;
        static Preferences()
        {
            try
            {
                var parts = File.ReadAllLines(path);
                int value;
                if (parts.Length > 0 && int.TryParse(parts[0], out value) && value >= 0 && value < 4) Pattern = value;
                if (parts.Length > 1 && int.TryParse(parts[1], out value) && value >= 0 && value < 7) Motion = value == 0 ? 6 : value;
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
                var picker = Get<Border>("TimerPicker");
                picker.Visibility = picker.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                if (picker.Visibility == Visibility.Visible)
                {
                    var grid = Get<System.Windows.Controls.Primitives.UniformGrid>("TimeChoices");
                    grid.Children.Clear();
                    foreach (int minutes in new[] { 1, 5, 10, 15, 20, 25, 30, 45, 60, 90, 120, 180 })
                    {
                        int duration = minutes;
                        var button = new Button { Content = minutes + " 分", Margin = new Thickness(4), Height = 38,
                            Background = Art.Ink(minutes == TimerMinutes ? "#287181" : "#193B48"),
                            Foreground = Art.Ink(minutes == TimerMinutes ? "#E5FFFF" : "#B8D9DE") };
                        button.Click += delegate
                        {
                            Preferences.Minutes = duration;
                            Preferences.Save();
                            Get<Button>("TimerDuration").Content = duration + " 分";
                            picker.Visibility = Visibility.Collapsed;
                            effect.Refresh();
                        };
                        grid.Children.Add(button);
                    }
                }
                effect.Refresh();
            };
            Get<Button>("CloseTimePicker").Click += delegate { Get<Border>("TimerPicker").Visibility = Visibility.Collapsed; effect.Refresh(); };
            root.PreviewMouseDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var picker = Get<Border>("TimerPicker");
                var target = e.OriginalSource as DependencyObject;
                if (picker.Visibility == Visibility.Visible && target != null &&
                    !picker.IsAncestorOf(target) && !Get<Button>("TimerDuration").IsAncestorOf(target))
                { picker.Visibility = Visibility.Collapsed; effect.Refresh(); }
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
                string[] moves = { "自動で切り替え", "球体", "渦巻き", "降り注ぐ", "爆発と収束", "ウェーブ", "吸い寄せ（標準）" };
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
