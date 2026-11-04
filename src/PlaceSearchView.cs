using System;
using System.Device.Location;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        PlaceSearch.Result[] placeResults = new PlaceSearch.Result[0];
        int placePage, placeCategory;
        bool placeComposing;
        string placeResultQuery;
        GeoCoordinate searchReference;
        readonly DispatcherTimer placeSearchWait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        CancellationTokenSource placeSearching;
        ParticleLoading placeSearchParticles;
        readonly DispatcherTimer placeSearchTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        readonly System.Diagnostics.Stopwatch placeSearchClock = new System.Diagnostics.Stopwatch();

        void InitializePlaceSearch()
        {
            InitializeWeatherPlaces();
            InitializePlaceEditor();
            var picker = Get<Border>("PlaceSearchPicker");
            placeSearchParticles = new ParticleLoading(); Get<Grid>("PlaceSearchLoading").Children.Add(placeSearchParticles);
            placeSearchTick.Tick += delegate { placeSearchParticles.Phase = placeSearchClock.Elapsed.TotalSeconds; placeSearchParticles.InvalidateVisual(); };
            picker.PreviewMouseDown += delegate { FinishMarketTransition(); };
            picker.PreviewKeyDown += delegate { FinishMarketTransition(); };
            Get<Button>("ClosePlaceSearch").Click += delegate { ClosePlaceSearch(true); };
            Get<Button>("SearchPlace").Click += delegate { placeSearchWait.Stop(); SearchPlaceQuery(); };
            placeSearchWait.Tick += delegate { placeSearchWait.Stop(); if (!placeComposing && Get<Border>("PlaceSearchPicker").Visibility == Visibility.Visible) SearchPlaceQuery(); };
            string[] filters = { "すべて", "駅", "建物・施設", "地名・住所" };
            for (int i = 0; i < filters.Length; i++)
            {
                int category = i; var button = new Button { Content = filters[i], FontSize = 11, Padding = new Thickness(3, 3, 3, 3), Margin = new Thickness(1) };
                button.Click += delegate { placeCategory = category; placePage = 0; DrawPlaceResults(); };
                Get<System.Windows.Controls.Primitives.UniformGrid>("PlaceSearchFilters").Children.Add(button);
            }
            var queryBox = Get<TextBox>("PlaceQuery");
            TextCompositionManager.AddPreviewTextInputStartHandler(queryBox, delegate { placeComposing = true; placeSearchWait.Stop(); });
            TextCompositionManager.AddPreviewTextInputHandler(queryBox, delegate { placeComposing = false; QueuePlaceSearch(); });
            queryBox.LostKeyboardFocus += delegate { placeComposing = false; QueuePlaceSearch(); };
            Get<TextBox>("PlaceQuery").MaxLength = 120;
            Get<TextBox>("PlaceQuery").TextChanged += delegate
            {
                CancelPlaceSearchRequest(); placeResultQuery = null; placeResults = new PlaceSearch.Result[0]; placePage = 0;
                Get<TextBlock>("PlaceSearchMessage").Text = Get<TextBox>("PlaceQuery").Text.Trim().Length < 2 ? "2文字以上で自動検索します" : "入力後に自動検索します"; DrawPlaceResults(); QueuePlaceSearch();
            };
            Get<TextBox>("PlaceQuery").KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Enter && !e.IsRepeat) { placeSearchWait.Stop(); SearchPlaceQuery(); e.Handled = true; } };
            Get<Button>("PlacePrevious").Click += delegate { placePage--; DrawPlaceResults(); AnimateEditorTransition(picker, false); };
            Get<Button>("PlaceNext").Click += delegate { placePage++; DrawPlaceResults(); AnimateEditorTransition(picker, false); };
            Get<Button>("PickOnMap").Click += delegate { ClosePlaceSearch(true); BeginMapPlaceSelection(); };
            root.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                var target = e.OriginalSource as DependencyObject; var opener = Get<Button>("PickPlace");
                if (picker.Visibility == Visibility.Visible && target != null && target != picker && !picker.IsAncestorOf(target) && target != opener && !opener.IsAncestorOf(target)) ClosePlaceSearch(true);
            };
            Closed += delegate { placeSearchWait.Stop(); CancelPlaceSearchRequest(); };
        }

        void OpenPlaceSearch()
        {
            if (Get<Border>("PlaceSearchPicker").Visibility == Visibility.Visible) { ClosePlaceSearch(true); return; }
            CancelPlaceSelection(); StopRadar();
            searchReference = SavedPlace.Coordinate ?? (activeWeather == null || activeWeather.Viewport == null ? null : activeWeather.Viewport.Marker);
            Get<Border>("PlaceSearchPicker").Visibility = Visibility.Visible;
            Get<TextBlock>("PlaceSearchMessage").Text = placeResults.Length == 0 ? "例：横浜駅 ／ ランドマークタワー ／ 住所" : placeResults.Length + "件 · 候補を選んで地図で確認";
            Get<Button>("RenamePlace").Visibility = SavedPlace.Coordinate == null ? Visibility.Collapsed : Visibility.Visible;
            DrawPlaceResults(); AnimateEditorTransition(Get<Border>("PlaceSearchPicker"), false);
        }

        void QueuePlaceSearch()
        {
            placeSearchWait.Stop();
            string query = Get<TextBox>("PlaceQuery").Text.Trim().Normalize(System.Text.NormalizationForm.FormKC);
            if (placeSearching != null || query == placeResultQuery) return;
            if (!placeComposing && query.Length >= 2 && Get<Border>("PlaceSearchPicker").Visibility == Visibility.Visible) placeSearchWait.Start();
        }

        void ClosePlaceSearch(bool animate)
        {
            placeSearchWait.Stop();
            CancelPlaceSearchRequest();
            var picker = Get<Border>("PlaceSearchPicker");
            if (picker.Visibility != Visibility.Visible) return;
            if (animate) AnimateEditorTransition(picker, true);
            else { FinishMarketTransition(); picker.Visibility = Visibility.Collapsed; }
        }

        void CancelPlaceSearchRequest()
        {
            if (placeSearching != null) { placeSearching.Cancel(); placeSearching = null; }
            placeSearchTick.Stop(); placeSearchClock.Stop(); Get<Grid>("PlaceSearchLoading").Visibility = Visibility.Collapsed;
            Get<Button>("SearchPlace").IsEnabled = true;
        }

        async void SearchPlaceQuery()
        {
            string query = Get<TextBox>("PlaceQuery").Text.Trim().Normalize(System.Text.NormalizationForm.FormKC);
            if (query.Length < 2) { Get<TextBlock>("PlaceSearchMessage").Text = "2文字以上で入力してください"; return; }
            CancelPlaceSearchRequest();
            var request = new CancellationTokenSource(); placeSearching = request;
            placeResults = new PlaceSearch.Result[0]; placePage = 0; DrawPlaceResults();
            Get<TextBlock>("PlaceSearchMessage").Text = ""; Get<Button>("SearchPlace").IsEnabled = false;
            Get<Grid>("PlaceSearchLoading").Visibility = Visibility.Visible; placeSearchParticles.Restart(); placeSearchClock.Restart(); placeSearchTick.Start();
            try
            {
                var results = await PlaceSearch.Search(query, request.Token);
                if (request.IsCancellationRequested) return;
                placeResultQuery = query; placeResults = results; DrawPlaceResults();
                if (results.Length == 0) Get<TextBlock>("PlaceSearchMessage").Text = PlaceSearch.PartialFailure ? "一部の検索元に接続できません · 再試行か地図指定" : "候補がありません。市区町村を添えるか、地図で指定してください";
            }
            catch (Exception)
            {
                if (!request.IsCancellationRequested) Get<TextBlock>("PlaceSearchMessage").Text = "検索できませんでした。再試行または地図で指定できます";
            }
            finally
            {
                if (placeSearching == request) { placeSearching = null; placeSearchTick.Stop(); placeSearchClock.Stop(); Get<Grid>("PlaceSearchLoading").Visibility = Visibility.Collapsed; Get<Button>("SearchPlace").IsEnabled = true; }
                request.Dispose();
            }
        }

        void DrawPlaceResults()
        {
            var filtered = PlaceSearch.Rank(placeResults, Get<TextBox>("PlaceQuery").Text.Trim().Normalize(System.Text.NormalizationForm.FormKC), searchReference, placeCategory);
            int pages = Math.Max(1, (filtered.Length + 2) / 3); placePage = Math.Max(0, Math.Min(placePage, pages - 1));
            Get<TextBlock>("PlacePage").Text = (placePage + 1) + "/" + pages;
            Get<Button>("PlacePrevious").IsEnabled = placePage > 0; Get<Button>("PlaceNext").IsEnabled = placePage < pages - 1;
            int filterIndex = 0;
            foreach (Button choice in Get<System.Windows.Controls.Primitives.UniformGrid>("PlaceSearchFilters").Children) choice.Background = filterIndex++ == placeCategory ? activeTab : Art.Ink("#193B48");
            if (placeResults.Length > 0) Get<TextBlock>("PlaceSearchMessage").Text = filtered.Length + "件 · 種類・所在地を確認" + (PlaceSearch.PartialFailure ? " · 一部未取得" : "");
            var rows = Get<StackPanel>("PlaceResults"); rows.Children.Clear();
            foreach (var result in filtered.Skip(placePage * 3).Take(3))
            {
                var item = result;
                var text = new StackPanel { MaxWidth = 350, Margin = new Thickness(5, 4, 5, 4) };
                text.Children.Add(new TextBlock { Text = item.Kind + " · " + item.Label, Foreground = Art.Ink("#E3F4F4"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = item.Area, Foreground = muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
                string distance = searchReference == null ? "" : "基準地点から約" + (searchReference.GetDistanceTo(item.Coordinate) / 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "km · ";
                text.Children.Add(new TextBlock { Text = distance + item.Source, Foreground = muted, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
                var button = new Button { Content = text, Height = 64, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Art.Ink("#193B48"), Margin = new Thickness(0, 2, 0, 2), ToolTip = item.Kind + " · " + item.Name + "\n" + item.Area + "\n" + item.Position + "\n" + item.Source };
                System.Windows.Automation.AutomationProperties.SetName(button, "地点候補 " + item.Name);
                button.Click += delegate { SelectSearchPlace(item); }; rows.Children.Add(button);
            }
        }

        void BeginMapPlaceSelection()
        {
            if (activeWeather == null) return;
            StopRadar(); selectingPlace = true; candidatePlace = null; candidatePlaceName = null;
            Get<Canvas>("Plot").Cursor = Cursors.Cross; Get<Button>("PickPlace").Content = "＋ 地点";
            Get<TextBlock>("MapHint").Visibility = Visibility.Visible;
            Get<TextBlock>("MapHint").Text = "地図をクリックして基準地点を指定してください"; effect.Refresh();
        }

        void SelectSearchPlace(PlaceSearch.Result result)
        {
            ClosePlaceSearch(true); BeginMapPlaceSelection(); selectingPlace = true; candidatePlace = result.Coordinate; candidatePlaceName = result.Name;
            Get<TextBlock>("PlaceSummary").Text = result.Name + "\n" + result.Position;
            Get<Border>("PlaceConfirm").Visibility = Visibility.Visible;
            mapViewport = new LiveData.Viewport { Latitude = result.Coordinate.Latitude, Longitude = result.Coordinate.Longitude, Zoom = MapScale.ForRegion(0), Name = "検索候補周辺", Marker = activeWeather == null ? SavedPlace.Coordinate : activeWeather.Viewport.Marker };
            Get<Button>("SavePlace").IsEnabled = false;
            choice[2] = 0; LoadView(false);
        }
    }
}
