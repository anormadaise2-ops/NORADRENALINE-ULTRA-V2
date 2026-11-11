$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$buildDirectory = Join-Path $projectRoot "BUILD"
$probePath = Join-Path $buildDirectory "bin\Release\nora-x1-probe.exe"

if (-not (Test-Path $probePath)) {
    & (Join-Path $projectRoot "BUILD-DRIVERS.ps1") -Configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "The NORA X1 probe could not be built."
    }
}

$cmake = Get-Command cmake -ErrorAction Stop
& $cmake.Source --build $buildDirectory --config Release --target nora-x1-probe
if ($LASTEXITCODE -ne 0) {
    throw "NORA X1 test target build failed with exit code $LASTEXITCODE."
}

& $cmake.Source --build $buildDirectory --config Release --target RUN_TESTS
if ($LASTEXITCODE -ne 0) {
    throw "NORA X1 tests failed with exit code $LASTEXITCODE."
}
