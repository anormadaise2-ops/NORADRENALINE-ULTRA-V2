$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "BUILD-DRIVERS.ps1") -Configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "NORA X1 SDK/diagnostics build failed with exit code $LASTEXITCODE."
}
