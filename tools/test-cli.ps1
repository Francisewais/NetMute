# Runs block, toggle, unblock, list and clear against a copy of curl.exe and checks with real
# connections that each one did its job. Needs administrator rights and an internet connection.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-cli.ps1 -Exe bin\NetMute.exe
param([Parameter(Mandatory)][string]$Exe)

$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$folder = Join-Path ([IO.Path]::GetTempPath()) ("NetMute-cli-test-" + $PID)
New-Item -ItemType Directory -Path $folder | Out-Null
$subject = Join-Path $folder 'curl-subject.exe'
Copy-Item (Join-Path $env:WINDIR 'System32\curl.exe') $subject

$urls = 'http://www.msftconnecttest.com/connecttest.txt', 'https://www.baidu.com/', 'https://example.com/'

function Invoke-NetMute([string[]]$Arguments) {
    $quoted = $Arguments | ForEach-Object { '"' + $_ + '"' }
    (Start-Process -FilePath $Exe -ArgumentList $quoted -Wait -PassThru).ExitCode
}

function Test-Online([string]$Url) {
    $code = & $subject -sS --noproxy '*' -o NUL -w '%{http_code}' --connect-timeout 8 --max-time 20 $Url 2>$null
    $LASTEXITCODE -eq 0 -and "$code" -match '^[23]'
}

function Assert-That([string]$Name, [bool]$Passed) {
    Write-Output ("[{0}] {1}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name)
    if (-not $Passed) { $script:failed = $true }
}

$failed = $false
$url = $urls | Where-Object { Test-Online $_ } | Select-Object -First 1
if (-not $url) { throw 'The test program cannot reach any test site before blocking.' }

try {
    Assert-That 'block exits with 0' ((Invoke-NetMute 'block', $subject) -eq 0)
    Assert-That 'program is offline after block' (-not (Test-Online $url))

    $listFile = Join-Path $folder 'list.txt'
    $list = Start-Process -FilePath $Exe -ArgumentList 'list' -Wait -PassThru -RedirectStandardOutput $listFile
    Assert-That 'list exits with 0' ($list.ExitCode -eq 0)
    Assert-That 'list names the program' ((Get-Content $listFile -Raw) -like "*curl-subject.exe*")

    Assert-That 'toggle exits with 0' ((Invoke-NetMute 'toggle', $subject) -eq 0)
    Assert-That 'program is online after toggling off' (Test-Online $url)

    Assert-That 'toggle exits with 0 again' ((Invoke-NetMute 'toggle', $subject) -eq 0)
    Assert-That 'program is offline after toggling on' (-not (Test-Online $url))

    Assert-That 'unblock exits with 0' ((Invoke-NetMute 'unblock', $subject) -eq 0)
    Assert-That 'program is online after unblock' (Test-Online $url)

    Assert-That 'block of a missing file fails' ((Invoke-NetMute 'block', (Join-Path $folder 'missing.exe')) -ne 0)
    Assert-That 'an unknown command fails' ((Invoke-NetMute 'frobnicate') -ne 0)

    Assert-That 'block before clear exits with 0' ((Invoke-NetMute 'block', $subject) -eq 0)
    Assert-That 'clear exits with 0' ((Invoke-NetMute 'clear') -eq 0)
    Assert-That 'program is online after clear' (Test-Online $url)
}
finally {
    Invoke-NetMute 'clear' | Out-Null
    Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failed) { throw 'Command-line test failed.' }
Write-Output 'All command-line checks passed.'
