using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OrbitCalendar
{
    internal partial class CalendarPanel
    {
        const int TodoMode = 5, TodosPerPage = 5, MaxTodoImages = 6;
        string todoDetailId, todoUndoId, editingTodoId;
        int todoPage;
        bool todoDeleteArmed, todoShowDone;
        DateTime? editingDue;
        DateTime todoMonth;
        List<string> editingImages = new List<string>(), importedImages = new List<string>();
        TodoDraft todoDraft;
        bool reopenTodoEditor;
        readonly DispatcherTimer todoUndoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        static readonly string[] weekdays = { "日", "月", "火", "水", "木", "金", "土" };
        readonly Brush todoAmber = Art.Ink("#F4DD8E"), todoRed = Art.Ink("#FF9699"), todoSoft = Art.Ink("#B8E9E4"), todoMuted = Art.Ink("#92B5BE"), todoCard = Art.Ink("#152E39");

        /// <summary>Editor contents kept in memory when the editor closes without 取消, restored on the next open.</summary>
        sealed class TodoDraft
        {
            public string Id, Title, Notes;
            public DateTime? Due;
            public List<string> Images, Imported;
        }

        void InitializeTodos()
        {
            Todos.PruneImages(null);
            Get<Button>("AddTodo").Click += delegate { OpenTodoEditor(null, null); };
            Get<Button>("TodoPrevious").Click += delegate { todoPage--; ShowTodos(); };
            Get<Button>("TodoNext").Click += delegate { todoPage++; ShowTodos(); };
            Get<Button>("TodoBack").Click += delegate { CloseTodoDetail(); };
            Get<Button>("TodoDoneToggle").Click += delegate { todoShowDone = !todoShowDone; todoPage = 0; todoDetailId = null; ShowTodos(); };
            Get<Button>("CompleteTodo").Click += delegate
            {
                var item = Todos.Find(todoDetailId);
                if (item == null) return;
                if (item.Completed == null) CompleteTodo(item.Id); else RestoreTodo(item.Id);
            };
            Get<Button>("EditTodo").Click += delegate { var item = Todos.Find(todoDetailId); if (item != null) OpenTodoEditor(item, null); };
            Get<Button>("DeleteTodo").Click += delegate
            {
                if (!todoDeleteArmed) { todoDeleteArmed = true; StyleDeleteButton(); effect.Refresh(); return; }
                try { Todos.Delete(todoDetailId); }
                catch (Exception ex) { if (!IsSaveError(ex)) throw; Get<TextBlock>("TodoDetailDue").Text = "削除できませんでした。もう一度お試しください"; effect.Refresh(); return; }
                todoDetailId = null; DrawDates(); ShowTodos();
            };
            Get<Button>("TodoUndoButton").Click += delegate
            {
                todoUndoTimer.Stop();
                string id = todoUndoId; todoUndoId = null;
                try { Todos.SetCompleted(id, false); } catch (Exception ex) { if (!IsSaveError(ex)) throw; }
                DrawDates(); ShowTodos();
            };
            todoUndoTimer.Tick += delegate
            {
                todoUndoTimer.Stop(); todoUndoId = null;
                if (mode == TodoMode && IsVisible && !IsClosing) { RenderTodos(); effect.StartArea(PanelBounds(Get<Grid>("TodoFooter"))); }
            };
            var scroll = Get<ScrollViewer>("TodoDetailScroll");
            scroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
            { if (e.VerticalChange != 0 && mode == TodoMode && IsVisible) RefreshViewAfterLayout(); };
            var strip = Get<TextBlock>("TodoDayTasks");
            strip.MouseLeftButtonUp += delegate
            {
                var due = Todos.DueOn(selected);
                if (due.Length == 0) return;
                todoShowDone = false;
                todoDetailId = due.Length == 1 ? due[0].Id : null;
                SwitchView(TodoMode);
            };
            InitializeTodoEditor();
        }

        static bool IsSaveError(Exception ex) { return ex is IOException || ex is UnauthorizedAccessException; }

        string ShortDate(DateTime day)
        {
            string date = day.Year == today.Year ? day.ToString("M/d", CultureInfo.InvariantCulture) : day.ToString("yyyy/M/d", CultureInfo.InvariantCulture);
            return date + " (" + weekdays[(int)day.DayOfWeek] + ")";
        }

        string DueLabel(DateTime? due, out Brush brush)
        {
            brush = todoMuted;
            if (!due.HasValue) return "期限なし";
            int days = (due.Value - today).Days;
            if (days < 0) { brush = todoRed; return "期限切れ · " + ShortDate(due.Value); }
            if (days == 0) { brush = todoAmber; return "今日まで"; }
            brush = days <= 2 ? todoAmber : todoSoft;
            if (days == 1) return "明日まで · " + ShortDate(due.Value);
            return ShortDate(due.Value) + " まで" + (days <= 7 ? " · あと" + days + "日" : "");
        }

        /// <summary>The ToDo tab shows how many tasks are due today or overdue, red when any are overdue.</summary>
        void UpdateTodoTab()
        {
            if (tabs == null) return;
            var open = Todos.Open;
            int overdue = open.Count(i => i.DueDate < today), urgent = overdue + open.Count(i => i.DueDate == today);
            var label = new TextBlock();
            label.Inlines.Add(new Run("ToDo"));
            if (urgent > 0) label.Inlines.Add(new Run(" " + urgent) { Foreground = overdue > 0 ? todoRed : todoAmber, FontWeight = FontWeights.SemiBold });
            tabs[TodoMode].Content = label;
            tabs[TodoMode].ToolTip = urgent == 0 ? null : (overdue > 0 ? "期限切れ " + overdue + "件" : "") + (overdue > 0 && urgent > overdue ? " · " : "") + (urgent > overdue ? "今日まで " + (urgent - overdue) + "件" : "");
            AutomationProperties.SetName(tabs[TodoMode], urgent == 0 ? "ToDo" : "ToDo 期限が今日までのタスク" + urgent + "件");
        }

        /// <summary>Re-renders the ToDo tab and replays the particle assembly over it.</summary>
        void ShowTodos()
        {
            if (mode != TodoMode) return;
            RenderTodos();
            if (IsVisible && !IsClosing) effect.StartArea(PanelBounds(Get<Grid>("TodoBody")));
        }

        void RenderTodos()
        {
            UpdateTodoTab();
            var item = todoDetailId == null ? null : Todos.Find(todoDetailId);
            if (item == null || (item.Completed != null) != todoShowDone) todoDetailId = null;
            bool detail = todoDetailId != null;
            Get<Grid>("TodoDetail").Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
            foreach (var name in new[] { "TodoHeader", "TodoList", "TodoFooter" }) SetVisible(name, !detail);
            if (detail) { RenderTodoDetail(item); return; }
            todoDeleteArmed = false;
            var open = Todos.Open;
            var done = Todos.Done;
            if (todoShowDone && done.Length == 0) todoShowDone = false;
            var shown = todoShowDone ? done : open;
            int overdue = open.Count(i => i.DueDate < today), dueToday = open.Count(i => i.DueDate == today);
            var summary = Get<TextBlock>("TodoSummary");
            if (todoShowDone)
            {
                Get<TextBlock>("TodoHeading").Text = "完了済み " + done.Length + "件";
                summary.Text = "完了が新しい順 · ↩ で未完了に戻す";
                summary.Foreground = Art.Ink("#D0E5EB");
            }
            else
            {
                Get<TextBlock>("TodoHeading").Text = open.Length == 0 ? "タスクはありません" : "タスク " + open.Length + "件";
                var parts = new List<string>();
                if (overdue > 0) parts.Add("期限切れ " + overdue);
                if (dueToday > 0) parts.Add("今日まで " + dueToday);
                summary.Text = open.Length == 0 ? "右上の ＋ から追加できます" : parts.Count > 0 ? string.Join(" · ", parts) + "  ·  期限の近い順" : "期限の近い順 · クリックで詳細";
                summary.Foreground = overdue > 0 ? todoRed : Art.Ink("#D0E5EB");
            }
            var list = Get<StackPanel>("TodoList");
            list.Children.Clear();
            int pages = Math.Max(1, (shown.Length + TodosPerPage - 1) / TodosPerPage);
            todoPage = Math.Max(0, Math.Min(todoPage, pages - 1));
            if (shown.Length == 0)
            {
                var empty = new StackPanel();
                empty.Children.Add(new TextBlock { Text = "✓", FontSize = 25, Foreground = aqua });
                empty.Children.Add(new TextBlock { Text = "すべて完了しています", FontSize = 16, Foreground = aqua, Margin = new Thickness(0, 9, 0, 7) });
                empty.Children.Add(new TextBlock { Text = "期限を設定したタスクは、カレンダーの日付に印が付きます。日付をダブルクリックすると、その日が期限のタスクを追加できます", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = muted });
                list.Children.Add(new Border { Child = empty, Padding = new Thickness(18), Margin = new Thickness(0, 6, 0, 0), CornerRadius = new CornerRadius(8), Background = todoCard, Tag = "todo-empty" });
            }
            foreach (var task in shown.Skip(todoPage * TodosPerPage).Take(TodosPerPage)) list.Children.Add(TodoCard(task));
            Get<StackPanel>("TodoPager").Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
            Get<TextBlock>("TodoPage").Text = (todoPage + 1) + " / " + pages;
            Get<Button>("TodoPrevious").IsEnabled = todoPage > 0;
            Get<Button>("TodoNext").IsEnabled = todoPage < pages - 1;
            var undone = todoUndoId == null ? null : Todos.Find(todoUndoId);
            bool undo = !todoShowDone && undone != null && undone.Completed != null;
            Get<StackPanel>("TodoUndo").Visibility = undo ? Visibility.Visible : Visibility.Collapsed;
            if (undo) Get<TextBlock>("TodoUndoText").Text = "「" + undone.Title + "」を完了しました";
            var toggle = Get<Button>("TodoDoneToggle");
            toggle.Content = todoShowDone ? "‹  未完了のタスク" : "完了済み " + done.Length + "件  ›";
            toggle.Visibility = !undo && (todoShowDone || done.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        UIElement TodoCard(TodoItem task)
        {
            string id = task.Id;
            bool finished = task.Completed != null;
            var card = new Border { Background = todoCard, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 0, 6), Height = 54, Tag = "todo-card" };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var mark = new Grid { Width = 22, Height = 22 };
            mark.Children.Add(new Ellipse { Stroke = aqua, StrokeThickness = 1.5, Fill = finished ? Art.Ink("#1E4A52") : null });
            mark.Children.Add(new TextBlock { Text = finished ? "↩" : "✓", FontSize = 12, Foreground = Art.Ink(finished ? "#B8E9E4" : "#3F6B73"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) });
            var action = new Button { Content = mark, Padding = new Thickness(0), ToolTip = finished ? "未完了に戻す" : "Done（完了にする）", Tag = "todo-done" };
            AutomationProperties.SetName(action, task.Title + (finished ? " を未完了に戻す" : " を完了にする"));
            action.Click += delegate { if (finished) RestoreTodo(id); else CompleteTodo(id); };
            grid.Children.Add(action);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 352 };
            text.Children.Add(new TextBlock { Text = task.Title, FontSize = 15, Foreground = Art.Ink(finished ? "#A9C4CB" : "#E3F4F4"), TextTrimming = TextTrimming.CharacterEllipsis });
            var meta = new TextBlock { FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            if (finished)
            {
                var completed = Todos.CompletedDate(task);
                meta.Inlines.Add(new Run(completed.HasValue ? ShortDate(completed.Value.Date) + " に完了" : "完了") { Foreground = todoSoft });
                if (task.DueDate.HasValue) meta.Inlines.Add(new Run("   期限 " + ShortDate(task.DueDate.Value)) { Foreground = todoMuted });
            }
            else
            {
                Brush dueBrush;
                meta.Inlines.Add(new Run(DueLabel(task.DueDate, out dueBrush)) { Foreground = dueBrush });
                var extras = new List<string>();
                if (task.ImageCount > 0) extras.Add("画像 " + task.ImageCount);
                if (task.HasLinks) extras.Add("リンク");
                string firstLine = (task.Notes ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                if (firstLine != null) extras.Add(firstLine);
                if (extras.Count > 0) meta.Inlines.Add(new Run("   " + string.Join(" · ", extras)) { Foreground = todoMuted });
            }
            text.Children.Add(meta);
            var open = new Button { Content = text, Padding = new Thickness(2, 0, 12, 0), ToolTip = task.Title, Tag = "todo-open" };
            AutomationProperties.SetName(open, task.Title + " の詳細");
            open.Click += delegate { todoDetailId = id; todoDeleteArmed = false; ShowTodos(); };
            Grid.SetColumn(open, 1);
            grid.Children.Add(open);
            card.Child = grid;
            return card;
        }

        void StyleDeleteButton()
        {
            var delete = Get<Button>("DeleteTodo");
            delete.Content = todoDeleteArmed ? "削除する" : "削除";
            delete.Background = todoDeleteArmed ? Art.Ink("#7E3442") : Brushes.Transparent;
            delete.ToolTip = todoDeleteArmed ? "もう一度押すと完全に削除します" : "完了ではなく、記録ごと削除します";
        }

        void RenderTodoDetail(TodoItem item)
        {
            StyleDeleteButton();
            bool finished = item.Completed != null;
            Get<Button>("CompleteTodo").Content = finished ? "↩  未完了に戻す" : "✓  Done";
            Get<Button>("CompleteTodo").ToolTip = finished ? "一覧に戻して、もう一度やることにする" : "完了して一覧から外す";
            Get<Button>("TodoBack").Content = finished ? "‹  完了済み" : "‹  一覧";
            Get<TextBlock>("TodoDetailTitle").Text = item.Title;
            Get<TextBlock>("TodoDetailTitle").ToolTip = item.Title;
            Brush brush;
            DueLabel(item.DueDate, out brush);
            var dueText = Get<TextBlock>("TodoDetailDue");
            string dueLine = "期限なし";
            if (item.DueDate.HasValue)
            {
                var day = item.DueDate.Value;
                int days = (day - today).Days;
                string relative = days < 0 ? "期限切れ（" + (-days) + "日超過）" : days == 0 ? "今日まで" : days == 1 ? "明日まで" : "あと" + days + "日";
                dueLine = "期限  " + day.ToString("yyyy/M/d", CultureInfo.InvariantCulture) + " (" + weekdays[(int)day.DayOfWeek] + ")" + (finished ? "" : "   ·   " + relative);
            }
            var completed = Todos.CompletedDate(item);
            if (finished) { dueLine += completed.HasValue ? "   ·   " + ShortDate(completed.Value.Date) + " に完了" : "   ·   完了"; brush = todoSoft; }
            dueText.Text = dueLine;
            dueText.Foreground = brush;
            var content = Get<StackPanel>("TodoDetailContent");
            content.Children.Clear();
            if (!string.IsNullOrEmpty(item.Notes)) content.Children.Add(NotesBlock(item.Notes));
            var images = item.Images ?? new List<string>();
            foreach (var name in images)
            {
                string file = Todos.ImagePath(name);
                var picture = Todos.LoadImage(name, 760);
                if (picture == null) { content.Children.Add(new TextBlock { Text = "画像を読み込めません（" + name + "）", FontSize = 11, Foreground = todoMuted, Margin = new Thickness(0, 10, 0, 0) }); continue; }
                var image = new Image { Source = picture, Stretch = Stretch.Uniform, MaxHeight = images.Count == 1 ? 240 : 170, HorizontalAlignment = HorizontalAlignment.Left };
                var open = new Button { Content = image, Padding = new Thickness(0), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "クリックで画像を開く", Tag = "todo-image" };
                open.Click += delegate { if (File.Exists(file)) Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); };
                content.Children.Add(open);
            }
            if (content.Children.Count == 0) content.Children.Add(new TextBlock { Text = "説明はありません。「編集」から説明や画像を追加できます", FontSize = 12, Foreground = todoMuted, TextWrapping = TextWrapping.Wrap });
            Get<ScrollViewer>("TodoDetailScroll").ScrollToTop();
        }

        TextBlock NotesBlock(string notes)
        {
            var block = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Art.Ink("#DBEDF0"), LineHeight = 20 };
            int position = 0;
            foreach (var link in Todos.Links(notes))
            {
                if (link.Index > position) block.Inlines.Add(new Run(notes.Substring(position, link.Index - position)));
                string url = link.Url;
                var hyperlink = new Hyperlink(new Run(url)) { Foreground = Art.Ink("#7BE5DA"), Cursor = Cursors.Hand, ToolTip = url + " を開く" };
                hyperlink.Click += delegate { OpenLink(url); };
                block.Inlines.Add(hyperlink);
                position = link.Index + url.Length;
            }
            if (position < notes.Length) block.Inlines.Add(new Run(notes.Substring(position)));
            return block;
        }

        void CloseTodoDetail()
        {
            todoDetailId = null; todoDeleteArmed = false;
            ShowTodos();
        }

        void CompleteTodo(string id)
        {
            var item = Todos.Find(id);
            if (item == null) return;
            try { Todos.SetCompleted(id, true); }
            catch (Exception ex) { if (!IsSaveError(ex)) throw; Get<TextBlock>("TodoSummary").Text = "完了を保存できませんでした。もう一度お試しください"; effect.Refresh(); return; }
            if (todoDetailId == id) todoDetailId = null;
            todoUndoId = id; todoUndoTimer.Stop(); todoUndoTimer.Start();
            DrawDates();
            ShowTodos();
        }

        void RestoreTodo(string id)
        {
            try { Todos.SetCompleted(id, false); }
            catch (Exception ex) { if (!IsSaveError(ex)) throw; Get<TextBlock>("TodoSummary").Text = "保存できませんでした。もう一度お試しください"; effect.Refresh(); return; }
            if (todoDetailId == id) todoDetailId = null;
            DrawDates();
            ShowTodos();
        }

        // ---------- Editor ----------

        void InitializeTodoEditor()
        {
            var picker = Get<Border>("TodoEditor");
            picker.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                FinishMarketTransition();
                var month = Get<Border>("TodoMonthPicker");
                var target = e.OriginalSource as DependencyObject;
                if (month.Visibility == Visibility.Visible && target != null && !month.IsAncestorOf(target) && !Get<Button>("TodoDueText").IsAncestorOf(target) && target != Get<Button>("TodoDueText"))
                    month.Visibility = Visibility.Collapsed;
            };
            picker.PreviewKeyDown += OnTodoEditorKey;
            Get<Button>("CloseTodoEditor").Click += delegate { CloseTodoEditor(true, true); };
            Get<Button>("CancelTodoEditor").Click += delegate { CloseTodoEditor(true, false); };
            Get<Button>("SaveTodo").Click += delegate { SaveTodo(); };
            Get<Button>("AddTodoImage").Click += delegate { BrowseTodoImages(); };
            Get<Button>("TodoDuePrevious").Click += delegate { ShiftDue(-1); };
            Get<Button>("TodoDueNext").Click += delegate { ShiftDue(1); };
            Get<Button>("TodoDueText").Click += delegate
            {
                var month = Get<Border>("TodoMonthPicker");
                if (month.Visibility == Visibility.Visible) { month.Visibility = Visibility.Collapsed; return; }
                var basis = editingDue ?? today;
                todoMonth = new DateTime(basis.Year, basis.Month, 1);
                RenderTodoMonth();
                month.Visibility = Visibility.Visible;
            };
            Get<Button>("TodoDueText").MouseWheel += delegate(object sender, MouseWheelEventArgs e) { ShiftDue(e.Delta > 0 ? -1 : 1); e.Handled = true; };
            Get<Button>("TodoMonthPrevious").Click += delegate { todoMonth = todoMonth.AddMonths(-1); RenderTodoMonth(); };
            Get<Button>("TodoMonthNext").Click += delegate { todoMonth = todoMonth.AddMonths(1); RenderTodoMonth(); };
            Get<Border>("TodoMonthPicker").MouseWheel += delegate(object sender, MouseWheelEventArgs e) { todoMonth = todoMonth.AddMonths(e.Delta > 0 ? -1 : 1); RenderTodoMonth(); e.Handled = true; };
            Get<TextBox>("TodoTitle").TextChanged += delegate { if (Get<TextBox>("TodoTitle").Text.Trim().Length > 0 && Get<TextBlock>("TodoEditorMessage").Foreground != todoSoft) Get<TextBlock>("TodoEditorMessage").Text = ""; };
            picker.PreviewDragOver += delegate(object sender, DragEventArgs e)
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                e.Effects = DragDropEffects.Copy; e.Handled = true;
            };
            picker.PreviewDrop += delegate(object sender, DragEventArgs e)
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return;
                e.Handled = true; AddTodoImages(files);
            };
        }

        void OnTodoEditorKey(object sender, KeyEventArgs e)
        {
            FinishMarketTransition();
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (e.Key == Key.Enter && (control || Keyboard.FocusedElement == Get<TextBox>("TodoTitle")) && !e.IsRepeat) { SaveTodo(); e.Handled = true; }
            else if (e.Key == Key.V && control && PasteTodoImage(false)) e.Handled = true;
        }

        void EditorMessage(string text, bool info)
        {
            var message = Get<TextBlock>("TodoEditorMessage");
            message.Text = text;
            message.Foreground = info ? todoSoft : Art.Ink("#FFB088");
        }

        void OpenTodoEditor(TodoItem item, DateTime? presetDue)
        {
            CloseMarketSettings();
            Get<Border>("TimerPicker").Visibility = Visibility.Collapsed;
            Get<Border>("SettingsPicker").Visibility = Visibility.Collapsed; Get<Border>("SpeedPicker").Visibility = Visibility.Collapsed;
            Get<Border>("TodoMonthPicker").Visibility = Visibility.Collapsed;
            todoEditorSession++;
            string id = item == null ? null : item.Id;
            var draft = todoDraft;
            todoDraft = null;
            if (draft != null && draft.Id != id) { Todos.DeleteImages(draft.Imported); draft = null; }
            editingTodoId = id;
            Get<TextBlock>("TodoEditorTitle").Text = item == null ? "タスクを追加" : "タスクを編集";
            Get<Button>("SaveTodo").Content = item == null ? "追加する" : "保存";
            if (draft != null)
            {
                Get<TextBox>("TodoTitle").Text = draft.Title;
                Get<TextBox>("TodoNotes").Text = draft.Notes;
                editingDue = draft.Due;
                editingImages = draft.Images;
                importedImages = draft.Imported;
                EditorMessage("書きかけの内容を戻しました（取消で破棄）", true);
            }
            else
            {
                Get<TextBox>("TodoTitle").Text = item == null ? "" : item.Title;
                Get<TextBox>("TodoNotes").Text = item == null ? "" : (item.Notes ?? "").Replace("\n", Environment.NewLine);
                editingDue = item == null ? null : item.DueDate;
                editingImages = item == null ? new List<string>() : new List<string>(item.Images ?? new List<string>());
                importedImages = new List<string>();
                EditorMessage("", false);
            }
            if (presetDue.HasValue) editingDue = presetDue.Value.Date;
            RenderDueChoices();
            RenderTodoImageStrip();
            var picker = Get<Border>("TodoEditor");
            picker.Visibility = Visibility.Visible;
            AnimateEditorTransition(picker, false);
            Dispatcher.BeginInvoke(new Action(delegate
            {
                var title = Get<TextBox>("TodoTitle");
                if (picker.IsVisible) { Keyboard.Focus(title); title.CaretIndex = title.Text.Length; }
            }), DispatcherPriority.Input);
        }

        /// <summary>Closes the editor. Unless the user chose 取消, the contents stay as a draft for the next open.</summary>
        void CloseTodoEditor(bool animate, bool keepDraft = true)
        {
            var picker = Get<Border>("TodoEditor");
            if (picker.Visibility != Visibility.Visible) return;
            todoEditorSession++;
            Get<Border>("TodoMonthPicker").Visibility = Visibility.Collapsed;
            string title = Get<TextBox>("TodoTitle").Text, notes = Get<TextBox>("TodoNotes").Text;
            var item = editingTodoId == null ? null : Todos.Find(editingTodoId);
            bool changed = item == null
                ? title.Trim().Length > 0 || notes.Trim().Length > 0 || editingImages.Count > 0 || editingDue.HasValue
                : title != item.Title || notes.Replace("\r\n", "\n").TrimEnd() != (item.Notes ?? "") || editingDue != item.DueDate || !editingImages.SequenceEqual(item.Images ?? new List<string>());
            if (keepDraft && changed)
                todoDraft = new TodoDraft { Id = editingTodoId, Title = title, Notes = notes, Due = editingDue, Images = editingImages, Imported = importedImages };
            else Todos.DeleteImages(importedImages);
            importedImages = new List<string>();
            if (animate) AnimateEditorTransition(picker, true);
            else { FinishMarketTransition(); picker.Visibility = Visibility.Collapsed; }
        }

        void SaveTodo()
        {
            string title = Get<TextBox>("TodoTitle").Text.Trim();
            if (title.Length == 0)
            {
                EditorMessage("タイトルを入力してください（説明・画像・期限は空欄でも追加できます）", false);
                Keyboard.Focus(Get<TextBox>("TodoTitle"));
                return;
            }
            string notes = Get<TextBox>("TodoNotes").Text;
            try
            {
                if (editingTodoId == null) Todos.Add(title, notes, editingDue, editingImages);
                else Todos.Update(editingTodoId, title, notes, editingDue, editingImages);
            }
            catch (Exception ex)
            {
                if (!IsSaveError(ex)) throw;
                EditorMessage("保存できませんでした。もう一度お試しください", false);
                return;
            }
            importedImages = new List<string>();
            CloseTodoEditor(true, false);
            DrawDates();
            if (mode == TodoMode) { if (editingTodoId == null) { todoShowDone = false; todoDetailId = null; } ShowTodos(); }
            else if (mode == 0 && IsVisible) { UpdateLayout(); effect.Refresh(); }
        }

        void RenderDueChoices()
        {
            var panel = Get<StackPanel>("TodoDueChoices");
            panel.Children.Clear();
            var options = new[] { Tuple.Create("なし", (DateTime?)null), Tuple.Create("今日", (DateTime?)today), Tuple.Create("明日", (DateTime?)today.AddDays(1)), Tuple.Create("1週間後", (DateTime?)today.AddDays(7)) };
            foreach (var option in options)
            {
                var value = option.Item2;
                bool active = editingDue == value;
                var button = new Button { Content = option.Item1, FontSize = 12, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 5, 0),
                    Background = Art.Ink(active ? "#287181" : "#193B48"), Foreground = Art.Ink(active ? "#E5FFFF" : "#B8D9DE") };
                button.Click += delegate { editingDue = value; Get<Border>("TodoMonthPicker").Visibility = Visibility.Collapsed; RenderDueChoices(); };
                panel.Children.Add(button);
            }
            var text = Get<Button>("TodoDueText");
            text.Content = (editingDue.HasValue ? ShortDate(editingDue.Value) : "期限なし") + "  ▾";
            text.Foreground = editingDue.HasValue ? (editingDue.Value < today ? todoRed : Art.Ink("#E3F4F4")) : todoMuted;
            if (Get<Border>("TodoMonthPicker").Visibility == Visibility.Visible) RenderTodoMonth();
        }

        void RenderTodoMonth()
        {
            Get<TextBlock>("TodoMonthLabel").Text = todoMonth.ToString("yyyy年 M月", CultureInfo.InvariantCulture);
            var grid = Get<UniformGrid>("TodoMonthDays");
            grid.Children.Clear();
            for (int col = 1; col <= 7; col++)
                grid.Children.Add(new TextBlock { Text = col.ToString(CultureInfo.InvariantCulture), FontFamily = new FontFamily("Consolas"), FontSize = 10, Height = 18, TextAlignment = TextAlignment.Center,
                    Foreground = col == 6 ? Art.Ink("#8EC5FF") : col == 7 ? todoRed : muted });
            var start = todoMonth.AddDays(1 - IsoDay(todoMonth));
            var due = Todos.Open.Where(t => t.DueDate.HasValue).Select(t => t.DueDate.Value).ToLookup(d => d);
            for (int i = 0; i < 42; i++)
            {
                var day = start.AddDays(i);
                bool active = editingDue == day, inMonth = day.Month == todoMonth.Month;
                bool red = Holidays.Name(day) != null || day.DayOfWeek == DayOfWeek.Sunday;
                var label = new Grid();
                label.Children.Add(new TextBlock { Text = day.Day.ToString(CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                if (due.Contains(day)) label.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = active ? Art.Ink("#0B2830") : todoAmber, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -5, 0) });
                var button = new Button { Content = label, Height = 27, Padding = new Thickness(0), FontSize = 12, FontFamily = new FontFamily("Bahnschrift"),
                    Background = active ? aqua : day == today ? Art.Ink("#1E4A52") : Brushes.Transparent,
                    Foreground = active ? Art.Ink("#0B2830") : red ? todoRed : day.DayOfWeek == DayOfWeek.Saturday ? Art.Ink("#8EC5FF") : Art.Ink("#D9E9EB"),
                    Opacity = inMonth ? 1 : .45, ToolTip = Holidays.Name(day) };
                AutomationProperties.SetName(button, day.ToString("yyyy年M月d日", ja));
                var value = day;
                button.Click += delegate { editingDue = value; Get<Border>("TodoMonthPicker").Visibility = Visibility.Collapsed; RenderDueChoices(); };
                grid.Children.Add(button);
            }
        }

        void ShiftDue(int days)
        {
            var start = editingDue ?? today.AddDays(days > 0 ? -1 : 1);
            var next = start.AddDays(days);
            if (next.Year < 2 || next.Year > 9998) return;
            editingDue = next;
            RenderDueChoices();
        }

        void RenderTodoImageStrip()
        {
            var strip = Get<StackPanel>("TodoImageStrip");
            strip.Children.Clear();
            foreach (var name in editingImages)
            {
                string captured = name;
                var tile = new Grid { Width = 46, Height = 46, Margin = new Thickness(0, 0, 5, 0), ToolTip = "添付した画像" };
                var thumb = Todos.LoadImage(name, 92);
                tile.Children.Add(new Border { CornerRadius = new CornerRadius(5), BorderBrush = Art.Ink("#528793"), BorderThickness = new Thickness(1), ClipToBounds = true,
                    Background = thumb == null ? (Brush)Art.Ink("#0B1923") : new ImageBrush(thumb) { Stretch = Stretch.UniformToFill } });
                var remove = new Button { Content = "×", Width = 18, Height = 18, Padding = new Thickness(0), FontSize = 11, Background = Art.Ink("#0B1923"),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -4, 0), ToolTip = "この画像を外す" };
                AutomationProperties.SetName(remove, "画像を外す");
                remove.Click += delegate
                {
                    editingImages.Remove(captured);
                    if (importedImages.Remove(captured)) Todos.DeleteImages(new[] { captured });
                    RenderTodoImageStrip();
                };
                tile.Children.Add(remove);
                strip.Children.Add(tile);
            }
            Get<Button>("AddTodoImage").IsEnabled = editingImages.Count < MaxTodoImages;
        }

        void BrowseTodoImages()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "タスクに添付する画像", Multiselect = true,
                Filter = "画像 (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp" };
            if (dialog.ShowDialog(this) == true) AddTodoImages(dialog.FileNames);
        }

        void AddTodoImages(IEnumerable<string> files)
        {
            int skipped = 0;
            foreach (var file in files)
            {
                if (editingImages.Count >= MaxTodoImages) { skipped++; continue; }
                string name = null;
                try { name = Todos.ImportFile(file); }
                catch (Exception ex) { if (!IsSaveError(ex)) throw; }
                if (name == null) { skipped++; continue; }
                editingImages.Add(name); importedImages.Add(name);
            }
            EditorMessage(skipped == 0 ? "" : editingImages.Count >= MaxTodoImages ? "画像は " + MaxTodoImages + "枚まで添付できます" : "画像ファイル（PNG・JPEG・GIF・BMP・WebP）を選んでください", false);
            RenderTodoImageStrip();
        }

        int todoEditorSession;
        static System.Net.Http.HttpClient todoImageClient;

        /// <summary>
        /// Attaches a picture from the clipboard: image files copied in Explorer, PNG/bitmap data from
        /// browsers, screenshots and Office, or the first img element of copied web content.
        /// Ctrl+V in a text box keeps pasting text when the clipboard has text.
        /// </summary>
        bool PasteTodoImage(bool force)
        {
            IDataObject data = null;
            try { data = Clipboard.GetDataObject(); }
            catch (System.Runtime.InteropServices.ExternalException) { }
            if (data == null) { if (force) EditorMessage("クリップボードを読み取れませんでした。もう一度お試しください", false); return force; }
            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (data.GetData(DataFormats.FileDrop) as string[] ?? new string[0]).Where(Todos.IsImageFile).ToArray();
                    if (files.Length > 0) { AddTodoImages(files); return true; }
                    if (force) { EditorMessage("コピーしたファイルは画像ではありません（PNG・JPEG・GIF・BMP・WebP）", false); return true; }
                    return false;
                }
                string text = data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string : null;
                if (!force && !string.IsNullOrWhiteSpace(text) && Keyboard.FocusedElement is TextBox) return false;
                var bitmap = ClipboardBitmap(data);
                string source = bitmap == null ? HtmlImageSource(data) : null;
                if (bitmap == null && source == null)
                {
                    if (force) EditorMessage("クリップボードに画像がありません。画像をコピーしてからお試しください", false);
                    return force;
                }
                if (editingImages.Count >= MaxTodoImages) { EditorMessage("画像は " + MaxTodoImages + "枚まで添付できます", false); return true; }
                if (bitmap != null) AddTodoBitmap(bitmap); else FetchTodoImage(source);
                return true;
            }
            catch (Exception ex)
            {
                if (!(IsSaveError(ex) || ex is System.Runtime.InteropServices.ExternalException)) throw;
                EditorMessage("クリップボードの画像を貼り付けられませんでした", false);
                return true;
            }
        }

        static BitmapSource ClipboardBitmap(IDataObject data)
        {
            foreach (var format in new[] { "PNG", "image/png" })
            {
                try
                {
                    if (!data.GetDataPresent(format)) continue;
                    var stream = data.GetData(format) as Stream;
                    if (stream == null) continue;
                    var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    frame.Freeze();
                    return frame;
                }
                catch (Exception ex) { if (!IsImageError(ex)) throw; }
            }
            if (!data.GetDataPresent(DataFormats.Dib) && !data.GetDataPresent(DataFormats.Bitmap)) return null;
            try
            {
                // WinForms reads device-independent bitmaps (browsers, Office, Snipping Tool) more reliably than WPF.
                using (var image = System.Windows.Forms.Clipboard.GetImage())
                {
                    if (image == null) return null;
                    var buffer = new MemoryStream();
                    image.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
                    buffer.Position = 0;
                    var frame = BitmapFrame.Create(buffer, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    frame.Freeze();
                    return frame;
                }
            }
            catch (Exception ex) { if (!IsImageError(ex)) throw; return null; }
        }

        static bool IsImageError(Exception ex)
        {
            return ex is System.Runtime.InteropServices.ExternalException || ex is NotSupportedException || ex is ArgumentException || ex is FileFormatException || ex is IOException || ex is InvalidOperationException || ex is OutOfMemoryException;
        }

        static readonly System.Text.RegularExpressions.Regex htmlImage = new System.Text.RegularExpressions.Regex(@"<img\b[^>]*?\bsrc\s*=\s*[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly System.Text.RegularExpressions.Regex htmlSource = new System.Text.RegularExpressions.Regex(@"^SourceURL:(\S+)", System.Text.RegularExpressions.RegexOptions.Multiline);

        /// <summary>The first picture in copied web content (CF_HTML), resolved against its SourceURL.</summary>
        static string HtmlImageSource(IDataObject data)
        {
            if (!data.GetDataPresent(DataFormats.Html)) return null;
            string html = data.GetData(DataFormats.Html) as string;
            if (string.IsNullOrEmpty(html)) return null;
            var match = htmlImage.Match(html);
            if (!match.Success) return null;
            string src = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return src;
            Uri absolute;
            if (Uri.TryCreate(src, UriKind.Absolute, out absolute)) return InformationFeeds.SafeUrl(absolute.AbsoluteUri) ? absolute.AbsoluteUri : null;
            var origin = htmlSource.Match(html);
            Uri basis;
            if (origin.Success && Uri.TryCreate(origin.Groups[1].Value, UriKind.Absolute, out basis) && Uri.TryCreate(basis, src, out absolute) && InformationFeeds.SafeUrl(absolute.AbsoluteUri)) return absolute.AbsoluteUri;
            return null;
        }

        void AddTodoBitmap(BitmapSource bitmap)
        {
            string name = Todos.ImportBitmap(bitmap);
            if (name == null) return;
            editingImages.Add(name); importedImages.Add(name);
            EditorMessage("", false);
            RenderTodoImageStrip();
        }

        async void FetchTodoImage(string source)
        {
            int session = todoEditorSession;
            byte[] bytes = null;
            try
            {
                if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = source.IndexOf(',');
                    if (comma > 0 && source.Substring(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) bytes = Convert.FromBase64String(source.Substring(comma + 1));
                }
                else
                {
                    EditorMessage("コピー元の画像を取得しています…", true);
                    if (todoImageClient == null)
                    {
                        todoImageClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 20 * 1024 * 1024 };
                        todoImageClient.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitCalendar");
                    }
                    using (var response = await todoImageClient.GetAsync(source))
                    {
                        var type = response.Content.Headers.ContentType;
                        if (response.IsSuccessStatusCode && (type == null || type.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))) bytes = await response.Content.ReadAsByteArrayAsync();
                    }
                }
            }
            catch (Exception ex) { if (!(ex is System.Net.Http.HttpRequestException || ex is System.Threading.Tasks.TaskCanceledException || ex is FormatException || ex is InvalidOperationException)) throw; }
            if (session != todoEditorSession || Get<Border>("TodoEditor").Visibility != Visibility.Visible) return;
            BitmapSource bitmap = null;
            if (bytes != null)
            {
                try { var frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad); frame.Freeze(); bitmap = frame; }
                catch (Exception ex) { if (!IsImageError(ex)) throw; }
            }
            if (bitmap == null) { EditorMessage("コピー元の画像を取得できませんでした。画像を保存してから「＋ ファイル」で追加してください", false); return; }
            if (editingImages.Count >= MaxTodoImages) { EditorMessage("画像は " + MaxTodoImages + "枚まで添付できます", false); return; }
            try { AddTodoBitmap(bitmap); }
            catch (Exception ex) { if (!IsSaveError(ex)) throw; EditorMessage("画像を保存できませんでした", false); }
        }
    }
}
