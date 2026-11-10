$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "bin\StartupRegistrationTests.exe"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force (Join-Path $root "bin") | Out-Null
& $csc /nologo /target:exe /out:$output (Join-Path $root "src\VoiceChatLauncher\StartupRegistration.cs") (Join-Path $PSScriptRoot "StartupRegistrationTests.cs")
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }
& $output
if ($LASTEXITCODE -ne 0) { throw "Startup registration tests failed" }
