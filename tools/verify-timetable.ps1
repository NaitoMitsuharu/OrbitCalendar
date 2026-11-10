param([string]$Exe = 'build/OrbitCalendar.exe')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $Exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-timetable-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1);$prefs.GetField('HiddenTabs',$sf).SetValue($null,0)
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$todos=$a.GetType('OrbitCalendar.Todos');$todos.GetField('folder',$sf).SetValue($null,$folder);$todos.GetField('path',$sf).SetValue($null,(Join-Path $folder 'todos.json'));$todos.GetField('imageFolder',$sf).SetValue($null,(Join-Path $folder 'todo-images'));$todos.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$tt=$a.GetType('OrbitCalendar.Timetables');$tt.GetField('folder',$sf).SetValue($null,$folder);$tt.GetField('path',$sf).SetValue($null,(Join-Path $folder 'timetables.json'));$tt.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Parse($text){$tt.GetMethod('Parse',$sf).Invoke($null,@($text))}
function Clocks($parsed){@($parsed.Departures|ForEach-Object {$tt.GetMethod('Clock',$sf).Invoke($null,@($_.Minutes))})-join ' '}

# --- Parser: the formats people type or copy from a timetable.
$p=Parse "5: 10 21 40 58`n6時 04 17急 31海老名`n7 04 13 (各停) 21`nこれは見出し`n23: 45`n0: 05 20"
Assert ((Clocks $p) -eq '05:10 05:21 05:40 05:58 06:04 06:17 06:31 07:04 07:13 07:21 23:45 24:05 24:20') "Parse wrong: $(Clocks $p)"
Assert (@($p.Departures|Where-Object {$_.Minutes -eq 377})[0].Label -eq '急' -and @($p.Departures|Where-Object {$_.Minutes -eq 391})[0].Label -eq '海老名' -and @($p.Departures|Where-Object {$_.Minutes -eq 433})[0].Label -eq '各停') 'Labels wrong'
Assert ($p.Unreadable.Count -eq 1 -and $p.Unreadable[0] -eq 4) "Unreadable lines wrong: $($p.Unreadable)"
$p=Parse "8:05`n8:15`n8:30快速";Assert ((Clocks $p) -eq '08:05 08:15 08:30') "One-time-per-line format wrong: $(Clocks $p)"
# --- Next trains: after-midnight trains belong to the previous service day; 土休日 falls back to 平日.
$entry=[Activator]::CreateInstance($a.GetType('OrbitCalendar.TimetableEntry'));$entry.Station='テスト駅';$entry.Weekday="22: 30 50`n23: 40`n0: 10 25";$entry.Holiday=$null
$next=$tt.GetMethod('Next',$sf)
$weekday=[DateTime]::new(2026,11,11,0,5,0) # Wednesday 00:05 → Tuesday's 24:10 / 24:25 are still to come
$r=$next.Invoke($null,@($entry,$weekday,3));Assert ($r.Count -ge 2 -and $r[0].Time -eq [DateTime]::new(2026,11,11,0,10,0) -and $r[1].Time -eq [DateTime]::new(2026,11,11,0,25,0)) "Midnight crossover wrong: $($r|ForEach-Object {$_.Time})"
$r=$next.Invoke($null,@($entry,[DateTime]::new(2026,11,14,22,40,0),2));Assert ($r[0].Time -eq [DateTime]::new(2026,11,14,22,50,0)) 'Holiday did not fall back to weekday'
$entry.Holiday="22: 45";$r=$next.Invoke($null,@($entry,[DateTime]::new(2026,11,14,22,40,0),1));Assert ($r[0].Time -eq [DateTime]::new(2026,11,14,22,45,0)) 'Holiday schedule not used on Saturday'
$r=$next.Invoke($null,@($entry,[DateTime]::new(2026,11,23,22,40,0),1));Assert ($r[0].Time -eq [DateTime]::new(2026,11,23,22,45,0)) 'Holiday schedule not used on a national holiday'

$st=$a.GetType('OrbitCalendar.Shell');$shell=$st.GetConstructor($if,$null,[type[]]@([bool]),$null).Invoke([object[]]@($false))
$panel=$st.GetField('panel',$if).GetValue($shell);$pt=$panel.GetType();$root=$panel.Content.Child
function Press($button){$button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function Click($name){Press $root.FindName($name)}
function Key($key){$e=New-Object Windows.Input.KeyEventArgs([Windows.Input.Keyboard]::PrimaryDevice,[Windows.PresentationSource]::FromVisual($panel),0,[Windows.Input.Key]::$key);$e.RoutedEvent=[Windows.Input.Keyboard]::PreviewKeyDownEvent;$panel.RaiseEvent($e)}
function SaveImage($element,$width,$height,$name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($element);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
function Snapshot($name){Pump 250;$panel.UpdateLayout();SaveImage $root 458 ([int]$root.Height) $name}
function Body{@($root.FindName('InformationBody').Children)}
function Tagged($tag){$found=@();$queue=[Collections.Generic.Queue[object]]::new();Body|ForEach-Object{$queue.Enqueue($_)};while($queue.Count){$x=$queue.Dequeue();if($x.Tag -eq $tag){$found+=$x};if($x -is [Windows.Controls.Panel]){$x.Children|ForEach-Object{$queue.Enqueue($_)}}elseif($x -is [Windows.Controls.Decorator] -and $x.Child){$queue.Enqueue($x.Child)}elseif($x -is [Windows.Controls.ContentControl] -and $x.Content -is [Windows.UIElement]){$queue.Enqueue($x.Content)}};$found}
# Departures relative to the real clock so the countdown is visible in the screenshots.
function Times($offsets,$labels){$now=[DateTime]::Now;$rows=[ordered]@{};for($i=0;$i -lt $offsets.Count;$i++){$t=$now.AddMinutes($offsets[$i]);$h=[string]$t.Hour;if(-not $rows.Contains($h)){$rows[$h]=@()};$rows[$h]+=('{0:00}{1}' -f $t.Minute,$labels[$i])};($rows.Keys|ForEach-Object {"$($_): "+($rows[$_]-join ' ')})-join "`r`n"}
try{
 $shell.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900
 Click 'TransitTab';Pump 300;Assert ($root.FindName('Choice3').Visibility -eq 'Visible' -and $root.FindName('Choice3').Content -eq '時刻表') 'No 時刻表 choice'
 Click 'Choice3';Pump 1900
 Assert ($null -eq $pt.GetField('loading',$if).GetValue($panel) -and $root.FindName('DataTitle').Text -eq '時刻表' -and @(Tagged 'timetable-empty').Count -eq 1) 'Empty timetable view wrong';Snapshot 'timetable-empty.png'
 Press @(Tagged 'timetable-add')[0];Pump 1900;$editor=$root.FindName('TimetableEditor');Assert ($editor.Visibility -eq 'Visible') 'Editor did not open';Click 'TimetableManualMode'
 Click 'SaveTimetable';Assert ($root.FindName('TimetableEditorMessage').Text.Contains('駅名')) 'Missing station accepted'
 $root.FindName('TimetableStation').Text='天王町';$root.FindName('TimetableLine').Text='相鉄本線';$root.FindName('TimetableDirection').Text='横浜方面'
 Click 'SaveTimetable';Assert ($root.FindName('TimetableEditorMessage').Text.Contains('時刻')) 'Empty times accepted'
 $root.FindName('TimetableText').Text=(Times @(2,9,17,26,41) @('','急','','各停','快速'))+"`r`nメモ書き"
 Assert ($root.FindName('TimetableCount').Text.StartsWith('5本') -and $root.FindName('TimetableCount').Text.Contains('読めない行')) "Count wrong: $($root.FindName('TimetableCount').Text)"
 Click 'TimetableHolidayTab';Assert ($root.FindName('TimetableText').Text -eq '' -and $root.FindName('TimetableCount').Text.Contains('平日と同じ')) 'Holiday tab wrong'
 Click 'TimetableWeekdayTab';Assert ($root.FindName('TimetableText').Text.Contains('メモ書き')) 'Weekday text lost when switching tabs'
 Assert ($root.FindName('TimetableEditorMessage').Text -eq '') 'Stale validation message stayed after typing times'
 Pump 1900;Snapshot 'timetable-editor.png'
 Click 'SaveTimetable';Pump 1900;Assert ($editor.Visibility -eq 'Collapsed' -and $tt.GetProperty('All',$sf).GetValue($null).Length -eq 1) 'Save failed'
 $trains=@(Tagged 'timetable-train');Assert ($trains.Count -eq 3) "Expected 3 upcoming trains, got $($trains.Count)"
 function Inline($tb){[Windows.Documents.TextRange]::new($tb.ContentStart,$tb.ContentEnd).Text};$c0=Inline $trains[0].Children[1];$c1=Inline $trains[1].Children[1]
 Assert ($c0 -match '^あと(2|3)分' -and $c1.Contains('急')) "Countdown wrong: $c0 / $c1"
 # Second station, different line, so the list has two cards.
 Press @(Tagged 'timetable-add')[0];Pump 900;Click 'TimetableManualMode';$root.FindName('TimetableStation').Text='横浜';$root.FindName('TimetableLine').Text='JR横須賀線';$root.FindName('TimetableDirection').Text='逗子・久里浜方面';$root.FindName('TimetableText').Text=(Times @(4,12,23) @('久里浜','逗子','久里浜'));Click 'SaveTimetable';Pump 1900
 Assert (@(Tagged 'timetable-card').Count -eq 2) 'Second timetable missing';Snapshot 'timetable.png'
 # Edit, Esc cancels without saving, delete needs two presses.
 Press @(Tagged 'timetable-edit')[1];Pump 900;Assert ($root.FindName('TimetableStation').Text -eq '横浜' -and $root.FindName('DeleteTimetable').Visibility -eq 'Visible') 'Edit did not load the entry'
 $root.FindName('TimetableStation').Text='変更してから取消';Key Escape;Pump 1200;Assert ($editor.Visibility -eq 'Collapsed' -and $tt.GetMethod('Find',$sf).Invoke($null,@($tt.GetProperty('All',$sf).GetValue($null)[1].Id)).Station -eq '横浜') 'Esc saved changes'
 Press @(Tagged 'timetable-edit')[1];Pump 900;Click 'DeleteTimetable';Assert ($tt.GetProperty('All',$sf).GetValue($null).Length -eq 2) 'Delete did not ask for confirmation';Click 'DeleteTimetable';Pump 1200;Assert ($tt.GetProperty('All',$sf).GetValue($null).Length -eq 1 -and @(Tagged 'timetable-card').Count -eq 1) 'Delete failed'
 # Persistence, and the other transit choices still work offline-or-not.
 $tt.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null;Assert ($tt.GetProperty('All',$sf).GetValue($null)[0].Station -eq '天王町' -and $tt.GetProperty('All',$sf).GetValue($null)[0].Weekday.Contains('メモ書き')) 'Not persisted as typed'
 $panel.Collapse();Pump 900;$panel.Open();Pump 1900;Assert (@(Tagged 'timetable-card').Count -eq 1) 'Reopen lost the timetable view'
 Assert ($root.FindName('RefreshData').Visibility -eq 'Collapsed') 'Refresh shown for offline timetable'
 Click 'Choice0';Pump 300;Assert (-not (@(Tagged 'timetable-card').Count) -and $root.FindName('RefreshData').Visibility -eq 'Visible') 'Timetable cards or hidden refresh leaked into 近隣'
 Click 'Choice3';Pump 300;Click 'WeatherTab';Pump 300;Assert ($root.FindName('RefreshData').Visibility -eq 'Visible') 'Refresh stayed hidden on another tab'
 Write-Output 'PASS: parser formats/labels/unreadable lines/after-midnight, holiday fallback + national holiday, empty state, validation, weekday/holiday tabs, live count, countdown, two cards, Esc cancel, two-step delete, persistence, reopen, switching back'
 Write-Output $folder
}finally{$shell.Dispose();$panel.Close()}
