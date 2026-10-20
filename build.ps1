$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework C# 编译器。' }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 '/reference:System.Windows.Forms.dll' '/reference:System.Drawing.dll' "/reference:$PSScriptRoot\dnlib.dll" "/resource:$PSScriptRoot\dnlib.dll,Embedded.dnlib.dll" "/out:$PSScriptRoot\PCL原版下载恢复工具.exe" "$PSScriptRoot\PclDownloadRestore.cs" "$PSScriptRoot\AdaptivePatcher.cs"
if ($LASTEXITCODE -ne 0) { throw '工具编译失败。' }
