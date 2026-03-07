# NovaLog Benchmark: Avalonia vs WinForms
# Compares startup time, WorkingSet64, and PrivateMemorySize64

param(
    [string]$AvaloniaProject = "$PSScriptRoot\..\NovaLog.Avalonia\NovaLog.Avalonia.csproj",
    [string]$WinFormsProject = "D:\dev\src\tools\NovaLog\NovaLog.csproj",
    [int]$WaitSeconds = 5
)

function Measure-App {
    param(
        [string]$Label,
        [string]$ExePath
    )

    if (-not (Test-Path $ExePath)) {
        Write-Host "[$Label] EXE not found: $ExePath" -ForegroundColor Red
        return $null
    }

    Write-Host "`n--- $Label ---" -ForegroundColor Cyan

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $ExePath -PassThru
    Start-Sleep -Seconds $WaitSeconds
    $sw.Stop()

    if ($proc.HasExited) {
        Write-Host "[$Label] Process exited prematurely" -ForegroundColor Red
        return $null
    }

    $proc.Refresh()
    $ws = [math]::Round($proc.WorkingSet64 / 1MB, 1)
    $pb = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)

    Write-Host "[$Label] PID:            $($proc.Id)"
    Write-Host "[$Label] WorkingSet:      $ws MB"
    Write-Host "[$Label] PrivateBytes:    $pb MB"
    Write-Host "[$Label] Wall-clock wait: $($sw.ElapsedMilliseconds) ms"

    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

    return @{
        Label        = $Label
        WorkingSetMB = $ws
        PrivateBytesMB = $pb
        WallClockMs  = $sw.ElapsedMilliseconds
    }
}

Write-Host "=== NovaLog Benchmark ===" -ForegroundColor Green
Write-Host "Building projects..." -ForegroundColor Yellow

# Build Avalonia
$avaloniaBin = $null
if (Test-Path $AvaloniaProject) {
    dotnet build $AvaloniaProject -c Release --nologo -v q 2>&1 | Out-Null
    $avaloniaDir = Split-Path $AvaloniaProject
    $avaloniaBin = Get-ChildItem "$avaloniaDir\bin\Release\net*\NovaLog.Avalonia.exe" -Recurse | Select-Object -First 1
}

# Build WinForms
$winformsBin = $null
if (Test-Path $WinFormsProject) {
    dotnet build $WinFormsProject -c Release --nologo -v q 2>&1 | Out-Null
    $winformsDir = Split-Path $WinFormsProject
    $winformsBin = Get-ChildItem "$winformsDir\bin\Release\net*\NovaLog.exe" -Recurse | Select-Object -First 1
}

$results = @()

if ($avaloniaBin) {
    $r = Measure-App -Label "Avalonia" -ExePath $avaloniaBin.FullName
    if ($r) { $results += $r }
}

if ($winformsBin) {
    $r = Measure-App -Label "WinForms" -ExePath $winformsBin.FullName
    if ($r) { $results += $r }
}

# Summary
Write-Host "`n=== Summary ===" -ForegroundColor Green
Write-Host ("{0,-12} {1,14} {2,14} {3,14}" -f "Version", "WorkingSet", "PrivateBytes", "WallClock")
Write-Host ("{0,-12} {1,14} {2,14} {3,14}" -f "-------", "----------", "-----------", "---------")
foreach ($r in $results) {
    Write-Host ("{0,-12} {1,11:F1} MB {2,11:F1} MB {3,11} ms" -f $r.Label, $r.WorkingSetMB, $r.PrivateBytesMB, $r.WallClockMs)
}

if ($results.Count -eq 2) {
    $delta_ws = $results[0].WorkingSetMB - $results[1].WorkingSetMB
    $delta_pb = $results[0].PrivateBytesMB - $results[1].PrivateBytesMB
    Write-Host ("`nDelta (Avalonia - WinForms): WS={0:+0.0;-0.0} MB  PB={1:+0.0;-0.0} MB" -f $delta_ws, $delta_pb) -ForegroundColor Yellow
}
