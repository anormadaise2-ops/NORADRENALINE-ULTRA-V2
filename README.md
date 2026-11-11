# NORADRENALINE ULTRA

NORADRENALINE ULTRA is a Windows desktop application by NORADRENALINE TECHNOLOGIES. The current milestone includes a local game library/launcher, a read-only Windows driver inventory, a guarded signed-INF installation flow, and an optional native NORA X1 graphics-capability probe.

## Requirements

- Windows 10 or later
- .NET 9 SDK
- Windows PowerShell and Windows Plug and Play (PnPUtil)

## Build and run

From the repository root:

```powershell
dotnet restore .\src\NoradrenalineUltra\NoradrenalineUltra.csproj
dotnet build .\src\NoradrenalineUltra\NoradrenalineUltra.csproj --no-restore
dotnet run --project .\src\NoradrenalineUltra\NoradrenalineUltra.csproj
```

Build a self-contained per-user Windows x64 installation package (Visual Studio 2022 C++ tools and the .NET 9 SDK required):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\BUILD-INSTALLER.ps1
```

The ZIP and SHA-256 sidecar are written to `dist`. Extract the ZIP and run `Install-NoradrenalineUltra.cmd`. The installer creates a current-user Start menu shortcut and an uninstall entry; it does not require administrator privileges or include GPU drivers. The package is not code-signed.

The **Game Center** scans local Steam and Epic install manifests, lets the user add a game executable, opens platform launch links, and can launch a directly selected executable. The optional **Above normal · game only** setting adjusts process priority only for a directly launched game; it does not change global power, registry, Defender, or driver settings and does not guarantee higher FPS. The game list is stored for the current Windows user under `%LOCALAPPDATA%`.

In the **Driver Center**, select **SCAN DRIVERS** to read the Windows Plug and Play inventory. Reports are exported only when the user chooses **EXPORT REPORT** and a destination. Reports include hardware IDs and should be treated as potentially identifying information.

## Driver installation safety

**INSTALL VERIFIED INF** accepts a single `.inf` file selected by the user. The application asks Windows SetupAPI to verify its catalog signature, shows the signer and catalog, requests explicit confirmation, and then invokes an elevated helper that rechecks the signature before calling the Windows `pnputil.exe` tool. Administrator approval is required. The Windows installation result is shown to the user; a fresh inventory scan is needed to check the resulting device state.

The application does not discover or download driver packages, select a “recommended” version, install packages in the background, or create kernel-mode drivers. Signature verification does not establish that a package is the right version for a particular device; obtain packages from the device or PC manufacturer's official support channel and confirm compatibility before installation.

Manufacturer links are limited to a small set of configured official support pages. No report or device information is uploaded.

## NORA X1 user-mode diagnostics

`NORA-X1-DRIVER` is a separate C++/Windows SDK project. Its first component is a read-only DXGI/D3D12 capability probe, not a kernel/display driver and not a replacement for AMD, NVIDIA, or Intel drivers. NORADRENALINE ULTRA can run the probe when it is built and copied beside the application.

Build both projects and run the native self-test:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\NORA-X1-DRIVER\BUILD-ALL.ps1
```

The probe JSON reports hardware adapter names/IDs, driver versions returned by DXGI, dedicated video memory, and whether a D3D12 device can be created. Software-rendering adapters are excluded.

## Graphics/upscaling status

No proprietary DLSS-equivalent upscaler, frame generation, or per-game graphics injection is implemented. DLSS/FSR/XeSS-like integration requires a real supported runtime/SDK and, in many cases, per-game integration. The app does not simulate such features or alter game files.

## Website and GitHub Pages

The static website is in `website`. `.github/workflows/deploy-website.yml` deploys it to GitHub Pages on pushes to `main`/`master` or when manually started from the Actions tab. Enable **Settings → Pages → Build and deployment → GitHub Actions** in the GitHub repository. The workflow configures the website to look up the latest non-draft GitHub release and its Windows ZIP/SHA-256. Until a release is published, the site intentionally disables the application download button. `.github/workflows/release.yml` creates a **draft** Windows release for review when a `v*` tag is pushed; publish it manually only after reviewing its build and checksum.

## Current status

Implemented: WPF desktop shell, Steam/Epic manifest discovery, manual game entries, platform/direct game launching, optional process-only priority, local JSON game storage, Windows Plug and Play device/driver inventory, device problem codes, search/filter, local JSON driver-report export, official support links for selected manufacturers, explicit signed-INF installation via PnPUtil, a separate C++ DXGI/D3D12 probe, and a native probe self-test.

Not implemented yet: a proprietary upscaler/frame-generation engine, FPS/frametime overlay, driver backup/restore, automatic driver downloads or recommendations, kernel/WDDM drivers, live hardware monitoring, system repair, network/global Windows optimization, update service, cloud APIs, and a code-signed installer. The website and build/deploy workflows are present, but require a configured GitHub repository and successful Actions runs before a live download or website URL exists.

## Architecture

- `Models/DriverDevice.cs`: device and driver inventory data.
- `Services/DriverInventoryService.cs`: read-only WMI/CIM inventory through Windows PowerShell.
- `Services/DriverPackageInstaller.cs`: Windows SetupAPI signature verification and PnPUtil installation.
- `Services/DriverReportExporter.cs`: user-initiated local JSON report export.
- `Services/GameLibraryService.cs`: local Steam/Epic manifest discovery.
- `Services/GameLauncher.cs`: platform or direct process launch with optional per-process priority.
- `Services/NoraX1ProbeService.cs`: versioned JSON communication with the native capability probe.
- `NORA-X1-DRIVER/`: separate C++ DXGI/D3D12 user-mode diagnostics project and build scripts.
