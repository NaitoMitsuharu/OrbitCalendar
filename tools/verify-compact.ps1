$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-compact-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
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
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI load timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function SaveImage($element,$width,$height,$name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($element);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
function Snapshot($name){Pump 250;$panel.UpdateLayout();SaveImage $root 458 ([int]$root.Height) $name}
function Reopen($expected){$panel.Collapse();Pump 900;Assert (-not $panel.IsVisible) 'Collapse failed';$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1800;Assert ($pt.GetField('mode',$if).GetValue($panel) -eq $expected) 'Previous tab not restored';Assert ($root.Height -eq $panel.PreferredHeight) 'Reopened size incorrect'}
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900
 $effect=$pt.GetField('effect',$if).GetValue($panel)
 $selection=$pt.GetField('selected',$if).GetValue($panel);$next=[DateTime]::new($selection.Year,$selection.Month,1).AddDays(10)
 if($next -eq $selection){$next=$next.AddDays(1)}
 $pt.GetMethod('SelectDate',$if).Invoke($panel,@($next))|Out-Null
 $grains=$effect.GetType().GetField('grains',$if).GetValue($effect);Assert ($effect.Busy -and @($grains|Where-Object Moving).Count -gt 0) 'Day selection still changes without particle motion'
 $globe=[Windows.Rect]::new(198,53,95,94);Assert (@($grains|Where-Object {$_.Moving -and $globe.Contains($_.Target)}).Count -eq 0) 'Unchanged globe scattered during day selection'
 Pump 1900;Snapshot 'calendar.png'
 $panel.Tick([DateTime]::Now,'22:30');$panel.UpdateTimerProgress(.25,$true);Pump 1900
 $bar=$root.FindName('TimerProgressTrack');$time=$root.FindName('TimerDuration');$focus=$root.FindName('FocusButton')
 $fixed=$focus.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($focus.RenderSize))
 foreach($label in @('05:00','99:59','180:00')){
  $panel.Tick([DateTime]::Now,$label);$panel.UpdateTimerProgress(.75,$true);$panel.UpdateLayout()
  $bb=$bar.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($bar.RenderSize));$tb=$time.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($time.RenderSize));$fb=$focus.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($focus.RenderSize))
  Assert ([Math]::Abs(($bb.Top+$bb.Bottom)/2-($tb.Top+$tb.Bottom)/2) -le 1 -and [Math]::Abs(($bb.Top+$bb.Bottom)/2-($fb.Top+$fb.Bottom)/2) -le 1) 'Timer gauge and controls are not on the same row'
  Assert ($bb.Width -ge 300 -and $bb.Left -lt 25 -and [Math]::Abs($tb.Left-$bb.Right-12) -le 1) 'Timer gauge does not fill the available row'
  Assert (-not $bb.IntersectsWith($tb) -and $fb -eq $fixed) 'Timer gauge overlaps time or shifts the play/stop button'
  Assert ([Math]::Abs($root.FindName('TimerProgress').Width-$bar.ActualWidth*.75) -lt .01) 'Responsive gauge progress incorrect'
 }
 Pump 1900;Snapshot 'timer-progress.png'
 $panel.Collapse();Pump 900;$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900
 Assert ($bar.Visibility -eq 'Visible' -and $bar.ActualWidth -ge 300) 'Inline gauge lost after reopening'
 $panel.UpdateTimerProgress(1,$true);Assert ([Math]::Abs($root.FindName('TimerProgress').Width-$bar.ActualWidth) -lt .01) 'Completed gauge exceeds track'
 $panel.UpdateTimerProgress(0,$false);$panel.Tick([DateTime]::Now,'');$panel.UpdateLayout()
 Assert ($focus.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($focus.RenderSize)) -eq $fixed) 'Stopping timer moved button'
 Click 'WeatherTab';WaitLoading
 $refresh=$root.FindName('RefreshData');$stamp=$root.FindName('DataStamp');$r=$refresh.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($refresh.RenderSize));$d=$stamp.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($stamp.RenderSize));Assert ($refresh.Parent.Name -eq 'DataStampRow' -and [Math]::Abs(($r.Top+$r.Bottom)/2-($d.Top+$d.Bottom)/2) -lt 2 -and $d.Right -le $r.Left) 'Refresh is not aligned with the stamp'
 $regions=$root.FindName('RegionChoices');$tools=$root.FindName('MapTools');$rb=$regions.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($regions.RenderSize));$mb=$tools.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($tools.RenderSize));Assert ($tools.Parent.Name -eq 'DataChoices' -and $rb.Right -lt $mb.Left -and $mb.Right -le 438) 'Combined weather controls overlap'
 Assert ($root.Height -eq 574) 'Radar window not compact';Snapshot 'weather.png'
 Click 'DailyMode';WaitLoading;Assert ($root.FindName('DataChoices').Visibility -eq 'Collapsed' -and $root.Height -eq 542) 'Empty forecast toolbar remains'
 $daily=$root.FindName('DailyWeatherBody');$last=$daily.Children[$daily.Children.Count-1];$bounds=$last.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($last.RenderSize));Assert ($bounds.Bottom -le $root.Height-10) 'Compact daily forecast clips bottom caption';Snapshot 'weather-daily.png'
 Click 'NewsTab';WaitLoading;$end=[DateTime]::UtcNow.AddSeconds(50);while($null -ne $pt.GetField('newsPreview',$if).GetValue($panel)){Pump 100;if([DateTime]::UtcNow -gt $end){throw 'News preview timeout'}};Pump 1900
 Assert ($root.FindName('DataChoices').Visibility -eq 'Collapsed' -and $refresh.Parent.Name -eq 'DataStampRow') 'Empty news toolbar remains';Snapshot 'news.png'
 Click 'MarketTab';WaitLoading;Click 'ManageMarkets'
 $transition=$pt.GetField('marketTransition',$if).GetValue($panel);Assert ($transition.IsVisible -and $transition.Busy) 'Market settings still fade in instead of particles'
 Pump 1900;Assert ($root.FindName('MarketPicker').Opacity -eq 1 -and $transition.IsVisible -eq $false) 'Editable market settings not restored';Snapshot 'market-watchlist.png'
 Click 'SearchMarkets';Assert $transition.IsVisible 'Search screen did not animate';Pump 1900
 $root.FindName('MarketQuery').Text='オルカン';Pump 400;Assert ($root.FindName('MarketResults').Children.Count -gt 0) 'Search input broke after transition';Snapshot 'market-search.png'
 $picker=$root.FindName('MarketPicker');$pb=$picker.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($picker.RenderSize));Assert ($pb.Top -ge 48 -and $pb.Bottom -le $root.Height-40) 'Compact market settings overlap navigation'
 $rows=$root.FindName('MarketResults');$rowsBottom=$rows.Children[$rows.Children.Count-1].TransformToAncestor($root).TransformBounds([Windows.Rect]::new($rows.Children[$rows.Children.Count-1].RenderSize));$message=$root.FindName('MarketMessage').TransformToAncestor($root).TransformBounds([Windows.Rect]::new($root.FindName('MarketMessage').RenderSize));Assert ($rowsBottom.Bottom -le $message.Top+2) 'Market rows overlap page controls'
 Click 'CloseMarketPicker';Assert ($picker.Visibility -eq 'Collapsed' -and $transition.Closing) 'Market popup does not close with particles';Pump 850;Assert (-not $transition.IsVisible) 'Closing popup particles stuck'
 Click 'TransitTab';WaitLoading;Snapshot 'transit.png'
 Write-Output 'PASS: inline refresh, compact forecast/news/weather controls, same-row timer filling available width without overlaps, day-selection motion preserving globe, particle market settings/search/close with native editable input'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
