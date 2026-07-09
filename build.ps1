$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root "src\VoiceChatLauncher"
$dependencyProject = Join-Path $root "src\VoiceChatLauncher\VoiceChatLauncher.Dependencies.csproj"
$packagesDir = Join-Path $root "packages"
$outDir = Join-Path $root "bin"
$outExe = Join-Path $outDir "VoiceChatLauncher.exe"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$gac = Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL"
$uiAutomationClient = Get-ChildItem (Join-Path $gac "UIAutomationClient") -Recurse -Filter "UIAutomationClient.dll" | Select-Object -First 1 -ExpandProperty FullName
$uiAutomationTypes = Get-ChildItem (Join-Path $gac "UIAutomationTypes") -Recurse -Filter "UIAutomationTypes.dll" | Select-Object -First 1 -ExpandProperty FullName
$windowsBase = Get-ChildItem (Join-Path $gac "WindowsBase") -Recurse -Filter "WindowsBase.dll" | Select-Object -First 1 -ExpandProperty FullName
$netstandard = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\netstandard.dll"
$onnxRuntimeManaged = Join-Path $packagesDir "microsoft.ml.onnxruntime.managed\1.18.1\lib\netstandard2.0\Microsoft.ML.OnnxRuntime.dll"
$onnxRuntimeNative = Join-Path $packagesDir "microsoft.ml.onnxruntime\1.18.1\runtimes\win-x64\native\onnxruntime.dll"
$onnxRuntimeProviders = Join-Path $packagesDir "microsoft.ml.onnxruntime\1.18.1\runtimes\win-x64\native\onnxruntime_providers_shared.dll"
$systemMemory = Join-Path $packagesDir "system.memory\4.5.5\lib\net461\System.Memory.dll"
$systemBuffers = Join-Path $packagesDir "system.buffers\4.5.1\lib\net461\System.Buffers.dll"
$systemUnsafe = Join-Path $packagesDir "system.runtime.compilerservices.unsafe\4.5.3\lib\net461\System.Runtime.CompilerServices.Unsafe.dll"
$sources = Get-ChildItem $srcDir -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notlike "*\obj\*" } |
    Sort-Object FullName |
    Select-Object -ExpandProperty FullName

if (-not (Test-Path $csc)) {
    throw "C# compiler was not found: $csc"
}

if (-not (Test-Path $onnxRuntimeManaged) -or -not (Test-Path $onnxRuntimeNative)) {
    & dotnet restore $dependencyProject --packages $packagesDir --configfile (Join-Path $root "NuGet.config")
    if ($LASTEXITCODE -ne 0) {
        throw "Dependency restore failed with exit code $LASTEXITCODE"
    }
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
    /reference:$netstandard `
    /reference:$onnxRuntimeManaged `
    /reference:$systemMemory `
    /reference:$systemBuffers `
    /reference:$systemUnsafe `
    /reference:$uiAutomationClient `
    /reference:$uiAutomationTypes `
    /reference:$windowsBase `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$configPath = Join-Path $outDir "config.ini"
if (-not (Test-Path $configPath)) {
    Copy-Item -Force (Join-Path $root "config.example.ini") $configPath
}

Copy-Item -Force $onnxRuntimeManaged $outDir
Copy-Item -Force $onnxRuntimeNative $outDir
Copy-Item -Force $systemMemory $outDir
Copy-Item -Force $systemBuffers $outDir
Copy-Item -Force $systemUnsafe $outDir
if (Test-Path $onnxRuntimeProviders) {
    Copy-Item -Force $onnxRuntimeProviders $outDir
}

Write-Host "Built $outExe"
