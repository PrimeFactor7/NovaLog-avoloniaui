param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DotnetArgs = @()
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'NovaLog.Avalonia\NovaLog.Avalonia.csproj'
$logsDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null

$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$logFile = Join-Path $logsDir "run_$timestamp.log"

function Write-Log {
    param(
        [string]$Message,
        [string]$Level = 'INFO'
    )

    $line = "[{0}] [{1}] {2}" -f (Get-Date -Format 'yyyy-MM-ddTHH:mm:ss.fffK'), $Level, $Message
    Write-Host $line
    Add-Content -Path $logFile -Value $line
}

try {
    Write-Log "NovaLog run script started."
    Write-Log "Project: $project"
    Write-Log "Log file: $logFile"
}
catch {
    Write-Host $_.Exception.ToString()
    exit 1
}

if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
    $global:PSNativeCommandUseErrorActionPreference = $false
}

$cmdArgs = @('run', '--project', $project) + $DotnetArgs
Write-Log ("Command: dotnet {0}" -f ($cmdArgs -join ' '))

$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & dotnet @cmdArgs 2>&1 | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord] -and $_.FullyQualifiedErrorId -eq 'NativeCommandError') {
            return
        }

        $line = $_.ToString() -replace "`0", ""
        Write-Host $line
        Add-Content -Path $logFile -Value $line
    }
    $exitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

if ($exitCode -eq 0) {
    Write-Log "dotnet exit code: $exitCode"
}
else {
    Write-Log "dotnet exit code: $exitCode" 'ERROR'
}

exit $exitCode
