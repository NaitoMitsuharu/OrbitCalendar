param([string]$Executable = 'build/next/OrbitCalendar.exe')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName System.Web.Extensions
$exe=(Resolve-Path $Executable).Path
$a=[Reflection.Assembly]::LoadFrom($exe);$sf=[Reflection.BindingFlags]'Static,Public,NonPublic'
$updates=$a.GetType('OrbitCalendar.AppUpdates');$installer=$a.GetType('OrbitCalendar.UpdateInstaller')
$folder=Join-Path $env:TEMP ('orbit-update-tests-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $folder|Out-Null
$json=[Web.Script.Serialization.JavaScriptSerializer]::new()
function Assert($ok,$message){if(-not $ok){throw $message}}
function Hash($path){return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Call($type,$name,$arguments=@()){for($i=0;$i -lt $arguments.Count;$i++){if($arguments[$i] -is [Management.Automation.PSObject]){$arguments[$i]=$arguments[$i].PSObject.BaseObject}};return $type.GetMethod($name,$sf).Invoke($null,[object[]]$arguments)}
function Reject($action,$message){$rejected=$false;try{& $action|Out-Null}catch{$rejected=$true};Assert $rejected $message}
function Fake($directory,$version,$health){
 New-Item -ItemType Directory $directory -Force|Out-Null
 $code=@'
using System;using System.IO;using System.Threading;
[assembly:System.Reflection.AssemblyVersion("VERSION.0")]
class Fake { static void Main(string[] args){
 string dir=AppDomain.CurrentDomain.BaseDirectory;
 if(Array.IndexOf(args,"--parent")>=0){File.WriteAllText(Path.Combine(dir,"parent.ready"),"ready");while(!File.Exists(Path.Combine(dir,"exit")))Thread.Sleep(40);return;}
 File.WriteAllText(Path.Combine(dir,"boot-VERSION"),"started");
 int i=Array.IndexOf(args,"--update-health");
 if(i>=0 && HEALTH){using(var e=EventWaitHandle.OpenExisting(args[i+1]))e.Set();Thread.Sleep(500);}
 }}
'@
 $code=$code.Replace('VERSION',$version).Replace('HEALTH',$health.ToString().ToLowerInvariant())
 $cs=Join-Path $directory 'fake.cs';[IO.File]::WriteAllText($cs,$code)
 & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe ("/out:"+(Join-Path $directory 'OrbitCalendar.exe')) $cs
 Assert ($LASTEXITCODE -eq 0) 'Fake build failed'
 return (Join-Path $directory 'OrbitCalendar.exe')
}
function WaitFile($path){$end=[DateTime]::UtcNow.AddSeconds(10);while(-not(Test-Path -LiteralPath $path)){if([DateTime]::UtcNow -gt $end){throw ('File timeout '+$path)};Start-Sleep -Milliseconds 50}}
# Validate release metadata before accepting a download location or executable.
$asset=@{name='OrbitCalendar.exe';state='uploaded';size=2048;digest=('sha256:'+('a'*64));browser_download_url='https://github.com/NaitoMitsuharu/OrbitCalendar/releases/download/v9.0.0/OrbitCalendar.exe'}
$release=@{tag_name='v9.0.0';name='test';body='release notes';draft=$false;prerelease=$false;assets=@($asset)}
$r=Call $updates 'Parse' @($json.Serialize($release));Assert ($r.IsNew -and $r.Version.ToString() -eq '9.0.0.0') 'New version comparison failed'
$release.prerelease=$true;Reject {Call $updates 'Parse' @($json.Serialize($release))} 'Prerelease accepted';$release.prerelease=$false
$release.draft=$true;Reject {Call $updates 'Parse' @($json.Serialize($release))} 'Draft accepted';$release.draft=$false
$asset.browser_download_url='https://example.com/OrbitCalendar.exe';Reject {Call $updates 'Parse' @($json.Serialize($release))} 'Untrusted download URL accepted';$asset.browser_download_url='https://github.com/NaitoMitsuharu/OrbitCalendar/releases/download/v9.0.0/OrbitCalendar.exe'
$asset.digest=$null;Reject {Call $updates 'Parse' @($json.Serialize($release))} 'Missing digest accepted';$asset.digest='sha256:'+('a'*64)
$asset.size=1000000000;Reject {Call $updates 'Parse' @($json.Serialize($release))} 'Oversize asset accepted';$asset.size=2048
$release.tag_name='v1.26.0';$asset.browser_download_url='https://github.com/NaitoMitsuharu/OrbitCalendar/releases/download/v1.26.0/OrbitCalendar.exe';$r=Call $updates 'Parse' @($json.Serialize($release));Assert (-not $r.IsNew) 'Equal version offers reinstall'
Reject {Call $updates 'Verify' @($exe,('a'*64),[version]'1.26.0.0')} 'Bad checksum accepted'
Reject {Call $updates 'Verify' @($exe,(Hash $exe),[version]'2.0.0.0')} 'Wrong assembly version accepted'
Assert (-not(Call $updates 'IsWorkDirectory' @($folder))) 'Unsafe cleanup directory accepted'
Write-Output 'PASS: release metadata, stable versions, fixed repository URL, digest, size, equal version and executable verification'
$startupBefore=Get-ItemPropertyValue -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name OrbitCalendar -ErrorAction SilentlyContinue
foreach($scenario in @('success','launcher','rollback','bad-hash','wrong-parent','locked-target')){
 $targetDir=Join-Path $folder $scenario;$target=Fake $targetDir '1.25.0' $true
 $stageSource=Fake (Join-Path $folder ('stage-'+$scenario)) '1.26.0' ($scenario -ne 'rollback')
 $work=Join-Path $env:TEMP ('OrbitCalendar-update-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $work|Out-Null
 $staged=Join-Path $work 'OrbitCalendar.new.exe';Copy-Item -LiteralPath $stageSource -Destination $staged
 $helper=Join-Path $work 'OrbitCalendar.Updater.exe';Copy-Item -LiteralPath $exe -Destination $helper
 foreach($file in @('location.txt','map-zoom.txt','display.txt','watchlist.json')){[IO.File]::WriteAllText((Join-Path $targetDir $file),('keep:'+ $file))}
 $oldHash=Hash $target;$fixtureHashes=@{};Get-ChildItem -LiteralPath $targetDir -File|Where-Object Extension -ne '.exe'|ForEach-Object{$fixtureHashes[$_.FullName]=Hash $_.FullName}
 $parent=Start-Process -FilePath $target -ArgumentList '--parent' -WindowStyle Hidden -PassThru
 WaitFile (Join-Path $targetDir 'parent.ready')
 $id=[guid]::NewGuid().ToString('N');$readyName='Local\OrbitCalendar.UpdateReady.'+$id
 $plan=@{Target=$target;Staged=$staged;Sha256=(Hash $staged);Version='1.26.0.0';OriginalSha256=$oldHash;ParentPid=$parent.Id;ParentStart=$parent.StartTime.ToUniversalTime().Ticks;ReadyEvent=$readyName;HealthEvent=('Local\OrbitCalendar.UpdateHealthy.'+$id);HealthSeconds=2}
 if($scenario -eq 'bad-hash'){$plan.Sha256='a'*64}
 if($scenario -eq 'wrong-parent'){$plan.ParentStart++}
 $lock=$null;if($scenario -eq 'locked-target'){$lock=[IO.File]::Open($target,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)}
 $ready=[Threading.EventWaitHandle]::new($false,[Threading.EventResetMode]::ManualReset,$readyName)
 $path=Join-Path $work 'plan.json';[IO.File]::WriteAllText($path,($plan | ConvertTo-Json -Compress))
 if($scenario -eq 'launcher'){
  $offer=[Activator]::CreateInstance($a.GetType('OrbitCalendar.AppUpdates+Release'));$offer.Tag='v1.26.0';$offer.Version=[version]'1.26.0.0';$offer.Sha256=Hash $staged;$offer.Size=(Get-Item -LiteralPath $staged).Length;$offer.DownloadUrl='https://github.com/NaitoMitsuharu/OrbitCalendar/releases/download/v1.26.0/OrbitCalendar.exe'
  $launch=Call $installer 'LaunchForProcess' @($staged,$offer,$target,$parent)
  $end=[DateTime]::UtcNow.AddSeconds(25);while(-not $launch.IsCompleted){if([DateTime]::UtcNow -gt $end){throw 'Launcher readiness timeout'};Start-Sleep -Milliseconds 50}
  Assert (-not $launch.IsFaulted -and -not $launch.IsCanceled) ('Launcher failed: '+$launch.Exception)
  $child=@(Get-CimInstance Win32_Process -Filter "Name='OrbitCalendar.Updater.exe'"|Where-Object ExecutablePath -eq $helper);Assert ($child.Count -eq 1) 'Launched helper not found'
  $worker=[Diagnostics.Process]::GetProcessById($child[0].ProcessId)
  $null=$worker.Handle
 }else{$worker=Start-Process -FilePath $helper -ArgumentList ('--apply-update "'+$path+'"') -WindowStyle Hidden -PassThru}
 try{
  if($scenario -ne 'launcher'){Assert ($ready.WaitOne(15000)) ('Helper readiness timeout: '+$scenario)}
  $status=$json.DeserializeObject([IO.File]::ReadAllText((Join-Path $work 'ready.json')))
  if($scenario -in @('bad-hash','wrong-parent')){
   Assert (-not $status.Success -and -not $parent.HasExited -and (Hash $target) -eq $oldHash) ('Invalid update modified/exited parent: '+$scenario)
   [IO.File]::WriteAllText((Join-Path $targetDir 'exit'),'exit')
  }else{
   Assert $status.Success ('Helper refused: '+$status.Message)
   [IO.File]::WriteAllText((Join-Path $targetDir 'exit'),'exit')
  }
  Assert ($worker.WaitForExit(20000)) ('Helper completion timeout: '+$scenario)
  Assert ($parent.WaitForExit(5000)) 'Old process did not exit'
  if($scenario -in @('success','launcher')){
   Assert ($worker.ExitCode -eq 0 -and (Hash $target) -eq (Hash $staged)) ('Successful update did not replace executable; helper exit='+$worker.ExitCode)
   WaitFile (Join-Path $targetDir 'boot-1.26.0')
   $result=$json.DeserializeObject([IO.File]::ReadAllText((Join-Path $work 'result.json')));Assert $result.Success 'Success result absent'
  }elseif($scenario -in @('rollback','locked-target')){
   Assert ($worker.ExitCode -eq 1 -and (Hash $target) -eq $oldHash) 'Failed update did not retain/restore original'
   WaitFile (Join-Path $targetDir 'boot-1.25.0')
   $result=$json.DeserializeObject([IO.File]::ReadAllText((Join-Path $work 'result.json')));Assert (-not $result.Success) 'Failure result absent'
  }
  foreach($file in $fixtureHashes.Keys){Assert ((Hash $file) -eq $fixtureHashes[$file]) ('Configuration changed: '+$file)}
  Assert (@(Get-ChildItem -LiteralPath $targetDir -Force -Filter '.OrbitCalendar.update-*').Count -eq 0) 'Adjacent update/backup files leaked'
  Write-Output ('PASS: '+$scenario+' transaction, original process identity, settings unchanged and owned-file cleanup')
 }finally{
  if($lock){$lock.Dispose()};$ready.Dispose()
  # Only stop the exact child processes created by this test, after checking their path.
  if(-not $parent.HasExited -and $parent.MainModule.FileName -eq $target){$parent.Kill();$parent.WaitForExit(5000)}
  if(-not $worker.HasExited -and $worker.MainModule.FileName -eq $helper){$worker.Kill();$worker.WaitForExit(5000)}
  $parent.Dispose();$worker.Dispose();Call $updates 'Cleanup' @($work)|Out-Null
 }
}
$startupAfter=Get-ItemPropertyValue -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name OrbitCalendar -ErrorAction SilentlyContinue
Assert ($startupBefore -eq $startupAfter) 'Actual startup registration changed'
Write-Output 'PASS: real startup registration unchanged'
Write-Output $folder
