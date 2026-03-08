param(
    [Parameter(Mandatory = $true)]
    [string]$Message
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root

# Stage all changes
git add -A

# Commit
git commit -m $Message

# Push
git push origin main

Write-Host "Done: committed and pushed to origin/main"
