$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'bin\SettingsVisibilityTests.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /out:$output /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'SettingsVisibilityTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
foreach ($style in @('Hidden', 'Normal')) {
    $result = Join-Path $root ('bin\settings-visibility-' + $style + '.txt')
    if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
    $process = Start-Process -FilePath $output -ArgumentList @(('"' + $result + '"'), $style) -WindowStyle $style -PassThru
    if (!$process.WaitForExit(20000)) { $process.Kill(); throw "Settings test timed out: $style" }
    if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $result)) { throw "Settings visibility test failed: $style" }
}

