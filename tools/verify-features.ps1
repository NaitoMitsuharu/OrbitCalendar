$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe))
$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-features-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1)
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
# In-memory public fixture only; never write or reveal the user's saved coordinates.
$place=$a.GetType('OrbitCalendar.SavedPlace');$place.GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Name',$sf).SetValue($null,$null,$null) # Isolate saved-place name from the user's settings.
$mapScale=$a.GetType('OrbitCalendar.MapScale');$mapScale.GetField('path',$sf).SetValue($null,(Join-Path $folder 'map-zoom.txt'));$mapScale.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
$fixturePlaces=$a.GetType('OrbitCalendar.SavedPlace');$fixturePlaces.GetField('path',$sf).SetValue($null,(Join-Path $folder 'fixture-location.txt'));[IO.File]::WriteAllText((Join-Path $folder 'fixture-location.txt'),'35.681236,139.767125');$fixtureCoordinate=$fixturePlaces.GetMethod('Load',$sf).Invoke($null,@());$fixturePlaces.GetProperty('Coordinate',$sf).SetValue($null,$fixtureCoordinate,$null)
$data=$a.GetType('OrbitCalendar.LiveData');$feeds=$a.GetType('OrbitCalendar.InformationFeeds')
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Await($task){$end=[DateTime]::UtcNow.AddSeconds(100);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $end){throw 'Request timed out'};Pump 50};if($task.IsFaulted){throw $task.Exception.ToString()};if($task.IsCanceled){throw 'Request canceled'};return $task.Result}
function Static($name,$args2){return $feeds.GetMethod($name,$sf).Invoke($null,[object[]]$args2)}
Assert (-not (Static 'SafeUrl' @('javascript:alert(1)'))) 'Unsafe article URL accepted'
$xml='<rss><channel><item><title>A &amp; B</title><link>https://example.com/a</link><pubDate>Tue, 03 Nov 2026 12:00:00 +0900</pubDate></item><item><title>Bad</title><link>file:///C:/bad</link></item></channel></rss>'
$news=@(Static 'ParseNews' @([Text.Encoding]::UTF8.GetBytes($xml),'fixture'));Assert ($news.Count -eq 1 -and $news[0].Title -eq 'A & B' -and $null -ne $news[0].Published) 'RSS title, date, or URL filter failed'
$sot=Static 'ParseSotetsu' @('[{"DATE":"2026/11/03 12:00:00","MSG":"遅延しています","IRREGULAR":true}]');Assert ($sot.Normal -eq $false -and $sot.Detail.Contains('遅延')) 'Sotetsu disruption parsed as normal'
$sot=Static 'ParseSotetsu' @('[{"MSG":"unknown"}]');Assert ($null -eq $sot.Normal) 'Missing status parsed as normal'
$tok=@(Static 'ParseTokyu' @('{"check_ty":"1,0","unten_ty":"運行情報あり","check_sh":"x","unten_sh":"不明"}'));Assert ($tok[0].Normal -eq $false -and $null -eq $tok[1].Normal) 'Tokyu unknown/disruption states failed'
$jr=@(Static 'ParseJr' @('<p>平常運転</p><li class="traininfo-routes__table__item"><span class="traininfo-routes__name">横須賀線</span><p class="traininfo-routes__status trouble"><span>遅延</span></p></li>'));Assert ($jr[0].Normal -eq $false -and $null -eq $jr[1].Normal) 'JR legend or missing route interpreted as normal'
foreach($i in 0..2){$n=Await ($feeds.GetMethod('News',$sf).Invoke($null,[object[]]@($i,$true,[Threading.CancellationToken]::None)));Assert ($n.Items.Length -gt 0) 'Live news empty';Await ($feeds.GetMethod('PreviewNews',$sf).Invoke($null,[object[]]@($n.Items[0],[Threading.CancellationToken]::None)))|Out-Null;Assert ($null -ne $n.Items[0].Picture -and $n.Items[0].Description.Length -gt 0) 'Live news preview missing';Write-Host ('News '+$i+': '+$n.Items.Length+' headlines')}
$transit=Await ($feeds.GetMethod('Transit',$sf).Invoke($null,[object[]]@($true,[Threading.CancellationToken]::None)))
Assert ($transit.Items.Length -eq 7 -and $null -ne $transit.Items[0].Normal) 'Transit route count/Sotetsu fetch failed'
foreach($r in $transit.Items){Write-Host ($r.Title+': '+$r.Normal+' '+$r.Detail)}
$forecast=Await ($data.GetMethod('ForecastWeather',$sf).Invoke($null,[object[]]@([Threading.CancellationToken]::None,$true)))
Assert ($forecast.daily.time.Length -eq 3 -and $forecast.hourly.time.Length -ge 48) 'Three day forecast unavailable'
Assert ($null -ne $forecast.Temperature([DateTime]::UtcNow)) 'Current temperature interpolation unavailable'
$forecastType=$a.GetType('OrbitCalendar.LiveData+WeatherForecast');$hourType=$a.GetType('OrbitCalendar.LiveData+HourlyWeather')
$f=[Activator]::CreateInstance($forecastType);$h=[Activator]::CreateInstance($hourType);$h.time=[long[]]@(0,3600);$h.temperature_2m=[Nullable[double][]]@(10,20);$f.hourly=$h
Assert ([Math]::Abs($f.Temperature([DateTime]::new(1970,1,1,0,30,0,[DateTimeKind]::Utc))-15) -lt .001) 'Temperature interpolation not linear'
Assert ($null -eq $f.Temperature([DateTime]::new(1970,1,2))) 'Missing temperature extrapolated'
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do {Pump 60;if([DateTime]::UtcNow -gt $end){throw 'UI load timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1700 }
function WaitNews { $end=[DateTime]::UtcNow.AddSeconds(50);while($null -ne $pt.GetField('newsPreview',$if).GetValue($panel)){Pump 100;if([DateTime]::UtcNow -gt $end){throw 'News preview timeout'}};Pump 1600 }
function Snapshot($name){Pump 200;$panel.UpdateLayout();$bounds=$root.FindName('DataSource').TransformToAncestor($root).TransformBounds([Windows.Rect]::new($root.FindName('DataSource').RenderSize));if($root.FindName('DataBody').Visibility -eq 'Visible'){Assert ($bounds.Bottom -lt $root.Height) 'Source/footer clipped'};$im=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($root);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1700
 Click 'WeatherTab';WaitLoading
 Assert ($root.FindName('WeatherTab').Content -eq '天気') 'Weather label wrong'
 $frames=$pt.GetField('radarFrames',$if).GetValue($panel);Assert ($frames.Length -gt 1) 'Radar forecast unavailable'
 $origin=$pt.GetField('radarOrigin',$if).GetValue($panel)
 Assert (@($frames|Where-Object {($_.ValidUtc-$origin).TotalMinutes -gt 60 -or $_.HourlyRain}).Count -eq 0) 'Rain timeline extends past 1 hour'
 $slider=$root.FindName('RadarTime');Assert (-not $slider.IsSnapToTickEnabled) 'Slider still snaps'
 Assert ($root.FindName('TemperaturePlot').Children.Count -ge 4) 'Temperature graph missing'
 $gapStart=$slider.Ticks[$slider.Ticks.Count-2];$gapEnd=$slider.Ticks[$slider.Ticks.Count-1];$seekA=$gapStart+($gapEnd-$gapStart)*.2;$seekB=$gapStart+($gapEnd-$gapStart)*.4
 $slider.Value=$seekA;Pump 150;$first=$pt.GetField('activeWeather',$if).GetValue($panel);$dot=$pt.GetField('temperatureCursor',$if).GetValue($panel);$x=$dot.X1
 $slider.Value=$seekB;Pump 150
 Assert ($slider.Value -eq $seekB -and $dot.X1 -gt $x -and $first -eq $pt.GetField('activeWeather',$if).GetValue($panel)) 'Continuous seek or map frame independence failed'
 $slider.Value=0;Snapshot 'weather.png';$slider.Value=$slider.Maximum;Pump 200;Snapshot 'weather-one-hour.png'
 $slider.Value=0;Click 'RadarPlay';Pump 400
 Assert ($slider.Value -gt 0 -and $slider.Value -lt 5) 'Playback stepped instead of moving smoothly'
 $pt.GetMethod('StopRadar',$if).Invoke($panel,@())|Out-Null
 $slider.Value=$slider.Maximum-.01;Click 'RadarPlay';Pump 1500
 Assert (-not $pt.GetField('radarPlayback',$if).GetValue($panel).IsEnabled -and $slider.Value -eq $slider.Maximum) 'Playback did not stop once'
 $endValue=$slider.Value;Pump 500;Assert ($slider.Value -eq $endValue) 'Playback looped'
 Click 'RadarPlay';Pump 300;Assert ($slider.Value -lt $slider.Maximum) 'Replay did not restart';$pt.GetMethod('StopRadar',$if).Invoke($panel,@())|Out-Null
 Click 'DailyMode';WaitLoading
 $daily=$root.FindName('DailyWeatherBody');Assert ($daily.Visibility -eq 'Visible' -and $daily.Children[0].Children.Count -eq 3) '3 forecast cards missing'
 $daily.Children[0].Children[2].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));Pump 1700
 Assert ($pt.GetField('forecastDay',$if).GetValue($panel) -eq 2) 'Forecast day selection failed';Snapshot 'weather-daily.png'
 Click 'RainMode';WaitLoading;Assert ($root.FindName('Plot').Visibility -eq 'Visible' -and $root.FindName('DailyWeatherBody').Visibility -eq 'Collapsed') 'Return to radar failed'
 Click 'NewsTab';WaitLoading;Assert ($root.Height -eq 542 -and $root.FindName('InformationBody').Children.Count -gt 1) 'News UI missing';WaitNews;Snapshot 'news.png'
 Click 'NewsCategory2';WaitLoading;Assert ($pt.GetField('choice',$if).GetValue($panel)[3] -eq 2) 'News category failed'
 Click 'TransitTab';WaitLoading;Assert (@($root.FindName('InformationBody').Children|Where-Object Tag -eq 'transit-disruption').Count -eq @($transit.Items|Where-Object Normal -eq $false|Select-Object -First 3).Count) 'Transit disruption count incorrect';Assert ($root.FindName('InformationBody').Children.Count -ge 1) 'Transit status missing';Snapshot 'transit.png'
 Click 'Choice1';WaitLoading;Assert ($pt.GetField('choice',$if).GetValue($panel)[4] -eq 1) 'Transit filter failed'
 Click 'CalendarTab';Pump 1700;Assert ($root.Height -eq 542 -and $root.FindName('CalendarBody').Visibility -eq 'Visible') 'Calendar return failed';Snapshot 'calendar.png'
 Write-Output 'PASS: live news/official transit/three-day weather; parser unknown/disruption safety; hourly temperature interpolation; <=1h radar, continuous seek/playback, single pass/replay; five-tab navigation, category/day selection'
 Write-Output $folder
} finally { $panel.Hide();$panel.Close() }
