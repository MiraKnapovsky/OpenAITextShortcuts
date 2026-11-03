$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$target = Join-Path $PSScriptRoot 'Tests.exe'
$sources = @('Core.cs','CopilotGesture.cs','ClipboardAccess.cs','SelectionCapture.cs','Tests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:exe /utf8output /codepage:65001 /warnaserror+ "/out:$target" /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& $target
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
