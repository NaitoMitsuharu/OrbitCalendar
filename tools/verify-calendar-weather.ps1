$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-calendar-weather-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null)
$data=$a.GetType('OrbitCalendar.LiveData');$catalog=$a.GetType('OrbitCalendar.Instruments').GetField('ById',$sf).GetValue($null)
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Await($task){$end=[DateTime]::UtcNow.AddSeconds(100);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $end){throw 'Request timeout'};Pump 50};if($task.IsFaulted){throw $task.Exception.ToString()};return $task.Result}
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI load timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function SaveImage($element,$width,$height,$name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($element);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
function Snapshot($name){Pump 250;$panel.UpdateLayout();SaveImage $root 458 ([int]$root.Height) $name}
function Reopen($expected){$panel.Collapse();Pump 900;Assert (-not $panel.IsVisible) 'Collapse failed';$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1800;Assert ($pt.GetField('mode',$if).GetValue($panel) -eq $expected) 'Previous tab not restored';Assert ($root.Height -eq $panel.PreferredHeight) 'Reopened size incorrect'}

$forecastType=$a.GetType('OrbitCalendar.LiveData+WeatherForecast');$daysType=$a.GetType('OrbitCalendar.LiveData+DaysWeather')
function Fixture([DateTime]$date){
 $f=[Activator]::CreateInstance($forecastType,$true);$d=[Activator]::CreateInstance($daysType,$true)
 $epoch=[DateTime]::SpecifyKind([DateTime]'1970-01-01',[DateTimeKind]::Utc)
 $d.time=[long[]]@(0..3|ForEach-Object {[long]($date.Date.AddDays($_-1).AddHours(-9)-$epoch).TotalSeconds})
 $d.weather_code=[Nullable[int][]]@(3,61,0,3);$d.temperature_2m_max=[Nullable[double][]]@(23,25,24,21);$d.temperature_2m_min=[Nullable[double][]]@(18,17,15,14);$d.precipitation_probability_max=[Nullable[double][]]@(0,90,0,30)
 $f.daily=$d;$f.Place='検証用の地点';return $f
}
$normalize=$forecastType.GetMethod('SelectForecastDays',$if)
$sampleDate=[DateTime]'2027-01-01';$fixture=Fixture $sampleDate;$normalize.Invoke($fixture,@($sampleDate))|Out-Null
Assert ($fixture.PreviousHigh -eq 23 -and $fixture.PreviousLow -eq 18 -and $fixture.daily.time.Length -eq 3 -and $fixture.daily.weather_code[0] -eq 61) 'Yesterday/today indices or year boundary incorrect'
$missing=Fixture $sampleDate;$missing.daily.temperature_2m_max[0]=$null;$missing.daily.temperature_2m_min[0]=$null;$normalize.Invoke($missing,@($sampleDate))|Out-Null
Assert ($null -eq $missing.PreviousHigh -and $null -eq $missing.PreviousLow) 'Missing yesterday became zero'
$invalid=Fixture $sampleDate;$rejected=$false;try{$normalize.Invoke($invalid,@($sampleDate.AddDays(5)))|Out-Null}catch{$rejected=$true};Assert $rejected 'Forecast for wrong date was accepted'
$formatter=$a.GetType('OrbitCalendar.CalendarPanel').GetMethod('ComparedTemperature',$sf)
Assert ($formatter.Invoke($null,@([double]25,[double]23)) -eq '25° (+2°)') 'Positive comparison incorrect'
Assert ($formatter.Invoke($null,@([double]17,[double]18)) -eq '17° (−1°)') 'Negative comparison incorrect'
Assert ($formatter.Invoke($null,@([double]17,[double]17)) -eq '17° (0°)') 'Unchanged temperature incorrect'
Assert ($formatter.Invoke($null,@([double]17,$null)) -eq '17° (—)') 'Missing comparison not shown as unavailable'
Assert ($formatter.Invoke($null,@($null,[double]17)) -eq '— (—)') 'Missing today not shown as unavailable'
$forecast=Await ($data.GetMethod('ForecastWeather',$sf).Invoke($null,@([Threading.CancellationToken]::None,$true)))
Assert ($forecast.daily.time.Length -eq 3 -and $null -ne $forecast.PreviousHigh -and $null -ne $forecast.PreviousLow) 'Live yesterday/three-day forecast missing'
$day=$forecastType.GetMethod('Utc',$sf).Invoke($null,@($forecast.daily.time[0]));Assert ($day.AddHours(9).Date -eq [DateTime]::UtcNow.AddHours(9).Date) 'Live forecast starts on yesterday'
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child

try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900
 $end=[DateTime]::UtcNow.AddSeconds(30);while($pt.GetField('updatingToday',$if).GetValue($panel)){Pump 50;if([DateTime]::UtcNow -gt $end){throw 'Calendar weather timeout'}}
 Assert ($root.FindName('TodayHigh').Text.Contains('(') -and $root.FindName('TodayLow').Text.Contains('(')) 'Live comparisons not rendered'
 Snapshot 'calendar-live.png'
 function CheckLayout {
  $panel.UpdateLayout();$high=$root.FindName('TodayHigh');$low=$root.FindName('TodayLow');$group=$root.FindName('TodayWeatherGroup');$holiday=$root.FindName('HolidayName');$date=$root.FindName('SelectedDate')
  $hb=$high.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($high.RenderSize));$lb=$low.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($low.RenderSize));$gb=$group.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($group.RenderSize));$db=$date.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($date.RenderSize));$holidayBounds=$holiday.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($holiday.RenderSize))
  Assert ([Math]::Abs($hb.Top-$lb.Top) -le 1 -and $hb.Right -lt $lb.Left) 'High/low not side by side'
  Assert ($gb.Left-$db.Right -ge 6 -and $gb.Left-$db.Right -le 22 -and $gb.Right -le 430 -and $gb.Bottom -le 487) 'Weather gap too large or overlaps summary edge'
  Assert (-not $holidayBounds.IntersectsWith($gb) -and $holidayBounds.Left -ge $gb.Right+6 -and $holidayBounds.Right -le 430) 'Weather overlaps holiday or extends outside summary'
  foreach($bounds in @($db,$hb,$lb,$holidayBounds)){
   Assert ([Math]::Abs(($bounds.Top+$bounds.Bottom)/2-($gb.Top+$gb.Bottom)/2) -le 1) 'Week/weather/temperatures/holiday are not on one line'
  }
  if($holiday.Text -eq '文化の日'){Assert ($holiday.ActualWidth -ge 44) 'Culture Day was unnecessarily truncated'}
 }
 CheckLayout
 $pt.GetMethod('SelectDate',$if).Invoke($panel,@([DateTime]::new(2026,11,3)))|Out-Null;Pump 1900
 $render=$pt.GetMethod('RenderTodayWeather',$if);$render.Invoke($panel,@($fixture))|Out-Null;Assert ($root.FindName('HolidayName').Text -eq '文化の日') 'Selected holiday is missing';$panel.UpdateLayout();$pt.GetField('effect',$if).GetValue($panel).Refresh();Pump 200;CheckLayout;Snapshot 'calendar-comparison.png'
 Assert ($root.FindName('TodayWeather').Text -eq '☂') 'Rain day representative icon changed'
 # Wide negative temperatures and differences must still fit with a long holiday name.
 $fixture.daily.temperature_2m_max[0]=-12.3;$fixture.PreviousHigh=1.1;$fixture.daily.temperature_2m_min[0]=-23.4;$fixture.PreviousLow=-34.5
 $render.Invoke($panel,@($fixture))|Out-Null;$root.FindName('HolidayName').Text='天皇の即位の日に関連する休日';$panel.UpdateLayout();CheckLayout;$pt.GetField('effect',$if).GetValue($panel).Refresh();Snapshot 'calendar-wide.png'
 $render.Invoke($panel,@($missing))|Out-Null;Assert ($root.FindName('TodayHigh').Text.Contains('(—)')) 'Missing yesterday not reflected in UI'
 $render.Invoke($panel,@($forecast))|Out-Null;Click 'WeatherTab';WaitLoading;Click 'DailyMode';WaitLoading
 Assert ($pt.GetField('weatherForecast',$if).GetValue($panel).daily.time.Length -eq 3) 'Three-day view included yesterday';Snapshot 'weather-daily.png'
 Write-Output 'PASS: live yesterday and 3-day data, year boundary/date selection, positive/negative/zero/missing comparisons, one-line week/weather/temperatures/holiday with compact gap and long-text layout, representative rain icon and daily-view regression'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
