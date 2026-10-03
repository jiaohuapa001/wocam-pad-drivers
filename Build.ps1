param([switch]$Package)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=Join-Path $root 'data\build'
$null=New-Item -ItemType Directory -Force -Path $out
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path $csc)) { throw '.NET Framework compiler not found.' }
Add-Type -AssemblyName System.Drawing
# Code-native pen nib icon; no external artwork or dependencies.
$bitmap=New-Object Drawing.Bitmap(64,64)
$g=[Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([Drawing.Color]::Transparent)
$blue=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(29,92,147))
$white=New-Object Drawing.SolidBrush([Drawing.Color]::White)
$g.FillEllipse($blue,1,1,62,62)
$points=[Drawing.Point[]]@((New-Object Drawing.Point(19,44)),(New-Object Drawing.Point(25,23)),(New-Object Drawing.Point(42,17)),(New-Object Drawing.Point(47,22)),(New-Object Drawing.Point(40,39)))
$g.FillPolygon($white,$points)
$line=New-Object Drawing.Pen([Drawing.Color]::FromArgb(29,92,147),3)
$g.DrawLine($line,20,44,35,29);$g.FillEllipse($blue,32,26,6,6)
$g.DrawLine([Drawing.Pens]::White,20,49,45,49)
$png=New-Object IO.MemoryStream
$bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)
$bytes=$png.ToArray()
$ico=Join-Path $out 'WacomLite.ico'
$stream=[IO.File]::Create($ico);$writer=New-Object IO.BinaryWriter($stream)
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]1)
$writer.Write([byte]64);$writer.Write([byte]64);$writer.Write([byte]0);$writer.Write([byte]0)
$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$bytes.Length);$writer.Write([uint32]22);$writer.Write($bytes)
$writer.Dispose();$png.Dispose();$line.Dispose();$blue.Dispose();$white.Dispose();$g.Dispose();$bitmap.Dispose()
$sources=@('Core.cs','Ink.cs','App.cs','Tests.cs','AssemblyInfo.cs') | ForEach-Object {Join-Path $root "src\$_"}
$exe=Join-Path $root 'WacomLite.exe'
& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$exe" "/win32icon:$ico" "/win32manifest:$root\src\app.manifest" "/resource:$root\src\fast-writing.json,fast-writing.json" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Xml.dll $sources
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
if($Package) {
    Compress-Archive -LiteralPath $exe,(Join-Path $root 'README.md') -Destination (Join-Path $out 'WacomLite-portable.zip') -Force
}
Write-Output $exe
