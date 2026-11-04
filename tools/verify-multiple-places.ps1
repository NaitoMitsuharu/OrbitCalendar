param([string]$ReloadPlaces)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
if($ReloadPlaces){
 $scale=$a.GetType('OrbitCalendar.MapScale');$scale.GetField('path',$sf).SetValue($null,(Join-Path $ReloadPlaces 'map-zoom.txt'));$scale.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
 $store=$a.GetType('OrbitCalendar.SavedPlace');$store.GetField('path',$sf).SetValue($null,(Join-Path $ReloadPlaces 'location.txt'));$store.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
 $items=$store.GetProperty('Entries',$sf).GetValue($null,$null)
 if($items.Count -ne 3 -or $store.GetProperty('Name',$sf).GetValue($null,$null) -ne '東京駅' -or $items[0].Zoom -ne 6 -or $items[1].Zoom -ne 9 -or -not $store.GetMethod('IsHidden',$sf).Invoke($null,[object[]]@(1)) -or -not $store.GetMethod('IsHidden',$sf).Invoke($null,[object[]]@(3))){throw 'Multiple places/per-place zoom/hidden tabs restore failed'}
 Write-Output 'PASS: multiple names/coordinates/active point/per-place zoom/hidden tabs restored in fresh process';exit 0
}
$folder=Join-Path $env:TEMP ('orbit-multiple-places-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
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
$tokyo=(Entries)[0].Id
Store 'Add' @([Device.Location.GeoCoordinate]::new(35.465646,139.620120),'横浜駅',9)|Out-Null;$yokohama=Active
Store 'Add' @([Device.Location.GeoCoordinate]::new(35.613190,140.113130),'千葉駅',7)|Out-Null;$chiba=Active
Assert ((Entries).Count -eq 3) 'Adding a place replaced an existing point'
Store 'Select' @($tokyo)|Out-Null;Assert ($nameProperty.GetValue($null,$null) -eq $null -or $nameProperty.GetValue($null,$null) -eq '保存地点') 'Legacy unnamed place unexpectedly renamed'
Store 'Rename' @($tokyo,'東京駅')|Out-Null;Store 'UpdateZoom' @(6)|Out-Null
Store 'Select' @($yokohama)|Out-Null;Assert ($place.GetProperty('Active',$sf).GetValue($null,$null).Zoom -eq 9) 'Place zooms not independent'
Store 'Select' @($tokyo)|Out-Null
Store 'SetHidden' @(1,$true)|Out-Null;Store 'SetHidden' @(3,$true)|Out-Null
& powershell -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -ReloadPlaces $folder;Assert ($LASTEXITCODE -eq 0) 'Fresh restore failed'
# Saving a bad destination must not discard the existing collection or active point.
$place.GetField('path',$sf).SetValue($null,$folder);$failed=$false;try{Store 'Add' @([Device.Location.GeoCoordinate]::new(35.0,139.0),'失敗',8)|Out-Null}catch{$failed=$true};$place.GetField('path',$sf).SetValue($null,$locationFile)
Assert ($failed -and (Entries).Count -eq 3 -and (Active) -eq $tokyo) 'Failed write mutated collection'
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Call($name,$arguments=@()){return $pt.GetMethod($name,$if).Invoke($panel,[object[]]$arguments)}
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI weather timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function WaitSearch { $end=[DateTime]::UtcNow.AddSeconds(40);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI search timeout'}}while($null -ne $pt.GetField('placeSearching',$if).GetValue($panel));Pump 300 }
function Bounds($el){$panel.UpdateLayout();return $el.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($el.RenderSize))}
function Snapshot($name){Pump 200;$panel.UpdateLayout();$im=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$visual=[Windows.Media.DrawingVisual]::new();$dc=$visual.RenderOpen();$brush=[Windows.Media.VisualBrush]::new($root);$brush.ViewboxUnits=[Windows.Media.BrushMappingMode]::Absolute;$brush.Viewbox=[Windows.Rect]::new(0,0,458,$root.Height);$brush.Stretch=[Windows.Media.Stretch]::Fill;$dc.DrawRectangle($brush,$null,[Windows.Rect]::new(0,0,458,$root.Height));$dc.Close();$im.Render($visual);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}


function WeatherChoice($tag){$button=@($root.FindName('WeatherChoices').Children|Where-Object Tag -eq $tag);Assert ($button.Count -eq 1) ('Missing weather choice '+$tag);$button[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function ManagerAction($tag){$buttons=@($root.FindName('RegisteredPlaces').Children|ForEach-Object {$_.Child.Children[1].Children}|Where-Object Tag -eq $tag);Assert ($buttons.Count -eq 1) ('Missing manager action '+$tag);$buttons[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Click 'WeatherTab';WaitLoading
 $tags=@($root.FindName('WeatherChoices').Children|ForEach-Object Tag)
 Assert ($tags.Count -eq 5 -and $tags -contains $tokyo -and $tags -contains $yokohama -and $tags -contains $chiba -and $tags -notcontains 'preset1' -and $tags -notcontains 'preset3') 'Dynamic/hidden weather tabs wrong'
 Assert ($root.FindName('DataTitle').Text -eq '東京駅周辺の雨雲' -and $pt.GetField('activeWeather',$if).GetValue($panel).Viewport.Zoom -eq 6) 'Active legacy point/zoom not used'
 WeatherChoice $yokohama;WaitLoading;Assert ($root.FindName('DataTitle').Text -eq '横浜駅周辺の雨雲' -and $pt.GetField('activeWeather',$if).GetValue($panel).Viewport.Zoom -eq 9) 'Switching saved place did not update rain/zoom'
 Snapshot 'multiple-weather-places.png'
 Click 'DailyMode';WaitLoading;WeatherChoice $chiba;WaitLoading
 Assert ($root.FindName('DataStamp').Text.StartsWith('千葉駅周辺') -and (Active) -eq $chiba) 'Daily view cannot switch places'
 $cached=$data.GetField('CachedForecast',$sf).GetValue($null);Assert ($cached.CoordinateKey -eq '35.61319,140.11313') 'Daily forecast uses previous coordinate'
 Snapshot 'multiple-places-daily.png'
 $extraIds=@();foreach($i in 0..7){Store 'Add' @([Device.Location.GeoCoordinate]::new(35.68+$i*.0001,139.76),('長い表示名の確認用地点'+$i),8)|Out-Null;$extraIds+=(Active)};Store 'Select' @($chiba)|Out-Null
 Call 'RebuildWeatherChoices'|Out-Null;$root.FindName('WeatherScroll').ScrollToRightEnd();Pump 300
 Assert ($root.FindName('WeatherScroll').ScrollableWidth -gt 0 -and $root.FindName('WeatherScroll').HorizontalOffset -gt 0) 'Many/long saved places cannot scroll'
 $stripBounds=Bounds ($root.FindName('WeatherScroll'));$toolsBounds=Bounds ($root.FindName('MapTools'));Assert ($stripBounds.Right -le $toolsBounds.Left) 'Scrollable choices overlap pinned add/manage controls'
 Click 'ManageWeatherPlaces';Pump 1900
 Assert ($root.FindName('RegisteredPlaceScroll').ScrollableHeight -gt 0) 'Many saved places stretch manager instead of scrolling'
 foreach($row in @($root.FindName('RegisteredPlaces').Children|Select-Object -First 3)){Assert ($row.Child.Children[0].ActualWidth -gt 40) 'Saved place name collapsed in scrollable manager'}
 Snapshot 'weather-places-many.png'
 Click 'CloseWeatherPlaces';Pump 900;foreach($id in $extraIds){Store 'Remove' @($id)|Out-Null};Call 'RebuildWeatherChoices'|Out-Null
 Click 'ManageWeatherPlaces';Pump 1900
 Assert ($root.FindName('WeatherPlacesPicker').Visibility -eq 'Visible' -and $root.FindName('RegisteredPlaces').Children.Count -eq 3) 'Place manager missing rows'
 $mb=Bounds ($root.FindName('WeatherPlacesPicker'));Assert ($mb.Top -ge 48 -and $mb.Bottom -lt $root.Height) 'Place manager clipped'
 Snapshot 'weather-places-manager.png'
 ManagerAction ('rename:'+$tokyo);Pump 1900;$root.FindName('SavedPlaceName').Text='とても長い名前の東京駅・丸の内口';Click 'SavePlaceName';Pump 2000
 Assert ((Active) -eq $chiba -and $root.FindName('DataStamp').Text.StartsWith('千葉駅周辺')) 'Renaming an inactive point selected it or relabeled active weather'
 Assert ((Entries)[0].Name -eq 'とても長い名前の東京駅・丸の内口') 'Inactive point not renamed'
 Snapshot 'multiple-places-renamed.png'
 Click 'ManageWeatherPlaces';Pump 1900;$root.FindName('WeatherPresetChoices').Children[1].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
 Assert (-not (Store 'IsHidden' @(1))) 'Hidden preset cannot be restored'
 ManagerAction ('delete:'+$tokyo);Assert ((Entries).Count -eq 3) 'First delete press immediately removed point';ManagerAction ('delete:'+$tokyo)
 Assert ((Entries).Count -eq 2 -and (Active) -eq $chiba) 'Deleting inactive point affected active selection'
 ManagerAction ('delete:'+$chiba);ManagerAction ('delete:'+$chiba);WaitLoading
 Assert ((Entries).Count -eq 1 -and (Active) -eq $yokohama -and $root.FindName('DataStamp').Text.StartsWith('横浜駅周辺')) 'Deleting selected point did not choose valid fallback'
 Click 'CalendarTab';Pump 1900
 Assert ($root.FindName('TodayWeather').ToolTip.ToString().Contains('横浜駅周辺')) 'Calendar does not follow active registered place'
 Click 'TransitTab';WaitLoading;Assert ($root.FindName('WeatherScroll').Visibility -eq 'Collapsed' -and $root.FindName('RegionChoices').Visibility -eq 'Visible') 'Weather tabs leaked into transit filters'
 # Model-only fallback after UI checks avoids requesting an actual current location.
 foreach($i in 0..3){Store 'SetHidden' @($i,$true)|Out-Null};Store 'Remove' @($yokohama)|Out-Null
 Assert ((Entries).Count -eq 0 -and -not (Store 'IsHidden' @(0)) -and $null -eq (Active)) 'Removing last place leaves no accessible weather tab'
 Assert (-not (Store 'SetHidden' @(0,$true))) 'Hiding the final available tab allowed'
 Write-Output 'PASS: legacy migration, additive places, independent names/zooms, atomic failure, fresh restart, rain/daily/calendar switching, themed scrollable choices/manager, inactive rename, reversible preset hiding, two-step deletion/fallback, transit separation, last-tab guard'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
