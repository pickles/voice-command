$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$models = Join-Path $root "models"
$packages = Join-Path $root "packages"
$dependencyProject = Join-Path $root "src\VoiceChatLauncher\VoiceChatLauncher.Dependencies.csproj"
$nugetConfig = Join-Path $root "NuGet.config"

New-Item -ItemType Directory -Force -Path $models | Out-Null

& dotnet restore $dependencyProject --packages $packages --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) {
    throw "Dependency restore failed with exit code $LASTEXITCODE"
}

function Save-Model($FileName) {
    $target = Join-Path $models $FileName
    if (Test-Path $target) {
        return
    }

    $url = "https://github.com/dscripka/openWakeWord/releases/download/v0.5.1/$FileName"
    Write-Host "Downloading $FileName"
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $target
}

Save-Model "melspectrogram.onnx"
Save-Model "embedding_model.onnx"

Write-Host "OpenWakeWord C# runtime is ready."
