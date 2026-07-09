$ErrorActionPreference = "Continue"

Write-Host "Voice Chat Launcher diagnostics"
Write-Host ""

Write-Host "ChatGPT Start menu apps:"
try {
    $apps = Get-StartApps | Where-Object {
        $_.Name -like "*ChatGPT*" -or
        $_.AppID -like "*ChatGPT*" -or
        $_.AppID -like "*OpenAI*"
    }

    if (-not $apps) {
        Write-Host "  None found"
    } else {
        foreach ($app in $apps) {
            Write-Host ("  Name:  {0}" -f $app.Name)
            Write-Host ("  AppID: {0}" -f $app.AppID)
            Write-Host ("  Use:   shell:AppsFolder\{0}" -f $app.AppID)
            Write-Host ""
        }
    }
} catch {
    Write-Host ("  Error: {0}" -f $_.Exception.Message)
}

Write-Host ""
Write-Host "ChatGPT app packages:"
try {
    $packages = Get-AppxPackage | Where-Object {
        $_.Name -like "*ChatGPT*" -or
        $_.PackageFamilyName -like "*ChatGPT*" -or
        $_.Publisher -like "*OpenAI*"
    }

    if (-not $packages) {
        Write-Host "  None found"
    } else {
        foreach ($package in $packages) {
            Write-Host ("  Name:    {0}" -f $package.Name)
            Write-Host ("  Family:  {0}" -f $package.PackageFamilyName)
            Write-Host ("  Install: {0}" -f $package.InstallLocation)
            Write-Host ""
        }
    }
} catch {
    Write-Host ("  Error: {0}" -f $_.Exception.Message)
}
