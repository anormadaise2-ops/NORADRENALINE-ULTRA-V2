param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\NoradrenalineUltra\NoradrenalineUltra.csproj"
$probeBuildScript = Join-Path $repositoryRoot "NORA-X1-DRIVER\BUILD-DRIVERS.ps1"
$publishDirectory = Join-Path $repositoryRoot "dist\publish"
$stagingDirectory = Join-Path $repositoryRoot "dist\NORADRENALINE-ULTRA-Windows-x64"
$archivePath = Join-Path $repositoryRoot "dist\NORADRENALINE-ULTRA-Windows-x64.zip"
$checksumPath = "$archivePath.sha256"
$applicationVersion = "0.1.0"
if ($env:GITHUB_REF_NAME -match '^v(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)$') {
    $applicationVersion = $Matches.version
}

if (-not (Test-Path $projectPath)) {
    throw "Application project not found: $projectPath"
}

$windowsPowerShell = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
& $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $probeBuildScript -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "The NORA X1 diagnostics probe build failed with exit code $LASTEXITCODE."
}

$cmake = Get-Command cmake -ErrorAction Stop
$nativeBuildDirectory = Join-Path $repositoryRoot "NORA-X1-DRIVER\BUILD"
& $cmake.Source --build $nativeBuildDirectory --config $Configuration --target RUN_TESTS
if ($LASTEXITCODE -ne 0) {
    throw "The NORA X1 diagnostics tests failed with exit code $LASTEXITCODE."
}

if (Test-Path $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

& dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDirectory `
    "-p:Version=$applicationVersion" `
    "-p:DebugType=none" `
    "-p:DebugSymbols=false"
if ($LASTEXITCODE -ne 0) {
    throw "The self-contained Windows application publish failed with exit code $LASTEXITCODE."
}

$applicationExecutable = Join-Path $publishDirectory "NoradrenalineUltra.exe"
$probeExecutable = Join-Path $publishDirectory "nora-x1-probe.exe"
foreach ($requiredFile in @($applicationExecutable, $probeExecutable)) {
    if (-not (Test-Path $requiredFile)) {
        throw "The self-contained package is missing a required executable: $requiredFile"
    }
}

if (Test-Path $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stagingDirectory "AppFiles") -Force | Out-Null
Copy-Item -Path (Join-Path $publishDirectory "*") -Destination (Join-Path $stagingDirectory "AppFiles") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot "installer\Install-NoradrenalineUltra.ps1") -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "installer\Uninstall-NoradrenalineUltra.ps1") -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "installer\Install-NoradrenalineUltra.cmd") -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "installer\README.txt") -Destination $stagingDirectory

if (Test-Path $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
if (Test-Path $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}
Compress-Archive -Path (Join-Path $stagingDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText(
    $checksumPath,
    "$hash  $([System.IO.Path]::GetFileName($archivePath))`r`n",
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Install package: $archivePath"
Write-Output "Application version: $applicationVersion"
Write-Output "SHA-256: $hash"

Remove-Item -LiteralPath $publishDirectory, $stagingDirectory -Recurse -Force
Write-Output "Intermediate publish files removed; the ZIP and SHA-256 are retained in dist."
