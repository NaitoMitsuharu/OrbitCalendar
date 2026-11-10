param([string]$Exe = 'build/OrbitCalendar.exe')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework,System.Device
Add-Type -Path (Join-Path $PSScriptRoot 'OdptMock.cs')
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $Exe));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-odpt-qa-'+[guid]::NewGuid());New-Item -ItemType Directory $folder|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$sf).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1);$prefs.GetField('HiddenTabs',$sf).SetValue($null,0)
$watch=$a.GetType('OrbitCalendar.Watchlist');$watch.GetField('path',$sf).SetValue($null,(Join-Path $folder 'watchlist.json'));$watch.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$todos=$a.GetType('OrbitCalendar.Todos');$todos.GetField('folder',$sf).SetValue($null,$folder);$todos.GetField('path',$sf).SetValue($null,(Join-Path $folder 'todos.json'));$todos.GetField('imageFolder',$sf).SetValue($null,(Join-Path $folder 'todo-images'));$todos.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$tt=$a.GetType('OrbitCalendar.Timetables');$tt.GetField('folder',$sf).SetValue($null,$folder);$tt.GetField('path',$sf).SetValue($null,(Join-Path $folder 'timetables.json'));$tt.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null
$odpt=$a.GetType('OrbitCalendar.Odpt');$tokenFile=Join-Path $folder 'odpt-tokens.bin';$odpt.GetField('tokenPath',$sf).SetValue($null,$tokenFile);$odpt.GetMethod('Reset',$sf).Invoke($null,@())|Out-Null
$mock=[OdptMock]::new();$base="http://127.0.0.1:$($mock.Port)"
$odpt.GetField('StandardBase',$sf).SetValue($null,"$base/standard/api/v4/");$odpt.GetField('ChallengeBase',$sf).SetValue($null,"$base/challenge/api/v4/")
function Route($name,$json){$mock.Routes[$name]=$json}
# --- ODPT-shaped fixtures (field names follow the ODPT API v4 vocabulary).
Route 'challenge odpt:Railway|odpt:operator=odpt.Operator:Sotetsu' '[{"owl:sameAs":"odpt.Railway:Sotetsu.Main","dc:title":"相鉄本線","odpt:railwayTitle":{"ja":"相鉄本線","en":"Sotetsu Main Line"}}]'
Route 'challenge odpt:Station|odpt:operator=odpt.Operator:Sotetsu' '[{"owl:sameAs":"odpt.Station:Sotetsu.Main.Yokohama","dc:title":"横浜","odpt:railway":"odpt.Railway:Sotetsu.Main"},{"owl:sameAs":"odpt.Station:Sotetsu.Main.Tennocho","odpt:stationTitle":{"ja":"天王町","en":"Tennocho"},"odpt:railway":"odpt.Railway:Sotetsu.Main"},{"owl:sameAs":"odpt.Station:Sotetsu.Main.Ebina","dc:title":"海老名","odpt:railway":"odpt.Railway:Sotetsu.Main"}]'
Route 'challenge odpt:RailDirection' '[{"owl:sameAs":"odpt.RailDirection:Inbound","dc:title":"上り"},{"owl:sameAs":"odpt.RailDirection:Outbound","dc:title":"下り"}]'
Route 'challenge odpt:TrainType|odpt:operator=odpt.Operator:Sotetsu' '[{"owl:sameAs":"odpt.TrainType:Sotetsu.Local","dc:title":"各停"},{"owl:sameAs":"odpt.TrainType:Sotetsu.Rapid","dc:title":"快速"}]'
Route 'challenge odpt:Station|owl:sameAs=odpt.Station:JR-East.SaikyoKawagoe.Shinjuku' '[{"owl:sameAs":"odpt.Station:JR-East.SaikyoKawagoe.Shinjuku","dc:title":"新宿"}]'
function Row($time,$type,$dest){'{"odpt:departureTime":"'+$time+'","odpt:trainType":"odpt.TrainType:Sotetsu.'+$type+'","odpt:destinationStation":["'+$dest+'"]}'}
$yk='odpt.Station:Sotetsu.Main.Yokohama';$eb='odpt.Station:Sotetsu.Main.Ebina';$sj='odpt.Station:JR-East.SaikyoKawagoe.Shinjuku'
function Table($dir,$cal,$rows){'{"owl:sameAs":"odpt.StationTimetable:Sotetsu.Main.Tennocho.'+$dir+'.'+$cal+'","odpt:operator":"odpt.Operator:Sotetsu","odpt:railway":"odpt.Railway:Sotetsu.Main","odpt:station":"odpt.Station:Sotetsu.Main.Tennocho","odpt:railDirection":"odpt.RailDirection:'+$dir+'","odpt:calendar":"odpt.Calendar:'+$cal+'","odpt:stationTimetableObject":['+($rows -join ',')+']}'}
$tables=@(
 (Table 'Inbound' 'Weekday' @((Row '05:10' 'Local' $yk),(Row '05:21' 'Local' $yk),(Row '23:53' 'Local' $yk),(Row '00:04' 'Local' $yk),(Row '00:13' 'Local' $yk))),
 (Table 'Inbound' 'SaturdayHoliday' @((Row '05:10' 'Local' $yk),(Row '00:13' 'Local' $yk))),
 (Table 'Outbound' 'Weekday' @((Row '05:30' 'Local' $eb),(Row '06:02' 'Rapid' $eb),(Row '06:15' 'Local' $sj))),
 (Table 'Outbound' 'Saturday' @((Row '05:40' 'Local' $eb))),
 (Table 'Outbound' 'SaturdayHoliday' @((Row '05:35' 'Local' $eb),(Row '06:05' 'Rapid' $eb)))
)
Route 'challenge odpt:StationTimetable|odpt:station=odpt.Station:Sotetsu.Main.Tennocho' ('['+($tables -join ',')+']')

function Assert($condition,$message){if(-not $condition){throw $message}}
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Until($condition,$message){$end=[DateTime]::UtcNow.AddSeconds(20);while(-not (& $condition)){if([DateTime]::UtcNow -gt $end){throw "Timeout: $message"};Pump 50}}
$st=$a.GetType('OrbitCalendar.Shell');$shell=$st.GetConstructor($if,$null,[type[]]@([bool]),$null).Invoke([object[]]@($false))
$panel=$st.GetField('panel',$if).GetValue($shell);$pt=$panel.GetType();$root=$panel.Content.Child
function Press($button){$button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function Click($name){Press $root.FindName($name)}
function SaveImage($element,$width,$height,$name){$im=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$im.Render($element);$e=[Windows.Media.Imaging.PngBitmapEncoder]::new();$e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($im));$s=[IO.File]::Create((Join-Path $folder $name));try{$e.Save($s)}finally{$s.Dispose()}}
function Snapshot($name){Pump 250;$panel.UpdateLayout();SaveImage $root 458 ([int]$root.Height) $name}
function Results{@($root.FindName('OdptResults').Children)}
function Inline($tb){[Windows.Documents.TextRange]::new($tb.ContentStart,$tb.ContentEnd).Text}
function Tagged($tag){$found=@();$queue=[Collections.Generic.Queue[object]]::new();$root.FindName('InformationBody').Children|ForEach-Object{$queue.Enqueue($_)};while($queue.Count){$x=$queue.Dequeue();if($x.Tag -eq $tag){$found+=$x};if($x -is [Windows.Controls.Panel]){$x.Children|ForEach-Object{$queue.Enqueue($_)}}elseif($x -is [Windows.Controls.Decorator] -and $x.Child){$queue.Enqueue($x.Child)}elseif($x -is [Windows.Controls.ContentControl] -and $x.Content -is [Windows.UIElement]){$queue.Enqueue($x.Content)}};$found}
try{
 $shell.Open();$pt.GetField('outside',$if).GetValue($panel).Enabled=$false;Pump 1900
 Click 'TransitTab';Pump 300;Click 'Choice3';Pump 1500
 Press @(Tagged 'timetable-add')[0];Pump 1900
 Assert ($pt.GetField('odptMode',$if).GetValue($panel) -and $root.FindName('OdptPanel').Visibility -eq 'Visible' -and $root.FindName('OdptOperators').Children.Count -eq 8) 'New timetable did not open in ODPT mode'
 Assert ($root.FindName('OdptStatus').Text.Contains('未設定')) 'Missing-token hint not shown'
 # Searching without a token opens the token form; tokens are stored encrypted (DPAPI), not as plain text.
 $root.FindName('OdptQuery').Text='天王町';Click 'OdptSearch';Pump 300;Assert ($root.FindName('OdptTokenPanel').Visibility -eq 'Visible') 'Token form did not open'
 Pump 1600;Snapshot 'odpt-tokens.png'
 $root.FindName('OdptChallengeToken').Password='wrong-token';Click 'OdptTokenSave'
 Assert ([IO.File]::Exists($tokenFile) -and -not ([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($tokenFile)).Contains('wrong-token'))) 'Token stored in plain text'
 Click 'OdptSearch';Until {$root.FindName('OdptStatus').Text.Contains('無効')} 'invalid token message'
 Click 'OdptTokenButton';$root.FindName('OdptChallengeToken').Password='test-token';Click 'OdptTokenSave'
 $odpt.GetField('tokens',$sf).SetValue($null,$null);Assert ($odpt.GetMethod('Token',$sf).Invoke($null,@($true)) -eq 'test-token') 'Token did not survive reload'
 # Station search → direction → save.
 Click 'OdptSearch';Until {(Results).Count -ge 1 -and (Results)[0].Tag -eq 'odpt-result'} 'station results'
 Assert ((Results).Count -eq 1 -and (Inline (Results)[0].Content.Children[0]).StartsWith('天王町') -and (Inline (Results)[0].Content.Children[0]).Contains('相鉄本線')) "Station result wrong: $(Inline (Results)[0].Content.Children[0])"
 Press (Results)[0];Until {(Results).Count -eq 2} 'direction choices'
 $labels=@(Results|ForEach-Object {Inline $_.Content.Children[0]});$up=@($labels|Where-Object {$_.StartsWith('横浜方面（上り）')});$down=@($labels|Where-Object {$_.StartsWith('海老名方面（下り）')})
 Assert ($up.Count -eq 1 -and $up[0].Contains('平日 5本') -and $up[0].Contains('土休日 2本')) "Inbound choice wrong: $($labels -join ' / ')"
 Assert ($down.Count -eq 1 -and $down[0].Contains('平日 3本') -and $down[0].Contains('土休日 2本')) "Outbound choice wrong (SaturdayHoliday should win over Saturday): $($labels -join ' / ')"
 $outboundButton=@(Results|Where-Object {(Inline $_.Content.Children[0]).StartsWith('海老名方面')})[0]
 Pump 1500;Snapshot 'odpt-directions.png'
 Press $outboundButton;Click 'SaveTimetable';Pump 1900
 $entry=@($tt.GetProperty('All',$sf).GetValue($null))[0]
 Assert ($entry.FromOdpt -and $entry.ChallengeData -and $entry.OdptDirection -eq 'odpt.RailDirection:Outbound' -and $entry.Direction -eq '海老名方面（下り）' -and $entry.Line -eq '相鉄本線') 'Saved entry wrong'
 Assert ($entry.Weekday -eq "5: 30各停・海老名`n6: 02快速・海老名 15各停・新宿") "Converted text wrong (types, through-service destination): $($entry.Weekday)"
 Assert (@(Tagged 'odpt-notice').Count -eq 1) 'ODPT accuracy notice missing'
 Snapshot 'odpt-timetable.png'
 # After-midnight trains from ODPT land at the end of the service day.
 $inbound="5: 10各停・横浜 21各停・横浜`n23: 53各停・横浜`n0: 04各停・横浜 13各停・横浜"
 $parsed=$tt.GetMethod('Parse',$sf).Invoke($null,@($inbound));Assert ($parsed.Departures[$parsed.Departures.Count-1].Minutes -eq 24*60+13) 'After-midnight conversion wrong'
 # Daily refresh: an old download is fetched again.
 $entry.Fetched=[DateTime]::UtcNow.AddDays(-2).ToString('o');$before=$mock.Requests.Count
 $pt.GetMethod('RefreshOdptTimetables',$if).Invoke($panel,@())|Out-Null;Until {-not $pt.GetField('odptRefreshing',$if).GetValue($panel)} 'refresh'
 Assert ($mock.Requests.Count -gt $before -and ([DateTime]::UtcNow - [DateTime]$entry.FetchedAt).TotalMinutes -lt 5) 'Stale ODPT timetable not refreshed'
 $before=$mock.Requests.Count;$pt.GetMethod('RefreshOdptTimetables',$if).Invoke($panel,@())|Out-Null;Pump 300;Assert ($mock.Requests.Count -eq $before) 'Fresh timetable fetched again'
 # Editing an ODPT entry reopens ODPT mode and saving without changes keeps it.
 Press @(Tagged 'timetable-edit')[0];Pump 900;Assert ($pt.GetField('odptMode',$if).GetValue($panel) -and $root.FindName('OdptStatus').Text.Contains('登録中')) 'Edit did not reopen ODPT mode';Click 'SaveTimetable';Pump 900
 Assert ($tt.GetProperty('All',$sf).GetValue($null).Length -eq 1) 'Unchanged save duplicated the entry'
 # Licence end (2027-03-12): challenge data is deleted and no longer requested.
 $odpt.GetField('ChallengeEnds',$sf).SetValue($null,[DateTime]::Today.AddDays(-1))
 $before=$mock.Requests.Count;$pt.GetMethod('ShowTimetableView',$if).Invoke($panel,@())|Out-Null;Pump 600
 $entry=@($tt.GetProperty('All',$sf).GetValue($null))[0]
 Assert ($null -eq $entry.Weekday -and $null -eq $entry.Holiday -and $mock.Requests.Count -eq $before) 'Expired challenge data not purged'
 $tt.GetMethod('Reload',$sf).Invoke($null,@())|Out-Null;Assert ($null -eq @($tt.GetProperty('All',$sf).GetValue($null))[0].Weekday) 'Purge not saved to disk'
 Assert (([string](Inline @(Tagged 'timetable-card')[0].Child.Children[1])).Contains('利用期間') -or (@(Tagged 'timetable-card')[0].Child.Children[1].Text).Contains('利用期間')) 'Expiry message missing on the card'
 Snapshot 'odpt-expired.png'
 Write-Output 'PASS: ODPT mode default, token form, DPAPI storage, invalid token, station search, directions (上り/下り, SaturdayHoliday preferred), through-service titles, conversion, notice, daily refresh, no duplicate refresh, edit, licence-end purge'
 Write-Output $folder
}finally{$shell.Dispose();$panel.Close();$mock.Dispose()}
