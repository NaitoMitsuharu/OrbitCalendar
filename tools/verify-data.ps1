param([switch]$SkipLiveFunds)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe))
$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-data-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'))
$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$catalog=$a.GetType('OrbitCalendar.Instruments').GetField('ById',$sf).GetValue($null)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null) # Public fixture, in-memory only
$data=$a.GetType('OrbitCalendar.LiveData');$fund=$a.GetType('OrbitCalendar.FundPrices')
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Await($task){$until=[DateTime]::UtcNow.AddSeconds(100);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $until){throw 'Request timed out'};Pump 50};if($task.IsFaulted){throw $task.Exception.ToString()};if($task.IsCanceled){throw 'Request cancelled'};return $task.Result}
function NewItem($name,$code){$i=[Activator]::CreateInstance($a.GetType('OrbitCalendar.Instrument'),$true);$i.Name=$name;$i.Label=$name;$i.Id='qa:'+$code;$i.Code=$code;$i.Key=$code;$i.Provider='sbi';$i.Kind='投資信託';$i.Unit='円';return $i}
function RejectCsv($item,$csv,$exception){$rejected=$false;try{$null=$fund.GetMethod('ParseSbi',$sf).Invoke($null,[object[]]@($item,$csv))}catch{$rejected=$_.Exception.ToString().Contains($exception)};Assert $rejected ('Invalid CSV accepted: '+$exception)}
$item=NewItem 'テスト, "ファンド"' '12345678'
$csv=@'
"ファンド名","テスト, ""ファンド"""
"年月日","基準価額（円）","前日比（円）"
"2026/11/02","40,292","-658"
"2026/10/30","40950","0"
'@
$parsed=$fund.GetMethod('ParseSbi',$sf).Invoke($null,[object[]]@($item,$csv))
Assert ($parsed.Bars.Count -eq 2 -and $parsed.Bars[1].Close -eq 40292 -and $parsed.Bars[0].Day -lt $parsed.Bars[1].Day) 'Quoted price/name or chronological order failed'
RejectCsv (NewItem '別のファンド' '12345678') $csv 'FundDataUnavailableException'
RejectCsv $item '<html>error</html>' 'FundDataUnavailableException'
RejectCsv $item ($csv.Replace('40,292','-100')) 'FundDataUnavailableException'
RejectCsv $item ($csv + "`n" + '"2026/11/02","12345","0"') 'InvalidDataException'
RejectCsv $item ($csv + "`n" + '"2026/11/02","12345') 'InvalidDataException'
$watch.GetMethod('NoData',$sf).Invoke($null,@($item))|Out-Null
Assert ($watch.GetMethod('Status',$sf).Invoke($null,@($item)).Contains('配信未確認')) 'No-data status missing'
$watch.GetMethod('Confirm',$sf).Invoke($null,@($item))|Out-Null
Assert ($watch.GetMethod('Status',$sf).Invoke($null,@($item)).Contains('取得確認済')) 'Recovery status missing'

if (-not $SkipLiveFunds) {
# Live, independently named funds across seven managers. Every response must match its fund name.
foreach($code in @('89311199','89311216','8931217C','9I312179','2931113C','4731B15C','9C311125','01313098','04318178')){
    $i=$catalog['f:'+$code];Assert ($null -ne $i) ('Missing catalog code '+$code)
    $view=Await ($data.GetMethod('Market',$sf).Invoke($null,[object[]]@($i,[Threading.CancellationToken]::None)))
    Assert ($view.IsFund -and $view.Bars.Count -ge 40) ('Insufficient NAV history '+$code)
    Assert ($view.Bars[0].Day -lt $view.Bars[$view.Bars.Count-1].Day) ('Unsorted NAV history '+$code)
    Write-Host $code $view.Bars[$view.Bars.Count-1].Day.ToString('yyyy/MM/dd') $view.Bars[$view.Bars.Count-1].Close
    if($code -eq '89311199'){$sbiView=$view}
}
# Compare SBI・V・S&P500 against the chart data used by SBI AM's official fund page.
$xmlText=(Invoke-WebRequest -UseBasicParsing 'https://apl.wealthadvisor.jp/xml/chart/funddata/2019092601.xml').Content
$xml=[xml]$xmlText;$days=@($xml.morningstarXML.fund.year.month.day|Where-Object {$_.price -match '^\d+(\.\d+)?$'})
$latest=$days[-1];$sameDay=$latest.GetAttribute('year')+$latest.GetAttribute('month')+$latest.GetAttribute('value')
$matched=@($sbiView.Bars|Where-Object {$_.Day.ToString('yyyyMMdd') -eq $sameDay})
Assert ($matched.Count -eq 1 -and $matched[0].Close -eq [double]$latest.GetAttribute('price')) 'SBI Securities NAV differs from SBI AM chart on the same date'
$bad=NewItem '存在しない投信' '99999999';$task=$data.GetMethod('Market',$sf).Invoke($null,[object[]]@($bad,[Threading.CancellationToken]::None))
while(-not $task.IsCompleted){Pump 50};Assert ($task.IsFaulted -and $task.Exception.ToString().Contains('FundDataUnavailableException')) 'Unknown code returned an unrelated NAV'

}

# Odd-zoom rainfall must use the geographically correct quarter of the parent tile.
$pixels=New-Object byte[] (256*256*4)
for($y=0;$y -lt 256;$y++){for($x=0;$x -lt 256;$x++){$p=($y*256+$x)*4;$pixels[$p]=$x;$pixels[$p+1]=$y;$pixels[$p+3]=255}}
$bmp=[Windows.Media.Imaging.BitmapSource]::Create(256,256,96,96,[Windows.Media.PixelFormats]::Bgra32,$null,$pixels,1024)
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp));$stream=[IO.MemoryStream]::new();$encoder.Save($stream)
$time=[Activator]::CreateInstance($a.GetType('OrbitCalendar.LiveData+RadarTime'));$time.basetime='20261103000000';$time.validtime='20261103030000';$time.Product='rasrf'
$request=[Threading.Tasks.TaskCompletionSource[byte[]]]::new();$request.SetResult($stream.ToArray())
$requests=[Collections.Generic.Dictionary[string,Threading.Tasks.Task[byte[]]]]::new()
$requests.Add('https://www.jma.go.jp/bosai/jmatile/data/rasrf/20261103000000/none/20261103030000/surf/rasrf/8/227/100.png',$request.Task)
$scaled=Await ($data.GetMethod('RainPixels',$sf).Invoke($null,[object[]]@(9,455,200,$time,[Threading.CancellationToken]::None,$requests)))
Assert ($scaled[0] -eq 128 -and $scaled[1] -eq 0 -and $scaled[(255*256+255)*4] -eq 255 -and $scaled[(255*256+255)*4+1] -eq 127) 'Forecast tile crop shifted at odd zoom'

$times=Await ($data.GetMethod('FutureRainTimes',$sf).Invoke($null,[object[]]@([Threading.CancellationToken]::None)))
$origin=$data.GetMethod('RainUtc',$sf).Invoke($null,@($times[0].validtime))
Assert (@($times|Where-Object Product -eq 'rasrf'|Select-Object -ExpandProperty basetime -Unique).Count -le 1) 'Long-range sequence mixes old and new issuances'
Assert ($times[0].basetime -eq $times[0].validtime) 'Timeline does not start with latest observation'
foreach($t in $times){Assert (($data.GetMethod('RainUtc',$sf).Invoke($null,@($t.validtime))) -ge $origin) 'Past frame in future timeline'}
$empty=[Array]::CreateInstance($a.GetType('OrbitCalendar.LiveData+RadarTime'),0)
$observationOnly=$data.GetMethod('SelectFutureRainTimes',$sf).Invoke($null,[object[]]@($times[0],$empty,$empty))
Assert ($observationOnly.Length -eq 1) 'Missing forecasts fabricated future data'

$viewport=[Activator]::CreateInstance($a.GetType('OrbitCalendar.LiveData+Viewport'))
$viewport.Latitude=35.56;$viewport.Longitude=139.8;$viewport.Zoom=9;$viewport.Name='東京湾周辺'
$viewport.Marker=[Device.Location.GeoCoordinate]::new(35.681236,139.767125) # Public Tokyo Station fixture
$weather=Await ($data.GetMethod('Weather',$sf).Invoke($null,[object[]]@(1,[Threading.CancellationToken]::None,$viewport)))
Write-Host 'Loaded frames:' (($weather.Frames|ForEach-Object {$_.ValidUtc.ToString('HH:mm')+':'+$_.HourlyRain}) -join ', ')
foreach($frame in $weather.Frames){if($frame.Forecast){Assert ($frame.ValidUtc -gt $weather.OriginUtc) 'Forecast before acquisition time'}}
$frames=$weather.Frames;$maxMinutes=($frames[-1].ValidUtc-$weather.OriginUtc).TotalMinutes
Assert (-not $frames[0].Forecast -and $weather -eq $frames[0] -and -not $frames[-1].HourlyRain -and $maxMinutes -gt 0 -and $maxMinutes -le 60) 'Live one-hour forecast unavailable'
Write-Host 'Live rainfall timeline (minutes from acquisition):' (($frames|ForEach-Object{[Math]::Max(0,[int]($_.ValidUtc-$weather.OriginUtc).TotalMinutes)}) -join ', ')
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Snapshot($name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($root);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
try {
    $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1800
    $root.Height=642;$panel.Height=642;$pt.GetField('mode',$if).SetValue($panel,2);$root.FindName('CalendarBody').Visibility='Collapsed';$root.FindName('DataBody').Visibility='Visible'
    foreach($name in @('MapTools','RadarControls')){$root.FindName($name).Visibility='Visible'}
    foreach($name in @('MapHint','RadarRange')){$root.FindName($name).Visibility='Visible'}
    $root.FindName('Plot').Height=196;$root.FindName('SectionLabel').Text='WEATHER   /   PRECIPITATION'
    $root.FindName('WeatherTab').Background=[Windows.Media.BrushConverter]::new().ConvertFromString('#296675');$root.FindName('CalendarTab').Background=[Windows.Media.Brushes]::Transparent
    $regions=@('現在地','日本広域','関東','関西');for($i=0;$i -lt 5;$i++){$b=$root.FindName('Choice'+$i);if($i -lt 4){$b.Content=$regions[$i]}else{$b.Visibility='Collapsed'}}
    $pt.GetMethod('ShowData',$if).Invoke($panel,@($weather))|Out-Null;$panel.UpdateLayout();$pt.GetField('effect',$if).GetValue($panel).Start($true);Pump 1900
    $slider=$root.FindName('RadarTime');Assert ($slider.Value -eq 0 -and $slider.Maximum -eq $maxMinutes -and $slider.Ticks.Count -eq $frames.Length) 'Slider is not proportional to actual forecast times'
    Snapshot 'weather-now.png';$slider.Value=$slider.Maximum;Pump 300
    Assert ($root.FindName('RainLegendUnit').Text -eq '(mm/h)' -and $root.FindName('RadarClock').Text -match '^\d{2}:\d{2}$' -and $root.FindName('DataSource').Content.Contains('雨雲の動き')) 'Rain legend units or clock incorrect'
    Snapshot 'weather-one-hour.png'
    $pt.GetMethod('ShowData',$if).Invoke($panel,@($frames[-1]))|Out-Null
    Assert ($slider.Ticks.Count -eq $frames.Length -and $slider.Value -eq $slider.Maximum) 'Selecting a future frame loses the rest of the timeline'
    $nowLabels=@($root.FindName('Plot').Children|Where-Object {$_.Tag -like 'city:*'}|ForEach-Object Tag)
    $slider.Value=0;Pump 150
    Assert ($root.FindName('RainLegendUnit').Text -eq '(mm/h)' -and $root.FindName('RadarClock').Text -match '^\d{2}:\d{2}$') 'Return to observation failed'
    Assert (-not (Compare-Object $nowLabels @($root.FindName('Plot').Children|Where-Object {$_.Tag -like 'city:*'}|ForEach-Object Tag))) 'Cities change with forecast time'
    $button=$root.FindName('RadarPlay');$button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));Pump 800
    Assert ($slider.Value -gt 0) 'Playback did not advance'
    $playback=$pt.GetField('radarPlayback',$if).GetValue($panel)
    $pt.GetMethod('StopRadar',$if).Invoke($panel,@())|Out-Null
    $slider.Value=$slider.Maximum-.01;$button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));Pump 500
    Assert (-not $playback.IsEnabled -and $slider.Value -eq $slider.Maximum -and $button.Content -eq '▶') 'Playback did not stop at the final frame'
    Pump 850
    Assert ($slider.Value -eq $slider.Maximum) 'Playback looped after the last frame'
    $button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    Assert ($playback.IsEnabled -and $slider.Value -eq 0) 'Replay did not restart at the beginning'
    $pt.GetMethod('StopRadar',$if).Invoke($panel,@())|Out-Null
    Assert $slider.IsMoveToPointEnabled 'Track clicks still move by a fixed increment'
    Assert (@($root.FindName('RainLegendBar').Children|Where-Object {$_ -is [Windows.Shapes.Rectangle]}).Count -eq 8) 'Rain scale does not have eight color bins'
    Assert (-not $root.FindName('RadarRange').Text.Contains('+')) 'Relative offsets remain in the range'
    $slider.Value=$slider.Maximum;Pump 200;Snapshot 'weather-one-hour.png'

    $sourceBounds=$root.FindName('DataSource').TransformToAncestor($root).TransformBounds([Windows.Rect]::new($root.FindName('DataSource').RenderSize))
    Assert ($sourceBounds.Bottom -lt $root.Height) 'Forecast UI extends outside window'
    Write-Output 'PASS: NAV parsing/status, odd-zoom alignment, future-only timeline, actual-time slider, units, playback and cities (live NAV tests run unless skipped)'
    Write-Output $folder
} finally {$panel.Hide();$panel.Close()}
