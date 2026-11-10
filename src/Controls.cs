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
        public static int Pattern, Motion = 6, Minutes = 30, LastTab, LoadingStyle;
        public static int AnimationSpeed = 1;
        /// <summary>Bit i set = tab i (1 株価, 2 天気, 3 News, 4 運行情報, 5 ToDo) is hidden. The calendar is always shown.</summary>
        public static int HiddenTabs;
        public static bool TabVisible(int tab) { return tab == 0 || (HiddenTabs & (1 << tab)) == 0; }
        public static double AnimationScale { get { return AnimationSpeed == 0 ? .5 : AnimationSpeed == 2 ? 1.4 : 1; } }
        static Preferences() { Load(); }
        internal static void Load()
        {
            Pattern = LastTab = LoadingStyle = 0; Motion = 6; Minutes = 30;
            AnimationSpeed = 1; HiddenTabs = 0;
            try
            {
                var parts = File.ReadAllLines(path);
                int value;
                if (parts.Length > 0 && int.TryParse(parts[0], out value) && value >= 0 && value < 4) Pattern = value;
                // Removed legacy effects migrate to attraction; retain its old ID.
                if (parts.Length > 1 && int.TryParse(parts[1], out value) && value >= 6 && value <= 8) Motion = value;
                if (parts.Length > 2 && int.TryParse(parts[2], out value) && value >= 1 && value <= 180) Minutes = value;
                if (parts.Length > 3 && int.TryParse(parts[3], out value) && value >= 0 && value <= 5) LastTab = value;
                if (parts.Length > 4 && int.TryParse(parts[4], out value) && value >= 0 && value < 6) LoadingStyle = value;
                if (parts.Length > 5 && int.TryParse(parts[5], out value) && value >= 0 && value < 3) AnimationSpeed = value;
                if (parts.Length > 6 && int.TryParse(parts[6], out value) && value >= 0 && value < 64) HiddenTabs = value & 62;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public static void Save()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllLines(path, new[] { Pattern.ToString(), Motion.ToString(), Minutes.ToString(), LastTab.ToString(), LoadingStyle.ToString(), AnimationSpeed.ToString(), HiddenTabs.ToString() }); }
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
            InitializeAnimationSpeed();
            InitializeStartupControl();
            InitializeUpdates();
            outside = new OutsideClick(delegate { if (IsVisible && !IsClosing && Get<Border>("TodoEditor").Visibility != Visibility.Visible && Get<Border>("TimetableEditor").Visibility != Visibility.Visible) collapse(); });
            IsVisibleChanged += delegate { outside.Enabled = IsVisible; };
            Closed += delegate { outside.Dispose(); };
            Get<Button>("TimerDuration").Click += delegate
            {
                CloseMarketSettings();
                CancelPlaceSelection();
                var picker = Get<Border>("TimerPicker");
                Get<Border>("SettingsPicker").Visibility = Visibility.Collapsed; Get<Border>("SpeedPicker").Visibility = Visibility.Collapsed;
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
            Get<Button>("ReplayPreview").Click += delegate { ReplaySettingPreview(); };
            Get<Button>("SettingsTransition").Click += delegate { showLoadingSettings = false; showTabSettings = false; UpdateSettingsSection(); effect.StartArea(PanelBounds(Get<Border>("SettingsPicker"))); };
            Get<Button>("SettingsLoading").Click += delegate { showLoadingSettings = true; showTabSettings = false; UpdateSettingsSection(); effect.StartArea(PanelBounds(Get<Border>("SettingsPicker"))); };
            Get<Button>("SettingsTabs").Click += delegate { showLoadingSettings = false; showTabSettings = true; UpdateSettingsSection(); effect.StartArea(PanelBounds(Get<Border>("SettingsPicker"))); };
            root.PreviewMouseDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var target = e.OriginalSource as DependencyObject;
                if (target == null) return;
                foreach (var name in new[] { "TimerPicker", "SettingsPicker" })
                {
                    var picker = Get<Border>(name);
                    var opener = Get<Button>(name == "TimerPicker" ? "TimerDuration" : "SettingsButton");
                    if (picker.Visibility == Visibility.Visible && target != opener &&
                        !picker.IsAncestorOf(target) && !opener.IsAncestorOf(target) &&
                        !(name == "SettingsPicker" && Get<Border>("SpeedPicker").IsAncestorOf(target))) ClosePanel(name);
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
                UpdateSettingsSection();
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
            if (name == "SettingsPicker") Get<Border>("SpeedPicker").Visibility = Visibility.Collapsed;
            panel.Visibility = Visibility.Collapsed;
            effect.StartArea(bounds);
        }

        void UpdateSettingChoices()
        {
            UpdateSpeedChoices();
            UpdateStartupButton();
            FillChoices("PatternChoices", new[] { "ドット", "グリッド", "ストライプ", "きらめき" }, Preferences.Pattern, delegate(int value)
            {
                Preferences.Pattern = value;
                PreviewSetting();
            });
            FillChoices("MotionChoices", new[] { "吸い寄せ", "流れ込み", "奥行き" },
                Preferences.Motion - 6, delegate(int value)
            {
                Preferences.Motion = value + 6;
                PreviewSetting();
            });
            FillTabChoices();
            FillChoices("LoadingChoices", new[] { "6本バー", "円形スピナー", "軌道リング", "ドットキューブ", "タイル組み替え", "ドットウェーブ" }, Preferences.LoadingStyle, delegate(int value)
            {
                Preferences.LoadingStyle = value;
                PreviewSetting();
            });
        }

        bool showLoadingSettings, showTabSettings;
        void UpdateSettingsSection()
        {
            Get<Grid>("SettingsSection").Height = showLoadingSettings ? 256 : 180;
            Get<StackPanel>("TransitionSettings").Visibility = showLoadingSettings || showTabSettings ? Visibility.Collapsed : Visibility.Visible;
            Get<StackPanel>("LoadingSettings").Visibility = showLoadingSettings ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("TabSettings").Visibility = showTabSettings ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("SettingsTransition").Background = Art.Ink(showLoadingSettings || showTabSettings ? "#193B48" : "#287181");
            Get<Button>("SettingsLoading").Background = Art.Ink(showLoadingSettings ? "#287181" : "#193B48");
            Get<Button>("SettingsTabs").Background = Art.Ink(showTabSettings ? "#287181" : "#193B48");
            Get<Button>("ReplayPreview").Visibility = showTabSettings ? Visibility.Hidden : Visibility.Visible;
            if (showLoadingSettings) { settingsLoadingParticles.Restart(); previewLoadingClock.Restart(); }
        }

        void ReplaySettingPreview()
        {
            if (showLoadingSettings) { settingsLoadingParticles.Phase = 0; settingsLoadingParticles.InvalidateVisual(); previewLoadingClock.Restart(); }
            effect.StartArea(PanelBounds(Get<Border>(showLoadingSettings ? "LoadingPreviewArea" : "PreviewArea")), true);
        }

        static readonly int[] tabOrder = { 5, 1, 2, 3, 4 };
        static readonly string[] tabNames = { "カレンダー", "株価", "天気", "News", "運行情報", "ToDo" };
        void FillTabChoices()
        {
            var grid = Get<System.Windows.Controls.Primitives.UniformGrid>("TabChoices");
            grid.Children.Clear();
            foreach (int tab in tabOrder)
            {
                int value = tab;
                bool shown = Preferences.TabVisible(tab);
                var button = new Button { Content = (shown ? "✓  " : "") + tabNames[tab], Margin = new Thickness(3), Height = 32, FontSize = 12,
                    Background = Art.Ink(shown ? "#287181" : "#193B48"), Foreground = Art.Ink(shown ? "#E5FFFF" : "#8AA9B0"),
                    ToolTip = shown ? tabNames[tab] + "を非表示にする" : tabNames[tab] + "を表示する" };
                System.Windows.Automation.AutomationProperties.SetName(button, tabNames[tab] + (shown ? " 表示中" : " 非表示"));
                button.Click += delegate { SetTabVisible(value, !Preferences.TabVisible(value)); };
                grid.Children.Add(button);
            }
        }

        void SetTabVisible(int tab, bool visible)
        {
            if (visible) Preferences.HiddenTabs &= ~(1 << tab); else Preferences.HiddenTabs |= 1 << tab;
            Preferences.Save();
            ApplyTabVisibility();
            var picker = Get<Border>("SettingsPicker");
            if (!visible && mode == tab)
            {
                // The open tab was turned off: go back to the calendar and keep the settings in view.
                SwitchView(0);
                picker.Visibility = Visibility.Visible;
            }
            FillTabChoices();
            Get<TextBlock>("TabSettingsHint").Text = tab == TodoMode && !visible ? "ToDoを非表示にすると、カレンダーの期限の印も消えます（タスクは保存されたままです）" : "押すと表示／非表示を切り替えます。カレンダーは常に表示されます";
            UpdateLayout();
            effect.Start(true);
        }

        void ApplyTabVisibility()
        {
            for (int i = 1; i < tabs.Length; i++) tabs[i].Visibility = Preferences.TabVisible(i) ? Visibility.Visible : Visibility.Collapsed;
            DrawDates();
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
            ReplaySettingPreview();
        }
    }
}
