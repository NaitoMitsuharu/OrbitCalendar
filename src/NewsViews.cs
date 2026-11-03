using System;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        CancellationTokenSource newsPreview;
        void CancelNewsPreview() { if (newsPreview != null) { newsPreview.Cancel(); newsPreview = null; } }
        void MoveNews(int direction)
        {
            if (information == null || information.Items.Length == 0) return;
            CancelNewsPreview(); infoPage = (infoPage + direction + information.Items.Length) % information.Items.Length;
            RenderNewsArticle(informationStale); UpdateLayout(); effect.StartArea(PanelBounds(Get<StackPanel>("InformationBody")), true);
        }
        void RenderNewsArticle(bool stale)
        {
            newsLoadingParticles = null;
            var body = Get<StackPanel>("InformationBody"); body.Children.Clear();
            if (information == null || information.Items.Length == 0) return;
            infoPage = Math.Max(0, Math.Min(infoPage, information.Items.Length - 1));
            var item = information.Items[infoPage];
            Get<TextBlock>("DataStamp").Text = information.Fetched.AddHours(9).ToString("HH:mm") + " 更新" + (stale ? " · 更新失敗（前回の記事）" : "");
            var content = new StackPanel { Width = 394 };
            content.Children.Add(new TextBlock { Text = item.Title, FontSize = 17, Foreground = Art.Ink("#E3F4F4"), TextWrapping = TextWrapping.Wrap, MaxHeight = 46, LineHeight = 23, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Title, Tag = "news-title" });
            var photo = new Grid { Width = 394, Height = 142, Margin = new Thickness(0, 8, 0, 8), Background = Art.Ink("#152E39"), ClipToBounds = true };
            if (item.Picture != null) photo.Children.Add(new Image { Source = item.Picture, Stretch = Stretch.UniformToFill, Width = 394, Height = 142, Tag = "news-image" });
            else if (!item.DetailsLoaded) { newsLoadingParticles = new ParticleLoading(); photo.Children.Add(newsLoadingParticles); }
            else photo.Children.Add(new TextBlock { Text = "画像はありません", Foreground = Art.Ink("#92B5BE"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(photo);
            content.Children.Add(new TextBlock { Text = item.Description.Length > 0 ? item.Description : item.DetailsLoaded ? "説明文を取得できませんでした" : "", FontSize = 13, Foreground = Art.Ink("#C5DEE2"), TextWrapping = TextWrapping.Wrap, MaxHeight = 54, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Description, Tag = "news-description" });
            content.Children.Add(new TextBlock { Text = item.Source + (item.Published.HasValue ? " · " + item.Published.Value.ToOffset(TimeSpan.FromHours(9)).ToString("M/d HH:mm") : " · 配信時刻なし"), Foreground = Art.Ink("#92B5BE"), FontSize = 10, Margin = new Thickness(0, 8, 0, 0) });
            var card = new Button { Content = content, Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "クリックして次の記事へ", Tag = "news-card" };
            AutomationProperties.SetName(card, "次の記事"); card.Click += delegate { MoveNews(1); }; body.Children.Add(card);
            var pager = new Grid { Height = 30, Margin = new Thickness(0, 4, 0, 0) };
            var previous = new Button { Content = "‹", FontSize = 18, Width = 35, HorizontalAlignment = HorizontalAlignment.Left, Tag = "news-previous" };
            var next = new Button { Content = "›", FontSize = 18, Width = 35, HorizontalAlignment = HorizontalAlignment.Right, Tag = "news-next" };
            previous.Click += delegate { MoveNews(-1); }; next.Click += delegate { MoveNews(1); };
            pager.Children.Add(previous); pager.Children.Add(new TextBlock { Text = (infoPage + 1) + " / " + information.Items.Length, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Art.Ink("#92B5BE"), FontSize = 12 }); pager.Children.Add(next); body.Children.Add(pager);
            if (!item.DetailsLoaded) LoadNewsPreview(item);
        }
        async void LoadNewsPreview(InformationFeeds.Item item)
        {
            CancelNewsPreview(); var request = new CancellationTokenSource(); newsPreview = request;
            try
            {
                await InformationFeeds.PreviewNews(item, request.Token);
                if (request.IsCancellationRequested || mode != 3 || information == null || information.Items[infoPage] != item) return;
                RenderNewsArticle(informationStale); UpdateLayout();
                if (effect.Busy) effect.Refresh(); else effect.StartArea(PanelBounds(Get<StackPanel>("InformationBody")));
                RefreshViewAfterLayout();
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (request.IsCancellationRequested || mode != 3) return;
                item.DetailsLoaded = true; RenderNewsArticle(informationStale); effect.Refresh();
            }
            finally { if (newsPreview == request) newsPreview = null; request.Dispose(); }
        }
    }
}
