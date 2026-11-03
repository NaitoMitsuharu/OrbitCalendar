using System;
using System.Windows;
using System.Windows.Controls;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        static readonly string[] speedNames = { "速い", "標準", "ゆっくり" };
        void InitializeAnimationSpeed()
        {
            Get<Button>("AnimationSpeed").Click += delegate
            {
                var picker = Get<Border>("SpeedPicker");
                if (picker.Visibility == Visibility.Visible) { ClosePanel("SpeedPicker"); return; }
                UpdateSpeedChoices();
                var bounds = PanelBounds(Get<Button>("AnimationSpeed"));
                picker.Margin = new Thickness(Math.Max(16, Math.Min(root.Width - 16 - picker.Width, bounds.Left + (bounds.Width - picker.Width) / 2)),
                    0, 0, Math.Max(16, Get<Grid>("Face").ActualHeight - bounds.Top + 5));
                picker.Visibility = Visibility.Visible;
                effect.StartArea(PanelBounds(picker));
            };
            root.PreviewMouseDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                var target = e.OriginalSource as DependencyObject;
                var picker = Get<Border>("SpeedPicker"); var trigger = Get<Button>("AnimationSpeed");
                if (target != null && picker.Visibility == Visibility.Visible && target != trigger &&
                    !picker.IsAncestorOf(target) && !trigger.IsAncestorOf(target)) ClosePanel("SpeedPicker");
            };
        }
        void UpdateSpeedChoices()
        {
            Get<Button>("AnimationSpeed").Content = "速度：" + speedNames[Preferences.AnimationSpeed] + " ▾";
            var choices = Get<StackPanel>("SpeedChoices"); choices.Children.Clear();
            string[] seconds = { "0.9", "1.8", "2.5" };
            for (int i = 0; i < speedNames.Length; i++)
            {
                int speed = i;
                var button = new Button { Content = speedNames[i] + " · 約" + seconds[i] + "秒", Height = 32, Margin = new Thickness(3),
                    Background = Art.Ink(i == Preferences.AnimationSpeed ? "#287181" : "#193B48") };
                button.Click += delegate
                {
                    Preferences.AnimationSpeed = speed; Preferences.Save();
                    Get<Border>("SpeedPicker").Visibility = Visibility.Collapsed; UpdateSpeedChoices();
                    effect.StartArea(PanelBounds(Get<Border>("SettingsPicker")), true);
                };
                choices.Children.Add(button);
            }
        }
    }
}
