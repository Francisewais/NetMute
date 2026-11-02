# Drives the real window with UI Automation and real mouse clicks, and checks every step with
# real connections: add a program, get offered its online helper, switch it off and on, allow
# all, use the file and folder dialogs and the running-program picker, make and use a desktop
# switch, close the window and find the rule still enforced, then remove everything.
# Needs administrator rights, an internet connection and a free desktop (it moves the mouse),
# and an English Windows, because it finds buttons by their text.
# It wipes every NetMute rule at the end, so it refuses to run if there are any.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-ui.ps1 -Exe bin\NetMute.exe
param(
    [Parameter(Mandatory)][string]$Exe,
    [string]$Screenshot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Mouse {
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    public static void Click(int x, int y) { SetCursorPos(x, y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
    public static void RightClick(int x, int y) { SetCursorPos(x, y); mouse_event(8, 0, 0, 0, UIntPtr.Zero); mouse_event(16, 0, 0, 0, UIntPtr.Zero); }
}
'@

$Exe = (Resolve-Path $Exe).Path
$folder = Join-Path ([IO.Path]::GetTempPath()) ("NetMute-ui-test-" + $PID)
New-Item -ItemType Directory -Path $folder | Out-Null
$subject = Join-Path $folder 'curl-subject.exe'
$helper = Join-Path $folder 'curl-helper.exe'
Copy-Item (Join-Path $env:WINDIR 'System32\curl.exe') $subject
Copy-Item (Join-Path $env:WINDIR 'System32\curl.exe') $helper

$urls = 'http://www.msftconnecttest.com/connecttest.txt', 'https://www.baidu.com/', 'https://example.com/'
$failed = $false

# This file stays plain ASCII: Windows PowerShell does not read scripts as UTF-8 without a BOM.
$dots = [string][char]0x2026
$arrow = [string][char]0x25BE

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

function Test-Online([string]$Url, [string]$Program = $subject) {
    # Windows PowerShell would otherwise treat anything curl prints on stderr as a fatal error.
    $ErrorActionPreference = 'Continue'
    $code = & $Program -sS --noproxy '*' -o NUL -w '%{http_code}' --connect-timeout 8 --max-time 20 $Url 2>$null
    $LASTEXITCODE -eq 0 -and "$code" -match '^[23]'
}

# A copy of curl in its own folder, so it has no relatives to be offered along with it.
function New-TestProgram([string]$SubFolder, [string]$Name) {
    $target = Join-Path $folder $SubFolder
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $path = Join-Path $target $Name
    Copy-Item (Join-Path $env:WINDIR 'System32\curl.exe') $path
    $path
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

# Looks through all of the process's windows: dialogs and menus are windows of their own.
function Find-Anywhere([int]$ProcessId, [string]$Name) {
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::NameProperty, $Name)
    foreach ($w in Get-Windows $ProcessId) {
        $found = $w.FindFirst([Windows.Automation.TreeScope]::Subtree, $condition)
        if ($found) { return $found }
    }
    return $null
}

function Wait-Element([int]$ProcessId, [string]$Name) {
    $found = Wait-Until { Find-Anywhere $ProcessId $Name }
    if (-not $found) { throw "Did not find '$Name' on screen." }
    $found
}

# A real click. Invoking a button that opens a dialog would block until the dialog closes.
function Click-Element($Element) {
    $point = $Element.GetClickablePoint()
    [Mouse]::Click([int]$point.X, [int]$point.Y)
}

function Open-RowMenu($Row) {
    $Row.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $point = $Row.GetClickablePoint()
    [Mouse]::RightClick([int]$point.X, [int]$point.Y)
}

# Types into whatever has the keyboard, like the file name box of a file dialog.
function Send-Text([string]$Text) {
    [Windows.Forms.SendKeys]::SendWait(($Text -replace '([+^%~(){}\[\]])', '{$1}'))
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
    try {
        $r = $Window.Current.BoundingRectangle
        $picture = New-Object Drawing.Bitmap([int]$r.Width, [int]$r.Height)
        $g = [Drawing.Graphics]::FromImage($picture)
        $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $picture.Size)
        $g.Dispose()
        $picture.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
        $picture.Dispose()
    }
    catch {
        Write-Output "No screenshot: $_"
    }
}

# Starts a slow download with the helper, so it stays online for as long as the test needs.
# Returns the process once it has a connection, or null if no server would give it one.
function Start-OnlineHelper {
    $downloads =
        'https://codeload.github.com/microsoft/vscode/zip/refs/heads/main',
        'https://speed.cloudflare.com/__down?bytes=100000000'
    foreach ($download in $downloads) {
        $process = Start-Process -FilePath $helper -PassThru -WindowStyle Hidden -ArgumentList `
            '-s', '--noproxy', '"*"', '--limit-rate', '20k', '-o', 'NUL', '--max-time', '240', "`"$download`""
        $online = Wait-Until -Seconds 12 {
            Get-NetTCPConnection -OwningProcess $process.Id -State Established -ErrorAction SilentlyContinue
        }
        if ($online) { return $process }
        if (-not $process.HasExited) { $process.Kill() }
    }
    return $null
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
    $helperProcess = Start-OnlineHelper

    $app = Start-Process -FilePath $Exe -ArgumentList "`"$subject`"" -PassThru
    $window = Wait-Until {
        Get-Windows $app.Id | Where-Object { $_.Current.Name -like 'NetMute*' } | Select-Object -First 1
    }
    Assert-That 'The window opens' ($null -ne $window)
    if (-not $window) { throw 'No window, nothing more to test.' }

    if ($helperProcess) {
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

    # The file dialog
    $viaDialog = New-TestProgram 'dialog' 'curl-dialog.exe'
    Click-Element (Wait-Element $app.Id "Add program$dots")
    Wait-Element $app.Id 'Choose a program to block' | Out-Null
    Start-Sleep -Milliseconds 800
    Send-Text $viaDialog
    [Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Assert-That 'The file dialog adds and blocks a program' ($null -ne (Wait-Until { (Get-BlockedList) -like '*curl-dialog.exe*' }))
    Assert-That 'That program is offline' (-not (Test-Online $url $viaDialog))

    # The folder dialog
    $viaFolderA = New-TestProgram 'whole' 'curl-folder-a.exe'
    $viaFolderB = New-TestProgram 'whole\sub' 'curl-folder-b.exe'
    Click-Element (Wait-Element $app.Id "Add folder$dots")
    Wait-Element $app.Id 'Choose a folder of programs to block' | Out-Null
    Start-Sleep -Milliseconds 800
    Send-Text (Join-Path $folder 'whole')
    [Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 800
    $selectFolder = Find-Anywhere $app.Id 'Select Folder'
    if ($selectFolder) { Invoke-Button $selectFolder }
    $blockSelected = Wait-Element $app.Id 'Block selected'
    Assert-That 'The folder dialog finds programs in subfolders' ($null -ne (Find-Anywhere $app.Id 'curl-folder-b.exe'))
    Click-Element $blockSelected
    Assert-That 'Both programs from the folder are blocked' ($null -ne (Wait-Until {
        $text = Get-BlockedList
        $text -like '*curl-folder-a.exe*' -and $text -like '*curl-folder-b.exe*'
    }))
    Assert-That 'A program from the folder is offline' (-not (Test-Online $url $viaFolderB))

    # The running-program picker
    $running = New-TestProgram 'picked' 'curl-picked.exe'
    $savedHelper = $helper
    $helper = $running
    $runningProcess = Start-OnlineHelper
    $helper = $savedHelper
    if ($runningProcess) {
        Click-Element (Wait-Element $app.Id "Pick a running program$dots")
        Wait-Element $app.Id 'Block selected' | Out-Null
        Start-Sleep -Milliseconds 800
        Send-Text 'curl-picked'
        $pickedRow = Wait-Element $app.Id 'curl-picked.exe'
        $pickedRow.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Click-Element (Wait-Element $app.Id 'Block selected')
        Assert-That 'The picker blocks a running program' ($null -ne (Wait-Until { (Get-BlockedList) -like '*curl-picked.exe*' }))
        Assert-That 'The picked program loses its connection' ($null -ne (Wait-Until {
            -not (Get-NetTCPConnection -OwningProcess $runningProcess.Id -State Established -ErrorAction SilentlyContinue)
        }))
        if (-not $runningProcess.HasExited) { $runningProcess.Kill() }
    }
    else {
        Write-Output '[SKIP] Picker: the program could not get online'
    }

    # The desktop switch
    $dialogRow = Wait-Element $app.Id 'curl-dialog.exe'
    Open-RowMenu $dialogRow
    Click-Element (Wait-Element $app.Id 'Create a switch on the desktop')
    Invoke-Button (Wait-Element $app.Id 'OK')
    $switch = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Network switch - curl-dialog.lnk'
    Assert-That 'The desktop switch is created' (Test-Path $switch)
    Start-Process $switch
    Assert-That 'Double-clicking the switch gives the network back' ($null -ne (Wait-Until { Test-Online $url $viaDialog }))
    Start-Process $switch
    Assert-That 'Double-clicking it again takes it away' ($null -ne (Wait-Until { -not (Test-Online $url $viaDialog) }))
    Start-Sleep -Seconds 3
    Remove-Item $switch -Force

    # Removing a row
    $window.SetFocus()
    Start-Sleep -Milliseconds 500
    $dialogRow = Wait-Element $app.Id 'curl-dialog.exe'
    Open-RowMenu $dialogRow
    Click-Element (Wait-Element $app.Id 'Remove from list and allow')
    Assert-That 'Removing a row gives the network back' ($null -ne (Wait-Until { Test-Online $url $viaDialog }))
    Assert-That 'The row is gone' ($null -ne (Wait-Until { -not (Find-Element $window 'curl-dialog.exe') }))

    # The rules live on without the window
    (Get-RowToggle (Find-Element $window 'curl-subject.exe')).Toggle()
    Assert-That 'Blocked once more' ($null -ne (Wait-Until { -not (Test-Online $url) }))

    $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    Assert-That 'The window closes' ($app.WaitForExit(15000) -and $app.ExitCode -eq 0)
    Assert-That 'The rule outlives the window' (-not (Test-Online $url))

    # A new window shows the old state, and can remove everything
    $app = Start-Process -FilePath $Exe -PassThru
    $window = Wait-Until {
        Get-Windows $app.Id | Where-Object { $_.Current.Name -like 'NetMute*' } | Select-Object -First 1
    }
    $row = Wait-Until { Find-Element $window 'curl-subject.exe' }
    Assert-That 'A new window shows the rule as checked' (
        $row -and (Get-RowToggle $row).Current.ToggleState -eq [Windows.Automation.ToggleState]::On)

    Click-Element (Wait-Element $app.Id "More $arrow")
    Click-Element (Wait-Element $app.Id "Remove all rules$dots")
    Invoke-Button (Wait-Element $app.Id 'Yes')
    Invoke-Button (Wait-Element $app.Id 'OK')
    Assert-That 'Remove all rules gives the network back' ($null -ne (Wait-Until { Test-Online $url }))
    Assert-That 'Remove all rules empties the list' ($null -ne (Wait-Until { -not (Find-Element $window 'curl-subject.exe') }))
    Assert-That 'No rules are left' ((Get-BlockedList) -notmatch '\\')

    $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    Assert-That 'The new window closes' ($app.WaitForExit(15000) -and $app.ExitCode -eq 0)
}
finally {
    if ($app -and -not $app.HasExited) { $app.Kill() }
    if ($helperProcess -and -not $helperProcess.HasExited) { $helperProcess.Kill() }
    Start-Process -FilePath $Exe -ArgumentList 'clear' -Wait
    Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failed) { throw 'Window test failed.' }
Write-Output 'All window checks passed.'
