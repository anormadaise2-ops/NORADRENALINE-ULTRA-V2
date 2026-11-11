$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$applicationProject = Join-Path $repositoryRoot "src\NoradrenalineUltra\NoradrenalineUltra.csproj"

& dotnet build $applicationProject --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "NORADRENALINE ULTRA build failed with exit code $LASTEXITCODE."
}

& (Join-Path $PSScriptRoot "BUILD-DRIVERS.ps1") -Configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "NORA X1 probe build failed."
}

& (Join-Path $PSScriptRoot "TEST-DRIVERS.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "NORA X1 test run failed."
}

Write-Output "Application and user-mode NORA X1 diagnostics built and tested. No kernel driver or installer was produced."
