$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-refinements-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
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
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 2200
 Assert ($null -eq $root.FindName('CloseButton')) 'Close button remains'
 $end=[DateTime]::UtcNow.AddSeconds(30);while($pt.GetField('updatingToday',$if).GetValue($panel)){Pump 100;if([DateTime]::UtcNow -gt $end){throw 'Calendar weather timeout'}}
 Assert ($root.FindName('TodayHigh').Text -match '\d' -and $root.FindName('TodayLow').Text -match '\d') 'Calendar high/low missing'
 $panel.UpdateTimerProgress(.25,$true);Pump 150;Assert ($root.FindName('TimerProgress').Width -eq 26) 'Timer 25% gauge incorrect';Snapshot 'timer-calendar.png'
 $panel.UpdateTimerProgress(.75,$true);Pump 150;Assert ($root.FindName('TimerProgress').Width -eq 78) 'Timer gauge did not increase';$panel.UpdateTimerProgress(0,$false)
 $mini=[Activator]::CreateInstance($a.GetType('OrbitCalendar.ParticleTimer'),[object[]]@([Action]{}));$mini.Width=188;$mini.Height=52;$mini.Update('05:00',.5);$mini.Width=$mini.PreferredWidth;$mini.Show();Pump 1800
 $mt=$mini.GetType();$effect=$mt.GetField('effect',$if).GetValue($mini);Assert ($effect.GetType().GetField('grains',$if).GetValue($effect).Length -gt 100) 'Compact timer is not particle rendered'
 Assert ($mt.GetField('glass',$if).GetValue($mini).Progress -eq .5 -and $mt.GetField('gauge',$if).GetValue($mini).Height -eq 15) 'Hourglass/progress not synchronized'
 SaveImage $mini.Content ([int]$mini.PreferredWidth) 52 'timer-particles.png';$mini.Hide()
 Click 'MarketTab';WaitLoading;Assert ($root.FindName('DataSource').Visibility -eq 'Collapsed') 'Market footer link remains'
 Click 'MarketRange';Pump 1500;Assert ($root.FindName('RangePicker').Visibility -eq 'Visible') 'Styled range menu did not open';Snapshot 'market-periods.png'
 $root.FindName('RangeChoices').Children[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));WaitLoading
 $plotView=$pt.GetField('marketSaved',$if).GetValue($panel);$short=$plotView['y:^N225:1'];Assert ($short.Bars.Count -gt 10 -and $short.Bars.Count -lt 30) 'One month stock data incorrect'
 Click 'MarketRange';$root.FindName('RangeChoices').Children[3].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));WaitLoading
 $long=$plotView['y:^N225:12'];Assert ($long.Bars.Count -gt 150 -and ($long.Bars[-1].Day-$long.Bars[0].Day).TotalDays -gt 300) 'One year stock data incorrect';Snapshot 'market-year.png';Reopen 1
 $fund=$catalog['f:89311199'];$fund1=Await ($data.GetMethod('MarketPeriod',$sf).Invoke($null,[object[]]@($fund,1,[Threading.CancellationToken]::None)));$fund12=Await ($data.GetMethod('MarketPeriod',$sf).Invoke($null,[object[]]@($fund,12,[Threading.CancellationToken]::None)))
 Assert ($fund1.IsFund -and $fund12.Bars.Count -gt $fund1.Bars.Count*5) 'SBI fund history ignores selected period'
 $five=Await ($data.GetMethod('MarketPeriod',$sf).Invoke($null,[object[]]@($catalog['y:^N225'],60,[Threading.CancellationToken]::None)));Assert ($five.Stamp.Contains('週足') -and ($five.Bars[-1].Day-$five.Bars[0].Day).TotalDays -gt 1400) 'Five year chart incorrect'
 Click 'WeatherTab';WaitLoading
 Assert ($root.FindName('Plot').Width -eq 356 -and $root.FindName('DataSource').Visibility -eq 'Collapsed') 'Weather width/footer incorrect'
 $plot=$root.FindName('Plot');$legend=$root.FindName('RainLegend');$lb=$legend.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($legend.RenderSize));$pb=$plot.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($plot.RenderSize));Assert ($lb.Bottom -lt $pb.Top) 'Legend not above map'
 $boundary=@($plot.Children|Where-Object Tag -eq 'prefectures');Assert ($boundary.Count -eq 1 -and $boundary[0].Data.Bounds.Width -gt 0) 'Prefecture boundaries missing'
 $line=@($root.FindName('TemperaturePlot').Children|Where-Object {$_ -is [Windows.Shapes.Polyline]})[0];Assert ($line.Points[0].X -eq 0 -and $line.Points[-1].X -eq 356) 'Temperature line does not span map width'
 $slider=$root.FindName('RadarTime');$slider.Value=$slider.Maximum;Pump 200;$track=$slider.Template.FindName('PART_Track',$slider);$thumb=$track.Thumb;$point=$thumb.TranslatePoint([Windows.Point]::new($thumb.ActualWidth/2,$thumb.ActualHeight/2),$root);Assert ([Math]::Abs($point.X-$pb.Right) -lt 1.2) 'Playback track does not align with map edge'
 $rb=$root.FindName('RadarControls').TransformToAncestor($root).TransformBounds([Windows.Rect]::new($root.FindName('RadarControls').RenderSize));Assert ($rb.Bottom -le $root.Height-8) 'Weather controls clipped'
 Assert (-not $root.FindName('RadarRange').Text.Contains('間隔')) 'Weather explanation remains';Snapshot 'weather-layout.png';Reopen 2
 Click 'DailyMode';WaitLoading;Reopen 2;Assert ($root.FindName('DailyWeatherBody').Visibility -eq 'Visible') 'Weather subview not retained'
 Click 'NewsTab';WaitLoading;$end=[DateTime]::UtcNow.AddSeconds(45);while($null -ne $pt.GetField('newsPreview',$if).GetValue($panel)){Pump 100;if([DateTime]::UtcNow -gt $end){throw 'News image timeout'}};Pump 1800
 $info=$pt.GetField('information',$if).GetValue($panel);Assert ($info.Items[0].Picture.PixelWidth -gt 0 -and $info.Items[0].Description.Length -gt 0) 'News image/summary unavailable'
 Snapshot 'news-card.png';$card=$root.FindName('InformationBody').Children[0];Assert ($card.Tag -eq 'news-card') 'News is still a headline list';$card.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));Pump 150;Assert ($pt.GetField('infoPage',$if).GetValue($panel) -eq 1) 'News click did not advance';Reopen 3
 Assert (([IO.File]::ReadAllLines((Join-Path $folder 'display.txt')))[3] -eq '3') 'Last tab not persisted'
 $second=[Activator]::CreateInstance($pt,[object[]]@([Action]{},[Action]{}));Assert ($pt.GetField('mode',$if).GetValue($second) -eq 3) 'Fresh panel ignores last tab';$second.Close()
 Click 'TransitTab';WaitLoading
 $itemType=$a.GetType('OrbitCalendar.InformationFeeds+Item');$feedType=$a.GetType('OrbitCalendar.InformationFeeds+Feed');$normal=[Activator]::CreateInstance($itemType,$true);$normal.Title='通常路線';$normal.Source='JR東日本';$normal.Url='https://example.com/';$normal.Detail='平常運転';$normal.Normal=$true
 $delay=[Activator]::CreateInstance($itemType,$true);$delay.Title='検証用の路線';$delay.Source='相模鉄道';$delay.Url='https://www.sotetsu.co.jp/train/status/';$delay.Detail='【表示検証】遅延のある路線だけを表示します';$delay.Normal=$false
 $unknown=[Activator]::CreateInstance($itemType,$true);$unknown.Title='未取得路線';$unknown.Source='東急電鉄';$unknown.Url='https://example.com/';$unknown.Normal=$null
 $fixture=[Activator]::CreateInstance($feedType,$true);$fixture.Items=@($normal,$delay,$unknown);$fixture.Fetched=[DateTime]::UtcNow;$fixture.PartialFailure=$true;$pt.GetField('information',$if).SetValue($panel,$fixture)
 $pt.GetMethod('RenderInformation',$if).Invoke($panel,@($false))|Out-Null;$panel.UpdateLayout();$pt.GetField('effect',$if).GetValue($panel).Refresh();Pump 200
 Assert ($root.FindName('InformationBody').Children.Count -eq 1) 'Transit shows normal/unknown as disruptions';Assert ($root.FindName('DataStamp').Text.Contains('確認できません')) 'Missing route information silently hidden';Snapshot 'transit-example.png'
 $fixture.Items=@($normal);$fixture.PartialFailure=$false;$pt.GetMethod('RenderInformation',$if).Invoke($panel,@($false))|Out-Null;Assert ($root.FindName('InformationBody').Children.Count -eq 1 -and $root.FindName('InformationBody').Children[0].Tag -eq 'transit-clear') 'Normal service confirmation missing';Reopen 4
 Click 'SettingsButton';Pump 1800;Click 'OpenCredits';Pump 1800;Assert ($root.FindName('CreditsPicker').Visibility -eq 'Visible' -and $root.FindName('CreditLinks').Children.Count -ge 8) 'Credits inaccessible'
 Write-Output 'PASS: tab retention/persistence, removed close/footer controls, calendar temperatures, timer/hourglass particles and gauges, themed range menu and live stock/fund periods, aligned weather axes/boundaries, news images/summaries/next article, disruptions-only transit and unknown-state disclosure, settings credits'
 Write-Output $folder
} finally {if($mini){$mini.Close()};$panel.Hide();$panel.Close()}
