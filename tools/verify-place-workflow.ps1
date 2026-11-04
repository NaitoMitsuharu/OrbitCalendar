param([string]$ReloadWorkflow)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
if($ReloadWorkflow){
 $store=$a.GetType('OrbitCalendar.SavedPlace');$store.GetField('path',$sf).SetValue($null,(Join-Path $ReloadWorkflow 'location.txt'));$store.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
 $items=$store.GetProperty('Entries',$sf).GetValue($null,$null)
 if($items.Count -ne 4 -or $items[0].Name -ne '横浜駅' -or $items[1].Name -ne '東京駅' -or $items[2].Name -ne '千葉駅' -or $items[3].Zoom -ne 18 -or $items[1].Zoom -ne 9){throw 'Restart lost order or adjusted zoom'}
 Write-Output 'PASS: tab order and independently adjusted place/zoom restored in fresh process';exit 0
}
$folder=Join-Path $env:TEMP ('orbit-place-workflow-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
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




function Store($name,$arguments=@()){return $place.GetMethod($name,$sf).Invoke($null,[object[]]$arguments)}
function Entries{return $place.GetProperty('Entries',$sf).GetValue($null,$null)}
function Active{return $place.GetProperty('ActiveId',$sf).GetValue($null,$null)}
Store 'Rename' @((Active),'東京駅')|Out-Null;$tokyo=Active
Store 'Add' @([Device.Location.GeoCoordinate]::new(35.465646,139.620120),'横浜駅',9)|Out-Null;$yokohama=Active
Store 'Add' @([Device.Location.GeoCoordinate]::new(35.613190,140.113130),'千葉駅',7)|Out-Null;$chiba=Active
Store 'Select' @($tokyo)|Out-Null
Assert (-not (Store 'Move' @($tokyo,-1)) -and -not (Store 'Move' @($chiba,1))) 'Out-of-range move accepted'
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Call($name,$arguments=@()){return $pt.GetMethod($name,$if).Invoke($panel,[object[]]$arguments)}
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI weather timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function WaitSearch { $end=[DateTime]::UtcNow.AddSeconds(40);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI search timeout'}}while($null -ne $pt.GetField('placeSearching',$if).GetValue($panel));Pump 300 }
function Bounds($el){$panel.UpdateLayout();return $el.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($el.RenderSize))}
function Snapshot($name){Pump 200;$panel.UpdateLayout();$im=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$visual=[Windows.Media.DrawingVisual]::new();$dc=$visual.RenderOpen();$brush=[Windows.Media.VisualBrush]::new($root);$brush.ViewboxUnits=[Windows.Media.BrushMappingMode]::Absolute;$brush.Viewbox=[Windows.Rect]::new(0,0,458,$root.Height);$brush.Stretch=[Windows.Media.Stretch]::Fill;$dc.DrawRectangle($brush,$null,[Windows.Rect]::new(0,0,458,$root.Height));$dc.Close();$im.Render($visual);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}


function WeatherChoice($tag){$button=@($root.FindName('WeatherChoices').Children|Where-Object Tag -eq $tag);Assert ($button.Count -eq 1) ('Missing weather choice '+$tag);$button[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function ManagerAction($tag){$buttons=@($root.FindName('RegisteredPlaces').Children|ForEach-Object {$_.Child.Children[1].Children}|Where-Object Tag -eq $tag);Assert ($buttons.Count -eq 1) ('Missing manager action '+$tag);$buttons[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}

function WaitAutoSearch { $end=[DateTime]::UtcNow.AddSeconds(70);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'Auto search timed out'}}while($pt.GetField('placeSearchWait',$if).GetValue($panel).IsEnabled -or $null -ne $pt.GetField('placeSearching',$if).GetValue($panel));Pump 150 }
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Click 'WeatherTab';WaitLoading
 $viewport=$pt.GetField('activeWeather',$if).GetValue($panel).Viewport;Click 'ManageWeatherPlaces';Pump 1900
 ManagerAction ('down:'+$tokyo);Pump 1900;Assert ((Entries)[0].Id -eq $yokohama -and (Active) -eq $tokyo) 'Moving changed active selection or failed'
 ManagerAction ('down:'+$tokyo);Pump 1900;ManagerAction ('up:'+$tokyo);Pump 1900
 Assert ((Entries)[1].Id -eq $tokyo -and $pt.GetField('activeWeather',$if).GetValue($panel).Viewport -eq $viewport -and $null -eq $pt.GetField('loading',$if).GetValue($panel)) 'Reorder changed viewport or fetched weather'
 $tabs=@($root.FindName('WeatherChoices').Children|Where-Object {$_.Tag -notmatch '^(current|preset)'}|ForEach-Object Tag);Assert ($tabs[0] -eq $yokohama -and $tabs[1] -eq $tokyo -and $tabs[2] -eq $chiba) 'Tab order differs from manager'
 Snapshot 'weather-places-order.png';Click 'CloseWeatherPlaces';Pump 900;Click 'PickPlace';Pump 1900
 $root.FindName('PlaceQuery').Text='横浜';WaitAutoSearch
 $raw=$pt.GetField('placeResults',$if).GetValue($panel);Assert ($raw.Length -gt 3 -and $root.FindName('PlaceResults').Children.Count -eq 3) 'Typing did not trigger search'
 Assert ($root.FindName('PlaceResults').Children[0].Content.Children.Count -eq 3) 'Kind/address/distance descriptions missing'
 $root.FindName('PlaceSearchFilters').Children[1].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));Assert ($root.FindName('PlaceResults').Children[0].Content.Children[0].Text.StartsWith('駅')) 'Station filter failed'
 Call 'QueuePlaceSearch'|Out-Null;Assert (-not $pt.GetField('placeSearchWait',$if).GetValue($panel).IsEnabled) 'Moving focus after completed search queued another query'
 Click 'PlaceNext';Pump 900;Assert ($pt.GetField('placePage',$if).GetValue($panel) -eq 1) 'Paging reset after focus left the query';Click 'PlacePrevious';Pump 1900
 Snapshot 'place-search-detailed.png'
 $root.FindName('PlaceSearchFilters').Children[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
 # Composition must not issue requests until completion.
 $pt.GetField('placeComposing',$if).SetValue($panel,$true);$root.FindName('PlaceQuery').Text='横浜市役所';Pump 800;Assert (-not $pt.GetField('placeSearchWait',$if).GetValue($panel).IsEnabled -and $null -eq $pt.GetField('placeSearching',$if).GetValue($panel)) 'Composing Japanese input triggered a request'
 $pt.GetField('placeComposing',$if).SetValue($panel,$false);Call 'QueuePlaceSearch'|Out-Null;WaitAutoSearch
 $root.FindName('PlaceQuery').Text='横浜';$root.FindName('PlaceQuery').Text='横浜ランドマークタワー';WaitAutoSearch
 $search=$a.GetType('OrbitCalendar.PlaceSearch');$raw=$pt.GetField('placeResults',$if).GetValue($panel)
 $ranked=$search.GetMethod('Rank',$sf).Invoke($null,[object[]]@($raw,'横浜ランドマークタワー',[Device.Location.GeoCoordinate]::new(35.681236,139.767125),0));$tower=$ranked[0]
 Assert ($tower.Name -eq '横浜ランドマークタワー' -and $tower.Category -eq 2 -and $tower.Area.Contains('横浜市') -and $tower.Source.Contains('Photon')) 'Building name not found/ranked, or area missing'
 Snapshot 'place-search-building.png'
 $root.FindName('PlaceResults').Children[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));WaitLoading
 Assert ($pt.GetField('candidatePlace',$if).GetValue($panel) -eq $tower.Coordinate -and $place.GetProperty('Entries',$sf).GetValue($null,$null).Count -eq 3) 'Candidate saved prematurely'
 $vp=[Activator]::CreateInstance($a.GetType('OrbitCalendar.LiveData+Viewport'));$vp.Latitude=$tower.Coordinate.Latitude;$vp.Longitude=$tower.Coordinate.Longitude;$vp.Zoom=17;$vp.Name='検索候補周辺';$pt.GetField('mapViewport',$if).SetValue($panel,$vp);Call 'LoadView' @($false)|Out-Null;WaitLoading
 Assert ($pt.GetField('activeWeather',$if).GetValue($panel).Viewport.Zoom -eq 17) 'Detailed street map not fetched'
 $wheel=[Windows.Input.MouseWheelEventArgs]::new([Windows.Input.Mouse]::PrimaryDevice,[Environment]::TickCount,120);$wheel.RoutedEvent=[Windows.Input.Mouse]::MouseWheelEvent;$root.FindName('Plot').RaiseEvent($wheel);Pump 350;WaitLoading
 Assert ($pt.GetField('activeWeather',$if).GetValue($panel).Viewport.Zoom -eq 18 -and $place.GetProperty('Active',$sf).GetValue($null,$null).Zoom -eq 9) 'Preview wheel failed or changed old saved zoom'
 $original=$pt.GetField('candidatePlace',$if).GetValue($panel)
 Call 'CompleteMapDrag' @([Windows.Vector]::new(45,25),[Windows.Point]::new(200,100))|Out-Null;WaitLoading
 Assert ($pt.GetField('candidatePlace',$if).GetValue($panel) -eq $original) 'Dragging map accidentally moved registration pin'
 Call 'CompleteMapDrag' @([Windows.Vector]::new(1,1),[Windows.Point]::new(178,95))|Out-Null;Pump 1900
 $adjusted=$pt.GetField('candidatePlace',$if).GetValue($panel);Assert ($root.FindName('MapHint').Text.Contains('ドラッグ')) 'Map hint did not recover after setting visible point';Assert ($adjusted.GetDistanceTo($original) -gt 1 -and (Entries).Count -eq 3 -and $root.FindName('SavePlace').IsEnabled) 'Click did not refine point or saved too early'
 Assert ((Bounds ($root.FindName('PlaceConfirm'))).Top -ge (Bounds ($root.FindName('Plot'))).Bottom) 'Registration confirmation covers map click area'
 Snapshot 'place-adjust-before-add.png'
 Click 'SavePlace';WaitLoading;Assert ((Entries).Count -eq 4 -and (Entries)[3].Zoom -eq 18 -and $place.GetProperty('Coordinate',$sf).GetValue($null,$null) -eq $adjusted) 'Refined coordinate and zoom not registered'
 & powershell -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -ReloadWorkflow $folder;Assert ($LASTEXITCODE -eq 0) 'Fresh restart failed'
 Click 'PickPlace';Pump 1900;$root.FindName('PlaceQuery').Text='キャンセル予定の検索';Click 'CalendarTab';Pump 900;Assert (-not $pt.GetField('placeSearchWait',$if).GetValue($panel).IsEnabled -and $null -eq $pt.GetField('placeSearching',$if).GetValue($panel)) 'Closing/tab switch left auto-search running'
 Write-Output 'PASS: persistent up/down ordering, unchanged selected place/map, automatic search/composition/cancel, station filtering and address/distance cards, live building search, detailed zoom/real wheel, pan versus click, preview isolation and adjusted coordinate/zoom registration'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
