using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        static readonly string[] presetNames = { "現在地", "日本広域", "関東", "関西" };
        string removingPlace;

        void InitializeWeatherPlaces()
        {
            var picker = Get<Border>("WeatherPlacesPicker");
            picker.PreviewMouseDown += delegate { FinishMarketTransition(); };
            picker.PreviewKeyDown += delegate { FinishMarketTransition(); };
            Get<Button>("ManageWeatherPlaces").Click += delegate
            {
                CancelPlaceSelection(); StopRadar(); removingPlace = null;
                DrawWeatherPlaces(); picker.Visibility = Visibility.Visible; AnimateEditorTransition(picker, false);
            };
            Get<Button>("CloseWeatherPlaces").Click += delegate { CloseWeatherPlaces(true); };
            Get<Button>("AddWeatherPlace").Click += delegate
            {
                CloseWeatherPlaces(false);
                if (showDailyWeather) { showDailyWeather = false; SwitchView(2); }
                OpenPlaceSearch();
            };
            root.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                var target = e.OriginalSource as DependencyObject; var opener = Get<Button>("ManageWeatherPlaces");
                if (picker.Visibility == Visibility.Visible && target != null && target != picker && !picker.IsAncestorOf(target) && target != opener && !opener.IsAncestorOf(target)) CloseWeatherPlaces(true);
            };
            var scroller = Get<ScrollViewer>("WeatherScroll");
            scroller.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) { scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta); e.Handled = true; };
            scroller.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
            { if (e.HorizontalChange != 0 && mode == 2 && IsVisible) Dispatcher.BeginInvoke(new Action(delegate { if (!IsClosing) effect.Refresh(); }), DispatcherPriority.ContextIdle); };
        }
        void CloseWeatherPlaces(bool animate)
        {
            var picker = Get<Border>("WeatherPlacesPicker");
            if (picker.Visibility != Visibility.Visible) return;
            if (animate) AnimateEditorTransition(picker, true); else { FinishMarketTransition(); picker.Visibility = Visibility.Collapsed; }
        }
        void WeatherPlaceAction(Action action)
        {
            try { action(); }
            catch (IOException) { WeatherPlaceFailure(); }
            catch (UnauthorizedAccessException) { WeatherPlaceFailure(); }
        }
        void WeatherPlaceFailure()
        {
            const string message = "保存できませんでした。もう一度お試しください";
            Get<TextBlock>("WeatherPlacesMessage").Text = message;
            if (Get<Border>("WeatherPlacesPicker").Visibility != Visibility.Visible) Get<TextBlock>("DataStamp").Text = message;
            effect.Refresh();
        }
        void ChooseSavedPlace(string id)
        {
            WeatherPlaceAction(delegate { SavedPlace.Select(id); ReloadPlace(); });
        }
        void RebuildWeatherChoices()
        {
            var rows = Get<StackPanel>("WeatherChoices"); rows.Children.Clear();
            if (!SavedPlace.IsHidden(0)) AddWeatherChoice("現在地", "現在地の周辺", "current", choice[2] == 0 && SavedPlace.ActiveId == null, delegate { ChooseSavedPlace(null); });
            foreach (var place in SavedPlace.Entries)
            {
                string id = place.Id;
                AddWeatherChoice(place.Name, place.Name + "周辺", id, choice[2] == 0 && SavedPlace.ActiveId == id, delegate { ChooseSavedPlace(id); });
            }
            if (!showDailyWeather)
                for (int i = 1; i <= 3; i++)
                {
                    int region = i; if (SavedPlace.IsHidden(region)) continue;
                    AddWeatherChoice(presetNames[i], presetNames[i], "preset" + i, choice[2] == i, delegate { choice[2] = region; mapViewport = null; saved.Remove(20 + region); SwitchView(2); });
                }
            if (IsVisible && mode == 2) RefreshViewAfterLayout();
        }
        void AddWeatherChoice(string name, string tooltip, string tag, bool selected, Action action)
        {
            var label = new TextBlock { Text = name, MaxWidth = 82, TextTrimming = TextTrimming.CharacterEllipsis };
            var button = new Button { Content = label, Tag = tag, ToolTip = tooltip, Background = selected ? activeTab : Brushes.Transparent, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(7, 3, 7, 3) };
            System.Windows.Automation.AutomationProperties.SetName(button, "天気の地点 " + name);
            button.Click += delegate { action(); }; Get<StackPanel>("WeatherChoices").Children.Add(button);
            if (selected) button.Loaded += delegate { button.BringIntoView(); };
        }
        void DrawWeatherPlaces()
        {
            Get<TextBlock>("WeatherPlacesMessage").Text = SavedPlace.Entries.Count + "地点 · 名前と縮尺は地点ごとに保存します";
            var rows = Get<StackPanel>("RegisteredPlaces"); rows.Children.Clear();
            if (SavedPlace.Entries.Count == 0) rows.Children.Add(new TextBlock { Text = "登録地点はありません。\n「＋ 地点を追加」から検索して登録できます。", Foreground = muted, Margin = new Thickness(5, 20, 5, 0), TextWrapping = TextWrapping.Wrap });
            foreach (var place in SavedPlace.Entries)
            {
                string id = place.Id;
                var row = new Grid { Height = 62, Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = new TextBlock { Text = place.Name, Foreground = Art.Ink("#E3F4F4"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = place.Name, Margin = new Thickness(9, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(name);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) }; Grid.SetColumn(actions, 1); row.Children.Add(actions);
                var select = PlaceRowButton(SavedPlace.ActiveId == id ? "選択中" : "選択", "select:" + id, delegate { ChooseSavedPlace(id); });
                select.IsEnabled = SavedPlace.ActiveId != id; actions.Children.Add(select);
                int index = SavedPlace.Entries.IndexOf(place);
                var up = PlaceRowButton("↑", "up:" + id, delegate { MoveWeatherPlace(id, -1); }); up.IsEnabled = index > 0; up.ToolTip = "上へ移動";
                var down = PlaceRowButton("↓", "down:" + id, delegate { MoveWeatherPlace(id, 1); }); down.IsEnabled = index < SavedPlace.Entries.Count - 1; down.ToolTip = "下へ移動";
                actions.Children.Add(up); actions.Children.Add(down);
                actions.Children.Add(PlaceRowButton("位置", "position:" + id, delegate { OpenSavedLocationEditor(id); }));
                actions.Children.Add(PlaceRowButton("名前", "rename:" + id, delegate { CloseWeatherPlaces(false); OpenPlaceEditor(id); }));
                actions.Children.Add(PlaceRowButton(removingPlace == id ? "削除する" : "削除", "delete:" + id, delegate
                {
                    if (removingPlace != id) { removingPlace = id; DrawWeatherPlaces(); Get<TextBlock>("WeatherPlacesMessage").Text = "もう一度「削除する」で、この地点を削除します"; return; }
                    WeatherPlaceAction(delegate
                    {
                        string previous = SavedPlace.ActiveId; SavedPlace.Remove(id); removingPlace = null;
                        if (previous != SavedPlace.ActiveId) ReloadPlace();
                        else { RebuildWeatherChoices(); DrawWeatherPlaces(); }
                    });
                }));
                rows.Children.Add(new Border { Child = row, Background = Art.Ink("#193B48"), CornerRadius = new CornerRadius(5), Tag = id });
            }
            var ranges = Get<System.Windows.Controls.Primitives.UniformGrid>("WeatherPresetChoices"); ranges.Children.Clear();
            for (int i = 0; i < 4; i++)
            {
                int region = i; bool visible = !SavedPlace.IsHidden(i);
                var button = new Button { Content = (visible ? "✓ " : "＋ ") + presetNames[i], Tag = i, Margin = new Thickness(2), Height = 30, Background = Art.Ink(visible ? "#245565" : "#193B48"), ToolTip = visible ? "タブを非表示にする" : "タブを表示する" };
                button.Click += delegate { WeatherPlaceAction(delegate
                {
                    if (!SavedPlace.SetHidden(region, !SavedPlace.IsHidden(region))) { Get<TextBlock>("WeatherPlacesMessage").Text = "少なくとも1つの地点か範囲を表示してください"; return; }
                    RebuildWeatherChoices(); DrawWeatherPlaces();
                    if ((region == choice[2]) && SavedPlace.IsHidden(region) && (region != 0 || SavedPlace.ActiveId == null))
                    {
                        if (SavedPlace.ActiveId != null || SavedPlace.Entries.Count > 0) { ChooseSavedPlace(SavedPlace.ActiveId ?? SavedPlace.Entries[0].Id); }
                        else
                        {
                            choice[2] = Enumerable.Range(0, 4).First(r => !SavedPlace.IsHidden(r)); mapViewport = null; saved.Remove(20 + choice[2]); CloseWeatherPlaces(false); SwitchView(2);
                        }
                    }
                }); };
                ranges.Children.Add(button);
            }
        }
        void MoveWeatherPlace(string id, int direction)
        {
            WeatherPlaceAction(delegate
            {
                if (!SavedPlace.Move(id, direction)) return;
                removingPlace = null; RebuildWeatherChoices(); DrawWeatherPlaces();
                AnimateEditorTransition(Get<Border>("WeatherPlacesPicker"), false);
            });
        }
        Button PlaceRowButton(string text, string tag, Action action)
        {
            var button = new Button { Content = text, Tag = tag, FontSize = (text == "↑" || text == "↓") ? 14 : 11, Padding = new Thickness(5, 4, 5, 4), MinWidth = (text == "↑" || text == "↓") ? 23 : 34 };
            button.Click += delegate { action(); }; return button;
        }
    }
}
