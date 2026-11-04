using System;
using System.IO;
using System.Reflection;
using System.Security;
using Microsoft.Win32;
using System.Windows.Controls;

namespace OrbitCalendar
{
    internal static class StartupRegistration
    {
        static readonly string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "OrbitCalendar";
        public static string Command { get { return "\"" + Assembly.GetExecutingAssembly().Location + "\" --autostart"; } }
        public static bool Enabled
        {
            get
            {
                try { using (var key = Registry.CurrentUser.OpenSubKey(registryPath)) return key != null && !string.IsNullOrWhiteSpace(key.GetValue(valueName) as string); }
                catch (SecurityException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
                catch (IOException) { return false; }
            }
        }
        public static void SetEnabled(bool enabled)
        {
            if (enabled) { using (var key = Registry.CurrentUser.CreateSubKey(registryPath)) key.SetValue(valueName, Command, RegistryValueKind.String); }
            else { using (var key = Registry.CurrentUser.OpenSubKey(registryPath, true)) if (key != null) key.DeleteValue(valueName, false); }
        }
    }
    internal partial class CalendarPanel
    {
        void InitializeStartupControl()
        {
            Get<Button>("StartupToggle").Click += delegate
            {
                try { StartupRegistration.SetEnabled(!StartupRegistration.Enabled); UpdateStartupButton(); }
                catch (Exception ex) { if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)) throw; Get<Button>("StartupToggle").ToolTip = "自動起動を保存できませんでした。もう一度お試しください"; }
                effect.Refresh();
            };
        }
        void UpdateStartupButton()
        {
            var button = Get<Button>("StartupToggle"); bool enabled = StartupRegistration.Enabled;
            button.Content = "自動起動 " + (enabled ? "ON" : "OFF");
            button.Background = Art.Ink(enabled ? "#287181" : "#193B48");
            button.ToolTip = "Windowsへのサインイン時に、時計をバックグラウンドで起動します";
        }
    }
}
