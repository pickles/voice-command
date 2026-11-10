$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "bin\NewWindowOptionTests.exe"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:exe /out:$output (Join-Path $PSScriptRoot "NewWindowOptionTests.cs")
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }
& $output
if ($LASTEXITCODE -ne 0) { throw "New window option tests failed" }
