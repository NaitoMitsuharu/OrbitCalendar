using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        CancellationTokenSource updateRequest;
        AppUpdates.Release availableUpdate;
        bool installingUpdate;
        readonly UpdateMonitor updateMonitor = new UpdateMonitor();
        void InitializeUpdates()
        {
            updateMonitor.Changed += SetUpdateNotice;
            SetUpdateNotice(null);
            Get<Button>("UpdateButton").Click += delegate { OpenUpdates(); };
            Get<Button>("CloseUpdatePicker").Click += delegate { CloseUpdates(true); };
            Get<Button>("CheckUpdate").Click += async delegate { await CheckUpdate(); };
            Get<Button>("InstallUpdate").Click += async delegate { await InstallUpdate(); };
            var picker = Get<Border>("UpdatePicker");
            picker.PreviewMouseDown += delegate { FinishMarketTransition(); };
            picker.PreviewKeyDown += delegate { FinishMarketTransition(); };
            root.PreviewMouseDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (installingUpdate) e.Handled = true; };
            IsVisibleChanged += delegate { if (!IsVisible) CloseUpdates(false); };
            Closed += delegate { CloseUpdates(false); updateMonitor.Dispose(); };
        }
        internal void StartUpdateChecks() { updateMonitor.Start(); }
        internal void StopUpdateChecks() { updateMonitor.Dispose(); }
        void SetUpdateNotice(AppUpdates.Release release)
        {
            bool newer = release != null && release.IsNew;
            var button = Get<Button>("UpdateButton");
            button.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
            button.ToolTip = newer ? "v" + release.Version.ToString(3) + " に更新できます" : "アプリの更新";
            // Refresh queues a capture if particles are still assembling; a partial
            // refresh would otherwise miss a notice arriving during the opening.
            if (IsVisible && Get<Border>("SettingsPicker").Visibility == Visibility.Visible) effect.Refresh();
        }
        internal async void OpenUpdates()
        {
            if (installingUpdate) return;
            CancelPlaceSelection(); CloseMarketSettings();
            foreach (var name in new[] { "SettingsPicker", "SpeedPicker", "TimerPicker", "RangePicker", "CreditsPicker" }) Get<Border>(name).Visibility = Visibility.Collapsed;
            availableUpdate = null;
            Get<TextBlock>("UpdateCurrent").Text = "現在のバージョン  v" + AppUpdates.CurrentLabel;
            Get<TextBlock>("UpdateNotes").Text = "";
            Get<Border>("UpdatePicker").Visibility = Visibility.Visible;
            AnimateEditorTransition(Get<Border>("UpdatePicker"), false, true);
            effect.Refresh();
            await CheckUpdate();
        }
        void CloseUpdates(bool animate)
        {
            if (installingUpdate) return;
            if (updateRequest != null) updateRequest.Cancel();
            availableUpdate = null;
            var picker = Get<Border>("UpdatePicker");
            if (picker.Visibility != Visibility.Visible) return;
            if (animate) AnimateEditorTransition(picker, true, true);
            else { if (editorTransitionPicker == picker) FinishMarketTransition(); picker.Visibility = Visibility.Collapsed; }
        }
        void UpdateStatus(string message, bool busy, bool install)
        {
            // The native controls remain interactive after the short particle opening.
            Get<TextBlock>("UpdateStatus").Text = message;
            Get<Button>("CheckUpdate").IsEnabled = !busy;
            Get<Button>("InstallUpdate").IsEnabled = install && !busy;
            Get<Button>("InstallUpdate").Visibility = install || availableUpdate != null ? Visibility.Visible : Visibility.Collapsed;
            Get<Border>("UpdateProgressTrack").Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Get<TextBlock>("UpdateProgressText").Text = "";
            Get<Border>("UpdateProgressFill").Width = 0;
        }
        async Task CheckUpdate()
        {
            if (installingUpdate) return;
            if (updateRequest != null) updateRequest.Cancel();
            var request = updateRequest = new CancellationTokenSource();
            availableUpdate = null;
            UpdateStatus("新しいバージョンを確認しています…", true, false);
            Get<TextBlock>("UpdateNotes").Text = "";
            try
            {
                // Closing this popup cancels its presentation. The shared check may
                // still update the notice and the background schedule.
                var release = await updateMonitor.CheckNow();
                if (request.IsCancellationRequested || updateRequest != request) return;
                PresentUpdate(release);
            }
            catch (OperationCanceledException) { if (!request.IsCancellationRequested && updateRequest == request) UpdateStatus("確認がタイムアウトしました。もう一度お試しください", false, false); }
            catch (Exception ex)
            {
                if (updateRequest == request && !request.IsCancellationRequested) UpdateStatus("更新情報を取得できませんでした。通信を確認して再試行してください", false, false);
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally { if (updateRequest == request) updateRequest = null; request.Dispose(); }
        }
        void PresentUpdate(AppUpdates.Release release)
        {
            FinishMarketTransition();
            SetUpdateNotice(release);
            availableUpdate = release.IsNew ? release : null;
            UpdateStatus(release.IsNew ? "v" + release.Version.ToString(3) + " に更新できます" : "最新版を使用しています", false, release.IsNew);
            string notes = release.Notes ?? "";
            Get<TextBlock>("UpdateNotes").Text = notes.Length > 6000 ? notes.Substring(0, 6000) + "…" : notes;
        }
        async Task InstallUpdate()
        {
            var release = availableUpdate;
            if (release == null || updateRequest != null || installingUpdate) return;
            var request = updateRequest = new CancellationTokenSource();
            string staged = null;
            UpdateStatus("更新ファイルをダウンロードしています…", true, false);
            try
            {
                var progress = new Progress<double>(delegate(double value)
                {
                    if (updateRequest != request || request.IsCancellationRequested) return;
                    Get<Border>("UpdateProgressFill").Width = Math.Max(0, Math.Min(1, value)) * 350;
                    Get<TextBlock>("UpdateProgressText").Text = Math.Round(value * 100) + "%";
                });
                staged = await AppUpdates.Download(release, progress, request.Token);
                request.Token.ThrowIfCancellationRequested();
                installingUpdate = true;
                Get<Button>("CloseUpdatePicker").IsEnabled = false;
                Get<TextBlock>("UpdateStatus").Text = "検証完了。更新して再起動しています…";
                await UpdateInstaller.Launch(staged, release);
                staged = null; // Ownership passes to the helper until the replacement app starts.
                Application.Current.Shutdown();
            }
            catch (OperationCanceledException)
            {
                if (!request.IsCancellationRequested) UpdateStatus("ダウンロードがタイムアウトしました。再試行してください", false, true);
            }
            catch (Exception ex)
            {
                if (!request.IsCancellationRequested)
                {
                    UpdateStatus("更新できませんでした。アプリはそのまま使用できます", false, true);
                    Get<TextBlock>("UpdateNotes").Text = ex is IOException || ex is UnauthorizedAccessException ? ex.Message : "通信を確認して、もう一度お試しください";
                }
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                installingUpdate = false;
                Get<Button>("CloseUpdatePicker").IsEnabled = true;
                if (updateRequest == request) updateRequest = null;
                request.Dispose();
                if (staged != null) AppUpdates.Cleanup(Path.GetDirectoryName(staged));
            }
        }
        internal void ShowUpdateResult(UpdateInstaller.Result result)
        {
            CancelPlaceSelection();
            Get<Border>("UpdatePicker").Visibility = Visibility.Visible;
            Get<TextBlock>("UpdateCurrent").Text = "現在のバージョン  v" + AppUpdates.CurrentLabel;
            UpdateStatus(result.Success ? result.Message : "更新を完了できませんでした", false, false);
            Get<TextBlock>("UpdateNotes").Text = result.Success ? "登録地点・縮尺・表示設定・自動起動の設定を引き継ぎました。" : result.Message;
            AnimateEditorTransition(Get<Border>("UpdatePicker"), false, true);
        }
    }
}
