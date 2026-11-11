$ErrorActionPreference = "Stop"

$packageDirectory = $PSScriptRoot
$payloadDirectory = Join-Path $packageDirectory "AppFiles"
$applicationExecutable = Join-Path $payloadDirectory "NoradrenalineUltra.exe"
$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\NORADRENALINE ULTRA"
$shortcutDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $shortcutDirectory "NORADRENALINE ULTRA.lnk"
$uninstallerSource = Join-Path $packageDirectory "Uninstall-NoradrenalineUltra.ps1"

if (-not (Test-Path $applicationExecutable -PathType Leaf)) {
    throw "The application files are missing. Extract the complete download before installing."
}
if (-not (Test-Path (Join-Path $payloadDirectory "nora-x1-probe.exe") -PathType Leaf)) {
    throw "The NORA X1 graphics probe is missing from this package."
}

$runningApplication = Get-Process -Name "NoradrenalineUltra" -ErrorAction SilentlyContinue
if ($runningApplication) {
    throw "Close NORADRENALINE ULTRA before installing or updating it."
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $payloadDirectory "*") -Destination $installDirectory -Recurse -Force
Copy-Item -LiteralPath $uninstallerSource -Destination (Join-Path $installDirectory "Uninstall-NoradrenalineUltra.ps1") -Force

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $installDirectory "NoradrenalineUltra.exe"
$shortcut.WorkingDirectory = $installDirectory
$shortcut.Description = "NORADRENALINE ULTRA"
$shortcut.Save()

$uninstallKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NORADRENALINE-ULTRA"
$uninstallKey = New-Item -Path $uninstallKeyPath -Force
$uninstallCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$installDirectory\Uninstall-NoradrenalineUltra.ps1`""
$uninstallKey.SetValue("DisplayName", "NORADRENALINE ULTRA")
$uninstallKey.SetValue("DisplayVersion", "0.1.0")
$uninstallKey.SetValue("Publisher", "NORADRENALINE TECHNOLOGIES")
$uninstallKey.SetValue("InstallLocation", $installDirectory)
$uninstallKey.SetValue("UninstallString", $uninstallCommand)
$uninstallKey.SetValue("NoModify", 1, [Microsoft.Win32.RegistryValueKind]::DWord)
$uninstallKey.SetValue("NoRepair", 1, [Microsoft.Win32.RegistryValueKind]::DWord)

Write-Output "NORADRENALINE ULTRA is installed for the current Windows user."
Write-Output "Start menu shortcut: $shortcutPath"
Write-Output "No administrator privileges were required."
