$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src\VoiceChatLauncher\Program.cs"
$outDir = Join-Path $root "bin"
$outExe = Join-Path $outDir "VoiceChatLauncher.exe"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$gac = Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL"
$systemSpeech = Get-ChildItem (Join-Path $gac "System.Speech") -Recurse -Filter "System.Speech.dll" | Select-Object -First 1 -ExpandProperty FullName
$uiAutomationClient = Get-ChildItem (Join-Path $gac "UIAutomationClient") -Recurse -Filter "UIAutomationClient.dll" | Select-Object -First 1 -ExpandProperty FullName
$uiAutomationTypes = Get-ChildItem (Join-Path $gac "UIAutomationTypes") -Recurse -Filter "UIAutomationTypes.dll" | Select-Object -First 1 -ExpandProperty FullName
$windowsBase = Get-ChildItem (Join-Path $gac "WindowsBase") -Recurse -Filter "WindowsBase.dll" | Select-Object -First 1 -ExpandProperty FullName

if (-not (Test-Path $csc)) {
    throw "C# compiler was not found: $csc"
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $csc `
    /nologo `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /out:$outExe `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:$systemSpeech `
    /reference:$uiAutomationClient `
    /reference:$uiAutomationTypes `
    /reference:$windowsBase `
    $src

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$configPath = Join-Path $outDir "config.ini"
if (-not (Test-Path $configPath)) {
    Copy-Item -Force (Join-Path $root "config.example.ini") $configPath
}

Write-Host "Built $outExe"
