$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$target = Join-Path $PSScriptRoot 'OpenAITextShortcuts.exe'
$sources = @('Core.cs','CopilotGesture.cs','Keyboard.cs','ClipboardAccess.cs','SelectionCapture.cs','AutoPaste.cs','StartupRegistration.cs','App.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:winexe /optimize+ /utf8output /codepage:65001 /warnaserror+ "/out:$target" /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll "/reference:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationClient.dll" "/reference:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationTypes.dll" "/reference:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $target"
