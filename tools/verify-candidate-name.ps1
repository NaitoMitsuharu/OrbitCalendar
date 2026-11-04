param([string]$ReloadCandidate)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
if($ReloadCandidate){
 $store=$a.GetType('OrbitCalendar.SavedPlace');$store.GetField('path',$sf).SetValue($null,(Join-Path $ReloadCandidate 'location.txt'));$store.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
 $items=$store.GetProperty('Entries',$sf).GetValue($null,$null)
 if($items.Count -ne 2 -or $store.GetProperty('Name',$sf).GetValue($null,$null) -ne '自宅' -or $items[1].Zoom -ne 15 -or $items[0].Name -ne '東京駅' -or $items[0].Zoom -ne 9){throw 'Restart lost candidate name or existing place'}
 Write-Output 'PASS: custom pre-registration name, adjusted coordinate/zoom and prior place restored in fresh process';exit 0
}
$folder=Join-Path $env:TEMP ('orbit-candidate-name-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
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

$place=$a.GetType('OrbitCalendar.SavedPlace');$place.GetField('path',$sf).SetValue($null,(Join-Path $folder 'location.txt'))
$nameProperty=$place.GetProperty('Name',$sf);$displayProperty=$place.GetProperty('DisplayName',$sf);$locationFile=Join-Path $folder 'location.txt'
[IO.File]::WriteAllText($locationFile,'35.681236,139.767125')
$legacy=$place.GetMethod('Load',$sf).Invoke($null,@());Assert ($null -ne $legacy -and $null -eq $nameProperty.GetValue($null,$null) -and $displayProperty.GetValue($null,$null) -eq '保存地点') 'Legacy coordinates-only file not compatible'
[IO.File]::Delete($locationFile)



$fixture=[Device.Location.GeoCoordinate]::new(35.681236,139.767125)
$place.GetMethod('Save',$sf).Invoke($null,[object[]]@($fixture,'東京駅'))|Out-Null
$result=[Activator]::CreateInstance($a.GetType('OrbitCalendar.PlaceSearch+Result'));$result.Name='天王町駅';$result.Coordinate=[Device.Location.GeoCoordinate]::new(35.454330,139.602670)
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Call($name,$arguments=@()){return $pt.GetMethod($name,$if).Invoke($panel,[object[]]$arguments)}
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI weather timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function WaitSearch { $end=[DateTime]::UtcNow.AddSeconds(40);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI search timeout'}}while($null -ne $pt.GetField('placeSearching',$if).GetValue($panel));Pump 300 }
function Bounds($el){$panel.UpdateLayout();return $el.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($el.RenderSize))}
function Snapshot($name){Pump 200;$panel.UpdateLayout();$im=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$visual=[Windows.Media.DrawingVisual]::new();$dc=$visual.RenderOpen();$brush=[Windows.Media.VisualBrush]::new($root);$brush.ViewboxUnits=[Windows.Media.BrushMappingMode]::Absolute;$brush.Viewbox=[Windows.Rect]::new(0,0,458,$root.Height);$brush.Stretch=[Windows.Media.Stretch]::Fill;$dc.DrawRectangle($brush,$null,[Windows.Rect]::new(0,0,458,$root.Height));$dc.Close();$im.Render($visual);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}


try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Click 'WeatherTab';WaitLoading
 Call 'SelectSearchPlace' @($result)|Out-Null;WaitLoading
 $name=$root.FindName('CandidatePlaceName');$confirm=$root.FindName('PlaceConfirm')
 Assert ($name.Text -eq '天王町駅' -and $confirm.Opacity -eq 1) 'Search result name not initially editable'
 Assert (-not $root.FindName('Face').IsAncestorOf($name)) 'Input is hidden in particle-only source'
 $name.Text='取消する名前';Click 'CancelPlace';Assert ($place.GetProperty('Entries',$sf).GetValue($null,$null).Count -eq 1 -and $name.Text -eq '' -and $confirm.Visibility -eq 'Collapsed') 'Canceled draft saved or not cleared'
 Call 'SelectSearchPlace' @($result)|Out-Null;WaitLoading;Assert ($name.Text -eq '天王町駅') 'New selection inherited canceled name'
 $name.Text=' 自宅 '
 $vp=[Activator]::CreateInstance($a.GetType('OrbitCalendar.LiveData+Viewport'));$vp.Latitude=35.455330;$vp.Longitude=139.603670;$vp.Zoom=15;$vp.Name='検索候補周辺';$pt.GetField('mapViewport',$if).SetValue($panel,$vp);Call 'LoadView' @($false)|Out-Null;WaitLoading
 Call 'CompleteMapDrag' @([Windows.Vector]::new(20,10),[Windows.Point]::new(178,95))|Out-Null;WaitLoading
 Call 'SelectPlaceAt' @([Windows.Point]::new(178,95))|Out-Null;Pump 100
 $adjusted=$pt.GetField('candidatePlace',$if).GetValue($panel)
 Assert ($name.Text -eq ' 自宅 ' -and $pt.GetField('candidatePlaceName',$if).GetValue($panel) -eq ' 自宅 ' -and $adjusted.GetDistanceTo($result.Coordinate) -gt 10) 'Map adjustment reset custom name or did not refine coordinate'
 Assert ($place.GetProperty('Name',$sf).GetValue($null,$null) -eq '東京駅' -and $place.GetProperty('Active',$sf).GetValue($null,$null).Zoom -eq 9) 'Draft changed saved place'
 $inputBounds=Bounds $name;$confirmBounds=Bounds $confirm;$mapBounds=Bounds ($root.FindName('Plot'));$saveBounds=Bounds ($root.FindName('SavePlace'))
 Assert ($confirmBounds.Top -ge $mapBounds.Bottom -and $inputBounds.Bottom -le $saveBounds.Top -and $inputBounds.Right -le $confirmBounds.Right-7) 'Name input/confirmation overlaps map or save controls'
 $name.Text='　 ';Click 'SavePlace';Assert ($place.GetProperty('Entries',$sf).GetValue($null,$null).Count -eq 1 -and $root.FindName('PlaceSummary').Text.Contains('名前')) 'Blank custom name accepted'
 $name.Text='自宅';Assert (-not $root.FindName('PlaceSummary').Text.Contains('名前')) 'Valid input did not clear empty-name prompt'
 $place.GetField('path',$sf).SetValue($null,$folder);Click 'SavePlace';Assert ($name.Text -eq '自宅' -and $confirm.Visibility -eq 'Visible' -and $root.FindName('PlaceSummary').Text.Contains('保存できません')) 'Failed save lost draft or closed confirmation'
 $place.GetField('path',$sf).SetValue($null,$locationFile);$name.Text=' 自宅 '
 Assert ($null -eq $pt.GetField('placeSearching',$if).GetValue($panel) -and -not $pt.GetField('placeSearchWait',$if).GetValue($panel).IsEnabled) 'Custom name triggered geographic search'
 Snapshot 'place-name-before-add.png'
 Click 'SavePlace';WaitLoading;Click 'SavePlace'
 $items=$place.GetProperty('Entries',$sf).GetValue($null,$null)
 Assert ($items.Count -eq 2 -and $items[1].Name -eq '自宅' -and $items[1].Zoom -eq 15 -and $items[1].Coordinate -eq $adjusted -and $name.Text -eq '' -and $confirm.Visibility -eq 'Collapsed') 'Custom name/position/zoom not added once, or draft not cleared'
 Assert ($root.FindName('DataTitle').Text -eq '自宅周辺の雨雲') 'Weather uses original search name'
 & powershell -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -ReloadCandidate $folder;Assert ($LASTEXITCODE -eq 0) 'Fresh restart failed'
 Click 'CalendarTab';Pump 1900;Assert ($root.FindName('TodayWeather').ToolTip.ToString().Contains('自宅周辺')) 'Calendar not using custom place name'
 Click 'WeatherTab';WaitLoading;Click 'PickPlace';Pump 1900;Click 'PickOnMap';Pump 900;Call 'SelectPlaceAt' @([Windows.Point]::new(178,95))|Out-Null;Pump 1900
 Assert ($name.Text -eq '地点3') 'Map-only selection did not suggest a new default name'
 $name.Text='職場';Click 'CalendarTab';Pump 1900;Assert ($name.Text -eq '' -and $place.GetProperty('Entries',$sf).GetValue($null,$null).Count -eq 2) 'Leaving with an edited draft registered it'
 Write-Output 'PASS: editable pre-registration name, search/map defaults, name retention through pan/zoom/refinement, map/input/button layout, empty-name validation, atomic failure draft retention, cancel/tab cleanup, no name search, single custom registration, weather/calendar names and fresh restart'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
