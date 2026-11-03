$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-loading-settings-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null)
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


$settingsPath=Join-Path $folder 'display.txt'
$load=$prefs.GetMethod('Load',$sf);$save=$prefs.GetMethod('Save',$sf);$setting=$prefs.GetField('LoadingMotion',$sf)
# Old four-line settings keep their values; the new option uses the default.
[IO.File]::WriteAllLines($settingsPath,@('2','5','45','0'));$load.Invoke($null,@())|Out-Null
Assert ($setting.GetValue($null) -eq 0 -and $prefs.GetField('Pattern',$sf).GetValue($null) -eq 2 -and $prefs.GetField('Minutes',$sf).GetValue($null) -eq 45) 'Legacy settings not preserved'
[IO.File]::WriteAllLines($settingsPath,@('0','6','30','0','99'));$load.Invoke($null,@())|Out-Null;Assert ($setting.GetValue($null) -eq 0) 'Invalid loading setting accepted'
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900;Click 'SettingsButton';Pump 1900
 $picker=$root.FindName('SettingsPicker');$pb=$picker.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($picker.RenderSize))
 Assert ($pb.Top -ge 48 -and $pb.Bottom -le $root.Height-40) 'Expanded settings overlap tabs or footer'
 $transition=$root.FindName('TransitionSettings');$loadingSettings=$root.FindName('LoadingSettings');Assert ($transition.Visibility -eq 'Visible' -and $loadingSettings.Visibility -eq 'Collapsed') 'Wrong default settings section'
 $preview=$root.FindName('PreviewArea');$replay=$root.FindName('ReplayPreview');$preBounds=$preview.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($preview.RenderSize));$replayBounds=$replay.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($replay.RenderSize));Assert ($preBounds.Bottom -le $replayBounds.Top) 'Transition preview overlaps footer controls'
 Snapshot 'settings.png';Click 'SettingsLoading';Pump 1900
 Assert ($loadingSettings.Visibility -eq 'Visible' -and $transition.Visibility -eq 'Collapsed') 'Loading settings section did not open'
 $choices=$root.FindName('LoadingChoices');Assert ($choices.Children.Count -eq 4) 'Loading animation options missing'
 $particles=$pt.GetField('settingsLoadingParticles',$if).GetValue($panel);$effect=$pt.GetField('effect',$if).GetValue($panel)
 $region=$particles.TransformToAncestor($root.FindName('Face')).TransformBounds([Windows.Rect]::new($particles.RenderSize));$timer=$pt.GetField('loadingAnimation',$if).GetValue($panel)
 $frames=@()
 foreach($choice in 0..3){
  $choices.Children[$choice].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));Pump 1900
  Assert ($setting.GetValue($null) -eq $choice) 'Choice did not apply'
  $saved=[IO.File]::ReadAllLines($settingsPath);Assert ($saved.Length -eq 5 -and $saved[4] -eq $choice.ToString()) 'Loading choice not saved'
  $phase=$particles.Phase;Pump 150;Assert ($particles.Phase -gt $phase) 'Preview does not animate in calendar tab'
  $timer.Stop();$particles.Phase=.9;$particles.InvalidateVisual();$effect.RefreshArea($region);Pump 40
  SaveImage $root 458 ([int]$root.Height) ('loading-style-{0}.png' -f $choice)
  $image=[Windows.Media.Imaging.RenderTargetBitmap]::new(204,116,96,96,[Windows.Media.PixelFormats]::Pbgra32);$image.Render($particles);$bytes=New-Object byte[] (204*116*4);$image.CopyPixels($bytes,204*4,0);$frames+=([Convert]::ToBase64String($bytes));$timer.Start()
 }
 Assert (@($frames|Select-Object -Unique).Count -eq 4) 'Loading motions render identically'
 $setting.SetValue($null,0);$load.Invoke($null,@())|Out-Null;Assert ($setting.GetValue($null) -eq 3) 'Saved selection does not reload'
 Click 'ReplayPreview';Assert ($particles.Phase -lt .2) 'Replay did not restart preview'
 Click 'SettingsTransition';Pump 1900;Assert ($transition.Visibility -eq 'Visible') 'Cannot return to transition settings'
 # Transition and loading settings must be independent.
 $root.FindName('MotionChoices').Children[2].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));Assert ($prefs.GetField('Motion',$sf).GetValue($null) -eq 2 -and $setting.GetValue($null) -eq 3) 'Transition choice overwrote loading motion'
 Click 'SettingsLoading';Pump 1900;Snapshot 'loading-settings.png'
 Click 'CloseSettingsPicker';Pump 1900;Click 'SettingsButton';Pump 1900;Assert ($loadingSettings.Visibility -eq 'Visible' -and $setting.GetValue($null) -eq 3) 'Reopening settings lost selection'
 Click 'CloseSettingsPicker';Pump 1900
 # Confirm the same renderer and saved style are used by the data loader.
 Click 'MarketTab';WaitLoading;$owner=[Threading.CancellationTokenSource]::new();$pt.GetMethod('BeginDataLoading',$if).Invoke($panel,@($owner))|Out-Null;$effect.Start($true);Pump 1900
 $main=$pt.GetField('mainLoadingParticles',$if).GetValue($panel);Assert ($main.IsVisible -and $main.GetType() -eq $particles.GetType()) 'Live loader is not using preview renderer'
 $phase=$main.Phase;Pump 150;Assert ($main.Phase -gt $phase) 'Live loader stopped after changing style'
 $pt.GetMethod('EndDataLoading',$if).Invoke($panel,@($owner))|Out-Null;$owner.Dispose();Click 'CalendarTab';Pump 1900
 Assert ($root.FindName('LoadingArea').Visibility -eq 'Collapsed') 'Loading style left a mask after switching tabs'
 # Save/reload every supported value, then exercise the old four-line format again.
 foreach($choice in 0..3){$setting.SetValue($null,$choice);$save.Invoke($null,@())|Out-Null;$setting.SetValue($null,99);$load.Invoke($null,@())|Out-Null;Assert ($setting.GetValue($null) -eq $choice) 'Persistence round-trip failed'}
 Write-Output 'PASS: four distinct loading motions, live preview/replay, selection persistence/reload and legacy fallback, independent transition settings, unclipped sections, same live renderer and tab cleanup'
 Write-Output $folder
}finally{$panel.Hide();$panel.Close()}
