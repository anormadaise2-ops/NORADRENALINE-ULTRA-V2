namespace NoradrenalineUltra.Models;

public sealed record DriverDevice(
    string DeviceName,
    string Manufacturer,
    string DeviceId,
    string DriverVersion,
    string DriverDate,
    string InfName,
    bool HasDriverRecord,
    bool? IsSigned,
    int? ProblemCode)
{
    public string SignatureStatus => IsSigned switch
    {
        true => "Signed",
        false => "Unsigned",
        _ => "Unavailable"
    };

    public string StatusLabel => ProblemCode switch
    {
        28 => "No driver",
        > 0 => $"Problem {ProblemCode}",
        0 when HasDriverRecord => "OK",
        0 => "No driver record",
        _ => "Unavailable"
    };
}
