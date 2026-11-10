using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        const int TimetableChoice = 3, TimetablesPerPage = 3;
        int timetablePage, timetableMinute = -1;
        string editingTimetableId;
        bool editingHolidayText, timetableDeleteArmed;
        string timetableWeekdayText, timetableHolidayText;

        bool ShowingTimetables { get { return mode == 4 && choice[4] == TimetableChoice; } }

        void InitializeTimetables()
        {
            var picker = Get<Border>("TimetableEditor");
            InitializeOdpt();
            picker.PreviewMouseDown += delegate { FinishMarketTransition(); };
            picker.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                FinishMarketTransition();
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && !e.IsRepeat) { SaveTimetable(); e.Handled = true; }
            };
            Get<Button>("CloseTimetableEditor").Click += delegate { CloseTimetableEditor(true); };
            Get<Button>("CancelTimetableEditor").Click += delegate { CloseTimetableEditor(true); };
            Get<Button>("SaveTimetable").Click += delegate { SaveTimetable(); };
            Get<Button>("TimetableWeekdayTab").Click += delegate { SwitchTimetableText(false); };
            Get<Button>("TimetableHolidayTab").Click += delegate { SwitchTimetableText(true); };
            Get<TextBox>("TimetableText").TextChanged += delegate
            {
                if (editingHolidayText) timetableHolidayText = Get<TextBox>("TimetableText").Text; else timetableWeekdayText = Get<TextBox>("TimetableText").Text;
                if (Timetables.Parse(Get<TextBox>("TimetableText").Text).Departures.Count > 0 && Get<TextBlock>("TimetableEditorMessage").Text.StartsWith("時刻")) Get<TextBlock>("TimetableEditorMessage").Text = "";
                UpdateTimetableCount();
            };
            Get<TextBox>("TimetableStation").TextChanged += delegate { if (Get<TextBox>("TimetableStation").Text.Trim().Length > 0) Get<TextBlock>("TimetableEditorMessage").Text = ""; };
            Get<Button>("DeleteTimetable").Click += delegate
            {
                if (!timetableDeleteArmed) { timetableDeleteArmed = true; Get<Button>("DeleteTimetable").Content = "削除する"; Get<Button>("DeleteTimetable").Background = Art.Ink("#7E3442"); return; }
                try { Timetables.Delete(editingTimetableId); }
                catch (Exception ex) { if (!IsSaveError(ex)) throw; Get<TextBlock>("TimetableEditorMessage").Text = "削除できませんでした。もう一度お試しください"; return; }
                CloseTimetableEditor(true);
                ShowTimetableView();
            };
        }

        /// <summary>Lays out the transit tab for the 時刻表 choice. No network: everything comes from the saved timetables.</summary>
        void ShowTimetableView()
        {
            if (!ShowingTimetables) return;
            informationKey = -1;
            Get<TextBlock>("DataTitle").Text = "時刻表";
            Get<Button>("RefreshData").Visibility = Visibility.Collapsed;
            nextUpdate = DateTime.MaxValue;
            RenderTimetables();
            RefreshOdptTimetables();
            if (IsVisible && !IsClosing) { UpdateLayout(); effect.Start(true); }
        }

        void RenderTimetables()
        {
            var now = DateTime.Now;
            timetableMinute = now.Hour * 60 + now.Minute;
            bool holiday = Timetables.IsHolidaySchedule(now.Hour < 4 ? now.Date.AddDays(-1) : now.Date);
            Get<TextBlock>("DataStamp").Text = now.ToString("HH:mm", CultureInfo.InvariantCulture) + " 現在 · " + (holiday ? "土休日ダイヤ" : "平日ダイヤ");
            var body = Get<StackPanel>("InformationBody");
            body.Children.Clear();
            var entries = Timetables.All;
            int pages = Math.Max(1, (entries.Length + TimetablesPerPage - 1) / TimetablesPerPage);
            timetablePage = Math.Max(0, Math.Min(timetablePage, pages - 1));
            if (entries.Length == 0)
            {
                var empty = new StackPanel();
                empty.Children.Add(new TextBlock { Text = "時刻表が登録されていません", FontSize = 16, Foreground = aqua, Margin = new Thickness(0, 0, 0, 7) });
                empty.Children.Add(new TextBlock { Text = "よく使う駅の時刻表を、駅や鉄道会社の時刻表を見ながら「時と分」で登録すると、今の時刻から次の電車を表示します", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = muted });
                body.Children.Add(new Border { Child = empty, Padding = new Thickness(18), Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(8), Background = todoCard, Tag = "timetable-empty" });
            }
            foreach (var entry in entries.Skip(timetablePage * TimetablesPerPage).Take(TimetablesPerPage)) body.Children.Add(TimetableCard(entry, now));
            var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            var add = new Button { Content = "＋ 時刻表を登録", Background = Art.Ink("#193B48"), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Tag = "timetable-add" };
            add.Click += delegate { OpenTimetableEditor(null); };
            footer.Children.Add(add);
            if (pages > 1)
            {
                var pager = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var previous = new Button { Content = "‹", Width = 32, FontSize = 18, Padding = new Thickness(0), IsEnabled = timetablePage > 0 };
                var next = new Button { Content = "›", Width = 32, FontSize = 18, Padding = new Thickness(0), IsEnabled = timetablePage < pages - 1 };
                previous.Click += delegate { timetablePage--; RenderTimetables(); UpdateLayout(); effect.StartArea(PanelBounds(body)); };
                next.Click += delegate { timetablePage++; RenderTimetables(); UpdateLayout(); effect.StartArea(PanelBounds(body)); };
                pager.Children.Add(previous);
                pager.Children.Add(new TextBlock { Text = (timetablePage + 1) + " / " + pages, Width = 52, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = Art.Ink("#AFCAD2") });
                pager.Children.Add(next);
                footer.Children.Add(pager);
            }
            body.Children.Add(footer);
            if (entries.Any(e => e.FromOdpt)) body.Children.Add(OdptNotice());
            else if (entries.Length > 0)
                body.Children.Add(new TextBlock { Text = "登録した時刻表どおりの時刻です。遅れ・運休は「近隣」「JR」「私鉄」で確認できます", FontSize = 10, Foreground = Art.Ink("#6F939C"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 0, 0) });
        }

        UIElement TimetableCard(TimetableEntry entry, DateTime now)
        {
            string id = entry.Id;
            var card = new Border { Background = todoCard, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 8, 9), Margin = new Thickness(0, 6, 0, 0), Tag = "timetable-card" };
            var stack = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = entry.Heading };
            title.Inlines.Add(new System.Windows.Documents.Run(entry.Station) { Foreground = Art.Ink("#E3F4F4"), FontWeight = FontWeights.SemiBold });
            string sub = string.Join("  ", new[] { entry.Line, entry.Direction }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (sub.Length > 0) title.Inlines.Add(new System.Windows.Documents.Run("   " + sub) { Foreground = todoMuted, FontSize = 12 });
            if (entry.FromOdpt) title.Inlines.Add(new System.Windows.Documents.Run("   ODPT") { Foreground = Art.Ink("#6F939C"), FontSize = 10 });
            head.Children.Add(title);
            var edit = new Button { Content = "編集", FontSize = 11, Padding = new Thickness(8, 2, 8, 2), Foreground = todoMuted, Tag = "timetable-edit" };
            AutomationProperties.SetName(edit, entry.Station + " の時刻表を編集");
            edit.Click += delegate { OpenTimetableEditor(Timetables.Find(id)); };
            Grid.SetColumn(edit, 1);
            head.Children.Add(edit);
            stack.Children.Add(head);
            var upcoming = Timetables.Next(entry, now, 3);
            if (upcoming.Count == 0)
            {
                bool empty = string.IsNullOrWhiteSpace(entry.Weekday) && string.IsNullOrWhiteSpace(entry.Holiday);
                string reason = !empty ? "この先の電車はありません"
                    : entry.FromOdpt && entry.ChallengeData && Odpt.ChallengeExpired(DateTime.Today) ? "チャレンジ限定データの利用期間（2027年3月12日まで）が終わったため、時刻を削除しました"
                    : entry.FromOdpt ? "ODPTから時刻表を取得できていません。アクセストークンを確認してください"
                    : "時刻が入っていません。「編集」から入力してください";
                stack.Children.Add(new TextBlock { Text = reason, FontSize = 12, Foreground = todoMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) });
            }
            else
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                for (int i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition());
                for (int i = 0; i < upcoming.Count; i++)
                {
                    var train = upcoming[i];
                    int wait = (int)Math.Ceiling((train.Time - now).TotalMinutes);
                    var cell = new StackPanel { Tag = "timetable-train" };
                    cell.Children.Add(new TextBlock { Text = train.Time.ToString("H:mm", CultureInfo.InvariantCulture), FontFamily = new FontFamily("Bahnschrift"), FontSize = i == 0 ? 26 : 22, Foreground = i == 0 ? aqua : Art.Ink("#D9E9EB") });
                    var info = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(1, 0, 6, 0) };
                    info.Inlines.Add(new System.Windows.Documents.Run(wait <= 0 ? "まもなく" : wait >= 60 ? "あと" + wait / 60 + "時間" + (wait % 60 > 0 ? wait % 60 + "分" : "") : "あと" + wait + "分") { Foreground = wait <= 3 ? todoRed : wait <= 10 ? todoAmber : todoSoft });
                    if (!string.IsNullOrEmpty(train.Label)) info.Inlines.Add(new System.Windows.Documents.Run("  " + train.Label) { Foreground = todoMuted });
                    info.ToolTip = string.IsNullOrEmpty(train.Label) ? null : train.Label;
                    cell.Children.Add(info);
                    Grid.SetColumn(cell, i);
                    row.Children.Add(cell);
                }
                stack.Children.Add(row);
            }
            card.Child = stack;
            return card;
        }

        /// <summary>Called every second; re-renders only when the minute changes so the countdown stays current.</summary>
        void TickTimetables(DateTime now)
        {
            if (!ShowingTimetables || !IsVisible || IsClosing) return;
            int minute = now.Hour * 60 + now.Minute;
            if (minute == timetableMinute) return;
            RenderTimetables();
            UpdateLayout();
            if (!effect.Busy) effect.Refresh();
        }

        // ---------- Editor ----------

        void OpenTimetableEditor(TimetableEntry entry)
        {
            Get<Border>("SettingsPicker").Visibility = Visibility.Collapsed; Get<Border>("SpeedPicker").Visibility = Visibility.Collapsed;
            Get<Border>("TimerPicker").Visibility = Visibility.Collapsed;
            editingTimetableId = entry == null ? null : entry.Id;
            timetableDeleteArmed = false;
            Get<TextBlock>("TimetableEditorTitle").Text = entry == null ? "時刻表を登録" : "時刻表を編集";
            Get<Button>("SaveTimetable").Content = entry == null ? "登録する" : "保存";
            Get<Button>("DeleteTimetable").Visibility = entry == null ? Visibility.Collapsed : Visibility.Visible;
            Get<Button>("DeleteTimetable").Content = "削除";
            Get<Button>("DeleteTimetable").Background = Brushes.Transparent;
            Get<TextBox>("TimetableStation").Text = entry == null ? "" : entry.Station;
            Get<TextBox>("TimetableLine").Text = entry == null ? "" : entry.Line;
            Get<TextBox>("TimetableDirection").Text = entry == null ? "" : entry.Direction;
            timetableWeekdayText = entry == null ? "" : (entry.Weekday ?? "").Replace("\n", Environment.NewLine);
            timetableHolidayText = entry == null ? "" : (entry.Holiday ?? "").Replace("\n", Environment.NewLine);
            Get<TextBlock>("TimetableEditorMessage").Text = "";
            editingHolidayText = true; SwitchTimetableText(false);
            PrepareOdptEditor(entry);
            Get<Button>("SaveTimetable").IsEnabled = true;
            var picker = Get<Border>("TimetableEditor");
            picker.Visibility = Visibility.Visible;
            AnimateEditorTransition(picker, false);
            Dispatcher.BeginInvoke(new Action(delegate { if (picker.IsVisible) Keyboard.Focus(Get<TextBox>(odptMode ? "OdptQuery" : entry == null ? "TimetableStation" : "TimetableText")); }), DispatcherPriority.Input);
        }

        void SwitchTimetableText(bool holiday)
        {
            if (editingHolidayText == holiday) return;
            editingHolidayText = holiday;
            Get<TextBox>("TimetableText").Text = holiday ? timetableHolidayText : timetableWeekdayText;
            Get<Button>("TimetableWeekdayTab").Background = Art.Ink(holiday ? "#193B48" : "#287181");
            Get<Button>("TimetableHolidayTab").Background = Art.Ink(holiday ? "#287181" : "#193B48");
            UpdateTimetableCount();
        }

        void UpdateTimetableCount()
        {
            string text = editingHolidayText ? timetableHolidayText : timetableWeekdayText;
            var parsed = Timetables.Parse(text);
            var count = Get<TextBlock>("TimetableCount");
            if (string.IsNullOrWhiteSpace(text))
            {
                count.Text = editingHolidayText ? "空欄なら平日と同じ時刻を使います" : "まだ入力されていません";
                count.Foreground = todoMuted;
                return;
            }
            string range = parsed.Departures.Count == 0 ? "" : "  " + Timetables.Clock(parsed.Departures[0].Minutes) + "〜" + Timetables.Clock(parsed.Departures[parsed.Departures.Count - 1].Minutes);
            count.Text = parsed.Departures.Count + "本" + range + (parsed.Unreadable.Count > 0 ? "  ·  読めない行 " + string.Join(",", parsed.Unreadable.Take(4)) + (parsed.Unreadable.Count > 4 ? "…" : "") : "");
            count.Foreground = parsed.Unreadable.Count > 0 ? Art.Ink("#FFB088") : todoSoft;
            count.ToolTip = parsed.Unreadable.Count > 0 ? "読めない行は無視されます（行番号：" + string.Join(", ", parsed.Unreadable) + "）" : null;
        }

        void CloseTimetableEditor(bool animate)
        {
            var picker = Get<Border>("TimetableEditor");
            if (picker.Visibility != Visibility.Visible) return;
            if (odptRequest != null) { odptRequest.Cancel(); odptRequest = null; }
            if (animate) AnimateEditorTransition(picker, true);
            else { FinishMarketTransition(); picker.Visibility = Visibility.Collapsed; }
        }

        void SaveTimetable()
        {
            if (odptMode)
            {
                if (!SaveOdptTimetable()) return;
                if (editingTimetableId == null) timetablePage = int.MaxValue;
                CloseTimetableEditor(true);
                ShowTimetableView();
                return;
            }
            string station = Get<TextBox>("TimetableStation").Text.Trim();
            var message = Get<TextBlock>("TimetableEditorMessage");
            if (station.Length == 0) { message.Text = "駅名を入力してください"; Keyboard.Focus(Get<TextBox>("TimetableStation")); return; }
            var weekday = Timetables.Parse(timetableWeekdayText);
            var holiday = Timetables.Parse(timetableHolidayText);
            if (weekday.Departures.Count == 0 && holiday.Departures.Count == 0) { message.Text = "時刻を1本以上入力してください（例  7: 04 13 21）"; Keyboard.Focus(Get<TextBox>("TimetableText")); return; }
            try { Timetables.Put(editingTimetableId, station, Get<TextBox>("TimetableLine").Text, Get<TextBox>("TimetableDirection").Text, timetableWeekdayText, timetableHolidayText); }
            catch (Exception ex) { if (!IsSaveError(ex)) throw; message.Text = "保存できませんでした。もう一度お試しください"; return; }
            if (editingTimetableId == null) timetablePage = int.MaxValue;
            CloseTimetableEditor(true);
            ShowTimetableView();
        }
    }
}
