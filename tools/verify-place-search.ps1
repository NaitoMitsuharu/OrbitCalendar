$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-place-search-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null)
$data=$a.GetType('OrbitCalendar.LiveData');$catalog=$a.GetType('OrbitCalendar.Instruments').GetField('ById',$sf).GetValue($null)
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Await($task){$end=[DateTime]::UtcNow.AddSeconds(100);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $end){throw 'Request timeout'};Pump 50};if($task.IsFaulted){throw $task.Exception.ToString()};return $task.Result}

$place=$a.GetType('OrbitCalendar.SavedPlace');$place.GetField('path',$sf).SetValue($null,(Join-Path $folder 'location.txt'))
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
 Assert ([IO.File]::Exists((Join-Path $folder 'location.txt')) -and $null -eq $pt.GetField('candidatePlace',$if).GetValue($panel)) 'Confirmed place not saved or selection not cleared'
 $coordinate=$place.GetProperty('Coordinate',$sf).GetValue($null,$null);Assert ($coordinate -eq $candidate) 'Wrong place saved'
 $forecast=$data.GetField('CachedForecast',$sf).GetValue($null);Assert ($null -ne $forecast -and $forecast.CoordinateKey -eq ([Math]::Round($candidate.Latitude,6).ToString([Globalization.CultureInfo]::InvariantCulture)+','+[Math]::Round($candidate.Longitude,6).ToString([Globalization.CultureInfo]::InvariantCulture))) 'Saved place did not update weather forecast'
 $reloaded=$place.GetMethod('Load',$sf).Invoke($null,@());Assert ([Math]::Abs($reloaded.Latitude-$candidate.Latitude) -lt .000001) 'Saved coordinates not reloadable'
 Click 'PickPlace';Pump 1900;Click 'PickOnMap';Pump 900;Assert ($pt.GetField('selectingPlace',$if).GetValue($panel) -and $root.FindName('PlaceConfirm').Visibility -eq 'Collapsed') 'Manual map selection unavailable'
 Call 'SelectPlaceAt' @([Windows.Point]::new(178,95))|Out-Null;Assert ($root.FindName('PlaceConfirm').Visibility -eq 'Visible') 'Manual map confirmation failed'
 Click 'CancelPlace';Assert ($place.GetProperty('Coordinate',$sf).GetValue($null,$null) -eq $candidate) 'Cancel changed saved location'
 Click 'PickPlace';Pump 1900;$root.FindName('PlaceQuery').Text='架空の地点検索qz987654321';Click 'SearchPlace';WaitSearch;Assert ($root.FindName('PlaceResults').Children.Count -eq 0 -and $root.FindName('PlaceSearchMessage').Text.Contains('候補がありません')) 'Empty search lacks useful fallback'
 $root.FindName('PlaceQuery').Text='千葉駅';Click 'SearchPlace';Assert ($root.FindName('PlaceSearchLoading').Visibility -eq 'Visible') 'Particle loader not shown'
 $root.FindName('PlaceQuery').Text='横浜駅';Assert ($null -eq $pt.GetField('placeSearching',$if).GetValue($panel)) 'Editing query did not cancel request'
 Click 'SearchPlace';Click 'CalendarTab';Pump 2200
 Assert ($picker.Visibility -eq 'Collapsed' -and $null -eq $pt.GetField('placeSearching',$if).GetValue($panel) -and -not $pt.GetField('placeSearchTick',$if).GetValue($panel).IsEnabled) 'Tab switch leaked modal/request/loader'
 Assert ($root.FindName('TodayWeather').ToolTip.ToString().Contains('保存')) 'Calendar not using saved place'
 Write-Output 'PASS: live station/town/address search, cache/ranking/validation, paged themed editor and particle loading, map candidate preview, explicit save/reload, weather and calendar location, cancellation/manual fallback/tab cleanup'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
