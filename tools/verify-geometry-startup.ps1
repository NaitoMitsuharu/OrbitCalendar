param([string]$ReloadGeometry)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
if($ReloadGeometry){
 $store=$a.GetType('OrbitCalendar.SavedPlace');$store.GetField('path',$sf).SetValue($null,(Join-Path $ReloadGeometry 'location.txt'));$store.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
 $items=$store.GetProperty('Entries',$sf).GetValue($null,$null)
 if($items.Count -ne 3 -or $items[0].Name -ne '横浜駅' -or $items[0].Zoom -ne 17 -or $items[1].Name -ne '東京駅' -or $items[1].Zoom -ne 12 -or $items[2].Name -ne '千葉駅' -or $store.GetProperty('Name',$sf).GetValue($null,$null) -ne '東京駅'){throw 'Restart lost edited position/zoom, name, order or active selection'}
 Write-Output 'PASS: edited existing places retain names, IDs/order and active selection after fresh restart';exit 0
}
$folder=Join-Path $env:TEMP ('orbit-geometry-startup-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
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
Store 'Save' @([Device.Location.GeoCoordinate]::new(35.681236,139.767125),'東京駅')|Out-Null;$tokyo=Active
Store 'Add' @([Device.Location.GeoCoordinate]::new(35.465646,139.620120),'横浜駅',10)|Out-Null;$yokohama=Active
Store 'Add' @([Device.Location.GeoCoordinate]::new(35.613190,140.113130),'千葉駅',8)|Out-Null;$chiba=Active
Store 'Select' @($tokyo)|Out-Null;Store 'Move' @($yokohama,-1)|Out-Null
$order=@((Entries)|ForEach-Object Id);$originalYokohama=(Entries)[0].Coordinate
$startup=$a.GetType('OrbitCalendar.StartupRegistration');$testRegistry='Software\OrbitCalendar\Tests\'+[guid]::NewGuid().ToString('N');$startup.GetField('registryPath',$sf).SetValue($null,$testRegistry)
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Call($name,$arguments=@()){return $pt.GetMethod($name,$if).Invoke($panel,[object[]]$arguments)}
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI weather timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function WaitSearch { $end=[DateTime]::UtcNow.AddSeconds(40);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI search timeout'}}while($null -ne $pt.GetField('placeSearching',$if).GetValue($panel));Pump 300 }
function Bounds($el){$panel.UpdateLayout();return $el.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($el.RenderSize))}
function Snapshot($name){Pump 200;$panel.UpdateLayout();$im=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$visual=[Windows.Media.DrawingVisual]::new();$dc=$visual.RenderOpen();$brush=[Windows.Media.VisualBrush]::new($root);$brush.ViewboxUnits=[Windows.Media.BrushMappingMode]::Absolute;$brush.Viewbox=[Windows.Rect]::new(0,0,458,$root.Height);$brush.Stretch=[Windows.Media.Stretch]::Fill;$dc.DrawRectangle($brush,$null,[Windows.Rect]::new(0,0,458,$root.Height));$dc.Close();$im.Render($visual);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}



function WaitFast { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 50;if([DateTime]::UtcNow -gt $end){throw 'Fast map load timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 400 }
function ManagerAction($tag){$buttons=@($root.FindName('RegisteredPlaces').Children|ForEach-Object {$_.Child.Children[1].Children}|Where-Object Tag -eq $tag);Assert ($buttons.Count -eq 1) ('Missing action '+$tag);$buttons[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function SetViewport($lat,$lon,$zoom){$vp=[Activator]::CreateInstance($a.GetType('OrbitCalendar.LiveData+Viewport'));$vp.Latitude=$lat;$vp.Longitude=$lon;$vp.Zoom=$zoom;$vp.Name='表示範囲';$pt.GetField('mapViewport',$if).SetValue($panel,$vp);Call 'LoadView' @($false)|Out-Null;WaitFast}
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Click 'WeatherTab';WaitLoading
 $originalView=$pt.GetField('activeWeather',$if).GetValue($panel);$originalForecast=$pt.GetField('weatherForecast',$if).GetValue($panel)
 Assert ($originalView.Frames.Length -gt 1) 'Normal rain timeline missing'
 $prefs.GetField('AnimationSpeed',$sf).SetValue($null,2)
 Click 'ManageWeatherPlaces';Pump 2700;Snapshot 'weather-place-position-action.png'
 $clock=[Diagnostics.Stopwatch]::StartNew();ManagerAction ('position:'+$yokohama)
 if($null -ne $pt.GetField('loading',$if).GetValue($panel)){Assert ($pt.GetField('mapLoading',$if).GetValue($panel)) 'Map loading did not use compact/fast loading';$lb=Bounds ($root.FindName('LoadingArea'));$pb=Bounds ($root.FindName('Plot'));Assert ([Math]::Abs($lb.Top-$pb.Top) -lt 2 -and $lb.Height -eq $pb.Height) 'Map loading masks outside map'}
 WaitFast;$clock.Stop();Write-Output ('Existing-place preview including settling: '+[Math]::Round($clock.Elapsed.TotalMilliseconds)+' ms')
 $name=$root.FindName('CandidatePlaceName');$view=$pt.GetField('activeWeather',$if).GetValue($panel);$effect=$pt.GetField('effect',$if).GetValue($panel)
 Assert ($name.IsReadOnly -and $name.Text -eq '横浜駅' -and $root.FindName('SavePlace').Content -eq '変更を保存') 'Existing edit permits renaming or still adds a point'
 Assert ($view.Viewport.Zoom -eq 10 -and $view.Frames.Length -eq 1 -and $view.Radar -ne $null -and $pt.GetField('weatherForecast',$if).GetValue($panel) -eq $originalForecast) 'Preview fetches full timeline or refetches forecast'
 Assert (-not $effect.Busy -and $effect.GetType().GetField('timeScale',$if).GetValue($effect) -le .14 -and $root.FindName('PlaceConfirm').Opacity -eq 1) 'Map/panel transition remains slow under slow global preference'
 Assert ($root.FindName('TemperaturePlot').Visibility -eq 'Collapsed' -and $root.FindName('RadarControls').Visibility -eq 'Collapsed') 'Preview shows weather for another saved point'
 SetViewport 35.4660 139.6205 16;Call 'SelectPlaceAt' @([Windows.Point]::new(190,100))|Out-Null;Click 'CancelPlace';WaitFast
 Assert ((Entries).Count -eq 3 -and (Entries)[0].Coordinate -eq $originalYokohama -and (Entries)[0].Zoom -eq 10 -and (Active) -eq $tokyo -and -not $name.IsReadOnly) 'Cancel changed stored geometry/selection or left readonly addition field'
 Assert ($pt.GetField('activeWeather',$if).GetValue($panel).Frames.Length -gt 1 -and $root.FindName('RadarControls').Visibility -eq 'Visible') 'Cancel did not restore full rain timeline'
 Call 'OpenSavedLocationEditor' @($yokohama)|Out-Null;WaitFast;SetViewport 35.4661 139.6206 17;Call 'SelectPlaceAt' @([Windows.Point]::new(188,99))|Out-Null
 $target=$pt.GetField('candidatePlace',$if).GetValue($panel);Assert ($target.GetDistanceTo($originalYokohama) -gt 10) 'Existing geometry did not move'
 $place.GetField('path',$sf).SetValue($null,$folder);Click 'SavePlace';Assert ((Entries)[0].Coordinate -eq $originalYokohama -and $root.FindName('PlaceConfirm').Visibility -eq 'Visible') 'Failed save changed geometry or lost draft';$place.GetField('path',$sf).SetValue($null,$locationFile)
 Call 'SelectPlaceAt' @([Windows.Point]::new(188,99))|Out-Null;Snapshot 'weather-place-position-edit.png';$target=$pt.GetField('candidatePlace',$if).GetValue($panel)
 Click 'SavePlace';WaitLoading
 Assert ((Entries).Count -eq 3 -and (Entries)[0].Coordinate -eq $target -and (Entries)[0].Name -eq '横浜駅' -and (Entries)[0].Zoom -eq 17 -and (Active) -eq $tokyo) 'Saving inactive point changed name/count/active selection'
 Assert ((@((Entries)|ForEach-Object Id) -join ',') -eq ($order -join ',')) 'Editing changed IDs/tab order'
 Call 'OpenSavedLocationEditor' @($tokyo)|Out-Null;WaitFast;SetViewport 35.6820 139.7680 12;Call 'SelectPlaceAt' @([Windows.Point]::new(180,97))|Out-Null;$tokyoAdjusted=$pt.GetField('candidatePlace',$if).GetValue($panel);Click 'SavePlace';WaitLoading
 $f=$data.GetField('CachedForecast',$sf).GetValue($null);$key=[Math]::Round($tokyoAdjusted.Latitude,6).ToString([Globalization.CultureInfo]::InvariantCulture)+','+[Math]::Round($tokyoAdjusted.Longitude,6).ToString([Globalization.CultureInfo]::InvariantCulture)
 Assert ($f.CoordinateKey -eq $key -and $root.FindName('DataTitle').Text -eq '東京駅周辺の雨雲') 'Active edit did not update weather location'
 & powershell -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -ReloadGeometry $folder;Assert ($LASTEXITCODE -eq 0) 'Fresh restart failed'
 Click 'SettingsButton';Pump 2700
 $toggle=$root.FindName('StartupToggle');Assert ($toggle.Content -eq '自動起動 OFF') 'Startup default not off in isolated registry'
 $sb=Bounds $toggle;$mb=Bounds ($root.FindName('SettingsMarkets'));$cb=Bounds ($root.FindName('CloseSettingsPicker'));Assert ($sb.Left -ge $mb.Right+3 -and $sb.Right -le $cb.Left-3) 'Startup switch overlaps header buttons'
 Click 'StartupToggle';Assert ($startup.GetProperty('Enabled',$sf).GetValue($null,$null) -and $toggle.Content -eq '自動起動 ON') 'Startup toggle did not enable'
 $reg=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testRegistry);try{$command=$reg.GetValue('OrbitCalendar')}finally{$reg.Dispose()}
 Assert ($command -eq $startup.GetProperty('Command',$sf).GetValue($null,$null) -and $command.StartsWith('"') -and $command.EndsWith('" --autostart')) 'Startup command not quoted or does not request background startup'
 Click 'StartupToggle';Assert (-not $startup.GetProperty('Enabled',$sf).GetValue($null,$null)) 'Startup toggle did not disable';Snapshot 'startup-setting.png'
 # Own the application's event before launching duplicate instances; no desktop integration or user startup keys are touched.
 $created=$false;$signal=[Threading.EventWaitHandle]::new($false,[Threading.EventResetMode]::AutoReset,'Local\OrbitCalendar.Open.4cf34b7f',[ref]$created)
 try{
  Assert $created 'Another app owns the singleton event'
  $child=Start-Process -FilePath (Resolve-Path build/OrbitCalendar.exe) -ArgumentList '--autostart' -WindowStyle Hidden -PassThru -Wait
  Assert (-not $signal.WaitOne(300)) 'Duplicate autostart unexpectedly opens window'
  $child=Start-Process -FilePath (Resolve-Path build/OrbitCalendar.exe) -WindowStyle Hidden -PassThru -Wait
  Assert ($signal.WaitOne(500)) 'Normal second launch no longer requests window'
 }finally{$signal.Dispose()}
 Write-Output 'PASS: manager geometry edit, name/ID/order/selection preservation, single-frame preview and fast assembly, read-only name, cancel/full timeline restoration, failure retention, active forecast update, fresh restart, startup UI layout/on/off/quoted command, quiet duplicate autostart'
 Write-Output $folder
}finally{
 $panel.Hide();$panel.Close()
 if($testRegistry.StartsWith('Software\OrbitCalendar\Tests\')){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testRegistry,$false)}
}
