param([string]$Executable='build/next/OrbitCalendar.exe')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework
$a=[Reflection.Assembly]::LoadFrom((Resolve-Path $Executable));$sf=[Reflection.BindingFlags]'Static,Public,NonPublic';$if=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$folder=Join-Path $env:TEMP ('orbit-assembly-motions-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory $folder|Out-Null
$prefs=$a.GetType('OrbitCalendar.Preferences');$settings=Join-Path $folder 'display.txt';$prefs.GetField('path',$sf).SetValue($null,$settings)
function Assert($ok,$message){if(-not $ok){throw $message}}
foreach($mode in 0..8){
 [IO.File]::WriteAllLines($settings,[string[]]@('2',"$mode",'15','0','4','0'));$prefs.GetMethod('Load',$sf).Invoke($null,@())|Out-Null
 $expected=if($mode -lt 6){6}else{$mode};Assert ($prefs.GetField('Motion',$sf).GetValue($null) -eq $expected) 'Legacy motion migration failed'
 Assert ($prefs.GetField('Pattern',$sf).GetValue($null) -eq 2 -and $prefs.GetField('Minutes',$sf).GetValue($null) -eq 15 -and $prefs.GetField('LoadingStyle',$sf).GetValue($null) -eq 4 -and $prefs.GetField('AnimationSpeed',$sf).GetValue($null) -eq 0) 'Motion migration changed other settings'
}
$prefs.GetField('Pattern',$sf).SetValue($null,0);$prefs.GetField('AnimationSpeed',$sf).SetValue($null,1)
$root=[Windows.Controls.Grid]::new();$root.Width=340;$root.Height=180;$root.Background=[Windows.Media.BrushConverter]::new().ConvertFromString('#0B1923')
$face=[Windows.Controls.Grid]::new();$face.Width=340;$face.Height=180
$stack=[Windows.Controls.StackPanel]::new();$stack.VerticalAlignment='Center';$stack.HorizontalAlignment='Center'
foreach($text in @('W45.2','01   02   03   04   05   06   07','08   09   10   11   12   13   14')){$t=[Windows.Controls.TextBlock]::new();$t.Text=$text;$t.FontFamily=[Windows.Media.FontFamily]::new('Consolas');$t.FontSize=if($text -eq 'W45.2'){42}else{16};$t.Foreground=[Windows.Media.BrushConverter]::new().ConvertFromString('#8FE4DD');$t.HorizontalAlignment='Center';$stack.Children.Add($t)|Out-Null}
$face.Children.Add($stack)|Out-Null;$source=[Windows.Controls.Grid]::new();$source.Opacity=0;$source.Children.Add($face)|Out-Null;$root.Children.Add($source)|Out-Null
$surface=[Windows.Controls.Border]::new();$surface.Background=$root.Background;$root.Children.Insert(0,$surface)
$effect=[Activator]::CreateInstance($a.GetType('OrbitCalendar.AssemblyEffect'),[object[]]@($face,$surface));$root.Children.Add($effect)|Out-Null;$type=$effect.GetType()
$root.Measure([Windows.Size]::new(340,180));$root.Arrange([Windows.Rect]::new(0,0,340,180));$root.UpdateLayout()
function Paint([double]$seconds){$type.GetField('seconds',$if).SetValue($effect,$seconds);$type.GetMethod('Paint',$if).Invoke($effect,@())|Out-Null}
function Distance{$grains=$type.GetField('grains',$if).GetValue($effect);$total=0.0;$count=0;foreach($g in $grains){if($g.Moving){$total+=($g.Current-$g.Target).Length;$count++}};return $total/[Math]::Max(1,$count)}
foreach($mode in @(6,7,8)){
 $prefs.GetField('Motion',$sf).SetValue($null,$mode);$effect.Start($false);$type.GetField('watch',$if).GetValue($effect).Stop()
 Paint 0;$d0=Distance;Paint .4;$d1=Distance;Paint .8;$d2=Distance;Paint 1.65;$end=Distance
 Assert ($d0 -gt $d1 -and $d1 -gt $d2 -and $end -lt .0001) ('Motion does not converge directly to glyphs: '+$mode)
 $encoder=[Windows.Media.Imaging.GifBitmapEncoder]::new()
 foreach($tick in 0..31){
  Paint ($tick*.055);$root.UpdateLayout();$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(340,180,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($root)
  $meta=[Windows.Media.Imaging.BitmapMetadata]::new('gif');$meta.SetQuery('/grctlext/Delay',[UInt16]6);$meta.SetQuery('/grctlext/Disposal',[byte]2)
  $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap,$null,$meta,$null))
 }
 $file=[IO.File]::Create((Join-Path $folder ('motion-'+$mode+'.gif')));try{$encoder.Save($file)}finally{$file.Dispose()}
 $effect.Stop();Write-Output ('PASS: motion '+$mode+' glyph distances '+[Math]::Round($d0)+' -> '+[Math]::Round($d1)+' -> '+[Math]::Round($d2)+' -> 0')
}
Write-Output 'PASS: legacy motion settings migrate to attraction while retaining appearance, timer, loader and speed settings'
Write-Output $folder
