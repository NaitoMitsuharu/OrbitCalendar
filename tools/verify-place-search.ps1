param([string]$ReloadLocation)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
if($ReloadLocation){
 $reloadType=$a.GetType('OrbitCalendar.SavedPlace');$reloadType.GetField('path',$sf).SetValue($null,$ReloadLocation)
 $reopened=$reloadType.GetMethod('Load',$sf).Invoke($null,@())
 if($null -eq $reopened -or $reloadType.GetProperty('Name',$sf).GetValue($null,$null) -ne '天王町駅'){throw 'Fresh process could not reload saved place name'}
 Write-Output 'PASS: saved place name restored in fresh process';exit 0
}
$folder=Join-Path $env:TEMP ('orbit-place-search-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
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

$search=$a.GetType('OrbitCalendar.PlaceSearch')
function Search($q){return Await ($search.GetMethod('Search',$sf).Invoke($null,[object[]]@($q,[Threading.CancellationToken]::None)))}
$station=@(Search '天王町駅');Assert ($station.Length -gt 0 -and $station[0].Name -eq '天王町駅') 'Exact station must rank first'
Assert ([Math]::Abs($station[0].Coordinate.Latitude-35.45433) -lt .002 -and [Math]::Abs($station[0].Coordinate.Longitude-139.60267) -lt .002) 'Station coordinates incorrect'
$town=@(Search '横浜市');Assert ($town.Length -gt 0 -and $town[0].Name.Contains('横浜市')) 'Town search failed'
$address=@(Search '東京都千代田区丸の内1-9-1');Assert ($address.Length -gt 0 -and $address[0].Name.Contains('丸の内')) 'Street address search failed'
$cached=@(Search '天王町駅');Assert ($cached[0] -eq $station[0]) 'Repeated query not cached'
$json='[{"geometry":{"type":"Point","coordinates":[139.6,35.4]},"properties":{"title":"町"}},{"geometry":{"type":"Point","coordinates":[139.7,35.5]},"properties":{"title":"駅名"}},{"geometry":{"type":"Point","coordinates":[139.7,35.5]},"properties":{"title":"駅名"}},{"geometry":{"type":"Point","coordinates":[0,0]},"properties":{"title":"範囲外"}},{"geometry":null,"properties":null}]'
$parsed=@($search.GetMethod('Parse',$sf).Invoke($null,[object[]]@($json,'駅名')));Assert ($parsed.Count -eq 2 -and $parsed[0].Name -eq '駅名') 'Invalid features, duplicates, exact-name ranking failed'
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Call($name,$arguments=@()){return $pt.GetMethod($name,$if).Invoke($panel,[object[]]$arguments)}
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI weather timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function WaitSearch { $end=[DateTime]::UtcNow.AddSeconds(40);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI search timeout'}}while($null -ne $pt.GetField('placeSearching',$if).GetValue($panel));Pump 300 }
function Bounds($el){$panel.UpdateLayout();return $el.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($el.RenderSize))}
function Snapshot($name){Pump 200;$panel.UpdateLayout();$im=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$visual=[Windows.Media.DrawingVisual]::new();$dc=$visual.RenderOpen();$brush=[Windows.Media.VisualBrush]::new($root);$brush.ViewboxUnits=[Windows.Media.BrushMappingMode]::Absolute;$brush.Viewbox=[Windows.Rect]::new(0,0,458,$root.Height);$brush.Stretch=[Windows.Media.Stretch]::Fill;$dc.DrawRectangle($brush,$null,[Windows.Rect]::new(0,0,458,$root.Height));$dc.Close();$im.Render($visual);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Click 'WeatherTab';WaitLoading
 Click 'PickPlace';Pump 1900
 $picker=$root.FindName('PlaceSearchPicker');Assert ($picker.Visibility -eq 'Visible' -and $picker.Opacity -eq 1) 'Search panel/native handoff failed'
 $bounds=Bounds $picker;Assert ($bounds.Top -ge 48 -and $bounds.Bottom -le $root.Height-40) 'Place panel outside window'
 $root.FindName('PlaceQuery').Text='天王町駅';Click 'SearchPlace';WaitSearch
 Assert ($root.FindName('PlaceResults').Children.Count -eq 4 -and $root.FindName('PlaceSearchLoading').Visibility -eq 'Collapsed') 'Search results/loader cleanup failed'
 Assert ($pt.GetField('placeResults',$if).GetValue($panel)[0].Name -eq '天王町駅') 'UI exact-match ordering failed'
 $last=Bounds $root.FindName('PlaceResults').Children[3];$pager=Bounds ($root.FindName('PlaceNext'));Assert ($last.Bottom -le $pager.Top) 'Results overlap paging'
 Snapshot 'place-search.png'
 Click 'PlaceNext';Pump 1900;Assert ($pt.GetField('placePage',$if).GetValue($panel) -eq 1 -and $picker.Opacity -eq 1) 'Next page/native editor transition failed'
 Click 'PlacePrevious';Pump 1900
 $root.FindName('PlaceResults').Children[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));WaitLoading
 Assert ($picker.Visibility -eq 'Collapsed' -and $pt.GetField('selectingPlace',$if).GetValue($panel)) 'Selection did not open map confirmation'
 $candidate=$pt.GetField('candidatePlace',$if).GetValue($panel);Assert ($candidate -eq $station[0].Coordinate) 'Selected candidate changed'
 $view=$pt.GetField('activeWeather',$if).GetValue($panel);Assert ([Math]::Abs($view.Viewport.Latitude-$candidate.Latitude) -lt .0001) 'Map did not center on candidate'
 Assert ($root.FindName('PlaceConfirm').Visibility -eq 'Visible' -and $root.FindName('SavePlace').IsEnabled -and $root.FindName('PlaceSummary').Text.Contains('天王町駅')) 'Map confirmation missing'
 Assert (-not [IO.File]::Exists((Join-Path $folder 'location.txt'))) 'Candidate saved without explicit confirmation'
 Snapshot 'place-search-confirm.png'
 Click 'SavePlace';WaitLoading
 Assert ($nameProperty.GetValue($null,$null) -eq '天王町駅' -and [IO.File]::ReadAllLines($locationFile)[1] -eq '天王町駅') 'Selected name not stored alongside coordinates'
 & powershell -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -ReloadLocation $locationFile
 Assert ($LASTEXITCODE -eq 0) 'Fresh process name reload failed'
 Assert ($root.FindName('DataTitle').Text -eq '天王町駅周辺の雨雲' -and $root.FindName('Choice0').ToolTip -eq '天王町駅周辺へ戻る') 'Radar or saved-place tooltip does not show name'
 Snapshot 'place-named-weather.png'
 Click 'DailyMode';WaitLoading;Assert ($root.FindName('DataStamp').Text.StartsWith('天王町駅周辺')) 'Daily weather missing saved name';Click 'RainMode';WaitLoading

 Assert ([IO.File]::Exists((Join-Path $folder 'location.txt')) -and $null -eq $pt.GetField('candidatePlace',$if).GetValue($panel)) 'Confirmed place not saved or selection not cleared'
 $coordinate=$place.GetProperty('Coordinate',$sf).GetValue($null,$null);Assert ($coordinate -eq $candidate) 'Wrong place saved'
 $forecast=$data.GetField('CachedForecast',$sf).GetValue($null);Assert ($null -ne $forecast -and $forecast.CoordinateKey -eq ([Math]::Round($candidate.Latitude,6).ToString([Globalization.CultureInfo]::InvariantCulture)+','+[Math]::Round($candidate.Longitude,6).ToString([Globalization.CultureInfo]::InvariantCulture))) 'Saved place did not update weather forecast'
 $nameProperty.SetValue($null,'discard-in-memory-name',$null);$reloaded=$place.GetMethod('Load',$sf).Invoke($null,@());Assert ($nameProperty.GetValue($null,$null) -eq '天王町駅') 'Name not restored from disk';Assert ([Math]::Abs($reloaded.Latitude-$candidate.Latitude) -lt .000001) 'Saved coordinates not reloadable'
 Click 'PickPlace';Pump 1900;Click 'PickOnMap';Pump 900;Assert ($pt.GetField('selectingPlace',$if).GetValue($panel) -and $root.FindName('PlaceConfirm').Visibility -eq 'Collapsed') 'Manual map selection unavailable'
 Call 'SelectPlaceAt' @([Windows.Point]::new(178,95))|Out-Null;Assert ($root.FindName('PlaceConfirm').Visibility -eq 'Visible') 'Manual map confirmation failed'
 Assert ($pt.GetField('candidatePlaceName',$if).GetValue($panel) -eq $null) 'Manual new point inherited old station name';Click 'CancelPlace';Assert ($place.GetProperty('Coordinate',$sf).GetValue($null,$null) -eq $candidate) 'Cancel changed saved location'
 Click 'PickPlace';Pump 1900;$root.FindName('PlaceQuery').Text='架空の地点検索qz987654321';Click 'SearchPlace';WaitSearch;Assert ($root.FindName('PlaceResults').Children.Count -eq 0 -and $root.FindName('PlaceSearchMessage').Text.Contains('候補がありません')) 'Empty search lacks useful fallback'
 $root.FindName('PlaceQuery').Text='千葉駅';Click 'SearchPlace';Assert ($root.FindName('PlaceSearchLoading').Visibility -eq 'Visible') 'Particle loader not shown'
 $root.FindName('PlaceQuery').Text='横浜駅';Assert ($null -eq $pt.GetField('placeSearching',$if).GetValue($panel)) 'Editing query did not cancel request'
 Click 'SearchPlace';Click 'CalendarTab';Pump 2200
 Assert ($picker.Visibility -eq 'Collapsed' -and $null -eq $pt.GetField('placeSearching',$if).GetValue($panel) -and -not $pt.GetField('placeSearchTick',$if).GetValue($panel).IsEnabled) 'Tab switch leaked modal/request/loader'
 Assert ($root.FindName('TodayWeather').ToolTip.ToString().Contains('天王町駅周辺')) 'Calendar not using saved place'
 $savedBefore=$place.GetProperty('Coordinate',$sf).GetValue($null,$null)
 $place.GetMethod('Save',$sf).Invoke($null,[object[]]@($savedBefore,'横浜市, 駅前'))|Out-Null
 $place.GetMethod('Load',$sf).Invoke($null,@())|Out-Null;Assert ($nameProperty.GetValue($null,$null) -eq '横浜市, 駅前') 'Comma in place name broke coordinates format'
 $place.GetMethod('Clear',$sf).Invoke($null,@())|Out-Null;Assert ($null -eq $nameProperty.GetValue($null,$null) -and $null -eq $place.GetProperty('Coordinate',$sf).GetValue($null,$null) -and $place.GetProperty('Entries',$sf).GetValue($null,$null).Count -gt 0) 'Automatic location did not deselect name and retain registered places'
 Write-Output 'PASS: live station/town/address search, cache/ranking/validation, paged themed editor and particle loading, map candidate preview, named save/reload in fresh process and legacy compatibility, weather and calendar names, cancellation/manual fallback/tab cleanup'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
