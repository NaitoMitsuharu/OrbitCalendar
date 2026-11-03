using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        readonly Dictionary<string, LiveData.View> marketSaved = new Dictionary<string, LiveData.View>();
        bool marketSearching;
        int marketPage, marketFilter;
        CancellationTokenSource checkingMarket;
        readonly System.Windows.Threading.DispatcherTimer searchWait = new System.Windows.Threading.DispatcherTimer();

        void InitializeMarketSettings()
        {
            Get<Button>("SettingsMarkets").Click += delegate { OpenMarketSettings(false); };
            Get<Button>("ManageMarkets").Click += delegate { OpenMarketSettings(false); };
            Get<Button>("CloseMarketPicker").Click += delegate { CloseMarketSettings(); };
            Get<Button>("BackParticles").Click += delegate { CloseMarketSettings(); Get<Button>("SettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            Get<Button>("RegisteredMarkets").Click += delegate { ShowMarketSettings(false); };
            Get<Button>("SearchMarkets").Click += delegate { ShowMarketSettings(true); Get<TextBox>("MarketQuery").Focus(); };
            searchWait.Interval = TimeSpan.FromMilliseconds(150);
            searchWait.Tick += delegate { searchWait.Stop(); marketPage = 0; DrawMarketSettings(); };
            Get<TextBox>("MarketQuery").TextChanged += delegate { CancelMarketCheck(); searchWait.Stop(); searchWait.Start(); };
            Get<TextBox>("MarketQuery").PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { searchWait.Stop(); marketPage = 0; DrawMarketSettings(); e.Handled = true; }
            };
            string[] filters = { "FilterAll", "FilterFunds", "FilterJapan", "FilterOther" };
            for (int i = 0; i < filters.Length; i++)
            {
                int value = i;
                Get<Button>(filters[i]).Click += delegate { CancelMarketCheck(); marketFilter = value; marketPage = 0; DrawMarketSettings(); };
            }
            Get<Button>("MarketPrevious").Click += delegate { CancelMarketCheck(); marketPage--; DrawMarketSettings(); };
            Get<Button>("MarketNext").Click += delegate { CancelMarketCheck(); marketPage++; DrawMarketSettings(); };
            Get<ScrollViewer>("MarketScroll").ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
            {
                if (e.HorizontalChange != 0 && mode == 1 && IsVisible)
                    Dispatcher.BeginInvoke(new Action(delegate { effect.Refresh(); }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            };
            Get<ScrollViewer>("MarketScroll").PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                var scroller = Get<ScrollViewer>("MarketScroll");
                scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta); e.Handled = true;
            };
            root.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                var target = e.OriginalSource as DependencyObject;
                var picker = Get<Border>("MarketPicker");
                if (picker.Visibility != Visibility.Visible || target == null || picker.IsAncestorOf(target)) return;
                foreach (string name in new[] { "SettingsButton", "ManageMarkets" })
                {
                    var button = Get<Button>(name);
                    if (button == target || button.IsAncestorOf(target)) return;
                }
                CloseMarketSettings();
            };
            IsVisibleChanged += delegate { if (!IsVisible) CloseMarketSettings(); };
            Get<TextBlock>("MarketCatalogHint").Text = "名簿 " + Instruments.Snapshot + " · SBI取扱は未照合です。価格取得未対応の投信も登録できます。愛称は公式情報＋主要な通称に対応。";
            RebuildMarketChoices();
        }

        void OpenMarketSettings(bool search)
        {
            CancelPlaceSelection();
            Get<Border>("SettingsPicker").Visibility = Visibility.Collapsed;
            Get<Border>("TimerPicker").Visibility = Visibility.Collapsed;
            effect.Refresh();
            var picker = Get<Border>("MarketPicker");
            picker.Visibility = Visibility.Visible;
            ShowMarketSettings(search);
            if (SystemParameters.ClientAreaAnimation)
                picker.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        void CloseMarketSettings()
        {
            searchWait.Stop();
            CancelMarketCheck();
            var picker = Get<Border>("MarketPicker");
            picker.BeginAnimation(UIElement.OpacityProperty, null);
            picker.Visibility = Visibility.Collapsed;
        }
        void ShowMarketSettings(bool search)
        {
            CancelMarketCheck();
            marketSearching = search; marketPage = 0;
            Get<TextBox>("MarketQuery").Visibility = search ? Visibility.Visible : Visibility.Collapsed;
            Get<TextBlock>("RegisteredHint").Visibility = search ? Visibility.Collapsed : Visibility.Visible;
            Get<TextBlock>("MarketCount").Visibility = search ? Visibility.Collapsed : Visibility.Visible;
            Get<StackPanel>("MarketFilters").Visibility = search ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("RegisteredMarkets").Background = search ? Brushes.Transparent : activeTab;
            Get<Button>("SearchMarkets").Background = search ? activeTab : Brushes.Transparent;
            Get<TextBlock>("MarketMessage").Text = search ? "名前・愛称・コードを入力" : "登録順に株価タブへ表示します";
            DrawMarketSettings();
        }
        void DrawMarketSettings()
        {
            var results = marketSearching ? Instruments.Search(Get<TextBox>("MarketQuery").Text, marketFilter) : Watchlist.Items.ToList();
            int pages = Math.Max(1, (results.Count + 2) / 3);
            marketPage = Math.Max(0, Math.Min(marketPage, pages - 1));
            Get<TextBlock>("MarketPage").Text = (marketPage + 1) + "/" + pages;
            Get<Button>("MarketPrevious").IsEnabled = marketPage > 0;
            Get<Button>("MarketNext").IsEnabled = marketPage < pages - 1;
            Get<TextBlock>("MarketCount").Text = "登録済み " + results.Count + " 件";
            string[] filters = { "FilterAll", "FilterFunds", "FilterJapan", "FilterOther" };
            for (int i = 0; i < filters.Length; i++) Get<Button>(filters[i]).Background = i == marketFilter ? activeTab : Brushes.Transparent;
            var rows = Get<StackPanel>("MarketResults"); rows.Children.Clear();
            if (results.Count == 0)
                rows.Children.Add(new TextBlock { Text = marketSearching ? "候補が見つかりません。\n名前の一部や協会コード（8桁）でお試しください。\n名簿に未収録の商品もあります。" : "表示する銘柄がありません。\n「＋ 銘柄を追加」から検索してください。",
                    Foreground = muted, Margin = new Thickness(5, 25, 5, 0), TextWrapping = TextWrapping.Wrap });
            foreach (var result in results.Skip(marketPage * 3).Take(3))
            {
                Instrument item = result;
                var grid = new Grid { Height = 66, Margin = new Thickness(0, 0, 0, 2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
                var text = new StackPanel { Margin = new Thickness(5, 3, 3, 3) };
                text.Children.Add(new TextBlock { Text = item.Name, FontSize = 13, Foreground = Art.Ink("#E3F4F4"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Name + "\n" + item.Aliases });
                text.Children.Add(new TextBlock { Text = item.Code + " · " + item.Kind, FontSize = 11, Foreground = muted, Margin = new Thickness(0, 2, 0, 1) });
                text.Children.Add(new TextBlock { Text = Watchlist.Status(item), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Watchlist.Status(item), FontSize = 11, Foreground = Art.Ink(item.Supported ? "#8FE4DD" : "#E1BE89") });
                grid.Children.Add(text);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(actions, 1); grid.Children.Add(actions);
                if (marketSearching)
                {
                    var check = ActionButton("確認", "価格取得を確認 " + item.Code, delegate { CheckMarket(item); });
                    check.IsEnabled = item.Supported; check.ToolTip = item.Supported ? "実際にデータを取得して確認" : "この運用会社の価格取得には未対応です";
                    actions.Children.Add(check);
                    bool added = Watchlist.Items.Any(i => i.Id == item.Id);
                    var add = ActionButton(added ? "済" : "追加", "銘柄を追加 " + item.Code, delegate
                    {
                        if (Watchlist.Items.Any(i => i.Id == item.Id)) return;
                        Watchlist.Items.Add(item); ChangeWatchlist();
                        DrawMarketSettings();
                        Get<TextBlock>("MarketMessage").Text = Watchlist.SaveError ?? (item.Label + " を追加しました");
                    });
                    add.IsEnabled = !added; add.Background = Art.Ink("#225968"); actions.Children.Add(add);
                }
                else
                {
                    int index = Watchlist.Items.IndexOf(item);
                    var up = ActionButton("↑", "銘柄を上へ " + item.Code, delegate { MoveMarket(item, -1); }); up.IsEnabled = index > 0;
                    var down = ActionButton("↓", "銘柄を下へ " + item.Code, delegate { MoveMarket(item, 1); }); down.IsEnabled = index < Watchlist.Items.Count - 1;
                    actions.Children.Add(up); actions.Children.Add(down);
                    actions.Children.Add(ActionButton("−", "銘柄を削除 " + item.Code, delegate { RemoveMarket(item); }));
                }
                rows.Children.Add(new Border { Child = grid, BorderBrush = Art.Ink("#2A4D58"), BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            if (marketSearching) Get<TextBlock>("MarketMessage").Text = Watchlist.SaveError ?? (results.Count + " 件 · " + Instruments.Catalog.Count + " 件の名簿から検索");
        }
        Button ActionButton(string label, string accessibility, Action action)
        {
            var button = new Button { Content = label, MinWidth = 27, Height = 29, Padding = new Thickness(5, 2, 5, 2), FontSize = 11 };
            AutomationProperties.SetName(button, accessibility);
            button.Click += delegate { action(); }; return button;
        }
        void MoveMarket(Instrument item, int delta)
        {
            int index = Watchlist.Items.IndexOf(item), destination = index + delta;
            if (index < 0 || destination < 0 || destination >= Watchlist.Items.Count) return;
            var selectedItem = SelectedMarket;
            Watchlist.Items.RemoveAt(index); Watchlist.Items.Insert(destination, item);
            if (selectedItem != null) choice[1] = Watchlist.Items.IndexOf(selectedItem);
            ChangeWatchlist(); DrawMarketSettings();
        }
        void RemoveMarket(Instrument item)
        {
            var selectedItem = SelectedMarket;
            Watchlist.Items.Remove(item); marketSaved.Remove(item.Id);
            choice[1] = selectedItem != null && Watchlist.Items.Contains(selectedItem) ? Watchlist.Items.IndexOf(selectedItem) : 0;
            ChangeWatchlist(); DrawMarketSettings();
        }
        void ChangeWatchlist()
        {
            Watchlist.Save(); RebuildMarketChoices();
            if (mode == 1) LoadView(false);
        }
        Instrument SelectedMarket { get { return choice[1] >= 0 && choice[1] < Watchlist.Items.Count ? Watchlist.Items[choice[1]] : null; } }
        void RebuildMarketChoices()
        {
            var stack = Get<StackPanel>("MarketChoices"); stack.Children.Clear();
            for (int i = 0; i < Watchlist.Items.Count; i++)
            {
                int index = i; var item = Watchlist.Items[i];
                var label = new TextBlock { Text = item.Label, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis };
                var button = new Button { Content = label, ToolTip = item.Name + "\n" + Watchlist.Status(item), Background = i == choice[1] ? activeTab : Brushes.Transparent, Margin = new Thickness(0, 0, 4, 0) };
                AutomationProperties.SetName(button, "表示 " + item.Code);
                button.Click += delegate { choice[1] = index;
                    foreach (Button sibling in stack.Children) sibling.Background = sibling == button ? activeTab : Brushes.Transparent;
                    LoadView(false); };
                stack.Children.Add(button);
                if (i == choice[1]) button.Loaded += delegate { button.BringIntoView(); };
            }
        }
        void CancelMarketCheck() { if (checkingMarket != null) checkingMarket.Cancel(); }
        async void CheckMarket(Instrument item)
        {
            CancelMarketCheck();
            var request = new CancellationTokenSource(); checkingMarket = request;
            var message = Get<TextBlock>("MarketMessage"); message.Text = item.Code + " の価格を確認中…";
            try
            {
                var view = await LiveData.Market(item, request.Token);
                if (request.IsCancellationRequested) return;
                Watchlist.Confirm(item); marketSaved[item.Id] = view;
                DrawMarketSettings(); message.Text = Watchlist.SaveError ?? ("取得成功 · 最新データ " + view.Bars.Last().Day.ToString("yyyy/MM/dd"));
            }
            catch (Exception) { if (!request.IsCancellationRequested) message.Text = "取得失敗 · 配信対象外または通信エラー。再確認できます"; }
            finally { if (checkingMarket == request) checkingMarket = null; request.Dispose(); }
        }
    }
}
