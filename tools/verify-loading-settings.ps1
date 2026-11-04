$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-loading-settings-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
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
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI load timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function SaveImage($element,$width,$height,$name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($element);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
function Snapshot($name){Pump 250;$panel.UpdateLayout();SaveImage $root 458 ([int]$root.Height) $name}
function Reopen($expected){$panel.Collapse();Pump 900;Assert (-not $panel.IsVisible) 'Collapse failed';$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1800;Assert ($pt.GetField('mode',$if).GetValue($panel) -eq $expected) 'Previous tab not restored';Assert ($root.Height -eq $panel.PreferredHeight) 'Reopened size incorrect'}


function ParticleFrame($element){
 $panel.UpdateLayout();$bounds=$element.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($element.RenderSize))
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($root)
 return [Windows.Media.Imaging.CroppedBitmap]::new($bitmap,[Windows.Int32Rect]::new([int][Math]::Round($bounds.X),[int][Math]::Round($bounds.Y),204,116))
}
function Foreground($bytes){
 $points=[Collections.Generic.HashSet[int]]::new()
 for($p=0;$p -lt $bytes.Length;$p+=4){if($bytes[$p+1] -gt 85 -and $bytes[$p+1] -gt $bytes[$p+2]*1.2){$points.Add($p/4)|Out-Null}}
 return ,$points
}
function MatchingShape($a,$b){
 $left=Foreground $a;$right=Foreground $b;$hits=0
 foreach($point in $left){$found=$false;foreach($dy in -1..1){foreach($dx in -1..1){if($right.Contains($point+$dy*204+$dx)){$found=$true}}};if($found){$hits++}}
 return $left.Count -gt 50 -and $right.Count -gt 50 -and $hits/$left.Count -gt .8 -and [Math]::Abs($left.Count-$right.Count)/[double][Math]::Max($left.Count,$right.Count) -lt .3
}
$settingsPath=Join-Path $folder 'display.txt'
$load=$prefs.GetMethod('Load',$sf);$save=$prefs.GetMethod('Save',$sf);$setting=$prefs.GetField('LoadingStyle',$sf)
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
 $choices=$root.FindName('LoadingChoices');Assert ($choices.Children.Count -eq 6) 'Loading animation options missing'
 $particles=$pt.GetField('settingsLoadingParticles',$if).GetValue($panel);$effect=$pt.GetField('effect',$if).GetValue($panel)
 $region=$particles.TransformToAncestor($root.FindName('Face')).TransformBounds([Windows.Rect]::new($particles.RenderSize));$timer=$pt.GetField('loadingAnimation',$if).GetValue($panel)
 $frames=@()
 foreach($choice in 0..5){
  $choices.Children[$choice].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));Pump 1900
  Assert ($setting.GetValue($null) -eq $choice) 'Choice did not apply'
  $saved=[IO.File]::ReadAllLines($settingsPath);Assert ($saved.Length -eq 6 -and $saved[4] -eq $choice.ToString()) 'Loading choice not saved'
  $phase=$particles.Phase;Pump 250;Assert ($particles.Phase -gt $phase) ('Preview not advancing: style='+$choice+' phase='+$phase+' -> '+$particles.Phase+' timer='+$timer.IsEnabled+' visible='+$panel.IsVisible+' settings='+$picker.Visibility)
  $timer.Stop();$particles.Phase=.9;$particles.InvalidateVisual();$effect.RefreshArea($region);Pump 40
  SaveImage $root 458 ([int]$root.Height) ('loading-style-{0}.png' -f $choice)
  $image=ParticleFrame $particles;$bytes=New-Object byte[] (204*116*4);$image.CopyPixels($bytes,204*4,0);$frames+=([Convert]::ToBase64String($bytes))
  if($choice -eq 3 -or $choice -eq 4){
   $particles.Phase=1.2;$particles.InvalidateVisual();$effect.RefreshArea($region);Pump 80
   $next=ParticleFrame $particles;$nextBytes=New-Object byte[] (204*116*4);$next.CopyPixels($nextBytes,204*4,0)
   Assert ([Convert]::ToBase64String($nextBytes) -ne $frames[$choice]) 'Cube or tiles do not move between frames'
  }
  $timer.Start()
 }
 Assert (@($frames|Select-Object -Unique).Count -eq 6) 'Loading motions render identically'
 $setting.SetValue($null,0);$load.Invoke($null,@())|Out-Null;Assert ($setting.GetValue($null) -eq 5) 'Saved selection does not reload'
 Click 'ReplayPreview';Assert ($particles.Phase -lt .2) 'Replay did not restart preview'
 Click 'SettingsTransition';Pump 1900;Assert ($transition.Visibility -eq 'Visible') 'Cannot return to transition settings'
 # Transition and loading settings must be independent.
 $root.FindName('MotionChoices').Children[2].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));Assert ($prefs.GetField('Motion',$sf).GetValue($null) -eq 2 -and $setting.GetValue($null) -eq 5) 'Transition choice overwrote loading motion'
 Click 'SettingsLoading';Pump 1900;Snapshot 'loading-settings.png'
 Click 'CloseSettingsPicker';Pump 1900;Click 'SettingsButton';Pump 1900;Assert ($loadingSettings.Visibility -eq 'Visible' -and $setting.GetValue($null) -eq 5) 'Reopening settings lost selection'
 Click 'CloseSettingsPicker';Pump 1900
 # Confirm the same renderer and saved style are used by the data loader.
 Click 'MarketTab';WaitLoading;$owner=[Threading.CancellationTokenSource]::new();$pt.GetMethod('BeginDataLoading',$if).Invoke($panel,@($owner))|Out-Null;$effect.Start($true);Pump 1900
 $main=$pt.GetField('mainLoadingParticles',$if).GetValue($panel);Assert ($main.IsVisible -and $main.GetType() -eq $particles.GetType()) 'Live loader is not using preview renderer'
 $phase=$main.Phase;Pump 150;Assert ($main.Phase -gt $phase) 'Live loader stopped after changing style'
 $timer.Stop();$main.Phase=.9;$main.InvalidateVisual();$mainRegion=$main.TransformToAncestor($root.FindName('Face')).TransformBounds([Windows.Rect]::new($main.RenderSize));$effect.RefreshArea($mainRegion);Pump 100
 $liveImage=ParticleFrame $main;$liveBytes=New-Object byte[] (204*116*4);$liveImage.CopyPixels($liveBytes,204*4,0)

 Assert (MatchingShape ([Convert]::FromBase64String($frames[5])) $liveBytes) 'Live loader shape differs from selected preview'
 $timer.Start()

 $pt.GetMethod('EndDataLoading',$if).Invoke($panel,@($owner))|Out-Null;$owner.Dispose();Click 'CalendarTab';Pump 1900
 Assert ($root.FindName('LoadingArea').Visibility -eq 'Collapsed') 'Loading style left a mask after switching tabs'
 # Save/reload every supported value, then exercise the old four-line format again.
 foreach($choice in 0..5){$setting.SetValue($null,$choice);$save.Invoke($null,@())|Out-Null;$setting.SetValue($null,99);$load.Invoke($null,@())|Out-Null;Assert ($setting.GetValue($null) -eq $choice) 'Persistence round-trip failed'}

 # Capture the actual application renderers together for visual comparison.
 Click 'SettingsButton';Pump 1900
 $gallery=[Windows.Window]::new();$gallery.Width=618;$gallery.Height=310;$gallery.WindowStyle='None';$gallery.ResizeMode='NoResize';$gallery.ShowInTaskbar=$false;$gallery.Background=[Windows.Media.BrushConverter]::new().ConvertFromString('#0B1923')
 $grid=[Windows.Controls.Grid]::new();$grid.Width=618;$grid.Height=310;$grid.Background=$gallery.Background
 foreach($col in 0..2){$grid.ColumnDefinitions.Add([Windows.Controls.ColumnDefinition]::new())};foreach($row in 0..1){$grid.RowDefinitions.Add([Windows.Controls.RowDefinition]::new())}
 $names=@('6本バー','円形スピナー','軌道リング','ドットキューブ','タイル組み替え','ドットウェーブ');$renderers=@()
 # Each instance is rendered separately with the selected design before assembling the gallery.
 $images=@();foreach($index in 0..5){
  $cell=[Windows.Controls.StackPanel]::new();$cell.Margin=[Windows.Thickness]::new(0,5,0,5)
  $caption=[Windows.Controls.TextBlock]::new();$caption.Text=$names[$index];$caption.FontSize=13;$caption.HorizontalAlignment='Center';$caption.Foreground=[Windows.Media.BrushConverter]::new().ConvertFromString('#C9EDEA');$cell.Children.Add($caption)|Out-Null
  $image=[Windows.Controls.Image]::new();$image.Width=204;$image.Height=116;$cell.Children.Add($image)|Out-Null;$images+=,$image
  [Windows.Controls.Grid]::SetColumn($cell,($index%3));[Windows.Controls.Grid]::SetRow($cell,[Math]::Floor($index/3));$grid.Children.Add($cell)|Out-Null
 }
 $gallery.Content=$grid;$gallery.Show();$gallery.UpdateLayout();$timer.Stop()
 for($frame=0;$frame -lt 48;$frame++){
  foreach($index in 0..5){
   $setting.SetValue($null,$index);$particles.Phase=$frame*.08;$particles.InvalidateVisual();$effect.RefreshArea($region);Pump 10
   $image=ParticleFrame $particles;$images[$index].Source=$image
  }
  Pump 5;SaveImage $grid 618 310 ('gallery-{0:D3}.png' -f $frame)
 }
 $setting.SetValue($null,0);$gallery.Close();$timer.Start()
 Write-Output 'PASS: six distinct loading designs, live preview/replay, selection persistence/reload and legacy fallback, independent transition settings, unclipped sections, same live renderer and tab cleanup'
 Write-Output $folder
}finally{if($gallery){$gallery.Close()};$panel.Hide();$panel.Close()}
