$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-polish-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null)
$data=$a.GetType('OrbitCalendar.LiveData');$catalog=$a.GetType('OrbitCalendar.Instruments').GetField('ById',$sf).GetValue($null)
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Await($task){$end=[DateTime]::UtcNow.AddSeconds(100);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $end){throw 'Request timeout'};Pump 50};if($task.IsFaulted){throw $task.Exception.ToString()};return $task.Result}
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI load timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function SaveImage($element,$width,$height,$name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($element);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
function Snapshot($name){Pump 250;$panel.UpdateLayout();SaveImage $root 458 ([int]$root.Height) $name}
function Reopen($expected){$panel.Collapse();Pump 900;Assert (-not $panel.IsVisible) 'Collapse failed';$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1800;Assert ($pt.GetField('mode',$if).GetValue($panel) -eq $expected) 'Previous tab not restored';Assert ($root.Height -eq $panel.PreferredHeight) 'Reopened size incorrect'}
$mini=$null
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1800
 $focus=$root.FindName('FocusButton');$before=$focus.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($focus.RenderSize))
 $panel.Tick([DateTime]::Now,'22:30');$panel.UpdateTimerProgress(.25,$true)
 $after=$focus.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($focus.RenderSize))
 Assert ($before.X -eq $after.X -and $before.Y -eq $after.Y) 'Play/stop button shifted when progress appeared'
 $bar=$root.FindName('TimerProgressTrack');$bb=$bar.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($bar.RenderSize));Assert ($bb.Left -lt 25 -and $bb.Right -lt $before.Left) 'Timer bar is not aligned left'
 $effect=$pt.GetField('effect',$if).GetValue($panel);$grains=$effect.GetType().GetField('grains',$if).GetValue($effect);Assert (@($grains|Where-Object {$_.Moving -and $bb.Contains($_.Target)}).Count -gt 0) 'Timer bar did not assemble from particles'
 Pump 1700;Snapshot 'timer-progress.png';$panel.UpdateTimerProgress(.75,$true);Assert ([Math]::Abs($root.FindName('TimerProgress').Width-$root.FindName('TimerProgressTrack').ActualWidth*.75) -lt .01) 'Gauge progression failed';$panel.UpdateTimerProgress(0,$false);$panel.Tick([DateTime]::Now,'')
 $weatherGroup=$root.FindName('TodayWeatherGroup');$wg=$weatherGroup.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($weatherGroup.RenderSize));$date=$root.FindName('SelectedDate');$db=$date.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($date.RenderSize));Assert ($wg.Left-$db.Right -ge 35 -and $wg.Right -le 430 -and $wg.Width -le 294) 'Calendar weather is too close to week label';Snapshot 'calendar.png'
 $mini=[Activator]::CreateInstance($a.GetType('OrbitCalendar.ParticleTimer'),[object[]]@([Action]{}));$mini.Update('05:00',.5);$small=$mini.PreferredWidth;$mini.Width=$small;$mini.Height=52;$mini.Show();Pump 1800
 Assert ($small -ge 120 -and $small -le 140) 'Compact timer still has excessive padding';SaveImage $mini.Content ([int]$small) 52 'timer.png'
 $mini.Update('11:11',.5);Assert ($mini.PreferredWidth -eq $small) 'Compact timer width changes between equal-length times'
 $mini.Update('180:00',.5);Assert ($mini.PreferredWidth -gt $small -and $mini.PreferredWidth -le 160) 'Long duration does not fit compact timer';$mini.Hide()
 Click 'WeatherTab';Assert ($root.FindName('LoadingArea').Visibility -eq 'Visible' -and -not $root.FindName('DataStamp').Text.Contains('取得中')) 'Radar loading indicator missing'
 $dots=$pt.GetField('mainLoadingParticles',$if).GetValue($panel);$phase=$dots.Phase;Pump 180;Assert ($dots.Phase -ne $phase) 'Loading animation is not advancing';WaitLoading;Assert ($root.FindName('LoadingArea').Visibility -eq 'Collapsed') 'Radar loading indicator stuck'
 # Old requests must never hide a newer request's indicator.
 $old=[Threading.CancellationTokenSource]::new();$new=[Threading.CancellationTokenSource]::new()
 $pt.GetMethod('BeginDataLoading',$if).Invoke($panel,@($old))|Out-Null;$pt.GetMethod('BeginDataLoading',$if).Invoke($panel,@($new))|Out-Null;$pt.GetMethod('EndDataLoading',$if).Invoke($panel,@($old))|Out-Null;Assert ($root.FindName('LoadingArea').Visibility -eq 'Visible') 'Stale request hid the current loader'
 $mask=$root.FindName('LoadingArea');$mb=$mask.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($mask.RenderSize));$controls=$root.FindName('DataChoices');$controlsBounds=$controls.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($controls.RenderSize)); $mapBounds=$root.FindName('RainLegend').TransformToAncestor($root).TransformBounds([Windows.Rect]::new($root.FindName('RainLegend').RenderSize));Assert ($mb.Left -lt 18 -and $mb.Top -ge $controlsBounds.Bottom -and $mb.Top -le $mapBounds.Top) 'Loading mask clips a button or leaves old content visible'
 $effect.Start($true);Pump 1800;Snapshot 'loading.png';$pt.GetMethod('EndDataLoading',$if).Invoke($panel,@($new))|Out-Null;$effect.Refresh();$old.Dispose();$new.Dispose();Snapshot 'weather.png'
 $data.GetField('CachedForecast',$sf).SetValue($null,$null);Click 'DailyMode';Assert ($root.FindName('LoadingArea').Visibility -eq 'Visible' -and $root.FindName('DataStamp').Text -eq '') 'Three-day forecast loading text remains';WaitLoading;Assert ($root.FindName('LoadingArea').Visibility -eq 'Collapsed') 'Three-day loader stuck';Snapshot 'weather-daily.png'
 Click 'NewsTab';Assert ($root.FindName('LoadingArea').Visibility -eq 'Visible' -and $root.FindName('DataStamp').Text -eq '') 'News loading indicator missing';WaitLoading
 $end=[DateTime]::UtcNow.AddSeconds(50);while($null -ne $pt.GetField('newsPreview',$if).GetValue($panel)){Pump 100;if([DateTime]::UtcNow -gt $end){throw 'News preview timeout'}};Pump 1800
 $cats=$root.FindName('NewsCategories');$cb=$cats.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($cats.RenderSize));$header=$root.FindName('DataHeader');$hb=$header.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($header.RenderSize));Assert ($cats.Visibility -eq 'Visible' -and [Math]::Abs($cb.Right-$hb.Right) -lt 1 -and $cb.Top -lt 90 -and $root.FindName('RegionChoices').Visibility -eq 'Collapsed') 'News categories are not in upper-right header';Snapshot 'news.png'
 Click 'NewsCategory1';WaitLoading;Assert ($pt.GetField('choice',$if).GetValue($panel)[3] -eq 1) 'Header category does not select news'
 Click 'TransitTab';Assert ($root.FindName('LoadingArea').Visibility -eq 'Visible') 'Transit loader missing';WaitLoading
 $feedType=$a.GetType('OrbitCalendar.InformationFeeds+Feed');$itemType=$a.GetType('OrbitCalendar.InformationFeeds+Item');$fixture=[Activator]::CreateInstance($feedType,$true);$fixture.Fetched=[DateTime]::UtcNow
 $normal=[Activator]::CreateInstance($itemType,$true);$normal.Title='検証用';$normal.Source='JR東日本';$normal.Normal=$true;$fixture.Items=@($normal);$pt.GetField('information',$if).SetValue($panel,$fixture)
 $pt.GetMethod('RenderInformation',$if).Invoke($panel,@($false))|Out-Null;$panel.UpdateLayout();$effect.Refresh();Assert ($root.FindName('InformationBody').Children[0].Tag -eq 'transit-clear') 'Normal status is still blank';Snapshot 'transit.png'
 $normal.Normal=$null;$fixture.PartialFailure=$true;$pt.GetMethod('RenderInformation',$if).Invoke($panel,@($false))|Out-Null;Assert ($root.FindName('InformationBody').Children[0].Tag -eq 'transit-unavailable') 'Unknown status falsely shown as normal'
 $normal.Normal=$true;$fixture.PartialFailure=$false;$pt.GetMethod('RenderInformation',$if).Invoke($panel,@($true))|Out-Null;Assert ($root.FindName('InformationBody').Children[0].Tag -eq 'transit-stale') 'Stale information falsely shown as current normal'
 # A canceled weather load must not leak its overlay into the calendar.
 Click 'WeatherTab';Click 'CalendarTab';Pump 1900;Assert ($root.FindName('LoadingArea').Visibility -eq 'Collapsed' -and $root.FindName('NewsCategories').IsVisible -eq $false) 'Loader/categories leaked across tabs'
 Write-Output 'PASS: compact timer sizing, fixed play/stop position, left particle gauge, centered weather, animated/cancel-safe loaders, header news categories, normal/unknown/stale transit states'
 Write-Output $folder
}finally{if($mini){$mini.Close()};$panel.Hide();$panel.Close()}
