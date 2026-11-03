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
        public static int Pattern, Motion = 6, Minutes = 30, LastTab;
        static Preferences()
        {
            try
            {
                var parts = File.ReadAllLines(path);
                int value;
                if (parts.Length > 0 && int.TryParse(parts[0], out value) && value >= 0 && value < 4) Pattern = value;
                if (parts.Length > 1 && int.TryParse(parts[1], out value) && value >= 0 && value < 7) Motion = value;
                if (parts.Length > 2 && int.TryParse(parts[2], out value) && value >= 1 && value <= 180) Minutes = value;
                if (parts.Length > 3 && int.TryParse(parts[3], out value) && value >= 0 && value <= 4) LastTab = value;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public static void Save()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllLines(path, new[] { Pattern.ToString(), Motion.ToString(), Minutes.ToString(), LastTab.ToString() }); }
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
                CloseMarketSettings();
                CancelPlaceSelection();
                var picker = Get<Border>("TimerPicker");
                Get<Border>("SettingsPicker").Visibility = Visibility.Collapsed;
                bool opening = picker.Visibility != Visibility.Visible;
                Rect bounds = PanelBounds(picker);
                picker.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
                if (picker.Visibility == Visibility.Visible)
                {
                    Get<TextBlock>("TimePickerHint").Text = timerRunning ? "選んだ時間は次回のタイマーに使います" : "時間を選び、▶ でスタート";
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
                            if (!timerRunning) Get<Button>("TimerDuration").Content = duration + " 分";
                            Rect closingBounds = PanelBounds(picker);
                            picker.Visibility = Visibility.Collapsed;
                            effect.StartArea(closingBounds);
                        };
                        grid.Children.Add(button);
                    }
                }
                effect.StartArea(opening ? PanelBounds(picker) : bounds);
            };
            Get<Button>("CloseTimePicker").Click += delegate { ClosePanel("TimerPicker"); };
            Get<Button>("CloseSettingsPicker").Click += delegate { ClosePanel("SettingsPicker"); };
            Get<Button>("ReplayPreview").Click += delegate { effect.StartArea(PanelBounds(Get<Border>("PreviewArea")), true); };
            root.PreviewMouseDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var target = e.OriginalSource as DependencyObject;
                if (target == null) return;
                foreach (var name in new[] { "TimerPicker", "SettingsPicker" })
                {
                    var picker = Get<Border>(name);
                    var opener = Get<Button>(name == "TimerPicker" ? "TimerDuration" : "SettingsButton");
                    if (picker.Visibility == Visibility.Visible && target != opener &&
                        !picker.IsAncestorOf(target) && !opener.IsAncestorOf(target)) ClosePanel(name);
                }
            };
            Get<Button>("SettingsButton").Click += delegate
            {
                if (Get<Border>("MarketPicker").Visibility == Visibility.Visible) { CloseMarketSettings(); return; }
                CancelPlaceSelection();
                Get<Border>("TimerPicker").Visibility = Visibility.Collapsed;
                var picker = Get<Border>("SettingsPicker");
                if (picker.Visibility == Visibility.Visible) { ClosePanel("SettingsPicker"); return; }
                picker.Visibility = Visibility.Visible;
                Get<TextBlock>("PreviewTitle").Text = "W" + IsoWeek(selected).ToString("00") + "." + IsoDay(selected);
                UpdateSettingChoices();
                effect.StartArea(PanelBounds(picker));
            };
        }

        Rect PanelBounds(FrameworkElement panel)
        {
            UpdateLayout();
            return panel.TransformToAncestor(Get<Grid>("Face")).TransformBounds(new Rect(panel.RenderSize));
        }

        void ClosePanel(string name)
        {
            var panel = Get<Border>(name);
            if (panel.Visibility != Visibility.Visible) return;
            Rect bounds = PanelBounds(panel);
            panel.Visibility = Visibility.Collapsed;
            effect.StartArea(bounds);
        }

        void UpdateSettingChoices()
        {
            FillChoices("PatternChoices", new[] { "ドット", "グリッド", "ストライプ", "きらめき" }, Preferences.Pattern, delegate(int value)
            {
                Preferences.Pattern = value;
                PreviewSetting();
            });
            FillChoices("MotionChoices", new[] { "自動", "球体", "渦巻き", "降り注ぐ", "爆発と収束", "ウェーブ", "吸い寄せ" },
                Preferences.Motion, delegate(int value)
            {
                Preferences.Motion = value;
                PreviewSetting();
            });
        }

        void FillChoices(string name, string[] names, int selectedValue, Action<int> choose)
        {
            var grid = Get<System.Windows.Controls.Primitives.UniformGrid>(name);
            grid.Children.Clear();
            for (int i = 0; i < names.Length; i++)
            {
                int value = i;
                var button = new Button { Content = names[i], Margin = new Thickness(3), Height = 32, FontSize = 12,
                    Background = Art.Ink(i == selectedValue ? "#287181" : "#193B48"),
                    Foreground = Art.Ink(i == selectedValue ? "#E5FFFF" : "#B8D9DE") };
                button.Click += delegate { choose(value); };
                grid.Children.Add(button);
            }
        }

        void PreviewSetting()
        {
            Preferences.Save();
            UpdateSettingChoices();
            effect.StartArea(PanelBounds(Get<Border>("PreviewArea")), true);
        }
    }
}
