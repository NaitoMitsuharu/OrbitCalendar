param([string]$Executable='build/next/OrbitCalendar.exe')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $Executable));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-particle-menus-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $folder|Out-Null
foreach($pair in @(@('Watchlist','watchlist.json'),@('Preferences','display.txt'),@('SavedPlace','location.txt'),@('MapScale','map-zoom.txt'))){$a.GetType('OrbitCalendar.'+$pair[0]).GetField('path',$sf).SetValue($null,(Join-Path $folder $pair[1]))}
$a.GetType('OrbitCalendar.Watchlist').GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('LastTab',$sf).SetValue($null,0);$prefs.GetField('Pattern',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1)
$place=$a.GetType('OrbitCalendar.SavedPlace');$place.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
function Store($name,$values=@()){return $place.GetMethod($name,$sf).Invoke($null,[object[]]$values)}
Store 'Add' @([Device.Location.GeoCoordinate]::new(35.4547,139.6024),'天王町駅',9)|Out-Null;$first=$place.GetProperty('ActiveId',$sf).GetValue($null,$null)
Store 'Add' @([Device.Location.GeoCoordinate]::new(35.4656,139.6201),'横浜駅',9)|Out-Null;Store 'Select' @($first)|Out-Null
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Assert($ok,$message){if(-not $ok){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Call($name,$values=@()){return $pt.GetMethod($name,$if).Invoke($panel,[object[]]$values)}
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading{$end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'Weather timeout'}}while($pt.GetField('loading',$if).GetValue($panel));Pump 2000}
function Frame{
 $panel.UpdateLayout();$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$visual=[Windows.Media.DrawingVisual]::new();$d=$visual.RenderOpen();$brush=[Windows.Media.VisualBrush]::new($root);$brush.ViewboxUnits=[Windows.Media.BrushMappingMode]::Absolute;$brush.Viewbox=[Windows.Rect]::new(0,0,458,$root.Height);$brush.Stretch=[Windows.Media.Stretch]::Fill;$d.DrawRectangle($brush,$null,[Windows.Rect]::new(0,0,458,$root.Height));$d.Close();$bitmap.Render($visual);return $bitmap
}
function Snapshot($name){$bitmap=Frame;$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $folder $name));try{$encoder.Save($stream)}finally{$stream.Dispose()}}
function Settled($name){Pump 2100;$picker=$root.FindName($name);Assert ($picker.Visibility -eq 'Visible' -and $picker.Opacity -eq 1 -and $picker.Effect.GetType().Name -eq 'ParticleMenuEffect' -and $pt.GetField('marketTransitionHost',$if).GetValue($panel).Visibility -eq 'Collapsed') ('Permanent particle menu missing: '+$name)}
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Click 'WeatherTab';WaitLoading
 Click 'ManageWeatherPlaces';Settled 'WeatherPlacesPicker';Snapshot 'place-management-particles.png'
 # Compare the actual raster with/without the settled effect. Merely attaching an
 # effect is insufficient: PS 2.0 must also render in WPF's software snapshots.
 $picker=$root.FindName('WeatherPlacesPicker');$with=Frame;$effect=$picker.Effect;$picker.Effect=$null;$plain=Frame;$picker.Effect=$effect
 $rect=$picker.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($picker.RenderSize));$crop=[Windows.Int32Rect]::new([int]$rect.X,[int]$rect.Y,[int]$rect.Width,[int]$rect.Height)
 $left=[byte[]]::new($crop.Width*$crop.Height*4);$right=[byte[]]::new($left.Length);$with.CopyPixels($crop,$left,$crop.Width*4,0);$plain.CopyPixels($crop,$right,$crop.Width*4,0)
 $differences=0;for($i=0;$i -lt $left.Length;$i+=4){if($left[$i] -ne $right[$i] -or $left[$i+1] -ne $right[$i+1] -or $left[$i+2] -ne $right[$i+2]){$differences++}}
 Assert ($differences -gt 1000) 'Particle shader not visible in rendered pixels'
 Call 'CloseWeatherPlaces' @($false)|Out-Null;Call 'OpenPlaceEditor' @($first)|Out-Null;Settled 'PlaceEditPicker'
 $root.FindName('SavedPlaceName').Text='天王町駅（表示確認）';Pump 100
 Snapshot 'place-name-particles.png';Click 'SavePlaceName';Pump 1900
 Assert ($place.GetProperty('Name',$sf).GetValue($null,$null) -eq '天王町駅（表示確認）') 'Text editing/save failed with particle effect'
 Call 'OpenSavedLocationEditor' @($first)|Out-Null;WaitLoading;Settled 'PlaceConfirm';Snapshot 'place-position-particles.png'
 Assert ($root.FindName('CandidatePlaceName').IsReadOnly -and $root.FindName('SavePlace').IsEnabled) 'Position editor controls did not update'
 Click 'CancelPlace';Pump 800
 Click 'PickPlace';Settled 'PlaceSearchPicker';$root.FindName('PlaceQuery').Text='横浜';Pump 120
 Assert ($root.FindName('PlaceQuery').Text -eq '横浜') 'Search input lost text';Snapshot 'place-search-particles.png'
 Call 'ClosePlaceSearch' @($false)|Out-Null
 for($pattern=0;$pattern -lt 4;$pattern++){
  $prefs.GetField('Pattern',$sf).SetValue($null,$pattern);Click 'ManageWeatherPlaces';Settled 'WeatherPlacesPicker'
  Assert ($root.FindName('WeatherPlacesPicker').Effect.Pattern -eq $pattern) 'Particle style did not follow settings'
  Call 'CloseWeatherPlaces' @($false)|Out-Null
 }
 Call 'OpenPlaceEditor' @($first)|Out-Null;Call 'SwitchView' @(0)|Out-Null;Pump 500
 Assert ($root.FindName('PlaceEditPicker').Visibility -eq 'Collapsed' -and $pt.GetField('marketTransitionHost',$if).GetValue($panel).Visibility -eq 'Collapsed') 'Menu/transition left behind after tab switch'
 Write-Output ('PASS: permanent menu particles affect '+$differences+' pixels, name input/save, readonly position editor, search input, four styles and tab cleanup')
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
