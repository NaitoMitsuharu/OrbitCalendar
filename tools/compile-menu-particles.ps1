$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
public static class MenuShaderCompiler {
 [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Blob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] UIntPtr GetBufferSize(); }
 [DllImport("d3dcompiler_47.dll", CallingConvention=CallingConvention.StdCall)]
 static extern int D3DCompile(byte[] source, UIntPtr length, [MarshalAs(UnmanagedType.LPStr)] string name, IntPtr defines, IntPtr include, [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string profile, uint flags, uint flags2, out IntPtr code, out IntPtr errors);
 static byte[] Read(IntPtr ptr) {
  var blob=(Blob)Marshal.GetObjectForIUnknown(ptr);
  try { var bytes=new byte[(int)blob.GetBufferSize().ToUInt64()]; Marshal.Copy(blob.GetBufferPointer(),bytes,0,bytes.Length); return bytes; }
  finally { Marshal.ReleaseComObject(blob); }
 }
 public static void Compile(string source, string target) {
  var bytes=Encoding.UTF8.GetBytes(File.ReadAllText(source)); IntPtr code, errors;
  int result=D3DCompile(bytes,(UIntPtr)bytes.Length,source,IntPtr.Zero,IntPtr.Zero,"main","ps_2_0",0,0,out code,out errors);
  try { if(result<0) throw new InvalidOperationException(errors!=IntPtr.Zero?Encoding.UTF8.GetString(Read(errors)):"Shader compile failed"); File.WriteAllBytes(target,Read(code)); }
  finally { if(code!=IntPtr.Zero)Marshal.Release(code); if(errors!=IntPtr.Zero)Marshal.Release(errors); }
 }
}
'@
[MenuShaderCompiler]::Compile((Join-Path (Get-Location) 'src\menu-particles.hlsl'),(Join-Path (Get-Location) 'src\menu-particles.ps'))
Write-Output 'Compiled embedded PS 2.0 particle menu shader (hardware/software compatible).'
