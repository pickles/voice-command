$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$python = Join-Path $root ".venv\Scripts\python.exe"
$cache = Join-Path $root ".pip-cache"

if (-not (Test-Path $python)) {
    python -m venv (Join-Path $root ".venv")
}

$env:PIP_CACHE_DIR = $cache
& $python -m pip install --upgrade pip
& $python -m pip install openwakeword sounddevice numpy

@"
import openwakeword.utils
openwakeword.utils.download_models()
print("OpenWakeWord is ready.")
"@ | & $python -
