param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$buildDirectory = Join-Path $projectRoot "BUILD"
$probePath = Join-Path $buildDirectory "bin\$Configuration\nora-x1-probe.exe"
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"

if (-not (Test-Path $vswhere)) {
    throw "Visual Studio Installer's vswhere.exe was not found. Install Visual Studio 2022 with the Desktop development with C++ workload."
}

$visualStudioPath = & $vswhere -latest -products "*" -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $visualStudioPath) {
    throw "The Visual Studio C++ x64 toolchain was not found. Install the Desktop development with C++ workload."
}

$cmake = Get-Command cmake -ErrorAction Stop
& $cmake.Source -S $projectRoot -B $buildDirectory -G "Visual Studio 17 2022" -A x64
if ($LASTEXITCODE -ne 0) {
    throw "CMake configuration failed with exit code $LASTEXITCODE."
}

& $cmake.Source --build $buildDirectory --config $Configuration --target nora-x1-probe
if ($LASTEXITCODE -ne 0) {
    throw "NORA X1 probe build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $probePath)) {
    throw "The native build completed without producing the expected probe: $probePath"
}

$applicationOutput = Join-Path $projectRoot "..\src\NoradrenalineUltra\bin\$Configuration\net9.0-windows"
if (Test-Path $applicationOutput) {
    Copy-Item -LiteralPath $probePath -Destination (Join-Path $applicationOutput "nora-x1-probe.exe") -Force
    Write-Output "Copied the probe beside NORADRENALINE ULTRA: $applicationOutput"
}
else {
    Write-Output "Probe built. Build NORADRENALINE ULTRA in $Configuration configuration, then rerun this script to copy the bridge beside it."
}
