param([string]$Executable='build/next/OrbitCalendar.exe',[switch]$Interactive)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('PresentationCore','PresentationFramework','WindowsBase')|ForEach-Object{'/reference:'+(Join-Path $framework ('WPF\'+$_+'.dll'))}
$fixture=Join-Path (Get-Location) 'build\ClockMenuFixture.exe'
& (Join-Path $framework 'csc.exe') /nologo /codepage:65001 /target:winexe ("/out:"+$fixture) /win32manifest:src\app.manifest /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Device.dll @references (Resolve-Path tools/ClockMenuFixture.cs).Path
if($LASTEXITCODE -ne 0){throw 'Clock fixture build failed'}
$arguments='"'+(Resolve-Path $Executable).Path+'"'+$(if(-not $Interactive){' --verify'}else{''})
$process=Start-Process -FilePath $fixture -ArgumentList $arguments -WindowStyle Hidden -PassThru
if($Interactive){Write-Output ('Interactive fixture PID '+$process.Id);exit 0}
if(-not $process.WaitForExit(45000)){if($process.MainModule.FileName -eq $fixture){$process.Kill()};throw 'Clock verification timeout'}
if($process.ExitCode -ne 0){Get-Content build/ClockMenuFixture.error.txt;throw 'Clock verification failed'}
Get-Content build/ClockMenuFixture.pass.txt
