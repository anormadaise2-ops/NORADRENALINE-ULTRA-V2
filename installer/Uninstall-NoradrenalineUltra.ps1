$ErrorActionPreference = "Stop"

$installDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot)
$expectedParent = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "Programs"))
$expectedDirectory = [System.IO.Path]::GetFullPath((Join-Path $expectedParent "NORADRENALINE ULTRA"))
if (-not [string]::Equals($installDirectory, $expectedDirectory, [System.StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($installDirectory, $expectedParent, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove an installation outside the expected per-user application directory."
}

if (Get-Process -Name "NoradrenalineUltra" -ErrorAction SilentlyContinue) {
    throw "Close NORADRENALINE ULTRA before uninstalling it."
}

$shortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\NORADRENALINE ULTRA.lnk"
$uninstallKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NORADRENALINE-ULTRA"
if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}
if (Test-Path $uninstallKeyPath) {
    Remove-Item -LiteralPath $uninstallKeyPath -Recurse -Force
}

$escapedInstallDirectory = $installDirectory.Replace("'", "''")
$cleanupCommand = "Start-Sleep -Seconds 2; Remove-Item -LiteralPath '$escapedInstallDirectory' -Recurse -Force"
Start-Process -FilePath "powershell.exe" `
    -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $cleanupCommand) `
    -WindowStyle Hidden

Write-Output "The current-user installation and Start menu shortcut are being removed."
Write-Output "User game-library data is kept under LocalAppData."
