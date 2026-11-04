using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        void InitializePlaceEditor()
        {
            var picker = Get<Border>("PlaceEditPicker");
            picker.PreviewMouseDown += delegate { FinishMarketTransition(); };
            picker.PreviewKeyDown += delegate { FinishMarketTransition(); };
            Get<Button>("RenamePlace").Click += delegate
            {
                if (SavedPlace.Coordinate == null) return;
                ClosePlaceSearch(false);
                Get<TextBox>("SavedPlaceName").Text = SavedPlace.Name ?? "";
                Get<TextBlock>("PlaceEditMessage").Text = "地点や地図の縮尺はそのまま、表示名だけを変更します";
                picker.Visibility = Visibility.Visible; AnimateEditorTransition(picker, false);
            };
            Get<Button>("CancelPlaceEdit").Click += delegate { ClosePlaceEditor(true); };
            Get<Button>("SavePlaceName").Click += delegate { SavePlaceName(); };
            Get<TextBox>("SavedPlaceName").KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Enter && !e.IsRepeat) { SavePlaceName(); e.Handled = true; } };
            root.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                var target = e.OriginalSource as DependencyObject;
                if (picker.Visibility == Visibility.Visible && target != null && target != picker && !picker.IsAncestorOf(target)) ClosePlaceEditor(true);
            };
        }

        void ClosePlaceEditor(bool animate)
        {
            var picker = Get<Border>("PlaceEditPicker");
            if (picker.Visibility != Visibility.Visible) return;
            if (animate) AnimateEditorTransition(picker, true);
            else { FinishMarketTransition(); picker.Visibility = Visibility.Collapsed; }
        }

        void SavePlaceName()
        {
            if (SavedPlace.Coordinate == null) { ClosePlaceEditor(true); return; }
            string name = Get<TextBox>("SavedPlaceName").Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { Get<TextBlock>("PlaceEditMessage").Text = "地点の名前を入力してください"; return; }
            string previous = SavedPlace.DisplayName;
            try { SavedPlace.Save(SavedPlace.Coordinate, name); }
            catch (IOException) { Get<TextBlock>("PlaceEditMessage").Text = "名前を保存できませんでした。もう一度お試しください"; return; }
            catch (UnauthorizedAccessException) { Get<TextBlock>("PlaceEditMessage").Text = "名前を保存できませんでした。もう一度お試しください"; return; }
            if (loading != null) { loading.Cancel(); ClearDataLoading(); nextUpdate = DateTime.MinValue; }
            RefreshPlaceName(previous);
            ClosePlaceEditor(true);
        }

        void RefreshPlaceName(string previous)
        {
            string oldArea = previous + "周辺", newArea = SavedPlace.DisplayName + "周辺";
            var views = new HashSet<LiveData.View>();
            Action<LiveData.View> rename = view =>
            {
                if (view == null || !views.Add(view)) return;
                if (view.Heading == oldArea + "の雨雲") view.Heading = newArea + "の雨雲";
                if (view.Viewport != null && view.Viewport.Name == oldArea) view.Viewport.Name = newArea;
                if (view.LocationHint != null) view.LocationHint = view.LocationHint.Replace(previous + "（保存した基準地点）", SavedPlace.DisplayName + "（保存した基準地点）").Replace(previous + "は表示範囲外", SavedPlace.DisplayName + "は表示範囲外");
            };
            foreach (var view in saved.Values) { rename(view); if (view.Frames != null) foreach (var frame in view.Frames) rename(frame); }
            rename(activeWeather);
            if (radarFrames != null) foreach (var frame in radarFrames) rename(frame);
            if (mapViewport != null && mapViewport.Name == oldArea) mapViewport.Name = newArea;
            if (weatherForecast != null && weatherForecast.Place == oldArea) weatherForecast.Place = newArea;
            if (LiveData.CachedForecast != null && LiveData.CachedForecast.Place == oldArea) LiveData.CachedForecast.Place = newArea;
            if (LiveData.CachedForecast != null && LiveData.CachedForecast.Place == newArea) RenderTodayWeather(LiveData.CachedForecast);
            Get<Button>("Choice0").ToolTip = newArea + "へ戻る";
            if (mode == 2)
            {
                if (showDailyWeather && weatherForecast != null) RenderDailyWeather();
                else if (activeWeather != null)
                {
                    Get<TextBlock>("DataTitle").Text = activeWeather.Heading;
                    Get<Canvas>("Plot").Children.Clear(); DrawRadar(activeWeather);
                }
                effect.Start(true);
            }
        }
    }
}
