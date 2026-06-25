$ErrorActionPreference = "Continue"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-SafeName($element) {
    try {
        return $element.Current.Name
    } catch {
        return ""
    }
}

function Get-SafeAutomationId($element) {
    try {
        return $element.Current.AutomationId
    } catch {
        return ""
    }
}

function Get-SafeClassName($element) {
    try {
        return $element.Current.ClassName
    } catch {
        return ""
    }
}

function Get-SafeControlType($element) {
    try {
        return $element.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")
    } catch {
        return ""
    }
}

function Get-SafeProcessName($element) {
    try {
        $processId = $element.Current.ProcessId
        if ($processId -gt 0) {
            return (Get-Process -Id $processId -ErrorAction SilentlyContinue).ProcessName
        }
    } catch {
    }

    return ""
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$windows = $root.FindAll(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.Condition]::TrueCondition
)

$chatWindows = @()
foreach ($window in $windows) {
    $name = Get-SafeName $window
    $processName = Get-SafeProcessName $window
    if ($name -like "*ChatGPT*" -or $processName -like "*ChatGPT*" -or $processName -like "*OpenAI*") {
        $chatWindows += $window
    }
}

if ($chatWindows.Count -eq 0) {
    Write-Host "No ChatGPT-like windows found. Open ChatGPT first, then run this script again."
    exit 0
}

foreach ($window in $chatWindows) {
    Write-Host ""
    Write-Host ("Window: Name='{0}' Process='{1}' Class='{2}'" -f (Get-SafeName $window), (Get-SafeProcessName $window), (Get-SafeClassName $window))

    $descendants = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition
    )

    $count = 0
    foreach ($item in $descendants) {
        $controlType = Get-SafeControlType $item
        $name = Get-SafeName $item
        $automationId = Get-SafeAutomationId $item
        $className = Get-SafeClassName $item

        if ($controlType -eq "Button" -or $name -match "音声|voice|mic|microphone|dictat|入力") {
            Write-Host ("  {0}: Name='{1}' AutomationId='{2}' Class='{3}'" -f $controlType, $name, $automationId, $className)
            $count++
        }
    }

    if ($count -eq 0) {
        Write-Host "  No named buttons or voice-like elements were visible through UI Automation."
    }
}
