using System;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitCalendar
{
    internal sealed class LoadingDots : FrameworkElement
    {
        public double Phase;
        public LoadingDots() { Width = 82; Height = 38; HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center; IsHitTestVisible = false; AutomationProperties.SetName(this, "読み込み中"); }
        protected override void OnRender(DrawingContext dc)
        {
            for (int i = 0; i < 6; i++)
            {
                double wave = .5 + .5 * Math.Sin(Phase * 4.5 - i * .8);
                var brush = new SolidColorBrush(Color.FromRgb((byte)(65 + wave * 60), (byte)(125 + wave * 110), (byte)(145 + wave * 80)));
                dc.DrawEllipse(brush, null, new Point(11 + i * 12, 19 - Math.Sin(Phase * 4.5 - i * .8) * 5), 1.5 + 1.2 * wave, 1.5 + 1.2 * wave);
            }
        }
    }
    internal partial class CalendarPanel
    {
        readonly DispatcherTimer loadingAnimation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        readonly System.Diagnostics.Stopwatch loadingClock = System.Diagnostics.Stopwatch.StartNew();
        LoadingDots mainLoadingDots, newsLoadingDots;
        CancellationTokenSource dataLoadingOwner;
        void InitializeLoading()
        {
            mainLoadingDots = new LoadingDots(); Get<Grid>("LoadingArea").Children.Add(mainLoadingDots);
            loadingAnimation.Tick += delegate
            {
                if (!IsVisible || IsClosing || mode < 2) return;
                var dots = dataLoadingOwner != null ? mainLoadingDots : mode == 3 && newsPreview != null ? newsLoadingDots : null;
                if (dots == null || !dots.IsVisible || !SystemParameters.ClientAreaAnimation) return;
                dots.Phase = loadingClock.Elapsed.TotalSeconds; dots.InvalidateVisual();
                effect.RefreshArea(PanelBounds(dots));
            };
            IsVisibleChanged += delegate { if (IsVisible) loadingAnimation.Start(); else loadingAnimation.Stop(); };
            Closed += delegate { loadingAnimation.Stop(); };
            for (int i = 0; i < 3; i++)
            {
                int category = i;
                Get<Button>("NewsCategory" + i).Click += delegate { if (mode == 3) { choice[3] = category; infoPage = 0; LoadView(false); } };
            }
        }
        void BeginDataLoading(CancellationTokenSource owner)
        {
            dataLoadingOwner = owner;
            var host = Get<Grid>("LoadingArea");
            var choices = PanelBounds(Get<Grid>("DataChoices"));
            double rowTop = Get<Grid>("Face").RowDefinitions[0].ActualHeight;
            host.Margin = new Thickness(14, Math.Max(0, choices.Bottom - rowTop + 2), 14, 14);
            host.Visibility = Visibility.Visible;
        }
        void EndDataLoading(CancellationTokenSource owner)
        {
            if (dataLoadingOwner != owner) return;
            ClearDataLoading();
        }
        void ClearDataLoading()
        {
            dataLoadingOwner = null; Get<Grid>("LoadingArea").Visibility = Visibility.Collapsed;
        }
    }
}
