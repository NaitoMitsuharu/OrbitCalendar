using System;
using System.Device.Location;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        bool selectingPlace;
        GeoCoordinate candidatePlace;
        string candidatePlaceName;

        void InitializePlacePicker()
        {
            InitializePlaceSearch();
            Get<Button>("PickPlace").Click += delegate { OpenPlaceSearch(); };
            Get<Button>("CancelPlace").Click += delegate { CancelPlaceSelection(); effect.Refresh(); };
            Get<Button>("SavePlace").Click += delegate
            {
                if (candidatePlace == null || !Get<Button>("SavePlace").IsEnabled) return;
                try
                {
                    SavedPlace.Save(candidatePlace, candidatePlaceName);
                    ReloadPlace();
                }
                catch (IOException) { PlaceSaveFailed(); }
                catch (UnauthorizedAccessException) { PlaceSaveFailed(); }
            };
            Get<Button>("AutoPlace").Click += delegate
            {
                try { SavedPlace.Clear(); ReloadPlace(); }
                catch (IOException) { Get<TextBlock>("MapHint").Text = "保存地点を解除できませんでした"; effect.Refresh(); }
                catch (UnauthorizedAccessException) { Get<TextBlock>("MapHint").Text = "保存地点を解除できませんでした"; effect.Refresh(); }
            };
            IsVisibleChanged += delegate { if (!IsVisible) CancelPlaceSelection(); };
        }

        void PlaceSaveFailed()
        {
            Get<TextBlock>("PlaceSummary").Text = "保存できませんでした。もう一度お試しください。";
            effect.Refresh();
        }

        void ReloadPlace()
        {
            CancelPlaceSelection();
            for (int i = 20; i <= 23; i++) saved.Remove(i);
            activeWeather = null;
            weatherForecast = null;
            LiveData.CachedForecast = null;
            mapViewport = null;
            choice[2] = 0;
            nextTodayWeather = DateTime.MinValue;
            Get<TextBlock>("TodayWeather").Text = "◌";
            Get<TextBlock>("TodayHigh").Text = ""; Get<TextBlock>("TodayLow").Text = "";
            UpdateTodayWeather();
            LoadView(true);
        }

        void CancelPlaceSelection()
        {
            ClosePlaceSearch(false);
            selectingPlace = false;
            Get<TextBlock>("MapHint").Visibility = Visibility.Collapsed;
            candidatePlace = null;
            candidatePlaceName = null;
            Get<Canvas>("Plot").Cursor = Cursors.Arrow;
            Get<Button>("PickPlace").Content = "地点指定";
            Get<Border>("PlaceConfirm").Visibility = Visibility.Collapsed;
            if (activeWeather != null && mode == 2)
            {
                var plot = Get<Canvas>("Plot");
                plot.Children.Clear();
                DrawRadar(activeWeather);
            }
        }

        static Point PlacePixel(LiveData.Viewport viewport, GeoCoordinate coordinate)
        {
            double scale = viewport.Zoom == 4 ? .76 : .88, world = 256 << viewport.Zoom;
            Func<double, double> mercator = degrees =>
            {
                double phi = degrees * Math.PI / 180;
                return (1 - Math.Log(Math.Tan(phi) + 1 / Math.Cos(phi)) / Math.PI) * world / 2;
            };
            return new Point((coordinate.Longitude - viewport.Longitude) / 360 * world * scale + LiveData.MapWidth / 2.0,
                (mercator(coordinate.Latitude) - mercator(viewport.Latitude)) * scale + LiveData.MapHeight / 2.0);
        }

        void SelectPlaceAt(Point point)
        {
            if (activeWeather == null) return;
            var viewport = activeWeather.Viewport;
            double scale = viewport.Zoom == 4 ? .76 : .88, world = 256 << viewport.Zoom;
            double phi = viewport.Latitude * Math.PI / 180;
            double x = (viewport.Longitude + 180) / 360 * world + (point.X - LiveData.MapWidth / 2.0) / scale;
            double y = (1 - Math.Log(Math.Tan(phi) + 1 / Math.Cos(phi)) / Math.PI) * world / 2 +
                (point.Y - LiveData.MapHeight / 2.0) / scale;
            double longitude = x / world * 360 - 180;
            double latitude = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / world))) * 180 / Math.PI;
            if (latitude < 20 || latitude > 48 || longitude < 120 || longitude > 155)
            {
                candidatePlace = null;
                candidatePlaceName = null;
                Get<Border>("PlaceConfirm").Visibility = Visibility.Collapsed;
                Get<Canvas>("Plot").Children.Clear();
                DrawRadar(activeWeather);
                Get<TextBlock>("MapHint").Text = "日本周辺の地図上で地点を選んでください";
                effect.Refresh();
                return;
            }
            candidatePlace = new GeoCoordinate(latitude, longitude);
            Get<Button>("SavePlace").IsEnabled = true;
            Get<TextBlock>("PlaceSummary").Text = (string.IsNullOrEmpty(candidatePlaceName) ? "" : candidatePlaceName + "（地図で調整）\n") + latitude.ToString("F4", CultureInfo.InvariantCulture) + "°, " +
                longitude.ToString("F4", CultureInfo.InvariantCulture) + "°\n雨雲と今日の天気の基準地点にします";
            Get<Border>("PlaceConfirm").Visibility = Visibility.Visible;
            var plot = Get<Canvas>("Plot");
            plot.Children.Clear(); DrawRadar(activeWeather);
            effect.Refresh();
        }

        void DrawCandidatePlace(LiveData.View view)
        {
            if (candidatePlace == null) return;
            Point point = PlacePixel(view.Viewport, candidatePlace);
            if (point.X < 0 || point.Y < 0 || point.X > LiveData.MapWidth || point.Y > LiveData.MapHeight) return;
            var dot = new Ellipse { Width = 12, Height = 12, Fill = Art.Ink("#FFBD76"), Stroke = Art.Ink("#FFF4E5"), StrokeThickness = 1.5 };
            Canvas.SetLeft(dot, point.X - 6); Canvas.SetTop(dot, point.Y - 6);
            Get<Canvas>("Plot").Children.Add(dot);
        }
    }
}
