param([string]$Executable='build/next/OrbitCalendar.exe')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $Executable));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-radar-map-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $folder|Out-Null
foreach($pair in @(@('Watchlist','watchlist.json'),@('Preferences','display.txt'),@('SavedPlace','location.txt'),@('MapScale','map-zoom.txt'))){$a.GetType('OrbitCalendar.'+$pair[0]).GetField('path',$sf).SetValue($null,(Join-Path $folder $pair[1]))}
$a.GetType('OrbitCalendar.Watchlist').GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('LastTab',$sf).SetValue($null,0)
$place=$a.GetType('OrbitCalendar.SavedPlace');$place.GetMethod('Load',$sf).Invoke($null,@())|Out-Null;$place.GetMethod('Save',$sf).Invoke($null,@([Device.Location.GeoCoordinate]::new(35.4547,139.6024),'天王町駅'))|Out-Null
$a.GetType('OrbitCalendar.MapScale').GetMethod('Remember',$sf).Invoke($null,@(0,9))|Out-Null
$data=$a.GetType('OrbitCalendar.LiveData')
function Assert($ok,$message){if(-not $ok){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Await($task){$end=[DateTime]::UtcNow.AddSeconds(100);while(-not $task.IsCompleted){Pump 50;if([DateTime]::UtcNow -gt $end){throw 'Request timeout'}};if($task.IsFaulted){throw $task.Exception.ToString()};return $task.Result}
function Static($name,$arguments=@()){return $data.GetMethod($name,$sf).Invoke($null,[object[]]$arguments)}
# Feed a real PNG into the request cache. Distinct quadrants and cell colors make
# incorrect parent coordinates/crops observable without relying on today's rain.
$pixels=[byte[]]::new(256*256*4)
for($y=0;$y -lt 256;$y++){for($x=0;$x -lt 256;$x++){$i=($y*256+$x)*4;$pixels[$i]=[byte]$x;$pixels[$i+1]=[byte]$y;$pixels[$i+2]=[byte](($x+$y)%256);$pixels[$i+3]=255}}
$bitmap=[Windows.Media.Imaging.BitmapSource]::Create(256,256,96,96,[Windows.Media.PixelFormats]::Bgra32,$null,$pixels,1024)
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.MemoryStream]::new();$encoder.Save($stream);$png=$stream.ToArray();$stream.Dispose()
$time=[Activator]::CreateInstance($a.GetType('OrbitCalendar.LiveData+RadarTime'));$time.basetime=$time.validtime='20261104095000';$time.Product='nowc'
$rainMethod=$data.GetMethod('RainPixels',$sf)
foreach($case in @(@(4,4,1),@(5,4,2),@(6,6,1),@(7,6,2),@(8,8,1),@(9,8,2),@(10,10,1),@(11,10,2),@(12,10,4),@(18,10,256))){
 $z=$case[0];$native=$case[1];$factor=$case[2];$x=[int](3*$factor+$factor-1);$y=[int](2*$factor+$factor-1)
 $requests=[Activator]::CreateInstance($rainMethod.GetParameters()[5].ParameterType)
 $url='https://www.jma.go.jp/bosai/jmatile/data/nowc/'+$time.basetime+'/none/'+$time.validtime+'/surf/hrpns/'+$native+'/3/2.png'
 $completion=[Threading.Tasks.TaskCompletionSource[byte[]]]::new();$completion.SetResult($png);$requests.Add($url,$completion.Task)
 $task=$rainMethod.Invoke($null,@($z,$x,$y,$time,[Threading.CancellationToken]::None,$requests));[byte[]]$actual=Await $task
 Assert ($requests.Count -eq 1 -and $actual.Length -eq $pixels.Length) ('Wrong native zoom URL/size at '+$z)
 foreach($px in @(0,1,63,127,128,255)){foreach($py in @(0,1,63,127,128,255)){
  $sx=[int](256-256/$factor+[Math]::Floor($px/$factor));$sy=[int](256-256/$factor+[Math]::Floor($py/$factor));$i=($py*256+$px)*4;$source=($sy*256+$sx)*4
  for($c=0;$c -lt 4;$c++){Assert ($actual[$i+$c] -eq $pixels[$source+$c]) ('Parent crop/color mismatch at zoom '+$z)}
 }}
}
Write-Output 'PASS: even parent tiles and exact RGBA/crop alignment at zooms 4..12 and 18, including odd scales'
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Call($name,$arguments=@()){return $pt.GetMethod($name,$if).Invoke($panel,[object[]]$arguments)}
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading{$end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI load timeout'}}while($pt.GetField('loading',$if).GetValue($panel));Pump 1900}
function Bounds($el){$panel.UpdateLayout();return $el.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($el.RenderSize))}
function Snapshot($name){Pump 100;$panel.UpdateLayout();$im=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$v=[Windows.Media.DrawingVisual]::new();$d=$v.RenderOpen();$brush=[Windows.Media.VisualBrush]::new($root);$brush.ViewboxUnits=[Windows.Media.BrushMappingMode]::Absolute;$brush.Viewbox=[Windows.Rect]::new(0,0,458,$root.Height);$brush.Stretch=[Windows.Media.Stretch]::Fill;$d.DrawRectangle($brush,$null,[Windows.Rect]::new(0,0,458,$root.Height));$d.Close();$im.Render($v);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Click 'WeatherTab';WaitLoading
 $view=$pt.GetField('activeWeather',$if).GetValue($panel);Assert ($view -and $view.Map.PixelWidth -eq 414 -and $view.Map.PixelHeight -eq 190) 'Wide live map not rendered'
 $pb=Bounds ($root.FindName('Plot'));$lb=Bounds ($root.FindName('RainLegend'));$rb=Bounds ($root.FindName('RadarControls'))
 Assert ($pb.Width -eq 414 -and $lb.Width -eq 414 -and [Math]::Abs($pb.Left-$lb.Left) -lt 2 -and $pb.Right -le 438) 'Map/legend clipped or misaligned'
 Assert ($root.FindName('RadarTime').Width -eq 366 -and $root.FindName('RadarControls').ColumnDefinitions[0].Width.Value -eq 356) 'Playback width changed'
 $line=@($root.FindName('TemperaturePlot').Children|Where-Object {$_ -is [Windows.Shapes.Polyline]})[0]
 Assert ($line.Points[$line.Points.Count-1].X -eq 356) 'Temperature timeline widened with map'
 $lastColor=@($root.FindName('RainLegendBar').Children|Where-Object {$_ -is [Windows.Shapes.Rectangle]})[-1]
 Assert ([Math]::Abs([Windows.Controls.Canvas]::GetLeft($lastColor)+$lastColor.Width-346) -lt 1) 'Legend colors did not expand'
 Snapshot 'weather-wide-map.png'
 # Keep the exact same observation while changing zoom; compare the center rain
 # color against the parent data, so a passing clear-sky sample cannot hide a crop error.
 foreach($z in @(7,8,9,10,11)){
  $map=Await (Static 'RadarMap' @($view.Radar,'天王町駅',35.4547,139.6024,$z,[Threading.CancellationToken]::None,$null))
  Assert ($map -and $map.ValidUtc -eq $view.ValidUtc -and $map.Map.PixelWidth -eq 414) ('Same-time live map failed at zoom '+$z)
  $world=256*[Math]::Pow(2,$z);$wx=[int][Math]::Floor((139.6024+180)/360*$world);$wy=[int][Math]::Floor((1-[Math]::Log([Math]::Tan(35.4547*[Math]::PI/180)+1/[Math]::Cos(35.4547*[Math]::PI/180))/[Math]::PI)*$world/2)
  $tx=[int][Math]::Floor($wx/256);$ty=[int][Math]::Floor($wy/256);$requests=[Activator]::CreateInstance($rainMethod.GetParameters()[5].ParameterType)
  [byte[]]$rain=Await ($rainMethod.Invoke($null,@($z,$tx,$ty,$view.Radar,[Threading.CancellationToken]::None,$requests)))
  Assert (@($requests.Keys|Where-Object {$_ -match '/hrpns/(5|7|9)/'}).Count -eq 0) 'Odd live URL requested'
  $i=(($wy%256)*256+$wx%256)*4
  if($rain[$i+3] -gt 48){$center=[byte[]]::new(4);$map.Map.CopyPixels([Windows.Int32Rect]::new(207,95,1,1),$center,4,0);for($c=0;$c -lt 3;$c++){Assert ($center[$c] -eq $rain[$i+$c]) ('Live center rain color missing at zoom '+$z)}}
  Write-Output ('PASS: same observation '+$map.ValidUtc.ToString('HH:mm')+' UTC at zoom '+$z)
 }
 $start=$view.Viewport;$wheel=$pt.GetField('mapWheel',$if).GetValue($panel);$wait=$pt.GetField('zoomWait',$if).GetValue($panel);$now=[DateTime]::UtcNow
 foreach($scenario in @('burst','fine','large','reverse')){
  $wheel.Reset();$pt.GetField('mapViewport',$if).SetValue($panel,$start)
  $deltas=switch($scenario){'burst'{@(120,120,120)}'fine'{@(40,40,40)}'large'{@(360)}'reverse'{@(120,-120)}}
  for($i=0;$i -lt $deltas.Count;$i++){Call 'ZoomMap' @([int]$deltas[$i],[Windows.Point]::new(207,95),$now.AddMilliseconds(10*$i))|Out-Null}
  $wait.Stop();$viewport=$pt.GetField('mapViewport',$if).GetValue($panel);$expected=if($scenario -eq 'reverse'){$start.Zoom}else{$start.Zoom+1}
  Assert ($viewport.Zoom -eq $expected) ('Wheel jumped levels: '+$scenario)
  Assert ([Math]::Abs($viewport.Latitude-$start.Latitude) -lt .000001 -and [Math]::Abs($viewport.Longitude-$start.Longitude) -lt .000001) 'Center moved during centered wheel zoom'
 }
 $wheel.Reset();Assert ($wheel.Step(60,$now) -eq 0 -and $wheel.Step(60,$now.AddSeconds(1)) -eq 0) 'Stale partial wheel delta carried over'
 $wheel.Reset();Assert ($wheel.Step(120,$now) -eq 1 -and $wheel.Step(120,$now.AddMilliseconds(40)) -eq 0 -and $wheel.Step(120,$now.AddMilliseconds(200)) -eq 1) 'Wheel pacing blocks deliberate continued scrolling'
 $wheel.Reset();$pt.GetField('mapViewport',$if).SetValue($panel,$start)
 $at=[Windows.Point]::new(100,50);$world=256*[Math]::Pow(2,$start.Zoom);$cy=(1-[Math]::Log([Math]::Tan($start.Latitude*[Math]::PI/180)+1/[Math]::Cos($start.Latitude*[Math]::PI/180))/[Math]::PI)*$world/2
 $longitude=$start.Longitude+($at.X-207)/.88/$world*360;$latY=$cy+($at.Y-95)/.88;$latitude=[Math]::Atan([Math]::Sinh([Math]::PI*(1-2*$latY/$world)))*180/[Math]::PI
 $anchor=[Device.Location.GeoCoordinate]::new($latitude,$longitude)
 Call 'ZoomMap' @(120,$at,$now)|Out-Null;$wait.Stop();$viewport=$pt.GetField('mapViewport',$if).GetValue($panel)
 $point=$pt.GetMethod('PlacePixel',$sf).Invoke($null,@($viewport,$anchor));Assert (($point-$at).Length -lt .00001) 'Off-center wheel anchor moved after widening map'
 $id=$place.GetProperty('ActiveId',$sf).GetValue($null,$null);Call 'OpenSavedLocationEditor' @($id)|Out-Null;WaitLoading
 Call 'SelectPlaceAt' @([Windows.Point]::new(400,95))|Out-Null
 Assert ($root.FindName('SavePlace').IsEnabled) 'New right side cannot be used for saved-place adjustment'
 $candidate=$pt.GetField('candidatePlace',$if).GetValue($panel);$editing=$pt.GetField('activeWeather',$if).GetValue($panel)
 $point=$pt.GetMethod('PlacePixel',$sf).Invoke($null,@($editing.Viewport,$candidate));Assert ([Math]::Abs($point.X-400) -lt .00001) 'Right-edge saved-place coordinates misaligned'
 Click 'CancelPlace';Pump 500;Assert ($place.GetProperty('Name',$sf).GetValue($null,$null) -eq '天王町駅') 'Cancel lost saved-place name'
 Click 'DailyMode';WaitLoading;Assert ($root.FindName('DailyWeatherBody').Visibility -eq 'Visible') 'Three-day weather regression'
 Click 'MarketTab';WaitLoading;Assert ($root.FindName('Plot').Width -eq 414 -and $root.FindName('Plot').Height -eq 220) 'Market plot dimensions changed'
 Write-Output 'PASS: wheel bursts, high-resolution deltas, multi-detent messages, direction reversal, stale deltas and continued scrolling'
 Write-Output 'PASS: pointer anchoring, right-edge saved-place editing, cancel, three-day weather and market layout'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
