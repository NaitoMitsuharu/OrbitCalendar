$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sources=@('tools/verify-update-monitor.cs','src/UpdateMonitor.cs','src/AppUpdates.cs')|ForEach-Object{(Resolve-Path $_).Path}
New-Item -ItemType Directory -Path build -Force|Out-Null
$test=Join-Path (Get-Location) 'build\verify-update-monitor.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe ("/out:"+$test) /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll (('/reference:')+(Join-Path $framework 'WPF\WindowsBase.dll')) @sources
if($LASTEXITCODE -ne 0){throw 'Monitor test build failed'}
& $test
if($LASTEXITCODE -ne 0){throw 'Monitor test failed'}
