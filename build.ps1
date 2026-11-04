param(
    [string]$OutputDirectory,
    [string]$BuildVersion
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
if (-not $BuildVersion) { $BuildVersion = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'version.txt') -Raw).Trim() }
if ($BuildVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid build version.' }
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot 'build' }
New-Item -ItemType Directory -Path $out -Force | Out-Null
$references = @('PresentationCore', 'PresentationFramework', 'WindowsBase', 'UIAutomationClient', 'UIAutomationTypes') | ForEach-Object {
    '/reference:' + (Join-Path $framework "WPF\$_.dll")
}
$references += '/reference:System.Device.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Net.Http.dll', '/reference:System.Xaml.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll'
$versionSource = Join-Path $out 'Version.g.cs'
$versionText = "[assembly: System.Reflection.AssemblyVersion(""$BuildVersion.0"")]`r`n[assembly: System.Reflection.AssemblyFileVersion(""$BuildVersion.0"")]`r`n[assembly: System.Reflection.AssemblyInformationalVersion(""$BuildVersion"")]"
[IO.File]::WriteAllText($versionSource, $versionText, [Text.UTF8Encoding]::new($false))
$source = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /optimize+ /codepage:65001 "/out:$out\OrbitCalendar.exe" "/win32manifest:$PSScriptRoot\src\app.manifest" "/win32icon:$PSScriptRoot\src\orbit.ico" "/resource:$PSScriptRoot\src\Calendar.xaml,Calendar.xaml" "/resource:$PSScriptRoot\src\menu-particles.ps,menu-particles.ps" "/resource:$PSScriptRoot\src\holidays.csv,holidays.csv" "/resource:$PSScriptRoot\src\earth.bin,earth.bin" "/resource:$PSScriptRoot\src\instruments.tsv,instruments.tsv" "/resource:$PSScriptRoot\src\cities.csv,cities.csv" "/resource:$PSScriptRoot\src\municipalities.tsv,municipalities.tsv" "/resource:$PSScriptRoot\src\prefectures.bin,prefectures.bin" @references @source $versionSource
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "$out\OrbitCalendar.exe"
