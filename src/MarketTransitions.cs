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
        Border editorTransitionPicker;
        Grid marketTransitionHost;
        Image marketTransitionImage;
        AssemblyEffect marketTransition;
        readonly DispatcherTimer marketTransitionFinish = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        void InitializeMarketTransitions()
        {
            InitializeParticleMenus();
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
            if (editorTransitionPicker != null) editorTransitionPicker.Opacity = 1;
        }
        void CaptureMarketTransition()
        {
            var picker = editorTransitionPicker; picker.Opacity = 1; UpdateLayout();
            var settledEffect = picker.Effect;
            picker.Effect = null;
            try
            {
                // The gathering effect adds its own grains; capture the clean
                // controls to avoid applying the settled pattern a second time.
                int width = (int)picker.ActualWidth, height = (int)picker.ActualHeight;
                var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(picker) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
                frame.Render(visual); frame.Freeze(); marketTransitionImage.Source = frame;
            }
            finally { picker.Effect = settledEffect; }
        }
        void AnimateMarketTransition(bool closing) { AnimateEditorTransition(Get<Border>("MarketPicker"), closing); }
        void AnimateEditorTransition(Border picker, bool closing, bool quick = false)
        {
            if (!IsVisible || picker.Visibility != Visibility.Visible || !SystemParameters.ClientAreaAnimation) { if (closing) picker.Visibility = Visibility.Collapsed; return; }
            FinishMarketTransition(); editorTransitionPicker = picker;
            marketTransitionHost.Width = picker.Width; marketTransitionHost.Height = picker.Height; marketTransitionHost.Margin = picker.Margin;
            marketTransitionImage.Width = picker.Width; marketTransitionImage.Height = picker.Height;
            CaptureMarketTransition();
            marketTransitionHost.Visibility = Visibility.Visible; picker.Opacity = 0; UpdateLayout();
            if (quick) marketTransition.StartQuick(new Rect(picker.RenderSize)); else marketTransition.Start(true);
            if (closing)
            {
                marketTransition.Stop(); marketTransition.Refresh(); picker.Visibility = Visibility.Collapsed;
                marketTransition.Dismiss(delegate { marketTransitionHost.Visibility = Visibility.Collapsed; picker.Opacity = 1; });
            }
            else { marketTransitionFinish.Interval = TimeSpan.FromSeconds(quick ? .26 : 1.8 * Preferences.AnimationScale); marketTransitionFinish.Start(); }
        }
    }
}
