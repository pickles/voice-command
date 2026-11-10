$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "bin\ApplicationMenuTests.exe"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$gac = Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL"
$client = Get-ChildItem (Join-Path $gac "UIAutomationClient") -Recurse -Filter UIAutomationClient.dll | Select-Object -First 1 -ExpandProperty FullName
$types = Get-ChildItem (Join-Path $gac "UIAutomationTypes") -Recurse -Filter UIAutomationTypes.dll | Select-Object -First 1 -ExpandProperty FullName
$windowsBase = Get-ChildItem (Join-Path $gac "WindowsBase") -Recurse -Filter WindowsBase.dll | Select-Object -First 1 -ExpandProperty FullName
& $csc /nologo /target:exe /out:$output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:$client /reference:$types /reference:$windowsBase (Join-Path $PSScriptRoot "ApplicationMenuTests.cs")
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }
& $output
if ($LASTEXITCODE -ne 0) { throw "Application menu tests failed" }

