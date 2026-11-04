param(
 [string]$Executable='build/next/OrbitCalendar.exe',
 # Omit this parameter for a download/verification-only check.
 [string]$TargetExecutable
)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$exe=(Resolve-Path $Executable).Path
$a=[Reflection.Assembly]::LoadFrom($exe);$sf=[Reflection.BindingFlags]'Static,Public,NonPublic'
$updates=$a.GetType('OrbitCalendar.AppUpdates');$installer=$a.GetType('OrbitCalendar.UpdateInstaller')
function Assert($ok,$message){if(-not $ok){throw $message}}
function Hash($path){return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Call($type,$name,$arguments=@()){for($i=0;$i -lt $arguments.Count;$i++){if($arguments[$i] -is [Management.Automation.PSObject]){$arguments[$i]=$arguments[$i].PSObject.BaseObject}};return $type.GetMethod($name,$sf).Invoke($null,[object[]]$arguments)}
function Await($task,[int]$seconds=60){$end=[DateTime]::UtcNow.AddSeconds($seconds);while(-not $task.IsCompleted){if([DateTime]::UtcNow -gt $end){throw 'Live request timeout'};Start-Sleep -Milliseconds 50};if($task.IsFaulted){throw $task.Exception.ToString()};if($task.IsCanceled){throw 'Request canceled'};return $task.Result}
$release=Await (Call $updates 'Check' @([Threading.CancellationToken]::None))
Write-Output ('Official release: '+$release.Tag)
$staged=[string](Await (Call $updates 'Download' @($release,$null,[Threading.CancellationToken]::None)) 180)
$work=Split-Path $staged
Assert ((Hash $staged) -eq $release.Sha256) 'Live asset checksum mismatch'
Write-Output ('PASS: official download, size, SHA-256 and assembly identity '+$release.Version)
if(-not $TargetExecutable){Call $updates 'Cleanup' @($work)|Out-Null;exit 0}

$target=(Resolve-Path $TargetExecutable).Path
Assert ($target -ne $exe) 'The test harness must use a separate candidate executable'
$matches=@(Get-CimInstance Win32_Process -Filter "Name='OrbitCalendar.exe'"|Where-Object ExecutablePath -eq $target)
Assert ($matches.Count -eq 1) 'Expected exactly one running app at the specified target path'
$parent=[Diagnostics.Process]::GetProcessById($matches[0].ProcessId)
Assert ($parent.MainModule.FileName -eq $target) 'Parent executable path changed'
$data=Join-Path $env:LOCALAPPDATA 'OrbitCalendar';$before=@{}
foreach($name in @('location.txt','map-zoom.txt','display.txt','watchlist.json')){$file=Join-Path $data $name;$before[$name]=if(Test-Path -LiteralPath $file){Hash $file}else{$null}}
$startupBefore=Get-ItemPropertyValue -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name OrbitCalendar -ErrorAction SilentlyContinue
$handoff=Call $installer 'LaunchForProcess' @($staged,$release,$target,$parent)
Await $handoff 30|Out-Null
$helperPath=Join-Path $work 'OrbitCalendar.Updater.exe'
$helpers=@(Get-CimInstance Win32_Process -Filter "Name='OrbitCalendar.Updater.exe'"|Where-Object ExecutablePath -eq $helperPath)
Assert ($helpers.Count -eq 1) 'Ready updater process not found'
$helper=[Diagnostics.Process]::GetProcessById($helpers[0].ProcessId)
$null=$helper.Handle
# Handoff has validated/staged the replacement. Only the exact recorded old app is stopped.
Assert (-not $parent.HasExited -and $parent.MainModule.FileName -eq $target) 'Old application changed before handoff'
$parent.Kill();Assert ($parent.WaitForExit(5000)) 'Old app did not exit'
Assert ($helper.WaitForExit(35000)) 'Update helper did not finish'
Assert ($helper.ExitCode -eq 0) 'Update failed or rolled back'
Start-Sleep -Milliseconds 1800
Assert ((Hash $target) -eq $release.Sha256) 'Installed file differs from official release'
$running=@(Get-Process OrbitCalendar -ErrorAction SilentlyContinue|Where-Object Path -eq $target)
Assert ($running.Count -eq 1 -and $running[0].Responding) 'Replacement app is not running/responding'
foreach($name in $before.Keys){$file=Join-Path $data $name;$after=if(Test-Path -LiteralPath $file){Hash $file}else{$null};Assert ($after -eq $before[$name]) ('User configuration changed: '+$name)}
$startupAfter=Get-ItemPropertyValue -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name OrbitCalendar -ErrorAction SilentlyContinue
Assert ($startupAfter -eq $startupBefore) 'Startup registration changed'
Assert (@(Get-ChildItem -LiteralPath (Split-Path $target) -Force -Filter '.OrbitCalendar.update-*').Count -eq 0) 'Installer left adjacent temporary/backup files'
Write-Output ('PASS: official live update and restart, PID '+$running[0].Id+', version '+[Reflection.AssemblyName]::GetAssemblyName($target).Version)
Write-Output 'PASS: real saved locations, zoom, display, watchlist and startup registration unchanged'
