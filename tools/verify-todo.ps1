param([string]$Exe = 'build/OrbitCalendar.exe')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $Exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-todo-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
# Isolate every store the panel touches from the user's real settings.
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1);$prefs.GetField('HiddenTabs',$sf).SetValue($null,0)
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$todos=$a.GetType('OrbitCalendar.Todos');$todos.GetField('folder',$sf).SetValue($null,$folder);$todos.GetField('path',$sf).SetValue($null,(Join-Path $folder 'todos.json'));$todos.GetField('imageFolder',$sf).SetValue($null,(Join-Path $folder 'todo-images'));$todos.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
$st=$a.GetType('OrbitCalendar.Shell');$shell=$st.GetConstructor($if,$null,[type[]]@([bool]),$null).Invoke([object[]]@($false))
$panel=$st.GetField('panel',$if).GetValue($shell);$pt=$panel.GetType();$root=$panel.Content.Child
function Press($button){$button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function Click($name){Press $root.FindName($name)}
function Call($name,$arguments){$pt.GetMethod($name,$if).Invoke($panel,$arguments)}
function SaveImage($element,$width,$height,$name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($element);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
function Snapshot($name){Pump 250;$panel.UpdateLayout();SaveImage $root 458 ([int]$root.Height) $name}
function Cards{@($root.FindName('TodoList').Children|Where-Object {$_.Tag -eq 'todo-card'})}
function OpenCount{$todos.GetProperty('Open',$sf).GetValue($null).Length}
function Sample($name,$color){$v=[Windows.Media.DrawingVisual]::new();$d=$v.RenderOpen();$d.DrawRectangle([Windows.Media.LinearGradientBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($color),[Windows.Media.ColorConverter]::ConvertFromString('#0B1923'),35),$null,[Windows.Rect]::new(0,0,640,360));$d.DrawEllipse([Windows.Media.Brushes]::White,$null,[Windows.Point]::new(470,120),60,60);$d.Close();$b=[Windows.Media.Imaging.RenderTargetBitmap]::new(640,360,96,96,[Windows.Media.PixelFormats]::Pbgra32);$b.Render($v);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($b));$p=Join-Path $folder $name;$s=[IO.File]::Create($p);try{$e.Save($s)}finally{$s.Dispose()};$p}
function Key($key){$e=New-Object Windows.Input.KeyEventArgs([Windows.Input.Keyboard]::PrimaryDevice,[Windows.PresentationSource]::FromVisual($panel),0,[Windows.Input.Key]::$key);$e.RoutedEvent=[Windows.Input.Keyboard]::PreviewKeyDownEvent;$panel.RaiseEvent($e)}
function DateButton($day){$name=$day.ToString('yyyy年M月d日',[Globalization.CultureInfo]::GetCultureInfo('ja-JP'));@($root.FindName('Dates').Children|Where-Object {$_ -is [Windows.Controls.Button] -and [Windows.Automation.AutomationProperties]::GetName($_).StartsWith($name)})[0]}
function MouseUp($element){$e=New-Object Windows.Input.MouseButtonEventArgs([Windows.Input.Mouse]::PrimaryDevice,0,([Windows.Input.MouseButton]::Left));$e.RoutedEvent=[Windows.UIElement]::MouseLeftButtonUpEvent;$element.RaiseEvent($e)}
$today=[DateTime]::Today
try{
 $shell.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900
 Click 'TodoTab';Pump 1900
 Assert ($pt.GetField('mode',$if).GetValue($panel) -eq 5 -and $root.FindName('TodoBody').Visibility -eq 'Visible' -and $root.FindName('DataBody').Visibility -eq 'Collapsed') 'ToDo tab did not open'
 Assert ($root.FindName('TodoHeading').Text -eq 'タスクはありません') 'Empty state heading missing';Snapshot 'todo-empty.png'
 # Title is the only required field.
 Click 'AddTodo';Pump 1900;Assert ($root.FindName('TodoEditor').Visibility -eq 'Visible') 'Editor did not open'
 Click 'SaveTodo';Assert ($root.FindName('TodoEditorMessage').Text.Contains('タイトル') -and (OpenCount) -eq 0) 'Empty title was accepted'
 $root.FindName('TodoTitle').Text='資料を提出する'
 $root.FindName('TodoNotes').Text="提出先はこちら https://example.com/form 。`r`n図は下の画像を参照"
 Call 'AddTodoImages' @(,[string[]]@((Sample 'chart.png' '#287181')))
 Press $root.FindName('TodoDueChoices').Children[2];Assert ($pt.GetField('editingDue',$if).GetValue($panel) -eq $today.AddDays(1)) 'Tomorrow chip failed'
 Press $root.FindName('TodoDueNext');Assert ($pt.GetField('editingDue',$if).GetValue($panel) -eq $today.AddDays(2)) 'Next-day button failed'
 Pump 400;$editor=$root.FindName('TodoEditor');SaveImage $editor 422 452 'todo-editor.png';Snapshot 'todo-editor-panel.png'
 Click 'SaveTodo';Pump 1200;Assert ($editor.Visibility -eq 'Collapsed' -and (OpenCount) -eq 1) 'Saving failed'
 $saved=$todos.GetProperty('Open',$sf).GetValue($null)[0];Assert ($saved.ImageCount -eq 1 -and [IO.File]::Exists($todos.GetMethod('ImagePath',$sf).Invoke($null,@($saved.Images[0])))) 'Image was not stored with the task'
 # Esc keeps a draft (title + image) for the next open; 取消 discards it and its imported images.
 Click 'AddTodo';Pump 600;$root.FindName('TodoTitle').Text='書きかけのタスク';Call 'AddTodoImages' @(,[string[]]@((Sample 'discard.png' '#7E3442')));$discard=$pt.GetField('importedImages',$if).GetValue($panel)[0];$discardPath=$todos.GetMethod('ImagePath',$sf).Invoke($null,@($discard))
 Assert ([IO.File]::Exists($discardPath)) 'Import failed';Key Escape
 Pump 1200;Assert ($editor.Visibility -eq 'Collapsed' -and [IO.File]::Exists($discardPath) -and (OpenCount) -eq 1) 'Esc lost the draft'
 Click 'AddTodo';Pump 600;Assert ($root.FindName('TodoTitle').Text -eq '書きかけのタスク' -and $pt.GetField('editingImages',$if).GetValue($panel).Count -eq 1 -and $root.FindName('TodoEditorMessage').Text.Contains('書きかけ')) 'Draft was not restored'
 Click 'CancelTodoEditor';Pump 1200;Assert (-not [IO.File]::Exists($discardPath) -and $null -eq $pt.GetField('todoDraft',$if).GetValue($panel)) 'Cancel did not discard the draft'
 Click 'AddTodo';Pump 600;Assert ($root.FindName('TodoTitle').Text -eq '') 'Discarded draft came back';Click 'CancelTodoEditor';Pump 900
 $add=$todos.GetMethod('Add',$sf);$none=[string[]]@()
 $add.Invoke($null,@('請求書の支払い','',[Nullable[DateTime]]$today.AddDays(-1),$none))|Out-Null
 $add.Invoke($null,@('歯医者の予約を確認','17:30 までに電話',[Nullable[DateTime]]$today,$none))|Out-Null
 $add.Invoke($null,@('週次レポートをまとめる','',[Nullable[DateTime]]$today.AddDays(5),$none))|Out-Null
 $add.Invoke($null,@('本を返す','',$null,$none))|Out-Null
 Call 'ShowTodos' @();Pump 1900
 $tabText=[Windows.Documents.TextRange]::new($root.FindName('TodoTab').Content.ContentStart,$root.FindName('TodoTab').Content.ContentEnd).Text;Assert ($tabText -eq 'ToDo 2') "Tab count wrong: $tabText / $($root.FindName('TodoTab').Content.GetType().Name)"
 $cards=Cards;Assert ($cards.Count -eq 5) "Expected 5 cards, got $($cards.Count)"
 Assert ($root.FindName('TodoHeading').Text -eq 'タスク 5件' -and $root.FindName('TodoSummary').Text.Contains('期限切れ 1')) 'Summary wrong'
 Assert ($cards[0].Child.Children[1].ToolTip -eq '請求書の支払い' -and $cards[4].Child.Children[1].ToolTip -eq '本を返す') 'Tasks not sorted by due date'
 Snapshot 'todo-list.png'
 # Detail with link + image, then Done from the detail view.
 Press $cards[2].Child.Children[1];Pump 1900
 Assert ($root.FindName('TodoDetail').Visibility -eq 'Visible' -and $root.FindName('TodoDetailTitle').Text -eq '資料を提出する') 'Detail did not open'
 $content=$root.FindName('TodoDetailContent');$notes=$content.Children[0];$links=@($notes.Inlines|Where-Object {$_ -is [Windows.Documents.Hyperlink]})
 Assert ($links.Count -eq 1 -and ([Windows.Documents.TextRange]::new($links[0].ContentStart,$links[0].ContentEnd).Text) -eq 'https://example.com/form') 'Link not detected'
 Assert (@($content.Children|Where-Object {$_.Tag -eq 'todo-image'}).Count -eq 1) 'Image not shown in detail';Snapshot 'todo-detail.png'
 Click 'CompleteTodo';Pump 1900
 Assert ($root.FindName('TodoDetail').Visibility -eq 'Collapsed' -and (OpenCount) -eq 4 -and $root.FindName('TodoUndo').Visibility -eq 'Visible') 'Done from detail failed';Snapshot 'todo-done-undo.png'
 Click 'TodoUndoButton';Pump 900;Assert ((OpenCount) -eq 5) 'Undo failed'
 # Done straight from the list.
 Press (Cards)[4].Child.Children[0];Pump 900;Assert ((OpenCount) -eq 4 -and -not (@(Cards|ForEach-Object {$_.Child.Children[1].ToolTip}) -contains '本を返す')) 'Done from list failed'
 # Calendar: marks and titles beside the week label.
 Click 'CalendarTab';Pump 1900
 Call 'SelectDate' @([DateTime]$today.AddDays(2));Pump 1900
 $strip=$root.FindName('TodoDayTasks');Assert ($strip.Text.Contains('資料を提出する') -and $root.FindName('SelectedDate').Text -match '^W\d\d\.\d$') "Task title missing next to week label: $($strip.Text)"
 $marks=@($root.FindName('Dates').Children|Where-Object {$_.Tag -eq 'todo-due-mark'});Assert ($marks.Count -ge 3) "Expected due marks, got $($marks.Count)"
 Snapshot 'todo-calendar.png'
 Call 'SelectDate' @([DateTime]$today);Pump 1900;Snapshot 'todo-calendar-today.png'
 MouseUp $root.FindName('TodoDayTasks');Pump 1900
 Assert ($pt.GetField('mode',$if).GetValue($panel) -eq 5 -and $root.FindName('TodoDetailTitle').Text -eq '歯医者の予約を確認') 'Clicking the task strip did not open the task'
 # Month picker: choose a day next month from the editor.
 Click 'AddTodo';Pump 600;$root.FindName('TodoTitle').Text='来月の更新手続き';Click 'TodoDueText';Assert ($root.FindName('TodoMonthPicker').Visibility -eq 'Visible') 'Month picker did not open'
 Click 'TodoMonthNext';$next=([DateTime]::new($today.Year,$today.Month,1)).AddMonths(1).AddDays(14);$days=$root.FindName('TodoMonthDays').Children;$target=@($days|Where-Object {$_ -is [Windows.Controls.Button] -and [Windows.Automation.AutomationProperties]::GetName($_) -eq $next.ToString('yyyy年M月d日',[Globalization.CultureInfo]::GetCultureInfo('ja-JP'))})[0]
 Pump 1900;Snapshot 'todo-month-picker.png';Press $target;Assert ($pt.GetField('editingDue',$if).GetValue($panel) -eq $next -and $root.FindName('TodoMonthPicker').Visibility -eq 'Collapsed') 'Month picker did not set the due date'
 Click 'SaveTodo';Pump 1200
 # Calendar: double-clicking a date adds a task due on that day (buttons are rebuilt between the clicks).
 Click 'CalendarTab';Pump 1900;DateButton $today.AddDays(3)|ForEach-Object{Press $_};Pump 120;Assert ($root.FindName('TodoEditor').Visibility -eq 'Collapsed' -and $pt.GetField('selected',$if).GetValue($panel) -eq $today.AddDays(3)) 'Single click did not just select';DateButton $today.AddDays(3)|ForEach-Object{Press $_};Pump 600;Assert ($root.FindName('TodoEditor').Visibility -eq 'Visible') 'Double click did not open the editor'
 Assert ($pt.GetField('editingDue',$if).GetValue($panel) -eq $today.AddDays(3)) 'Calendar add did not preset the due date';$root.FindName('TodoTitle').Text='カレンダーから追加';Click 'SaveTodo';Pump 1200
 Assert ($root.FindName('TodoDayTasks').Text.Contains('カレンダーから追加')) 'Calendar strip not refreshed after adding'
 $add.Invoke($null,@('同じ日の2件目','',[Nullable[DateTime]]$today.AddDays(3),$none))|Out-Null;Call 'DrawDates' @();$pt.GetField('effect',$if).GetValue($panel).Refresh();Pump 200;Assert ($root.FindName('TodoDayTasks').Text.StartsWith('● 2件')) "Multiple tasks not counted: $($root.FindName('TodoDayTasks').Text)";Snapshot 'todo-calendar-multiple.png'
 # No notifications any more.
 Assert ($null -eq $pt.GetMethod('TodoReminder',$if)) 'Reminder still present'
 # Long titles and holidays: the strip must stay left of ＋ and the weather; holiday and task both shown.
 $holiday=$today.AddDays(1);while($null -eq $a.GetType('OrbitCalendar.Holidays').GetMethod('Name',$sf).Invoke($null,@($holiday))){$holiday=$holiday.AddDays(1)};$holidayName=$a.GetType('OrbitCalendar.Holidays').GetMethod('Name',$sf).Invoke($null,@($holiday))
 $long='とても長いタイトルのタスク：来週の打ち合わせ資料を整理して関係者全員に共有し、確認依頼のメールまで送る'
 $add.Invoke($null,@($long,'',[Nullable[DateTime]]$holiday,$none))|Out-Null
 Call 'SelectDate' @([DateTime]$holiday);Pump 1900
 $strip=$root.FindName('HolidayName');$taskLine=$root.FindName('TodoDayTasks');$weather=$root.FindName('TodayWeatherGroup');$face=$root.FindName('Face')
 $sb=$strip.TransformToAncestor($face).TransformBounds([Windows.Rect]::new($strip.RenderSize));$wb=$weather.TransformToAncestor($face).TransformBounds([Windows.Rect]::new($weather.RenderSize))
 Assert ($strip.Text -eq $holidayName -and $taskLine.Text.Contains('とても長い')) "Holiday + task strip wrong: $($strip.Text) / $($taskLine.Text)";$tb=$taskLine.TransformToAncestor($face).TransformBounds([Windows.Rect]::new($taskLine.RenderSize));Assert ($tb.Width -ge 280 -and $tb.Top -ge $sb.Bottom - 1 -and $tb.Top -ge $wb.Bottom - 1) "Task line too narrow or overlapping: $tb"
 Assert ($sb.Right -le $wb.Left + .5) "Strip overlaps weather: strip $($sb.Right) weather $($wb.Left)"
 Assert ($taskLine.ToolTip.Contains($long)) 'Tooltip lacks full title'
 Snapshot 'todo-calendar-holiday.png'
 Click 'TodoTab';Pump 1900;Click 'TodoBack';Pump 900;Snapshot 'todo-list-long.png'
 $longId=@($todos.GetProperty('Open',$sf).GetValue($null)|Where-Object {$_.Title -eq $long})[0].Id;$pt.GetField('todoDetailId',$if).SetValue($panel,$longId);Call 'ShowTodos' @();Pump 1900;Snapshot 'todo-detail-long.png'
 $title=$root.FindName('TodoDetailTitle');Assert ($title.ActualHeight -le 63) 'Long detail title is not clamped'
 Click 'TodoBack';Pump 900
 # Completed view: list, restore from the list.
 Click 'TodoTab';Pump 1900;Click 'TodoBack';Pump 900;Assert ($root.FindName('TodoDoneToggle').Content -like '完了済み 1件*') "Done toggle wrong: $($root.FindName('TodoDoneToggle').Content)"
 Click 'TodoDoneToggle';Pump 1900;Assert ($root.FindName('TodoHeading').Text -eq '完了済み 1件' -and (Cards).Count -eq 1) 'Completed view wrong';Snapshot 'todo-completed.png'
 Press (Cards)[0].Child.Children[0];Pump 900;Assert ((OpenCount) -eq 9 -and -not $pt.GetField('todoShowDone',$if).GetValue($panel)) 'Restore from completed view failed'
 # Clipboard pictures copied from other apps. Keep the user's clipboard text and put it back afterwards.
 $savedText=$null;try{if([Windows.Clipboard]::ContainsText()){$savedText=[Windows.Clipboard]::GetText()}}catch{}
 try{
  Click 'AddTodo';Pump 1900;$root.FindName('TodoTitle').Text='クリップボード画像';$notesBox=$root.FindName('TodoNotes');[Windows.Input.Keyboard]::Focus($notesBox)|Out-Null
  $pic=[Windows.Media.Imaging.BitmapFrame]::Create([IO.File]::OpenRead((Join-Path $folder 'chart.png')),[Windows.Media.Imaging.BitmapCreateOptions]::None,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
  function ImageCount{$pt.GetField('editingImages',$if).GetValue($panel).Count}
  # 1) Bitmap only (screenshot, Paint, browser "copy image"): Ctrl+V in the notes adds a picture.
  [Windows.Clipboard]::SetImage($pic);Assert ($pt.GetMethod('PasteTodoImage',$if).Invoke($panel,@($false)) -and (ImageCount) -eq 1) 'Bitmap paste failed'
  # 2) PNG stream (Chrome, Snipping Tool).
  $png=[IO.MemoryStream]::new([IO.File]::ReadAllBytes((Join-Path $folder 'chart.png')));$d=[Windows.DataObject]::new();$d.SetData('PNG',$png);[Windows.Clipboard]::SetDataObject($d,$true);Assert ($pt.GetMethod('PasteTodoImage',$if).Invoke($panel,@($false)) -and (ImageCount) -eq 2) 'PNG paste failed'
  # 3) Web content with a data: image and no bitmap.
  $b64=[Convert]::ToBase64String([IO.File]::ReadAllBytes([string](Join-Path $folder 'chart.png')));$html="Version:0.9`r`nStartHTML:0`r`nEndHTML:0`r`nStartFragment:0`r`nEndFragment:0`r`nSourceURL:https://example.com/page`r`n<html><body><!--StartFragment--><img src=`"data:image/png;base64,$b64`"><!--EndFragment--></body></html>"
  $d=[Windows.DataObject]::new();$d.SetData([Windows.DataFormats]::Html,$html);[Windows.Clipboard]::SetDataObject($d,$true);Assert ($pt.GetMethod('PasteTodoImage',$if).Invoke($panel,@($false))) 'HTML image paste not handled';Pump 400;Assert ((ImageCount) -eq 3) 'HTML data image not added'
  # 4) Text + picture (Word/Excel): Ctrl+V in a text box keeps pasting text.
  $d=[Windows.DataObject]::new();$d.SetText('表のテキスト');$d.SetImage($pic);[Windows.Clipboard]::SetDataObject($d,$true)
  Assert (-not $pt.GetMethod('PasteTodoImage',$if).Invoke($panel,@($false)) -and (ImageCount) -eq 3) 'Text paste was hijacked'
  # 5) Image file copied in Explorer.
  $files=[Collections.Specialized.StringCollection]::new();[void]$files.Add((Join-Path $folder 'chart.png'));[Windows.Clipboard]::SetFileDropList($files);Assert ($pt.GetMethod('PasteTodoImage',$if).Invoke($panel,@($false)) -and (ImageCount) -eq 4) "File paste failed: $(ImageCount)"
  Pump 1900;Snapshot 'todo-paste.png'
  # Closing the window while editing (e.g. via the clock) reopens the editor with the draft.
  $panel.Collapse();Pump 900;$panel.Open();Pump 1900;Assert ($root.FindName('TodoEditor').Visibility -eq 'Visible' -and $root.FindName('TodoTitle').Text -eq 'クリップボード画像' -and (ImageCount) -eq 4) 'Editor not restored after reopening'
  Click 'SaveTodo';Pump 1200
 }finally{try{if($savedText){[Windows.Clipboard]::SetText($savedText)}else{[Windows.Clipboard]::Clear()}}catch{}}
 # Tab order and the 表示するタブ setting.
 $strip=$root.FindName('TodoTab').Parent;Assert ($strip.Children.IndexOf($root.FindName('TodoTab')) -eq 1 -and $strip.Children.IndexOf($root.FindName('MarketTab')) -eq 2) 'ToDo tab is not between カレンダー and 株価'
 Click 'SettingsButton';Pump 600;Click 'SettingsTabs';Pump 1900;Assert ($root.FindName('TabSettings').Visibility -eq 'Visible' -and $root.FindName('TabChoices').Children.Count -eq 5) 'Tab settings missing';Snapshot 'settings-tabs.png'
 Press $root.FindName('TabChoices').Children[0];Pump 1900
 Assert ($pt.GetField('mode',$if).GetValue($panel) -eq 0 -and $root.FindName('TodoTab').Visibility -eq 'Collapsed' -and $root.FindName('SettingsPicker').Visibility -eq 'Visible') 'Hiding the open ToDo tab failed'
 Assert (@($root.FindName('Dates').Children|Where-Object {$_.Tag -eq 'todo-due-mark'}).Count -eq 0 ) 'Calendar still shows ToDo while hidden';Snapshot 'settings-tabs-hidden.png'
 Press $root.FindName('TabChoices').Children[3];Pump 900;Assert ($root.FindName('NewsTab').Visibility -eq 'Collapsed') "Hiding News failed: $($prefs.GetField('HiddenTabs',$sf).GetValue($null)) $($root.FindName('TabChoices').Children.Count) $($root.FindName('SettingsPicker').Visibility) $($root.FindName('TabChoices').Children[3].Content)"
 $prefs.GetMethod('Load',$sf).Invoke($null,@())|Out-Null;Assert ($prefs.GetField('HiddenTabs',$sf).GetValue($null) -eq ((1 -shl 5) -bor (1 -shl 3))) 'Hidden tabs not saved'
 Press $root.FindName('TabChoices').Children[0];Press $root.FindName('TabChoices').Children[3];Pump 900;Assert ($root.FindName('TodoTab').Visibility -eq 'Visible' -and $root.FindName('NewsTab').Visibility -eq 'Visible') 'Re-enabling tabs failed'
 Click 'CloseSettingsPicker';Click 'TodoTab';Pump 1900
 # Persistence across restarts and tab restore.
 $todos.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null;Assert ((OpenCount) -eq 10) 'Tasks were not persisted'
 $panel.Collapse();Pump 900;$panel.Open();Pump 1900;Assert ($pt.GetField('mode',$if).GetValue($panel) -eq 5) 'ToDo tab not restored'
 $prefs.GetMethod('Load',$sf).Invoke($null,@())|Out-Null;Assert ($prefs.GetField('LastTab',$sf).GetValue($null) -eq 5) 'LastTab=5 rejected on load'
 # Paging past six tasks.
 1..4|ForEach-Object {$add.Invoke($null,@("追加タスク $_",'',$null,$none))|Out-Null};Click 'TodoBack';Pump 1900
 Assert ($root.FindName('TodoPager').Visibility -eq 'Visible' -and (Cards).Count -eq 5) 'Paging missing';$lastCard=(Cards)[4];$footer=$root.FindName('TodoFooter');$cb=$lastCard.TransformToAncestor($face).TransformBounds([Windows.Rect]::new($lastCard.RenderSize));$fb=$footer.TransformToAncestor($face).TransformBounds([Windows.Rect]::new($footer.RenderSize));Assert ($cb.Bottom -le $fb.Top + .5) "Last card overlaps footer: $($cb.Bottom) > $($fb.Top)";Snapshot 'todo-list-paged.png';Click 'TodoNext';Pump 900;Assert ((Cards).Count -eq 5) 'Second page wrong'
 Write-Output 'PASS: empty state, required title, link + image + due editor, Esc keeps draft, cancel discards draft images, month picker, calendar add, multi-task strip, long title + holiday layout, clipboard bitmap/PNG/HTML/text-vs-image/file paste, editor reopen, tab order + show/hide setting, completed view + restore, tab count, sorted list, detail, Done from detail/list, undo, calendar marks and strip, strip opens task, persistence, tab restore, paging'
 Write-Output $folder
}finally{$shell.Dispose();$panel.Close()}
