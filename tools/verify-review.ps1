$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-review-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Name',$sf).SetValue($null,$null,$null) # Isolate saved-place name from the user's settings.
$mapScale=$a.GetType('OrbitCalendar.MapScale');$mapScale.GetField('path',$sf).SetValue($null,(Join-Path $folder 'map-zoom.txt'));$mapScale.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
$fixturePlaces=$a.GetType('OrbitCalendar.SavedPlace');$fixturePlaces.GetField('path',$sf).SetValue($null,(Join-Path $folder 'fixture-location.txt'));[IO.File]::WriteAllText((Join-Path $folder 'fixture-location.txt'),'35.681236,139.767125');$fixtureCoordinate=$fixturePlaces.GetMethod('Load',$sf).Invoke($null,@());$fixturePlaces.GetProperty('Coordinate',$sf).SetValue($null,$fixtureCoordinate,$null)
$data=$a.GetType('OrbitCalendar.LiveData');$catalog=$a.GetType('OrbitCalendar.Instruments').GetField('ById',$sf).GetValue($null)
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Await($task){$end=[DateTime]::UtcNow.AddSeconds(100);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $end){throw 'Request timeout'};Pump 50};if($task.IsFaulted){throw $task.Exception.ToString()};return $task.Result}
$st=$a.GetType('OrbitCalendar.Shell');$shell=$st.GetConstructor($if,$null,[type[]]@([bool]),$null).Invoke([object[]]@($false))
$panel=$st.GetField('panel',$if).GetValue($shell);$mini=$st.GetField('countdown',$if).GetValue($shell);$pt=$panel.GetType();$root=$panel.Content.Child
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI load timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function SaveImage($element,$width,$height,$name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($element);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
function Snapshot($name){Pump 250;$panel.UpdateLayout();SaveImage $root 458 ([int]$root.Height) $name}
function Reopen($expected){$panel.Collapse();Pump 900;Assert (-not $panel.IsVisible) 'Collapse failed';$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1800;Assert ($pt.GetField('mode',$if).GetValue($panel) -eq $expected) 'Previous tab not restored';Assert ($root.Height -eq $panel.PreferredHeight) 'Reopened size incorrect'}
try{
 $shell.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900
 $focus=$root.FindName('FocusButton');$position=$focus.TranslatePoint([Windows.Point]::new(0,0),$root)
 Click 'FocusButton';Assert ($st.GetField('focusTimer',$if).GetValue($shell).IsRunning -and $focus.Content -eq '■') 'Real timer did not start'
 $remaining=$root.FindName('TimerDuration').Content;Click 'TimerDuration';$root.FindName('TimeChoices').Children[1].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));Assert ($root.FindName('TimerDuration').Content -eq $remaining -and $panel.TimerMinutes -eq 5) 'Choosing next duration replaced current countdown'
 Click 'FocusButton';Assert (-not $st.GetField('focusTimer',$if).GetValue($shell).IsRunning -and $focus.Content -eq '▶' -and $root.FindName('TimerDuration').Content -eq '5 分') 'Real timer did not stop'
 $bar=$root.FindName('TimerProgressTrack');Assert ($bar.Visibility -eq 'Collapsed' -and $position -eq $focus.TranslatePoint([Windows.Point]::new(0,0),$root)) 'Stopping timer shifted controls'
 Click 'FocusButton';Click 'TimerDuration';Click 'FocusButton';Assert (-not $root.FindName('TimePickerHint').Text.Contains('次回')) 'Timer picker hint stayed in running state';Click 'CloseTimePicker'
 # Stop during the particle entrance must remove the bar immediately.
 Click 'FocusButton';$bounds=$bar.TransformToAncestor($root.FindName('Face')).TransformBounds([Windows.Rect]::new($bar.RenderSize));Click 'FocusButton';$effect=$pt.GetField('effect',$if).GetValue($panel);$grains=$effect.GetType().GetField('grains',$if).GetValue($effect);Assert (@($grains|Where-Object {$bounds.Contains($_.Target)}).Count -eq 0) 'Stopped timer left flying gauge particles'
 Click 'FocusButton';$panel.Collapse();Pump 850;Assert (-not $panel.IsVisible -and $mini.IsVisible) 'Compact timer did not appear immediately after closing'
 $shell.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Assert ($panel.IsVisible -and -not $mini.IsVisible -and $focus.Content -eq '■') 'Reopening lost timer state';Click 'FocusButton'
 # A visible compact timer must rebuild its raster when minute digits shrink.
 $probe=[Activator]::CreateInstance($a.GetType('OrbitCalendar.ParticleTimer'),[object[]]@([Action]{}));$probe.Update('180:00',.5);$probe.Width=$probe.PreferredWidth;$probe.Height=52;$probe.Show();Pump 1800
 $probe.Update('05:00',.5);$probe.Width=$probe.PreferredWidth;$probe.UpdateLayout();Pump 180
 $mt=$probe.GetType();$me=$mt.GetField('effect',$if).GetValue($probe);$layer=$me.GetType().GetField('layer',$if).GetValue($me);$face=$me.GetType().GetField('face',$if).GetValue($me);Assert ($layer.PixelWidth -eq [int]$face.RenderSize.Width) 'Compact timer raster distorted after resizing';SaveImage $probe.Content ([int]$probe.PreferredWidth) 52 'timer.png';$probe.Hide()
 Click 'CalendarTab';Pump 1900;$panel.Width=366.4;$panel.Height=433.6;$panel.UpdateLayout();$scaled=$root.TransformToAncestor($panel).TransformBounds([Windows.Rect]::new($root.RenderSize));Assert ($scaled.Right -le $panel.ActualWidth+1 -and $scaled.Bottom -le $panel.ActualHeight+1) 'Scaled view extends beyond window';$shell.Open();Pump 1800;Snapshot 'calendar.png';Click 'SettingsButton';Pump 1900;Snapshot 'settings.png';Click 'CloseSettingsPicker';Click 'TimerDuration';Pump 1900;Snapshot 'time-selection.png';Click 'CloseTimePicker'
 Click 'MarketTab';WaitLoading;Assert ($root.FindName('MarketValue').Visibility -eq 'Visible' -and $root.FindName('MarketValue').Text.Contains('pt')) 'Market value is hidden'
 Snapshot 'market.png'
 # Deliberately long labels and large values must occupy separate rows.
 $view=$pt.GetField('marketSaved',$if).GetValue($panel)['y:^N225:3'];$name=$view.MarketName;$value=$view.MarketValue
 $view.MarketName='長い銘柄名を表示した場合にも価格の数字が隠れないことを確認するテスト';$view.MarketValue='123,456,789.12 円';$pt.GetMethod('ShowData',$if).Invoke($panel,@($view))|Out-Null;$panel.UpdateLayout();$effect.Refresh();Pump 150
 $nb=$root.FindName('DataTitle').TransformToAncestor($root).TransformBounds([Windows.Rect]::new($root.FindName('DataTitle').RenderSize));$vb=$root.FindName('MarketValue').TransformToAncestor($root).TransformBounds([Windows.Rect]::new($root.FindName('MarketValue').RenderSize));Assert ($nb.Bottom -le $vb.Top -and $vb.Right -lt 458) 'Long market label overlaps price';Snapshot 'market-long-title.png'
 $view.MarketName=$name;$view.MarketValue=$value
 Click 'WeatherTab';Assert ($root.FindName('LoadingArea').Visibility -eq 'Visible') 'Weather loader absent';WaitLoading;Assert ($root.FindName('MarketValue').Visibility -eq 'Collapsed') 'Market value leaked into weather';Snapshot 'weather.png';Click 'DailyMode';WaitLoading;Snapshot 'weather-daily.png'
 Click 'NewsTab';WaitLoading;$end=[DateTime]::UtcNow.AddSeconds(50);while($null -ne $pt.GetField('newsPreview',$if).GetValue($panel)){Pump 100;if([DateTime]::UtcNow -gt $end){throw 'News preview timeout'}};Pump 1800;Snapshot 'news.png'
 $pt.GetMethod('RenderInformation',$if).Invoke($panel,@($true))|Out-Null;$pt.GetMethod('MoveNews',$if).Invoke($panel,@(1))|Out-Null;Pump 200;Assert ($root.FindName('DataStamp').Text.Contains('更新失敗')) 'Article navigation hid stale-feed warning'
 $panel.Collapse();Pump 850;$shell.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Assert ($root.FindName('DataStamp').Text.Contains('更新失敗')) 'Reopening hid stale-feed warning'
 Click 'TransitTab';WaitLoading;Assert ($root.FindName('MarketValue').Visibility -eq 'Collapsed') 'Market value leaked into transit';Snapshot 'transit.png'
 foreach($tab in @('WeatherTab','NewsTab','TransitTab','MarketTab','CalendarTab')){Click $tab;Pump 30};Pump 1900;Assert ($root.FindName('LoadingArea').Visibility -eq 'Collapsed') 'Loading overlay stuck after rapid tab changes'
 $panel.Collapse();Pump 850;$shell.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Assert ($pt.GetField('mode',$if).GetValue($panel) -eq 0) 'Rapid switching lost selected tab'
 Write-Output 'PASS: real timer start/stop/next-duration/reopen, immediate compact window, gauge cancel, resized particle raster, long market labels/prices, all-tab layout/loaders, rapid switching, persistent stale-news warnings'
 Write-Output $folder
}finally{if($probe){$probe.Close()};$shell.Dispose();$panel.Close() }
