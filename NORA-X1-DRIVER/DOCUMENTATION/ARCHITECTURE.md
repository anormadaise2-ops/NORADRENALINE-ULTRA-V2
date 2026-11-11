# NORA X1 user-mode diagnostics

The first NORA X1 component is a read-only Windows user-mode probe. It enumerates display adapters with DXGI and reports whether a Direct3D 12 device can be created for each adapter. Its JSON output is versioned and is consumed by NORADRENALINE ULTRA when `nora-x1-probe.exe` is present beside the application.

This is not a GPU driver, kernel service, display driver, or replacement for AMD, NVIDIA, or Intel software. It does not change device state, install packages, inject code into games, or claim support for DLSS, FSR, XeSS, frame generation, or generic upscaling. Those capabilities require separate licensed/runtime integrations and, in many cases, per-game integration.

No kernel-mode component is included: the current read-only DXGI diagnostics do not require kernel access. Any future kernel work needs a demonstrated technical requirement, WDK-based isolated testing, and Microsoft's driver signing process.

## Build and test

Run `.\BUILD-DRIVERS.ps1` to build the x64 probe with the Visual Studio 2022 C++ toolchain and copy it to the NORADRENALINE ULTRA output directory. Run `.\TEST-DRIVERS.ps1` to execute its self-test. The test only checks local JSON/vendor helper behavior; it does not install or modify a driver.

Run `nora-x1-probe.exe --json` for a local hardware capability report. The output contains adapter model names, device IDs, driver versions reported by DXGI, dedicated video memory, and Direct3D 12 device availability.

## Communication boundary

NORADRENALINE ULTRA starts the probe as a child process and reads one JSON response from standard output. There is no listening network port, privileged service, named pipe, or persistent background process in this milestone.
