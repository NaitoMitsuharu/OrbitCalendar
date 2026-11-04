param([string]$ReloadOptions)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
if($ReloadOptions){
 $place=$a.GetType('OrbitCalendar.SavedPlace');$place.GetField('path',$sf).SetValue($null,(Join-Path $ReloadOptions 'location.txt'));$place.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
 $scale=$a.GetType('OrbitCalendar.MapScale');$scale.GetField('path',$sf).SetValue($null,(Join-Path $ReloadOptions 'map-zoom.txt'));$scale.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
 if($place.GetProperty('Name',$sf).GetValue($null,$null) -ne 'いつもの駅' -or $scale.GetMethod('ForRegion',$sf).Invoke($null,[object[]]@(0)) -ne 8 -or $scale.GetMethod('ForRegion',$sf).Invoke($null,[object[]]@(1)) -ne 5){throw 'Fresh-process name/region scale restore failed'}
 Write-Output 'PASS: edited name and per-region zoom restored in fresh process';exit 0
}
$folder=Join-Path $env:TEMP ('orbit-place-options-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Name',$sf).SetValue($null,$null,$null) # Isolate saved-place name from the user's settings.
$mapScale=$a.GetType('OrbitCalendar.MapScale');$mapScale.GetField('path',$sf).SetValue($null,(Join-Path $folder 'map-zoom.txt'));$mapScale.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
$data=$a.GetType('OrbitCalendar.LiveData');$catalog=$a.GetType('OrbitCalendar.Instruments').GetField('ById',$sf).GetValue($null)
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Await($task){$end=[DateTime]::UtcNow.AddSeconds(100);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $end){throw 'Request timeout'};Pump 50};if($task.IsFaulted){throw $task.Exception.ToString()};return $task.Result}

$place=$a.GetType('OrbitCalendar.SavedPlace');$place.GetField('path',$sf).SetValue($null,(Join-Path $folder 'location.txt'))
$nameProperty=$place.GetProperty('Name',$sf);$displayProperty=$place.GetProperty('DisplayName',$sf);$locationFile=Join-Path $folder 'location.txt'
[IO.File]::WriteAllText($locationFile,'35.681236,139.767125')
$legacy=$place.GetMethod('Load',$sf).Invoke($null,@());Assert ($null -ne $legacy -and $null -eq $nameProperty.GetValue($null,$null) -and $displayProperty.GetValue($null,$null) -eq '保存地点') 'Legacy coordinates-only file not compatible'
[IO.File]::Delete($locationFile)


$place.GetMethod('Save',$sf).Invoke($null,[object[]]@([Device.Location.GeoCoordinate]::new(35.681236,139.767125),'東京駅'))|Out-Null
function Zoom([int]$index){return $mapScale.GetMethod('ForRegion',$sf).Invoke($null,[object[]]@($index))}
function Remember([int]$index,[int]$zoom){return $mapScale.GetMethod('Remember',$sf).Invoke($null,[object[]]@($index,$zoom))}
Assert ((Zoom 0) -eq 9 -and (Zoom 1) -eq 4 -and (Zoom 2) -eq 7 -and (Zoom 3) -eq 7) 'Default zooms changed'
Assert (-not (Remember 0 11) -and -not (Remember -1 7)) 'Invalid zoom/region accepted'
[IO.File]::WriteAllLines((Join-Path $folder 'map-zoom.txt'),@('99','5','bad','6'));$mapScale.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
Assert ((Zoom 0) -eq 9 -and (Zoom 1) -eq 5 -and (Zoom 2) -eq 7 -and (Zoom 3) -eq 6) 'Invalid zoom recovery failed'
$mapScale.GetField('path',$sf).SetValue($null,$folder);Assert (-not (Remember 0 8) -and (Zoom 0) -eq 9) 'Failed zoom save changed durable preference';$mapScale.GetField('path',$sf).SetValue($null,(Join-Path $folder 'map-zoom.txt'))
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Call($name,$arguments=@()){return $pt.GetMethod($name,$if).Invoke($panel,[object[]]$arguments)}
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI weather timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function WaitSearch { $end=[DateTime]::UtcNow.AddSeconds(40);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI search timeout'}}while($null -ne $pt.GetField('placeSearching',$if).GetValue($panel));Pump 300 }
function Bounds($el){$panel.UpdateLayout();return $el.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($el.RenderSize))}
function Snapshot($name){Pump 200;$panel.UpdateLayout();$im=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$visual=[Windows.Media.DrawingVisual]::new();$dc=$visual.RenderOpen();$brush=[Windows.Media.VisualBrush]::new($root);$brush.ViewboxUnits=[Windows.Media.BrushMappingMode]::Absolute;$brush.Viewbox=[Windows.Rect]::new(0,0,458,$root.Height);$brush.Stretch=[Windows.Media.Stretch]::Fill;$dc.DrawRectangle($brush,$null,[Windows.Rect]::new(0,0,458,$root.Height));$dc.Close();$im.Render($visual);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}

try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Click 'WeatherTab';WaitLoading
 Assert ($root.FindName('DataTitle').Text -eq '東京駅周辺の雨雲') 'Fixture title not loaded'
 $slider=$root.FindName('RadarTime');$slider.Value=$slider.Maximum/2;Pump 150
 $beforeSlider=$slider.Value;$beforeView=$pt.GetField('activeWeather',$if).GetValue($panel);$beforeCoordinate=$place.GetProperty('Coordinate',$sf).GetValue($null,$null)
 Click 'PickPlace';Pump 1900;Assert ($root.FindName('RenamePlace').Visibility -eq 'Visible') 'Rename entry missing'
 Click 'RenamePlace';Pump 1900
 $editor=$root.FindName('PlaceEditPicker');Assert ($editor.Visibility -eq 'Visible' -and $editor.Opacity -eq 1 -and $root.FindName('SavedPlaceName').Text -eq '東京駅') 'Name editor/native handoff failed'
 $eb=Bounds $editor;$input=Bounds ($root.FindName('SavedPlaceName'));$actions=Bounds ($root.FindName('SavePlaceName'))
 Assert ($eb.Top -ge 48 -and $eb.Bottom -le $root.Height-40 -and $input.Bottom -lt $actions.Top) 'Name editor clipping/overlap'
 Snapshot 'place-name-editor.png'
 $root.FindName('SavedPlaceName').Text=' ';Click 'SavePlaceName';Assert ($editor.Visibility -eq 'Visible' -and $root.FindName('PlaceEditMessage').Text.Contains('入力')) 'Blank name accepted'
 $place.GetField('path',$sf).SetValue($null,$folder);$root.FindName('SavedPlaceName').Text='保存できない名前';Click 'SavePlaceName'
 Assert ($editor.Visibility -eq 'Visible' -and $nameProperty.GetValue($null,$null) -eq '東京駅' -and $root.FindName('PlaceEditMessage').Text.Contains('保存できません')) 'Failed save lost original name or hid editor'
 $place.GetField('path',$sf).SetValue($null,$locationFile)
 $root.FindName('SavedPlaceName').Text='取消する名前';Click 'CancelPlaceEdit';Pump 900;Assert ($nameProperty.GetValue($null,$null) -eq '東京駅') 'Cancel renamed point'
 Click 'PickPlace';Pump 1900;Click 'RenamePlace';Pump 1900;$root.FindName('SavedPlaceName').Text=' いつもの駅 ';Click 'SavePlaceName';Pump 2000
 Assert ($nameProperty.GetValue($null,$null) -eq 'いつもの駅' -and $editor.Visibility -eq 'Collapsed' -and $place.GetProperty('Coordinate',$sf).GetValue($null,$null) -eq $beforeCoordinate) 'Rename moved point or not saved'
 Assert ($root.FindName('DataTitle').Text -eq 'いつもの駅周辺の雨雲' -and $slider.Value -eq $beforeSlider -and $pt.GetField('activeWeather',$if).GetValue($panel) -eq $beforeView -and $null -eq $pt.GetField('loading',$if).GetValue($panel)) 'Rename reset map/timeline or required network'
 Assert ($root.FindName('TodayWeather').ToolTip.ToString().Contains('いつもの駅周辺')) 'Calendar tooltip not renamed'
 Click 'DailyMode';WaitLoading;Assert ($root.FindName('DataStamp').Text.StartsWith('いつもの駅周辺')) 'Daily view uses old name';Click 'RainMode';WaitLoading
 # Exercise the actual wheel handler and its persistence/cache invalidation.
 $wheel=[Windows.Input.MouseWheelEventArgs]::new([Windows.Input.Mouse]::PrimaryDevice,[Environment]::TickCount,-120);$wheel.RoutedEvent=[Windows.Input.Mouse]::MouseWheelEvent;$root.FindName('Plot').RaiseEvent($wheel)
 Assert ((Zoom 0) -eq 8 -and [IO.File]::ReadAllLines((Join-Path $folder 'map-zoom.txt'))[0] -eq '8') 'Wheel did not immediately persist scale'
 $panel.Collapse();Pump 900;Assert (-not $panel.IsVisible) 'Close immediately after wheel failed';$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;WaitLoading
 Assert ($pt.GetField('activeWeather',$if).GetValue($panel).Viewport.Zoom -eq 8) 'Immediate close/reopen lost requested zoom'
 Click 'CalendarTab';Pump 1900;Click 'WeatherTab';WaitLoading
 Assert ($pt.GetField('activeWeather',$if).GetValue($panel).Viewport.Zoom -eq 8) 'Returning tab reused stale zoom'
 Click 'Choice0';WaitLoading;Assert ($pt.GetField('activeWeather',$if).GetValue($panel).Viewport.Zoom -eq 8) 'Returning to saved point reset zoom'
 Assert ((Zoom 1) -eq 5 -and (Zoom 2) -eq 7 -and (Zoom 3) -eq 6) 'Saved-point zoom overwrote other regions'
 Click 'Choice1';WaitLoading;Assert ($pt.GetField('activeWeather',$if).GetValue($panel).Viewport.Zoom -eq 5) 'National region did not use saved zoom'
 & powershell -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -ReloadOptions $folder
 Assert ($LASTEXITCODE -eq 0) 'Fresh process restore failed'
 Click 'PickPlace';Pump 1900;Click 'RenamePlace';Pump 1900;$root.FindName('SavedPlaceName').Text='未確定'
 $keyEvent=[Windows.Input.KeyEventArgs]::new([Windows.Input.Keyboard]::PrimaryDevice,[Windows.PresentationSource]::FromVisual($panel),[Environment]::TickCount,[Windows.Input.Key]::Escape);$keyEvent.RoutedEvent=[Windows.Input.Keyboard]::PreviewKeyDownEvent;$panel.RaiseEvent($keyEvent);Pump 900
 Assert ($editor.Visibility -eq 'Collapsed' -and $nameProperty.GetValue($null,$null) -eq 'いつもの駅') 'Escape saved draft or left editor open'
 Click 'PickPlace';Pump 1900;Click 'RenamePlace';Pump 1900;$root.FindName('SavedPlaceName').Text='未確定';Click 'CalendarTab';Pump 1900
 Assert ($editor.Visibility -eq 'Collapsed' -and $nameProperty.GetValue($null,$null) -eq 'いつもの駅') 'Tab switch saved draft or left editor open'
 Write-Output 'PASS: themed rename editor, blank/cancel handling, unchanged coordinate/map/timeline, cached and calendar/daily names, real wheel, immediate close/reopen and per-region zoom persistence, return to tab/saved point, fresh process restore, tab cleanup'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
