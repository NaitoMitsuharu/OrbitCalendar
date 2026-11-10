using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        bool odptMode;
        Odpt.OperatorInfo odptOperator = Odpt.Operators[0];
        Odpt.StationHit odptStation;
        Odpt.DirectionChoice odptChoice;
        CancellationTokenSource odptRequest;
        readonly Dictionary<string, DateTime> odptAttempts = new Dictionary<string, DateTime>();
        bool odptRefreshing;
        const string OdptContact = "https://github.com/NaitoMitsuharu/OrbitCalendar/issues";

        void InitializeOdpt()
        {
            Get<Button>("TimetableOdptMode").Click += delegate { SetTimetableMode(true); };
            Get<Button>("TimetableManualMode").Click += delegate { SetTimetableMode(false); };
            Get<Button>("OdptSearch").Click += delegate { SearchOdptStations(); };
            Get<TextBox>("OdptQuery").KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && !e.IsRepeat) { SearchOdptStations(); e.Handled = true; } };
            Get<Button>("OdptTokenButton").Click += delegate { ShowOdptTokens(true); };
            Get<Button>("OdptTokenCancel").Click += delegate { ShowOdptTokens(false); };
            Get<Button>("OdptTokenSave").Click += delegate
            {
                try { Odpt.SaveTokens(Get<PasswordBox>("OdptStandardToken").Password, Get<PasswordBox>("OdptChallengeToken").Password); }
                catch (Exception ex)
                {
                    if (!(IsSaveError(ex) || ex is System.Security.Cryptography.CryptographicException)) throw;
                    Get<TextBlock>("TimetableEditorMessage").Text = "トークンを保存できませんでした"; return;
                }
                odptAttempts.Clear();
                ShowOdptTokens(false);
                OdptStatus(TokenHint(), false);
            };
        }

        IEnumerable<UIElement> ManualTimetableParts()
        {
            var grid = (Grid)Get<TextBox>("TimetableText").Parent;
            return grid.Children.OfType<UIElement>().Where(e => { int row = Grid.GetRow(e); return row >= 1 && row <= 6 && e != Get<Grid>("OdptPanel") && e != Get<Grid>("OdptTokenPanel"); });
        }

        void SetTimetableMode(bool odpt)
        {
            odptMode = odpt;
            foreach (var part in ManualTimetableParts()) part.Visibility = odpt ? Visibility.Collapsed : Visibility.Visible;
            Get<Grid>("OdptPanel").Visibility = odpt ? Visibility.Visible : Visibility.Collapsed;
            Get<Grid>("OdptTokenPanel").Visibility = Visibility.Collapsed;
            Get<Button>("TimetableOdptMode").Background = Art.Ink(odpt ? "#287181" : "#193B48");
            Get<Button>("TimetableManualMode").Background = Art.Ink(odpt ? "#193B48" : "#287181");
            Get<TextBlock>("TimetableEditorMessage").Text = "";
            if (odpt) { RenderOdptOperators(); if (odptChoice == null && odptStation == null) OdptStatus(TokenHint(), false); }
        }

        string TokenHint()
        {
            if (odptOperator.Challenge && Odpt.ChallengeExpired(DateTime.Today)) return odptOperator.Title + "のデータはチャレンジ限定で、利用期間（2027年3月12日まで）が終了しています";
            if (!Odpt.HasToken(odptOperator.Challenge)) return (odptOperator.Challenge ? "チャレンジ用" : "通常") + "のアクセストークンが未設定です。右上の「アクセストークン」から設定してください";
            return "駅名を入れて検索し、方面を選んでください";
        }

        void OdptStatus(string text, bool error)
        {
            var status = Get<TextBlock>("OdptStatus");
            status.Text = text;
            status.Foreground = error ? Art.Ink("#FFB088") : todoSoft;
        }

        void ShowOdptTokens(bool show)
        {
            if (show)
            {
                Get<PasswordBox>("OdptStandardToken").Password = Odpt.Token(false);
                Get<PasswordBox>("OdptChallengeToken").Password = Odpt.Token(true);
            }
            Get<Grid>("OdptTokenPanel").Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Get<Grid>("OdptPanel").Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            Get<Button>("SaveTimetable").IsEnabled = !show;
            if (show) Keyboard.Focus(Get<PasswordBox>(Odpt.HasToken(false) || odptOperator.Challenge ? "OdptChallengeToken" : "OdptStandardToken"));
        }

        void RenderOdptOperators()
        {
            var grid = Get<UniformGrid>("OdptOperators");
            grid.Children.Clear();
            foreach (var op in Odpt.Operators)
            {
                var value = op;
                bool active = op == odptOperator;
                var button = new Button { Content = op.Title, FontSize = 11, Height = 28, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(2, 0, 2, 0),
                    Background = Art.Ink(active ? "#287181" : "#193B48"), Foreground = Art.Ink(active ? "#E5FFFF" : "#B8D9DE"),
                    ToolTip = op.Challenge ? "チャレンジ限定データ（2027年3月12日まで）" : "ODPT基本ライセンスのデータ" };
                button.Click += delegate
                {
                    if (odptOperator == value) return;
                    odptOperator = value; odptStation = null; odptChoice = null;
                    Get<StackPanel>("OdptResults").Children.Clear();
                    RenderOdptOperators();
                    OdptStatus(TokenHint(), false);
                };
                grid.Children.Add(button);
            }
        }

        async void SearchOdptStations()
        {
            string query = Get<TextBox>("OdptQuery").Text.Trim();
            if (query.Length == 0) { OdptStatus("駅名を入力してください", true); return; }
            if (!Odpt.HasToken(odptOperator.Challenge)) { ShowOdptTokens(true); return; }
            if (odptRequest != null) odptRequest.Cancel();
            var request = odptRequest = new CancellationTokenSource();
            var results = Get<StackPanel>("OdptResults");
            results.Children.Clear();
            odptStation = null; odptChoice = null;
            OdptStatus(odptOperator.Title + "の駅を検索しています…", false);
            try
            {
                var hits = await Odpt.FindStations(odptOperator, query, request.Token);
                if (request != odptRequest) return;
                if (hits.Count == 0) { OdptStatus("「" + query + "」に一致する" + odptOperator.Title + "の駅が見つかりません", true); return; }
                OdptStatus(hits.Count > 5 ? "候補が多いため上位5件を表示しています。駅名を詳しく入れると絞り込めます" : "駅と路線を選んでください", false);
                foreach (var hit in hits.Take(5))
                {
                    var value = hit;
                    var button = OdptResultButton(hit.Title, hit.RailwayTitle);
                    button.Click += delegate { ChooseOdptStation(value); };
                    results.Children.Add(button);
                }
            }
            catch (OdptException ex) { if (request == odptRequest) OdptStatus(ex.Message, true); }
            catch (OperationCanceledException) { }
        }

        Button OdptResultButton(string title, string detail)
        {
            var text = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
            text.Inlines.Add(new System.Windows.Documents.Run(title) { Foreground = Art.Ink("#E3F4F4") });
            if (!string.IsNullOrEmpty(detail)) text.Inlines.Add(new System.Windows.Documents.Run("   " + detail) { Foreground = todoMuted, FontSize = 11 });
            var content = new Grid { Width = 372 };
            content.Children.Add(text);
            return new Button { Content = content, Height = 30, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(10, 0, 10, 0), Background = Art.Ink("#193B48"), Tag = "odpt-result" };
        }

        async void ChooseOdptStation(Odpt.StationHit station)
        {
            if (odptRequest != null) odptRequest.Cancel();
            var request = odptRequest = new CancellationTokenSource();
            odptStation = station; odptChoice = null;
            var results = Get<StackPanel>("OdptResults");
            results.Children.Clear();
            OdptStatus(station.Title + "（" + station.RailwayTitle + "）の時刻表を取得しています…", false);
            try
            {
                var directions = await Odpt.Directions(odptOperator, station, request.Token);
                if (request != odptRequest) return;
                if (directions.Count == 0) { OdptStatus(station.Title + "（" + station.RailwayTitle + "）の駅時刻表はODPTにありません", true); return; }
                OdptStatus("方面を選んでください", false);
                foreach (var choice in directions)
                {
                    var value = choice;
                    var button = OdptResultButton(choice.Label, "平日 " + choice.WeekdayCount + "本 · 土休日 " + choice.HolidayCount + "本");
                    button.Click += delegate { ChooseOdptDirection(value, (Button)button); };
                    results.Children.Add(button);
                }
            }
            catch (OdptException ex) { if (request == odptRequest) OdptStatus(ex.Message, true); }
            catch (OperationCanceledException) { }
        }

        void ChooseOdptDirection(Odpt.DirectionChoice choice, Button chosen)
        {
            odptChoice = choice;
            foreach (var button in Get<StackPanel>("OdptResults").Children.OfType<Button>()) button.Background = Art.Ink(button == chosen ? "#287181" : "#193B48");
            OdptStatus(odptStation.Title + "  " + odptStation.RailwayTitle + "  " + choice.Label + " を登録します（平日 " + choice.WeekdayCount + "本 · 土休日 " + choice.HolidayCount + "本）", false);
            Get<TextBlock>("TimetableEditorMessage").Text = "";
        }

        /// <summary>Opens the editor for an ODPT entry or a new one, in ODPT mode.</summary>
        void PrepareOdptEditor(TimetableEntry entry)
        {
            odptStation = null; odptChoice = null;
            Get<StackPanel>("OdptResults").Children.Clear();
            if (entry != null && entry.FromOdpt) odptOperator = Odpt.Operator(entry.OdptOperator) ?? Odpt.Operators[0];
            SetTimetableMode(entry == null || entry.FromOdpt);
            if (entry != null && entry.FromOdpt)
            {
                Get<TextBox>("OdptQuery").Text = entry.Station;
                var fetched = entry.FetchedAt;
                OdptStatus("登録中：" + entry.Heading + (fetched.HasValue ? "（" + fetched.Value.ToLocalTime().ToString("M/d HH:mm") + " 取得）" : "") + "。変更するときは検索し直してください", false);
            }
            else
            {
                Get<TextBox>("OdptQuery").Text = "";
                OdptStatus(TokenHint(), false);
            }
        }

        bool SaveOdptTimetable()
        {
            var message = Get<TextBlock>("TimetableEditorMessage");
            if (odptChoice == null || odptStation == null)
            {
                var existing = editingTimetableId == null ? null : Timetables.Find(editingTimetableId);
                if (existing != null && existing.FromOdpt) return true; // nothing changed
                message.Text = "駅を検索して、方面を選んでください"; return false;
            }
            try
            {
                Timetables.PutOdpt(editingTimetableId, odptStation.Title, odptStation.RailwayTitle, odptChoice.Label, odptChoice.Weekday, odptChoice.Holiday,
                    odptOperator.Id, odptStation.Id, odptStation.RailwayId, odptChoice.RailDirectionId);
            }
            catch (Exception ex) { if (!IsSaveError(ex)) throw; message.Text = "保存できませんでした。もう一度お試しください"; return false; }
            return true;
        }

        /// <summary>
        /// Keeps ODPT timetables current (ガイドライン: update when the source updates): once a day per entry,
        /// one request at a time, retrying failures after an hour. Challenge data is purged after the licence ends.
        /// </summary>
        async void RefreshOdptTimetables()
        {
            if (Timetables.PurgeExpiredChallengeData(DateTime.Today) > 0 && ShowingTimetables) RenderTimetables();
            if (odptRefreshing) return;
            var due = Timetables.All.Where(e => e.FromOdpt && !(e.ChallengeData && Odpt.ChallengeExpired(DateTime.Today)) && Odpt.HasToken(e.ChallengeData)
                && (!e.FetchedAt.HasValue || DateTime.UtcNow - e.FetchedAt.Value > TimeSpan.FromHours(24))
                && (!odptAttempts.ContainsKey(e.Id) || DateTime.UtcNow - odptAttempts[e.Id] > TimeSpan.FromHours(1))).ToList();
            if (due.Count == 0) return;
            odptRefreshing = true;
            try
            {
                foreach (var entry in due)
                {
                    odptAttempts[entry.Id] = DateTime.UtcNow;
                    try { await Odpt.Refresh(entry, CancellationToken.None); }
                    catch (OdptException) { }
                    catch (Exception ex) { if (!IsSaveError(ex)) throw; }
                }
            }
            finally { odptRefreshing = false; }
            if (ShowingTimetables && IsVisible && !IsClosing && Get<Border>("TimetableEditor").Visibility != Visibility.Visible) { RenderTimetables(); UpdateLayout(); if (!effect.Busy) effect.Refresh(); }
        }

        /// <summary>第10条3: tell users the data's accuracy is not guaranteed by ODPT/providers, and where to ask about the app.</summary>
        UIElement OdptNotice()
        {
            var text = new TextBlock { FontSize = 10, Foreground = Art.Ink("#6F939C"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0), Tag = "odpt-notice" };
            text.Inlines.Add(new System.Windows.Documents.Run("ODPTの時刻表は公共交通オープンデータセンターのデータです。センター・協議会・鉄道会社はデータの正確性を保証しません。アプリについてのお問い合わせは "));
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("GitHub")) { Foreground = Art.Ink("#7BE5DA") };
            link.Click += delegate { OpenLink(OdptContact); };
            text.Inlines.Add(link);
            text.Inlines.Add(new System.Windows.Documents.Run(" へ（鉄道会社には問い合わせないでください）"));
            return text;
        }
    }
}
