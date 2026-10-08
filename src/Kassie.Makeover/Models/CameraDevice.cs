namespace Kassie.Makeover.Models;

public sealed record CameraDevice(int Index, string DisplayName)
{
    public override string ToString() => DisplayName;
}
