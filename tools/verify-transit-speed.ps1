$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-transit-speed-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null)
$a.GetType('OrbitCalendar.SavedPlace').GetProperty('Name',$sf).SetValue($null,$null,$null) # Isolate saved-place name from the user's settings.
$mapScale=$a.GetType('OrbitCalendar.MapScale');$mapScale.GetField('path',$sf).SetValue($null,(Join-Path $folder 'map-zoom.txt'));$mapScale.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
$data=$a.GetType('OrbitCalendar.LiveData');$catalog=$a.GetType('OrbitCalendar.Instruments').GetField('ById',$sf).GetValue($null)
function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Await($task){$end=[DateTime]::UtcNow.AddSeconds(100);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $end){throw 'Request timeout'};Pump 50};if($task.IsFaulted){throw $task.Exception.ToString()};return $task.Result}
$st=$a.GetType('OrbitCalendar.Shell');$shell=$st.GetConstructor($if,$null,[type[]]@([bool]),$null).Invoke([object[]]@($false))
$panel=$st.GetField('panel',$if).GetValue($shell);$mini=$st.GetField('countdown',$if).GetValue($shell);$pt=$panel.GetType();$root=$panel.Content.Child
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function WaitLoading { $end=[DateTime]::UtcNow.AddSeconds(100);do{Pump 100;if([DateTime]::UtcNow -gt $end){throw 'UI load timeout'}}while($null -ne $pt.GetField('loading',$if).GetValue($panel));Pump 1800 }
function SaveImage($element,$width,$height,$name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($element);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
function Snapshot($name){Pump 250;$panel.UpdateLayout();SaveImage $root 458 ([int]$root.Height) $name}
function Reopen($expected){$panel.Collapse();Pump 900;Assert (-not $panel.IsVisible) 'Collapse failed';$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1800;Assert ($pt.GetField('mode',$if).GetValue($panel) -eq $expected) 'Previous tab not restored';Assert ($root.Height -eq $panel.PreferredHeight) 'Reopened size incorrect'}

function Bounds($element){$panel.UpdateLayout();return $element.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($element.RenderSize))}
$speed=$prefs.GetField('AnimationSpeed',$sf);$load=$prefs.GetMethod('Load',$sf);$save=$prefs.GetMethod('Save',$sf);$settingsPath=Join-Path $folder 'display.txt'
[IO.File]::WriteAllLines($settingsPath,@('2','5','45','0','4'));$load.Invoke($null,@())|Out-Null
Assert ($speed.GetValue($null) -eq 1 -and $prefs.GetField('Pattern',$sf).GetValue($null) -eq 2 -and $prefs.GetField('LoadingStyle',$sf).GetValue($null) -eq 4) 'Legacy settings lost or speed does not default to standard'
[IO.File]::WriteAllLines($settingsPath,@('0','6','30','0','0','99'));$load.Invoke($null,@())|Out-Null;Assert ($speed.GetValue($null) -eq 1) 'Invalid speed accepted'
try{
 $shell.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 2200;$effect=$pt.GetField('effect',$if).GetValue($panel)
 Click 'SettingsButton';Pump 2100
 $replay=Bounds ($root.FindName('ReplayPreview'));$trigger=Bounds ($root.FindName('AnimationSpeed'));$credits=Bounds ($root.FindName('OpenCredits'));$picker=Bounds ($root.FindName('SettingsPicker'))
 Assert ($replay.Right+6 -le $trigger.Left -and $trigger.Right+6 -le $credits.Left) 'Speed control overlaps replay or credits'
 Assert ($picker.Top -ge 48 -and $picker.Bottom -le $root.Height-40) 'Settings no longer fit'
 Click 'AnimationSpeed';Pump 2000;$menu=$root.FindName('SpeedPicker');$mb=Bounds $menu
 Assert ($menu.Visibility -eq 'Visible' -and $root.FindName('SpeedChoices').Children.Count -eq 3 -and $mb.Top -ge $picker.Top -and $mb.Bottom -le $trigger.Top) 'Themed speed menu missing or not anchored above button'
 Snapshot 'settings-speed.png'
 foreach($index in 0..2){
  if($menu.Visibility -ne 'Visible'){Click 'AnimationSpeed';Pump 50}
  $root.FindName('SpeedChoices').Children[$index].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
  Assert ($speed.GetValue($null) -eq $index -and $menu.Visibility -eq 'Collapsed' -and $root.FindName('SettingsPicker').Visibility -eq 'Visible') 'Selecting speed closed settings or did not apply'
  $lines=[IO.File]::ReadAllLines($settingsPath);Assert ($lines.Length -eq 6 -and $lines[5] -eq $index.ToString()) 'Speed not saved'
  $speed.SetValue($null,99);$load.Invoke($null,@())|Out-Null;Assert ($speed.GetValue($null) -eq $index) 'Speed not restored'
  if([Windows.SystemParameters]::ClientAreaAnimation){
   Pump 1100
   if($index -eq 0){Assert (-not $effect.Busy) 'Fast transition did not complete in about 0.9s'}else{Assert $effect.Busy 'Standard or slow transition ended too early'}
   Pump 1600;Assert (-not $effect.Busy) 'Transition did not settle'
  }else{Assert (-not $effect.Busy) 'Reduced-motion transition still animates'}
 }
 # Escape dismisses only the speed menu first.
 Click 'AnimationSpeed';Pump 100
 $keyEvent=[Windows.Input.KeyEventArgs]::new([Windows.Input.Keyboard]::PrimaryDevice,[Windows.PresentationSource]::FromVisual($panel),0,[Windows.Input.Key]::Escape);$keyEvent.RoutedEvent=[Windows.Input.Keyboard]::PreviewKeyDownEvent;$panel.RaiseEvent($keyEvent)
 Assert ($menu.Visibility -eq 'Collapsed' -and $root.FindName('SettingsPicker').Visibility -eq 'Visible') 'Escape did not dismiss the speed menu independently'
 $speed.SetValue($null,0);Click 'CloseSettingsPicker';Pump 1100
 # Opening and closing share the scale, with the surface and particles finishing together.
 foreach($index in 0..2){
  $speed.SetValue($null,$index);$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false
  if([Windows.SystemParameters]::ClientAreaAnimation){Pump 1100;Assert (($index -eq 0) -eq (-not $effect.Busy)) 'Open animation ignored speed';Pump 1600}
  $panel.Collapse();Pump 500
  if([Windows.SystemParameters]::ClientAreaAnimation -and $index -gt 0){Assert $panel.IsVisible 'Close window finished before its slower particle animation';Pump 600}
  Assert (-not $panel.IsVisible) 'Close animation did not finish'
 }
 # Editable market popup must return to native controls at the selected speed.
 $speed.SetValue($null,0);$panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1100;Click 'MarketTab';WaitLoading;Click 'ManageMarkets';Pump 1200
 $transitionHost=$pt.GetField('marketTransitionHost',$if).GetValue($panel);Assert ($transitionHost.Visibility -eq 'Collapsed' -and $root.FindName('MarketPicker').Opacity -eq 1) 'Fast market popup still waits for standard duration'
 Click 'CloseMarketPicker';Pump 600;$speed.SetValue($null,2);Click 'ManageMarkets'
 if([Windows.SystemParameters]::ClientAreaAnimation){Pump 1900;Assert ($transitionHost.Visibility -eq 'Visible') 'Slow market popup became editable before particles finished'}
 Pump 1200;Assert ($transitionHost.Visibility -eq 'Collapsed' -and $root.FindName('MarketPicker').Opacity -eq 1) 'Slow market popup did not become editable';Click 'CloseMarketPicker';Pump 1100
 $speed.SetValue($null,1);Click 'TransitTab';WaitLoading
 $feedType=$a.GetType('OrbitCalendar.InformationFeeds+Feed');$itemType=$a.GetType('OrbitCalendar.InformationFeeds+Item');$fixture=[Activator]::CreateInstance($feedType,$true);$fixture.Fetched=[DateTime]::UtcNow
 function Route($name,$company,$normal){$item=[Activator]::CreateInstance($itemType,$true);$item.Title=$name;$item.Source=$company;$item.Normal=$normal;$item.Detail='【表示確認用】運行状況';$item.Url='https://example.com/';return $item}
 $choice=$pt.GetField('choice',$if).GetValue($panel);$body=$root.FindName('InformationBody')
 function RenderRoutes($items,[bool]$stale=$false){$fixture.Items=@($items);$fixture.PartialFailure=@($items|Where-Object {$null -eq $_.Normal}).Count -gt 0;$pt.GetField('information',$if).SetValue($panel,$fixture);$pt.GetMethod('RenderInformation',$if).Invoke($panel,@($stale))|Out-Null;$panel.UpdateLayout();$effect.Refresh();Pump 80}
 function MissingText {foreach($child in $body.Children){if($child.Tag -eq 'transit-missing'){return $child.Child.Text};if($child -is [Windows.Controls.Border] -and $child.Child -is [Windows.Controls.StackPanel]){foreach($text in $child.Child.Children){if($text.Tag -eq 'transit-missing-names'){return $text.Text}}}};return ''}
 $normal=Route '相鉄線' '相模鉄道' $true;$unknown=Route '横須賀線' 'JR東日本' $null;$privateUnknown=Route '東急東横線' '東急電鉄' $null;$delay=Route '京浜東北線' 'JR東日本' $false
 $choice[4]=0;RenderRoutes @($normal,$unknown,$privateUnknown)
 Assert ((MissingText).Contains('横須賀線') -and (MissingText).Contains('東急東横線') -and -not (MissingText).Contains('相鉄線')) 'Unconfirmed route names missing or normal route incorrectly listed';Snapshot 'transit-unconfirmed.png'
 RenderRoutes @($normal,$unknown,$delay)
 Assert (@($body.Children|Where-Object Tag -eq 'transit-missing').Count -eq 1 -and @($body.Children|Where-Object Tag -eq 'transit-disruption').Count -eq 1 -and (MissingText).Contains('横須賀線')) 'Missing names not shown alongside disruptions';Snapshot 'transit-mixed.png'
 $choice[4]=1;RenderRoutes @($normal,$unknown,$privateUnknown,$delay);Assert ((MissingText).Contains('横須賀線') -and -not (MissingText).Contains('東急')) 'JR filter lists private unconfirmed routes'
 $choice[4]=2;RenderRoutes @($normal,$unknown);Assert ($body.Children[0].Tag -eq 'transit-clear' -and -not $root.FindName('DataStamp').Text.Contains('一部')) 'Unknown JR route leaked into healthy private filter'
 $choice[4]=0;RenderRoutes @($normal,$unknown) $true;Assert ((MissingText).Contains('相鉄線') -and (MissingText).Contains('横須賀線') -and $body.Children[0].Tag -eq 'transit-stale') 'Failed refresh does not identify all currently unconfirmed routes'
 $many=@();foreach($i in 0..6){$many+=Route ('検証用の路線'+($i+1)) 'JR東日本' $(if($i -lt 5){$false}else{$null})};RenderRoutes $many
 Assert ($body.Children.Count -eq 5 -and @($body.Children|Where-Object Tag -eq 'transit-disruption').Count -eq 3) 'Disruption pager/notice count incorrect'
 $last=Bounds ($body.Children[$body.Children.Count-1]);Assert ($last.Bottom -lt $root.Height-10) 'Unconfirmed names push disruption paging outside window';Snapshot 'transit-paged.png'
 RenderRoutes @($normal);Assert ($body.Children[0].Tag -eq 'transit-clear' -and (MissingText) -eq '') 'Confirmed normal route shown as unconfirmed'
 Click 'SettingsButton';Pump 2000;Click 'AnimationSpeed';Pump 100;Click 'CalendarTab';Pump 2000;Assert ($menu.Visibility -eq 'Collapsed' -and $root.FindName('SettingsPicker').Visibility -eq 'Collapsed') 'Speed menu leaked across tabs'
 Write-Output 'PASS: speed persistence/legacy defaults, 3 measured durations and synchronized closing, styled menu layout/Escape/cleanup, market editable handoff, named unconfirmed routes with mixed disruptions/filtering/stale refresh/pagination'
 Write-Output $folder
}finally{$shell.Dispose();$panel.Close()}
