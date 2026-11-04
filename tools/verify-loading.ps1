$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-loading-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Name',$sf).SetValue($null,$null,$null) # Isolate saved-place name from the user's settings.
$mapScale=$a.GetType('OrbitCalendar.MapScale');$mapScale.GetField('path',$sf).SetValue($null,(Join-Path $folder 'map-zoom.txt'));$mapScale.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
$data=$a.GetType('OrbitCalendar.LiveData');$catalog=$a.GetType('OrbitCalendar.Instruments').GetField('ById',$sf).GetValue($null)
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Await($task){$end=[DateTime]::UtcNow.AddSeconds(100);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $end){throw 'Request timeout'};Pump 50};if($task.IsFaulted){throw $task.Exception.ToString()};return $task.Result}
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI load timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function SaveImage($element,$width,$height,$name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($element);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
function Snapshot($name){Pump 250;$panel.UpdateLayout();SaveImage $root 458 ([int]$root.Height) $name}
function Reopen($expected){$panel.Collapse();Pump 900;Assert (-not $panel.IsVisible) 'Collapse failed';$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1800;Assert ($pt.GetField('mode',$if).GetValue($panel) -eq $expected) 'Previous tab not restored';Assert ($root.Height -eq $panel.PreferredHeight) 'Reopened size incorrect'}

try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900
 $effect=$pt.GetField('effect',$if).GetValue($panel);$mask=$root.FindName('LoadingArea');$begin=$pt.GetMethod('BeginDataLoading',$if);$end=$pt.GetMethod('EndDataLoading',$if)
 foreach($tab in @('MarketTab','WeatherTab','NewsTab','TransitTab')){
  Click $tab
  Assert ($mask.Visibility -eq 'Visible') ($tab+' has no loader')
  Assert (-not $root.FindName('DataStamp').Text.Contains('取得中')) ($tab+' still displays loading text')
  WaitLoading
  if($tab -eq 'NewsTab'){$until=[DateTime]::UtcNow.AddSeconds(45);while($null -ne $pt.GetField('newsPreview',$if).GetValue($panel)){Pump 100;if([DateTime]::UtcNow -gt $until){throw 'News preview timeout'}};Pump 1800}
  Assert ($mask.Visibility -eq 'Collapsed') ($tab+' loader stuck after completion')
  $owner=[Threading.CancellationTokenSource]::new();$begin.Invoke($panel,@($owner))|Out-Null;$panel.UpdateLayout();$effect.Start($true);Pump 1900
  $particles=$pt.GetField('mainLoadingParticles',$if).GetValue($panel);$region=$particles.TransformToAncestor($root.FindName('Face')).TransformBounds([Windows.Rect]::new($particles.RenderSize))
  Assert ($particles.Width -ge 180 -and $particles.IsVisible) 'Particle field missing or too small'
  $before=$particles.Phase;Pump 150;Assert ($particles.Phase -gt $before) ($tab+' particle motion stopped')
  $grains=$effect.GetType().GetField('grains',$if).GetValue($effect);Assert (@($grains|Where-Object {$region.Contains($_.Target)}).Count -gt 100) 'Loader still consists of a few dots'
  Snapshot ($tab+'-loading.png')
  if($tab -eq 'WeatherTab'){
   # Freeze the live clock, then capture one full cycle from the actual particle renderer.
   $pt.GetField('loadingAnimation',$if).GetValue($panel).Stop()
   for($frame=0;$frame -lt 48;$frame++){
    $particles.Phase=$frame*3.8/48;$particles.InvalidateVisual();$effect.RefreshArea($region);Pump 25
    SaveImage $root 458 ([int]$root.Height) ('loading-{0:D3}.png' -f $frame)
   }
   $pt.GetField('loadingAnimation',$if).GetValue($panel).Start()
  }
  $end.Invoke($panel,@($owner))|Out-Null;$panel.UpdateLayout();$effect.Start($true)
  Assert ($effect.Busy -and $mask.Visibility -eq 'Collapsed') 'Result did not assemble after loader'
  Pump 1900;$owner.Dispose()
 }
 # A canceled request must not dismiss a newer loader, and switching tabs must remove it.
 $old=[Threading.CancellationTokenSource]::new();$new=[Threading.CancellationTokenSource]::new()
 $begin.Invoke($panel,@($old))|Out-Null;$begin.Invoke($panel,@($new))|Out-Null;$end.Invoke($panel,@($old))|Out-Null;Assert ($mask.Visibility -eq 'Visible') 'Stale request hid a newer loader'
 Click 'CalendarTab';Pump 1900;Assert ($mask.Visibility -eq 'Collapsed') 'Loader leaked into calendar'
 $old.Dispose();$new.Dispose()
 Write-Output 'PASS: particle fields and advancing motion in market/weather/news/transit, completion assembly, full-cycle capture, stale-owner safety and cancellation'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
