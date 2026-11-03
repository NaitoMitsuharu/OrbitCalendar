using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        Grid marketTransitionHost;
        Image marketTransitionImage;
        AssemblyEffect marketTransition;
        readonly DispatcherTimer marketTransitionFinish = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        void InitializeMarketTransitions()
        {
            var picker = Get<Border>("MarketPicker");
            marketTransitionHost = new Grid { Width = picker.Width, Height = picker.Height, VerticalAlignment = VerticalAlignment.Bottom, Margin = picker.Margin, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            var background = new Border { Background = Art.Ink("#102835"), CornerRadius = new CornerRadius(10) }; marketTransitionHost.Children.Add(background);
            marketTransitionImage = new Image { Width = picker.Width, Height = picker.Height };
            var source = new Grid { Opacity = 0 }; source.Children.Add(marketTransitionImage); marketTransitionHost.Children.Add(source);
            marketTransition = new AssemblyEffect(marketTransitionImage, background) { IsHitTestVisible = false }; marketTransitionHost.Children.Add(marketTransition);
            ((Grid)Get<Grid>("Effects").Parent).Children.Add(marketTransitionHost);
            marketTransitionFinish.Tick += delegate { FinishMarketTransition(); };
            picker.PreviewMouseDown += delegate { FinishMarketTransition(); };
            picker.PreviewKeyDown += delegate { FinishMarketTransition(); };
            IsVisibleChanged += delegate { if (!IsVisible) FinishMarketTransition(); };
            Closed += delegate { FinishMarketTransition(); };
        }
        void FinishMarketTransition()
        {
            marketTransitionFinish.Stop();
            if (marketTransition == null) return;
            marketTransition.Stop(); marketTransitionHost.Visibility = Visibility.Collapsed;
            Get<Border>("MarketPicker").Opacity = 1;
        }
        void CaptureMarketTransition()
        {
            var picker = Get<Border>("MarketPicker"); picker.Opacity = 1; UpdateLayout();
            int width = (int)picker.ActualWidth, height = (int)picker.ActualHeight;
            var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(picker) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
            frame.Render(visual); frame.Freeze(); marketTransitionImage.Source = frame;
        }
        void AnimateMarketTransition(bool closing)
        {
            var picker = Get<Border>("MarketPicker");
            if (!IsVisible || picker.Visibility != Visibility.Visible || !SystemParameters.ClientAreaAnimation) { if (closing) picker.Visibility = Visibility.Collapsed; return; }
            marketTransitionFinish.Stop(); marketTransition.Stop(); CaptureMarketTransition();
            marketTransitionHost.Visibility = Visibility.Visible; picker.Opacity = 0; UpdateLayout();
            marketTransition.Start(true);
            if (closing)
            {
                marketTransition.Stop(); marketTransition.Refresh(); picker.Visibility = Visibility.Collapsed;
                marketTransition.Dismiss(delegate { marketTransitionHost.Visibility = Visibility.Collapsed; picker.Opacity = 1; });
            }
            else marketTransitionFinish.Start();
        }
    }
}
