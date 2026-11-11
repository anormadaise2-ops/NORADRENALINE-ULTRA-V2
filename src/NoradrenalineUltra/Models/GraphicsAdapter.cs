namespace NoradrenalineUltra.Models;

public sealed record GraphicsAdapter(
    string Name,
    string Vendor,
    string DeviceId,
    ulong DedicatedVideoMemoryBytes,
    bool Direct3D12Available,
    string DriverVersion);
