# Drives the real window through UI Automation and checks every step with real connections:
# add a program, get offered its online helper, switch it off and on, allow all, close the
# window, and find the rule still enforced. Needs administrator rights, an internet connection
# and a desktop, and an English Windows, because it finds buttons by their text.
# It wipes every NetMute rule at the end, so it refuses to run if there are any.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-ui.ps1 -Exe bin\NetMute.exe
param(
    [Parameter(Mandatory)][string]$Exe,
    [string]$Screenshot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$Exe = (Resolve-Path $Exe).Path
$folder = Join-Path ([IO.Path]::GetTempPath()) ("NetMute-ui-test-" + $PID)
New-Item -ItemType Directory -Path $folder | Out-Null
$subject = Join-Path $folder 'curl-subject.exe'
$helper = Join-Path $folder 'curl-helper.exe'
Copy-Item (Join-Path $env:WINDIR 'System32\curl.exe') $subject
Copy-Item (Join-Path $env:WINDIR 'System32\curl.exe') $helper

$urls = 'http://www.msftconnecttest.com/connecttest.txt', 'https://www.baidu.com/', 'https://example.com/'
$failed = $false

function Assert-That([string]$Name, [bool]$Passed) {
    Write-Output ("[{0}] {1}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name)
    if (-not $Passed) { $script:failed = $true }
}

function Wait-Until([scriptblock]$Condition, [int]$Seconds = 20) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $result = & $Condition
        if ($result) { return $result }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Test-Online([string]$Url) {
    $code = & $subject -sS --noproxy '*' -o NUL -w '%{http_code}' --connect-timeout 8 --max-time 20 $Url 2>$null
    $LASTEXITCODE -eq 0 -and "$code" -match '^[23]'
}

function Get-BlockedList {
    $file = Join-Path $folder 'list.txt'
    Start-Process -FilePath $Exe -ArgumentList 'list' -Wait -RedirectStandardOutput $file
    Get-Content $file -Raw
}

function Find-Element($Parent, [string]$Name) {
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::NameProperty, $Name)
    $Parent.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

function Get-Windows([int]$ProcessId) {
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId)
    [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $condition)
}

function Invoke-Button($Button) {
    $Button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# A list row carries its check box either itself or as a child, depending on the Windows version.
function Get-RowToggle($Row) {
    $pattern = $null
    if ($Row.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) { return $pattern }
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::IsTogglePatternAvailableProperty, $true)
    $box = $Row.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($box) { return $box.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern) }
    throw 'The list row has no check box.'
}

function Save-Picture($Window, [string]$Path) {
    $r = $Window.Current.BoundingRectangle
    $picture = New-Object Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [Drawing.Graphics]::FromImage($picture)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $picture.Size)
    $g.Dispose()
    $picture.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    $picture.Dispose()
}

if ((Get-BlockedList) -match '\\') {
    Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
    throw 'This machine has NetMute rules, and this test would wipe them. Run it on a test machine.'
}

$url = $urls | Where-Object { Test-Online $_ } | Select-Object -First 1
if (-not $url) { throw 'The test program cannot reach any test site before blocking.' }

$app = $null
$helperProcess = $null
try {
    # The helper plays steamwebhelper.exe: same folder, and online while its program is added.
    $download = 'https://speed.cloudflare.com/__down?bytes=500000000'
    $helperProcess = Start-Process -FilePath $helper -PassThru -WindowStyle Hidden `
        -ArgumentList '-s', '--noproxy', '"*"', '--limit-rate', '20k', '-o', 'NUL', '--max-time', '240', "`"$download`""
    $helperOnline = Wait-Until {
        Get-NetTCPConnection -OwningProcess $helperProcess.Id -State Established -ErrorAction SilentlyContinue
    }

    $app = Start-Process -FilePath $Exe -ArgumentList "`"$subject`"" -PassThru
    $window = Wait-Until {
        Get-Windows $app.Id | Where-Object { $_.Current.Name -like 'NetMute*' } | Select-Object -First 1
    }
    Assert-That 'The window opens' ($null -ne $window)
    if (-not $window) { throw 'No window, nothing more to test.' }

    if ($helperOnline) {
        $accept = Wait-Until {
            foreach ($w in Get-Windows $app.Id) {
                $button = Find-Element $w 'Block them too'
                if ($button) { return $button }
            }
        }
        Assert-That 'Adding a program offers its online helper' ($null -ne $accept)
        if ($accept) {
            Invoke-Button $accept
            Assert-That 'The helper gets blocked too' ($null -ne (Wait-Until { (Get-BlockedList) -like '*curl-helper.exe*' }))
            Assert-That 'The helper loses its open connection' ($null -ne (Wait-Until {
                -not (Get-NetTCPConnection -OwningProcess $helperProcess.Id -State Established -ErrorAction SilentlyContinue)
            }))
        }
    }
    else {
        Write-Output '[SKIP] Helper offer: the helper could not get online'
    }

    Assert-That 'The added program is blocked' (-not (Test-Online $url))

    $row = Wait-Until { Find-Element $window 'curl-subject.exe' }
    Assert-That 'The program has a row in the list' ($null -ne $row)
    $toggle = Get-RowToggle $row
    Assert-That 'Its box is checked' ($toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On)

    $toggle.Toggle()
    Assert-That 'Unchecking gives the network back' ($null -ne (Wait-Until { Test-Online $url }))
    Assert-That 'The box follows' ($null -ne (Wait-Until { (Get-RowToggle $row).Current.ToggleState -eq [Windows.Automation.ToggleState]::Off }))

    (Get-RowToggle $row).Toggle()
    Assert-That 'Checking takes it away again' ($null -ne (Wait-Until { -not (Test-Online $url) }))

    if ($Screenshot) { Save-Picture $window $Screenshot }

    Invoke-Button (Find-Element $window 'Allow all')
    Assert-That 'Allow all gives the network back' ($null -ne (Wait-Until { Test-Online $url }))
    Assert-That 'Allow all keeps the row' ($null -ne (Find-Element $window 'curl-subject.exe'))

    (Get-RowToggle (Find-Element $window 'curl-subject.exe')).Toggle()
    Assert-That 'Blocked once more' ($null -ne (Wait-Until { -not (Test-Online $url) }))

    $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    Assert-That 'The window closes' ($app.WaitForExit(15000) -and $app.ExitCode -eq 0)
    Assert-That 'The rule outlives the window' (-not (Test-Online $url))

    $again = Start-Process -FilePath $Exe -ArgumentList 'clear' -Wait -PassThru
    Assert-That 'Clear succeeds' ($again.ExitCode -eq 0)
    Assert-That 'Online after clear' (Test-Online $url)
}
finally {
    if ($app -and -not $app.HasExited) { $app.Kill() }
    if ($helperProcess -and -not $helperProcess.HasExited) { $helperProcess.Kill() }
    Start-Process -FilePath $Exe -ArgumentList 'clear' -Wait
    Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failed) { throw 'Window test failed.' }
Write-Output 'All window checks passed.'
