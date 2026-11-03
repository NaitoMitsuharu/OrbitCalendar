$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$out = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$references = @('PresentationCore', 'PresentationFramework', 'WindowsBase', 'UIAutomationClient', 'UIAutomationTypes') | ForEach-Object {
    '/reference:' + (Join-Path $framework "WPF\$_.dll")
}
$references += '/reference:System.Device.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Net.Http.dll', '/reference:System.Xaml.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll'
$source = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /optimize+ /codepage:65001 "/out:$out\OrbitCalendar.exe" "/win32manifest:$PSScriptRoot\src\app.manifest" "/win32icon:$PSScriptRoot\src\orbit.ico" "/resource:$PSScriptRoot\src\Calendar.xaml,Calendar.xaml" "/resource:$PSScriptRoot\src\holidays.csv,holidays.csv" "/resource:$PSScriptRoot\src\earth.bin,earth.bin" @references @source
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "$out\OrbitCalendar.exe"
