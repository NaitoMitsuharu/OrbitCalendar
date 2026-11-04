using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        string editingLocationId;
        bool placeAdjustmentCaptured;
        LiveData.View placeReturnWeather;
        LiveData.Viewport placeReturnViewport;
        int placeReturnChoice;
        double placeReturnMinute;

        void CapturePlaceAdjustment()
        {
            if (placeAdjustmentCaptured) return;
            placeAdjustmentCaptured = true;
            placeReturnWeather = activeWeather; placeReturnViewport = mapViewport;
            placeReturnChoice = choice[2]; placeReturnMinute = Get<Slider>("RadarTime").Value;
        }

        void RestorePlaceAdjustment()
        {
            placeAdjustmentCaptured = false;
            if (loading != null) loading.Cancel();
            zoomWait.Stop(); ClearDataLoading();
            activeWeather = placeReturnWeather; mapViewport = placeReturnViewport; choice[2] = placeReturnChoice;
            radarFrames = activeWeather == null ? null : activeWeather.Frames ?? new[] { activeWeather };
            nextUpdate = DateTime.MinValue;
            if (mode == 2 && !showDailyWeather)
            {
                SetVisible("TemperaturePlot", true); SetVisible("RadarControls", true);
                if (activeWeather != null)
                {
                    ShowData(activeWeather);
                    var slider = Get<Slider>("RadarTime"); slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, placeReturnMinute));
                }
                else
                {
                    radarFrames = null; Get<Canvas>("Plot").Children.Clear();
                    Get<TextBlock>("DataTitle").Text = LiveData.Regions[choice[2]];
                }
            }
            placeReturnWeather = null; placeReturnViewport = null;
        }

        void OpenSavedLocationEditor(string id)
        {
            var entry = SavedPlace.Entries.FirstOrDefault(p => p.Id == id);
            if (entry == null) return;
            CancelPlaceSelection(); StopRadar();
            showDailyWeather = false;
            root.Height = PreferredHeight; if (IsVisible) Shell.Position(this, 458, PreferredHeight);
            CapturePlaceAdjustment();
            editingLocationId = id; selectingPlace = true; candidatePlace = entry.Coordinate;
            Get<TextBox>("CandidatePlaceName").Text = entry.Name;
            Get<TextBox>("CandidatePlaceName").IsReadOnly = true;
            Get<TextBox>("CandidatePlaceName").ToolTip = "名前は保持します。名前の変更は管理画面から行えます";
            Get<Button>("SavePlace").Content = "変更を保存";
            Get<Button>("SavePlace").IsEnabled = false;
            Get<Canvas>("Plot").Cursor = Cursors.Cross;
            ShowCandidateConfirmation();
            mapViewport = new LiveData.Viewport { Latitude = entry.Latitude, Longitude = entry.Longitude, Zoom = entry.Zoom, Name = entry.Name + "（位置・縮尺の編集）", Marker = SavedPlace.Coordinate };
            choice[2] = 0; LoadView(false);
        }
    }
}
