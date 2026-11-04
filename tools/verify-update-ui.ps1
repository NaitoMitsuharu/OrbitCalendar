param([string]$Executable='build/next/OrbitCalendar.exe')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $Executable));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-update-ui-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $folder|Out-Null
foreach($pair in @(@('Watchlist','watchlist.json'),@('Preferences','display.txt'),@('SavedPlace','location.txt'),@('MapScale','map-zoom.txt'))){$a.GetType('OrbitCalendar.'+$pair[0]).GetField('path',$sf).SetValue($null,(Join-Path $folder $pair[1]))}
$a.GetType('OrbitCalendar.Watchlist').GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('LastTab',$sf).SetValue($null,0)
$place=$a.GetType('OrbitCalendar.SavedPlace');$place.GetProperty('Coordinate',$sf).SetValue($null,[Device.Location.GeoCoordinate]::new(35.681236,139.767125),$null);$place.GetProperty('Name',$sf).SetValue($null,'東京駅',$null)
$startup=$a.GetType('OrbitCalendar.StartupRegistration');$testRegistry='Software\OrbitCalendar\Tests\'+[guid]::NewGuid().ToString('N');$startup.GetField('registryPath',$sf).SetValue($null,$testRegistry)
function Assert($ok,$message){if(-not $ok){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
$panel=[Activator]::CreateInstance($a.GetType('OrbitCalendar.CalendarPanel'),[object[]]@([Action]{},[Action]{}));$pt=$panel.GetType();$root=$panel.Content.Child
function Call($name,$arguments=@()){return $pt.GetMethod($name,$if).Invoke($panel,[object[]]$arguments)}
function Click($name){$root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function Bounds($el){$panel.UpdateLayout();return $el.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($el.RenderSize))}
function Snapshot($name){Pump 200;$panel.UpdateLayout();$im=[Windows.Media.Imaging.RenderTargetBitmap]::new(458,[int]$root.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$visual=[Windows.Media.DrawingVisual]::new();$dc=$visual.RenderOpen();$brush=[Windows.Media.VisualBrush]::new($root);$brush.ViewboxUnits=[Windows.Media.BrushMappingMode]::Absolute;$brush.Viewbox=[Windows.Rect]::new(0,0,458,$root.Height);$brush.Stretch=[Windows.Media.Stretch]::Fill;$dc.DrawRectangle($brush,$null,[Windows.Rect]::new(0,0,458,$root.Height));$dc.Close();$im.Render($visual);$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$stream=[IO.File]::Create((Join-Path $folder $name));try{$encoder.Save($stream)}finally{$stream.Dispose()}}
try{
 $panel.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 2000
 Click 'SettingsButton';Pump 2000
 $sb=Bounds ($root.FindName('StartupToggle'));$ub=Bounds ($root.FindName('UpdateButton'));$cb=Bounds ($root.FindName('CloseSettingsPicker'))
 Assert ($ub.Left -ge $sb.Right+3 -and $ub.Right -le $cb.Left-3) 'Update button overlaps startup/close controls'
 Snapshot 'settings-update.png'
 Click 'UpdateButton';Pump 500
 Assert ($root.FindName('UpdatePicker').Visibility -eq 'Visible' -and $root.FindName('SettingsPicker').Visibility -eq 'Collapsed') 'Update dialog not exclusive'
 $end=[DateTime]::UtcNow.AddSeconds(30);while($pt.GetField('updateRequest',$if).GetValue($panel)) {Pump 100;if([DateTime]::UtcNow -gt $end){throw 'Update check timeout'}}
 Assert ($root.FindName('CheckUpdate').IsEnabled -and -not $root.FindName('InstallUpdate').IsEnabled) 'Live latest/equal check button states invalid'
 Write-Output ('Live check: '+$root.FindName('UpdateStatus').Text)
 $release=[Activator]::CreateInstance($a.GetType('OrbitCalendar.AppUpdates+Release'));$release.Version=[version]'1.27.0.0';$release.Notes="地点や設定を引き継いで更新します。`n新しい機能・表示の改善。"
 Call 'PresentUpdate' @($release)|Out-Null
 Assert ($root.FindName('InstallUpdate').IsEnabled -and $root.FindName('UpdateStatus').Text -eq 'v1.27.0 に更新できます') 'New version cannot be installed'
 $pb=Bounds ($root.FindName('UpdatePicker'));$ib=Bounds ($root.FindName('InstallUpdate'));$xb=Bounds ($root.FindName('CheckUpdate'));$note=Bounds ($root.FindName('UpdateNotes'));$status=Bounds ($root.FindName('UpdateStatus'))
 Assert ($xb.Right -le $ib.Left-6 -and $ib.Right -lt $pb.Right-10 -and $status.Bottom -le $note.Top+1 -and $note.Bottom -le $ib.Top) 'Update dialog overlaps or clips'
 Snapshot 'app-update.png'
 Call 'UpdateStatus' @('更新ファイルをダウンロードしています…',$true,$false)|Out-Null
 Assert (-not $root.FindName('CheckUpdate').IsEnabled -and -not $root.FindName('InstallUpdate').IsEnabled -and $root.FindName('UpdateProgressTrack').Visibility -eq 'Visible') 'Download state permits competing action'
 $root.FindName('UpdateProgressFill').Width=175;$root.FindName('UpdateProgressText').Text='50%';Snapshot 'app-update-download.png'
 $cancel=[Threading.CancellationTokenSource]::new();$pt.GetField('updateRequest',$if).SetValue($panel,$cancel)
 Click 'CloseUpdatePicker';Pump 1000
 Assert ($cancel.IsCancellationRequested -and $root.FindName('UpdatePicker').Visibility -eq 'Collapsed') 'Closing did not cancel download'
 $pt.GetField('updateRequest',$if).SetValue($panel,$null);$cancel.Dispose()
 $result=[Activator]::CreateInstance($a.GetType('OrbitCalendar.UpdateInstaller+Result'));$result.Success=$true;$result.Message='v1.26.0 に更新しました'
 Call 'ShowUpdateResult' @($result)|Out-Null;Pump 500
 Assert ($root.FindName('UpdateStatus').Text -eq $result.Message -and $root.FindName('CheckUpdate').IsEnabled) 'Update result not shown'
 Call 'SwitchView' @(1)|Out-Null;Pump 200
 Assert ($root.FindName('UpdatePicker').Visibility -eq 'Collapsed') 'Tab change leaves update dialog behind'
 Write-Output 'PASS: settings header spacing, live check, release notes, progress/button states, cancellation, results and tab cleanup'
 Write-Output $folder
}finally{
 $panel.Hide();$panel.Close()
 if($testRegistry.StartsWith('Software\OrbitCalendar\Tests\')){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testRegistry,$false)}
}
